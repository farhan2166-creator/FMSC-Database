-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.AdjustBillingChargeRate
 @UserId int,@BillingChargeId bigint,@NewAmount decimal(12,2),@PaidReductionAction nvarchar(10),
 @Reason nvarchar(500),@RequestId uniqueidentifier,@ExpectedNet decimal(12,2)=NULL,@ExpectedBalance decimal(12,2)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequireAnyPermission @UserId,N'Fees.Adjust',N'Fees.Configuration.Manage';
 IF @NewAmount IS NULL OR @NewAmount<0 OR @NewAmount<>FLOOR(@NewAmount) OR @RequestId IS NULL
  OR @PaidReductionAction IS NULL OR @PaidReductionAction NOT IN(N'Credit',N'Refund') OR NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL
  THROW 54920,'Choose a valid new rate, credit/refund choice and reason.',1;
 DECLARE @Lock int,@Admission int,@Enrollment int,@Fee int,@Assignment int,@Month date,@BaseChargeDate date,@Discount decimal(12,2),@PreviousGross decimal(12,2),@Concession nvarchar(20),
  @OldNet decimal(12,2),@Balance decimal(12,2),@NewNet decimal(12,2),@Difference decimal(12,2),@Released decimal(12,2)=0;
 DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
  BEGIN TRAN;
  EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54745,'Billing is running. Try again shortly.',1;
  IF EXISTS(SELECT 1 FROM dbo.FeeBillCorrections WHERE RequestId=@RequestId)
  BEGIN
   IF NOT EXISTS(SELECT 1 FROM dbo.FeeBillCorrections WHERE RequestId=@RequestId AND BillingChargeId=@BillingChargeId
    AND NewGross=@NewAmount AND PaidReductionAction=@PaidReductionAction) THROW 54921,'The correction request belongs to another change.',1;
   COMMIT;
   SELECT BillingChargeId,Difference,ReleasedPaidAmount FROM dbo.FeeBillCorrections WHERE RequestId=@RequestId;RETURN;
  END;
  SELECT @Admission=B.AdmissionId,@Enrollment=B.EnrollmentId,@Fee=B.FeeTypeId,@Assignment=B.StudentFeeAssignmentId,
   @Month=B.BillingMonth,@BaseChargeDate=B.ChargeDate,@Discount=COALESCE(C.NewDiscount,B.DiscountAmount),@PreviousGross=COALESCE(C.NewGross,B.GrossAmount),@Concession=A.ConcessionLabel
  FROM dbo.BillingCharges B WITH(UPDLOCK,HOLDLOCK) JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=B.EnrollmentId
  LEFT JOIN dbo.StudentFeeAssignments A ON A.StudentFeeAssignmentId=B.StudentFeeAssignmentId
  OUTER APPLY(SELECT TOP(1) NewDiscount,NewGross FROM dbo.FeeBillCorrections WHERE BillingChargeId=B.BillingChargeId ORDER BY FeeBillCorrectionId DESC) C
  WHERE B.BillingChargeId=@BillingChargeId AND B.CorrectionOfBillingChargeId IS NULL AND B.ChargeKind=N'Monthly'
   AND B.Status<>N'Cancelled' AND E.Status=N'Active';
  IF @Admission IS NULL THROW 54922,'Choose a non-reversed monthly bill on an active placement.',1;
  DECLARE @Resource nvarchar(255)=CONCAT(N'FMSC.StudentCredit.',@Admission);
  EXEC @Lock=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54901,'Student credit is busy. Try again.',1;
  SELECT @OldNet=SUM(NetAmount),@Balance=SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK)
   WHERE (BillingChargeId=@BillingChargeId OR CorrectionOfBillingChargeId=@BillingChargeId) AND Status<>N'Cancelled';
  IF (@ExpectedNet IS NOT NULL AND @ExpectedNet<>@OldNet) OR (@ExpectedBalance IS NOT NULL AND @ExpectedBalance<>@Balance)
   THROW 54923,'A bill or payment changed after preview. Refresh the review; nothing was changed.',1;
  SET @Discount=CASE WHEN @Concession IN(N'Free',N'Waived') OR (@Concession=N'Scholarship' AND @Discount=@PreviousGross) THEN @NewAmount WHEN @Discount>@NewAmount THEN @NewAmount ELSE @Discount END;
  SET @NewNet=@NewAmount-@Discount;SET @Difference=@NewNet-@OldNet;
  IF @Difference>0
  BEGIN
   INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,
    GrossAmount,DiscountAmount,ChargeKind,Remarks,CorrectionOfBillingChargeId)
   VALUES(@Assignment,@Enrollment,@Admission,@Fee,@Month,CASE WHEN @BaseChargeDate>CONVERT(date,GETDATE()) THEN @BaseChargeDate ELSE CONVERT(date,GETDATE()) END,CASE WHEN @BaseChargeDate>CONVERT(date,GETDATE()) THEN @BaseChargeDate ELSE CONVERT(date,GETDATE()) END,@Difference,0,N'Manual',
    LEFT(CONCAT(N'Rate difference for ',CONVERT(nvarchar(7),@Month,120),N': ',@Reason),500),@BillingChargeId);
   EXEC dbo.ApplyStudentCredit_Internal @Admission,@UserId;
  END;
  IF @Difference<0
  BEGIN
   DECLARE @Remaining decimal(12,2)=-@Difference,@Charge bigint,@Net decimal(12,2),@Due decimal(12,2),
    @Take decimal(12,2),@PaidPart decimal(12,2),@Origin int,@Available decimal(12,2),@PayTake decimal(12,2),@Payment int,
    @Receipt nvarchar(40),@Today date=CONVERT(date,GETDATE()),@Phase int;
   DECLARE Bills CURSOR LOCAL FAST_FORWARD FOR SELECT B.BillingChargeId,B.NetAmount,dbo.GetFeeChargeBalance(B.BillingChargeId),P.Phase
    FROM dbo.BillingCharges B CROSS JOIN(VALUES(0),(1)) P(Phase)
    WHERE (B.BillingChargeId=@BillingChargeId OR B.CorrectionOfBillingChargeId=@BillingChargeId)
     AND B.Status<>N'Cancelled' AND B.NetAmount>0 ORDER BY P.Phase,B.BillingChargeId DESC;
   OPEN Bills; FETCH NEXT FROM Bills INTO @Charge,@Net,@Due,@Phase;
   WHILE @@FETCH_STATUS=0 AND @Remaining>0
   BEGIN
    SELECT @Net=NetAmount,@Due=dbo.GetFeeChargeBalance(@Charge) FROM dbo.BillingCharges WHERE BillingChargeId=@Charge;
    DECLARE @AvailableOnBill decimal(12,2)=CASE WHEN @Phase=0 THEN @Due ELSE @Net END;
    SET @Take=CASE WHEN @Remaining>@AvailableOnBill THEN @AvailableOnBill ELSE @Remaining END;
    SET @PaidPart=CASE WHEN @Take>@Due THEN @Take-@Due ELSE 0 END;
    SET @Released=@Released+@PaidPart;
    IF @PaidPart>0
    BEGIN
     DECLARE Payments CURSOR LOCAL FAST_FORWARD FOR
      SELECT P.FeePaymentId,SUM(A.Amount)-COALESCE((SELECT -SUM(RA.Amount) FROM dbo.FeePayments R
        JOIN dbo.FeePaymentAllocations RA ON RA.FeePaymentId=R.FeePaymentId
        WHERE R.RelatedFeePaymentId=P.FeePaymentId AND R.Status=N'Completed' AND RA.BillingChargeId=@Charge AND RA.Amount<0),0)
      FROM dbo.FeePaymentAllocations A WITH(UPDLOCK,HOLDLOCK) JOIN dbo.FeePayments P WITH(UPDLOCK,HOLDLOCK) ON P.FeePaymentId=A.FeePaymentId
      WHERE A.BillingChargeId=@Charge AND A.Amount>0 AND P.Status=N'Completed'
      GROUP BY P.FeePaymentId ORDER BY P.FeePaymentId;
     OPEN Payments; FETCH NEXT FROM Payments INTO @Origin,@Available;
     WHILE @@FETCH_STATUS=0 AND @PaidPart>0
     BEGIN
      IF @Available>0
      BEGIN
       SET @PayTake=CASE WHEN @PaidPart>@Available THEN @Available ELSE @PaidPart END;
       SET @Receipt=NULL;
       IF @PaidReductionAction=N'Refund' EXEC dbo.GetNextFeeReceiptNumber @ReceiptDate=@Today,@ReceiptNumber=@Receipt OUTPUT;
       ELSE SET @Receipt=CONCAT(N'CR-',CONVERT(nvarchar(36),NEWID()));
       INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Remarks,Status,
        RelatedFeePaymentId,CreatedByUserId,IsCreditApplication)
       VALUES(@Admission,@Enrollment,@Today,@Receipt,-@PayTake,CASE WHEN @PaidReductionAction=N'Refund' THEN N'Refund' ELSE N'Credit Adjustment' END,
        @Reason,N'Completed',@Origin,@UserId,CASE WHEN @PaidReductionAction=N'Credit' THEN 1 ELSE 0 END);
       SET @Payment=CONVERT(int,SCOPE_IDENTITY());
       INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount,Remarks)
        VALUES(@Payment,@Admission,@Charge,-@PayTake,@Reason);
       SET @PaidPart=@PaidPart-@PayTake;
      END;
      FETCH NEXT FROM Payments INTO @Origin,@Available;
     END;
     CLOSE Payments; DEALLOCATE Payments;
     IF @PaidPart>0 THROW 54924,'The paid difference could not be reconciled. Nothing was changed.',1;
    END;
    UPDATE dbo.BillingCharges SET DiscountAmount=DiscountAmount+@Take,Remarks=LEFT(CONCAT(Remarks,N' | Rate correction: ',@Reason),500)
      WHERE BillingChargeId=@Charge;
    UPDATE dbo.BillingCharges SET Status=CASE WHEN dbo.GetFeeChargeBalance(@Charge)=0 THEN N'Paid'
      WHEN dbo.GetFeeChargeBalance(@Charge)<NetAmount THEN N'PartiallyPaid' ELSE N'Open' END WHERE BillingChargeId=@Charge;
    SET @Remaining=@Remaining-@Take;
    FETCH NEXT FROM Bills INTO @Charge,@Net,@Due,@Phase;
   END;
   CLOSE Bills; DEALLOCATE Bills;
   IF @Remaining>0 THROW 54924,'The rate reduction could not be fully reconciled. Nothing was changed.',1;
   IF @Released>0 AND @PaidReductionAction=N'Credit'
    INSERT dbo.StudentCreditEntries(AdmissionId,Amount,EntryKind,RequestId,Reason,CreatedByUserId)
     VALUES(@Admission,@Released,N'BillReduction',@RequestId,@Reason,@UserId);
  END;
  INSERT dbo.FeeBillCorrections(BillingChargeId,RequestId,PreviousNet,NewGross,NewDiscount,Difference,ReleasedPaidAmount,PaidReductionAction,Reason,CreatedByUserId)
   VALUES(@BillingChargeId,@RequestId,@OldNet,@NewAmount,@Discount,@Difference,@Released,@PaidReductionAction,@Reason,@UserId);
  COMMIT;
  SELECT @BillingChargeId BillingChargeId,@Difference Difference,@Released ReleasedPaidAmount;
 END TRY
 BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
