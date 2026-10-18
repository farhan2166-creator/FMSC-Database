-- Operator choices for concession periods, admission concessions and leaving.
-- The installer owns the transaction and verified recovery backup.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 21 requires the installer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>20
 THROW 51000,'Migration 21 requires schema 20.',1;
ALTER TABLE dbo.MutationRequests DROP CONSTRAINT CK_MutationRequests_Kind;
ALTER TABLE dbo.MutationRequests WITH CHECK ADD CONSTRAINT CK_MutationRequests_Kind CHECK(
 (Kind=N'FeeAdjustment' AND FeePaymentId IS NOT NULL AND BankCertificateId IS NULL)
 OR (Kind=N'BankCertificateCreate' AND BankCertificateId IS NOT NULL AND FeePaymentId IS NULL)
 OR (Kind=N'FeeConcession' AND FeePaymentId IS NULL AND BankCertificateId IS NULL));
GO
CREATE OR ALTER PROCEDURE dbo.SaveStudentFeeDiscount
 @UserId int,@StudentFeeAssignmentId int,@DiscountAmount decimal(12,2),@EffectiveFrom date=NULL,@ConcessionLabel nvarchar(20)=NULL,
 @EffectiveThrough date=NULL,@ApplyToBills bit=0,@PaidReductionAction nvarchar(10)=N'Credit',
 @Reason nvarchar(500)=NULL,@Preview bit=0,@ExpectedSnapshot varchar(64)=NULL,
 @NewAmount decimal(12,2)=NULL,@UpdateAgreement bit=1,@BillingChargeId bigint=NULL,@FullFeeWaiver bit=NULL,@ApplyAvailableCredit bit=0
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 SET @DiscountAmount=COALESCE(@DiscountAmount,0);
 IF @NewAmount IS NOT NULL AND (@NewAmount<0 OR @NewAmount<>FLOOR(@NewAmount)) THROW 53101,'Enter a non-negative whole-rupee agreed fee.',1;
 IF @UpdateAgreement=0 AND @ApplyToBills=0 THROW 53103,'Choose whether to change the agreement, issued bills, or both.',1;
 IF @DiscountAmount<0 OR @DiscountAmount<>FLOOR(@DiscountAmount) THROW 53101,'Enter a whole-rupee concession, zero or more.',1;
 IF @ConcessionLabel IS NOT NULL AND @ConcessionLabel NOT IN(N'Free',N'Scholarship',N'Waived') THROW 53103,'Choose a valid concession label.',1;
 IF @PaidReductionAction IS NULL OR @PaidReductionAction NOT IN(N'Credit',N'Refund') THROW 53103,'Choose credit or refund for paid reductions.',1;
 DECLARE @Legacy bit=CASE WHEN @EffectiveFrom IS NULL AND @EffectiveThrough IS NULL AND @ApplyToBills=0 AND @Preview=0 AND @ExpectedSnapshot IS NULL AND @NewAmount IS NULL AND @UpdateAgreement=1 AND @BillingChargeId IS NULL AND @ApplyAvailableCredit=0 THEN 1 ELSE 0 END;
 IF @Legacy=0 AND NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 53103,'Enter the reason for this concession change.',1;
 SET @Reason=COALESCE(NULLIF(LTRIM(RTRIM(@Reason)),N''),N'Concession revised');
 DECLARE @Enrollment int,@Admission int,@Fee int,@Frequency nvarchar(30),@YearStart date,@YearEnd date,@Start date,@State nvarchar(30),
  @From date,@Through date,@Lock int,@Locked int,@Raw nvarchar(max),@Snapshot varchar(64),@Full bit;
 DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
  BEGIN TRAN;
  EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54745,'Billing is running. Try again shortly.',1;
  SELECT @Enrollment=A.EnrollmentId,@Admission=A.AdmissionId,@Fee=A.FeeTypeId,@Frequency=A.Frequency,
   @YearStart=DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1),@YearEnd=DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1),
   @Start=DATEFROMPARTS(YEAR(E.EnrollmentDate),MONTH(E.EnrollmentDate),1),@State=E.Status,
   @Full=CASE WHEN @ConcessionLabel IN(N'Free',N'Waived') THEN 1 ELSE COALESCE(@FullFeeWaiver,CASE WHEN @ConcessionLabel=N'Scholarship' AND @DiscountAmount=A.Amount THEN 1 ELSE 0 END) END
  FROM dbo.StudentFeeAssignments A WITH(UPDLOCK,HOLDLOCK) JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=A.EnrollmentId
  JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
  WHERE A.StudentFeeAssignmentId=@StudentFeeAssignmentId AND (A.CancelledAt IS NULL OR @UpdateAgreement=0) AND E.Status IN(N'Active',N'Pending',N'Promoted',N'Left',N'Cancelled');
  IF @Enrollment IS NULL THROW 53102,'Choose an existing fee agreement or issued bill.',1;
  IF @State NOT IN(N'Active',N'Pending') AND @UpdateAgreement=1 THROW 53103,'Closed-placement agreements stay historical. Choose issued bills only to correct an amount owed.',1;
  IF @Legacy=1 AND @Frequency<>N'Monthly' THROW 53103,'Review the annual or one-time bill explicitly before changing its terms.',1;
  SET @From=COALESCE(@EffectiveFrom,DATEADD(month,1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)));
  SET @Through=COALESCE(@EffectiveThrough,@YearEnd);
  IF DAY(@From)<>1 OR DAY(@Through)<>1 OR @From<@YearStart OR @From<@Start OR @Through>@YearEnd OR @From>@Through
   THROW 53103,'Choose first-of-month concession dates within this placement and academic year.',1;
  DECLARE @CreditResource nvarchar(255)=CONCAT(N'FMSC.StudentCredit.',@Admission);
  EXEC @Lock=sys.sp_getapplock @Resource=@CreditResource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54901,'Student credit is busy. Try again.',1;
  SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Fee;
  SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission;
  SELECT @Locked=COUNT(*) FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission;
  SELECT @Locked=COUNT(*) FROM dbo.FeePaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission;
  SELECT @Locked=COUNT(*) FROM dbo.StudentCreditEntries WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission;
  -- Capture the complete agreed schedule before splitting it. Values outside
  -- the chosen period, including scheduled rate changes, are copied unchanged.
  SELECT A.*,CASE WHEN A.EffectiveFrom>@From THEN A.EffectiveFrom ELSE @From END PeriodFrom,
   CASE WHEN A.EffectiveTo<@Through THEN A.EffectiveTo ELSE @Through END PeriodThrough
  INTO #Periods FROM dbo.StudentFeeAssignments A
  WHERE A.EnrollmentId=@Enrollment AND A.FeeTypeId=@Fee AND (A.CancelledAt IS NULL OR @UpdateAgreement=0)
   AND (@Frequency=N'Monthly' OR A.StudentFeeAssignmentId=@StudentFeeAssignmentId)
   AND ISNULL(A.EffectiveFrom,'19000101')<=@Through AND ISNULL(A.EffectiveTo,'99991231')>=@From;
  IF @UpdateAgreement=1 AND NOT EXISTS(SELECT 1 FROM #Periods) THROW 53103,'No fee agreement covers this period. Add or restore the fee assignment first.',1;
  SELECT B.BillingChargeId,COALESCE(B.BillingMonth,DATEFROMPARTS(YEAR(B.ChargeDate),MONTH(B.ChargeDate),1)) BillingMonth,F.FeeTypeName,B.ChargeDate,G.GrossAmount,COALESCE(C.NewGross,B.GrossAmount) OldGross,COALESCE(C.NewDiscount,B.DiscountAmount) OldDiscount,
   X.OldNet,X.Balance,
   CASE WHEN @Full=1 OR @DiscountAmount>G.GrossAmount THEN G.GrossAmount ELSE @DiscountAmount END NewDiscount,
   CONVERT(decimal(12,2),0) Difference,CONVERT(decimal(12,2),0) ReleasedPaidAmount
  INTO #Bills FROM dbo.BillingCharges B JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
  OUTER APPLY(SELECT TOP(1) C.NewGross,C.NewDiscount FROM dbo.FeeBillCorrections C WHERE C.BillingChargeId=B.BillingChargeId ORDER BY C.FeeBillCorrectionId DESC) C
  CROSS APPLY(SELECT COALESCE(@NewAmount,C.NewGross,B.GrossAmount) GrossAmount) G
  CROSS APPLY(SELECT SUM(Q.NetAmount) OldNet,SUM(dbo.GetFeeChargeBalance(Q.BillingChargeId)) Balance FROM dbo.BillingCharges Q
   WHERE (Q.BillingChargeId=B.BillingChargeId OR Q.CorrectionOfBillingChargeId=B.BillingChargeId) AND Q.Status<>N'Cancelled') X
  WHERE B.EnrollmentId=@Enrollment AND B.FeeTypeId=@Fee AND B.CorrectionOfBillingChargeId IS NULL
   AND ((@Frequency=N'Monthly' AND B.ChargeKind=N'Monthly') OR (@Frequency<>N'Monthly' AND B.StudentFeeAssignmentId=@StudentFeeAssignmentId))
   AND (@BillingChargeId IS NULL OR B.BillingChargeId=@BillingChargeId)
   AND B.Status<>N'Cancelled' AND COALESCE(B.BillingMonth,DATEFROMPARTS(YEAR(B.ChargeDate),MONTH(B.ChargeDate),1)) BETWEEN @From AND @Through;
  IF @BillingChargeId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM #Bills) THROW 53103,'This bill is no longer eligible or is outside the chosen period. Reload it before reviewing.',1;
  UPDATE #Bills SET Difference=GrossAmount-NewDiscount-OldNet;
  UPDATE #Bills SET ReleasedPaidAmount=CASE WHEN -Difference>Balance THEN -Difference-Balance ELSE 0 END;
  IF @Legacy=1 AND EXISTS(SELECT 1 FROM #Bills) THROW 53104,'Issued bills exist. Open Fee Discount and review whether they should change.',1;
  SET @Raw=CONCAT((SELECT @UserId Actor,@StudentFeeAssignmentId Assignment,@DiscountAmount Discount,@ConcessionLabel Label,@From [From],@Through ThroughMonth,
    @ApplyToBills ApplyToBills,@PaidReductionAction PaidReductionAction,@Reason Reason,@NewAmount NewAmount,@UpdateAgreement UpdateAgreement,@BillingChargeId Bill,@Full FullFeeWaiver,@ApplyAvailableCredit ApplyAvailableCredit,CONVERT(date,GETDATE()) ReviewDate FOR JSON PATH,INCLUDE_NULL_VALUES),
   (SELECT * FROM dbo.Enrollments WHERE EnrollmentId=@Enrollment FOR JSON PATH),
   (SELECT * FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Fee ORDER BY StudentFeeAssignmentId FOR JSON PATH),
   (SELECT * FROM dbo.BillingCharges WHERE AdmissionId=@Admission ORDER BY BillingChargeId FOR JSON PATH),
   (SELECT * FROM dbo.FeePayments WHERE AdmissionId=@Admission ORDER BY FeePaymentId FOR JSON PATH),
   (SELECT * FROM dbo.FeePaymentAllocations WHERE AdmissionId=@Admission ORDER BY FeePaymentAllocationId FOR JSON PATH),
   (SELECT * FROM dbo.StudentCreditEntries WHERE AdmissionId=@Admission ORDER BY CreditEntryId FOR JSON PATH),
   (SELECT C.* FROM dbo.FeeBillCorrections C JOIN dbo.BillingCharges B ON B.BillingChargeId=C.BillingChargeId WHERE B.AdmissionId=@Admission ORDER BY C.FeeBillCorrectionId FOR JSON PATH));
  SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
  DECLARE @Difference decimal(12,2)=CASE WHEN @ApplyToBills=1 THEN COALESCE((SELECT SUM(Difference) FROM #Bills),0) ELSE 0 END,
   @Released decimal(12,2)=CASE WHEN @ApplyToBills=1 THEN COALESCE((SELECT SUM(ReleasedPaidAmount) FROM #Bills),0) ELSE 0 END;
  DECLARE @CurrentCredit decimal(12,2)=COALESCE((SELECT SUM(Amount) FROM dbo.StudentCreditEntries WHERE AdmissionId=@Admission),0),
   @CreditToUse decimal(12,2)=0,@EligibleDue decimal(12,2),@AvailableCredit decimal(12,2);
  SET @AvailableCredit=@CurrentCredit+CASE WHEN @PaidReductionAction=N'Credit' THEN @Released ELSE 0 END;
  SELECT @EligibleDue=COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) FROM dbo.BillingCharges WHERE AdmissionId=@Admission AND Status<>N'Cancelled' AND ChargeDate<=CONVERT(date,GETDATE());
  IF @ApplyToBills=1 SELECT @EligibleDue=@EligibleDue+COALESCE(SUM(CASE WHEN Difference>=0 THEN Difference WHEN -Difference>Balance THEN -Balance ELSE Difference END),0) FROM #Bills WHERE ChargeDate<=CONVERT(date,GETDATE());
  IF @ApplyToBills=1 AND @ApplyAvailableCredit=1 SET @CreditToUse=CASE WHEN @AvailableCredit>@EligibleDue THEN @EligibleDue ELSE @AvailableCredit END;
  IF @Preview=1
  BEGIN
   SELECT @Snapshot Snapshot,@StudentFeeAssignmentId StudentFeeAssignmentId,@Admission AdmissionId,@Enrollment EnrollmentId,
    @From EffectiveFrom,@Through EffectiveThrough,@Difference Difference,@Released ReleasedPaidAmount,
    (SELECT COUNT(*) FROM #Bills) IssuedBills,@ApplyToBills ApplyToBills,@CurrentCredit CurrentCredit,@CreditToUse CreditToUse,@AvailableCredit-@CreditToUse RemainingCredit;
   SELECT BillingMonth [Month],FeeTypeName Fee,GrossAmount [Agreed Fee],OldNet [Current Bill],CASE WHEN @ApplyToBills=1 THEN GrossAmount-NewDiscount ELSE OldNet END [Proposed Bill],
    CASE WHEN @ApplyToBills=1 THEN Difference ELSE 0 END [Bill Change],CASE WHEN @ApplyToBills=1 THEN ReleasedPaidAmount ELSE 0 END [Paid Amount Released]
   FROM #Bills ORDER BY BillingMonth;
   SELECT PeriodFrom [From],PeriodThrough [Through],Amount [Old Fee],CASE WHEN @UpdateAgreement=1 THEN COALESCE(@NewAmount,Amount) ELSE Amount END [New Fee],DiscountAmount [Old Concession],
    CASE WHEN @UpdateAgreement=0 THEN DiscountAmount WHEN @Full=1 OR @DiscountAmount>COALESCE(@NewAmount,Amount) THEN COALESCE(@NewAmount,Amount) ELSE @DiscountAmount END [New Concession] FROM #Periods ORDER BY PeriodFrom;
   COMMIT;RETURN;
  END;
  IF @Legacy=0 AND (@ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot) THROW 53104,'Fees or payments changed after review. Review the concession again; nothing was saved.',1;
  IF @UpdateAgreement=1 AND @Frequency=N'Monthly'
  BEGIN
  DECLARE @Id int,@L date,@R date,@OldFrom date,@OldTo date,@Amount decimal(12,2),@OldDiscount decimal(12,2),@OldLabel nvarchar(20),@Remarks nvarchar(500);
  DECLARE Periods CURSOR LOCAL FAST_FORWARD FOR SELECT StudentFeeAssignmentId,PeriodFrom,PeriodThrough,EffectiveFrom,EffectiveTo,Amount,DiscountAmount,ConcessionLabel,Remarks FROM #Periods ORDER BY PeriodFrom;
  OPEN Periods;FETCH NEXT FROM Periods INTO @Id,@L,@R,@OldFrom,@OldTo,@Amount,@OldDiscount,@OldLabel,@Remarks;
  WHILE @@FETCH_STATUS=0
  BEGIN
   DECLARE @PeriodAmount decimal(12,2)=COALESCE(@NewAmount,@Amount);
   IF @OldFrom IS NULL OR @OldFrom<@L
   BEGIN
    UPDATE dbo.StudentFeeAssignments SET EffectiveTo=DATEADD(month,-1,@L),UpdatedAt=SYSDATETIME() WHERE StudentFeeAssignmentId=@Id;
    INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,Remarks,EffectiveFrom,EffectiveTo,ConcessionLabel)
    VALUES(@Enrollment,@Admission,@Fee,@PeriodAmount,CASE WHEN @Full=1 OR @DiscountAmount>@PeriodAmount THEN @PeriodAmount ELSE @DiscountAmount END,N'Monthly',@Reason,@L,CASE WHEN @R=@YearEnd AND @OldTo IS NULL THEN NULL ELSE @R END,@ConcessionLabel);
   END
   ELSE UPDATE dbo.StudentFeeAssignments SET EffectiveFrom=@L,EffectiveTo=CASE WHEN @R=@YearEnd AND @OldTo IS NULL THEN NULL ELSE @R END,
    Amount=@PeriodAmount,DiscountAmount=CASE WHEN @Full=1 OR @DiscountAmount>@PeriodAmount THEN @PeriodAmount ELSE @DiscountAmount END,ConcessionLabel=@ConcessionLabel,Remarks=@Reason,UpdatedAt=SYSDATETIME() WHERE StudentFeeAssignmentId=@Id;
   IF @R<@YearEnd AND (@OldTo IS NULL OR @OldTo>@R)
    INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,Remarks,EffectiveFrom,EffectiveTo,ConcessionLabel)
    VALUES(@Enrollment,@Admission,@Fee,@Amount,@OldDiscount,N'Monthly',@Remarks,DATEADD(month,1,@R),@OldTo,@OldLabel);
   FETCH NEXT FROM Periods INTO @Id,@L,@R,@OldFrom,@OldTo,@Amount,@OldDiscount,@OldLabel,@Remarks;
  END;
  CLOSE Periods;DEALLOCATE Periods;
  END;
  IF @UpdateAgreement=1 AND @Frequency<>N'Monthly'
   UPDATE dbo.StudentFeeAssignments SET Amount=COALESCE(@NewAmount,Amount),
    DiscountAmount=CASE WHEN @Full=1 OR @DiscountAmount>COALESCE(@NewAmount,Amount) THEN COALESCE(@NewAmount,Amount) ELSE @DiscountAmount END,
    ConcessionLabel=@ConcessionLabel,Remarks=@Reason,UpdatedAt=SYSDATETIME() WHERE StudentFeeAssignmentId=@StudentFeeAssignmentId;
  IF @ApplyToBills=1
  BEGIN
   DECLARE @Bill bigint,@Gross decimal(12,2),@NewDiscount decimal(12,2),@OldNet decimal(12,2),@Balance decimal(12,2),@Request uniqueidentifier;
   DECLARE @Result TABLE(BillingChargeId bigint,Difference decimal(12,2),ReleasedPaidAmount decimal(12,2));
   DECLARE Bills CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId,GrossAmount,NewDiscount,OldNet,Balance FROM #Bills WHERE Difference<>0 OR GrossAmount<>OldGross OR NewDiscount<>OldDiscount ORDER BY CASE WHEN Difference<0 THEN 0 ELSE 1 END,BillingMonth,BillingChargeId;
   OPEN Bills;FETCH NEXT FROM Bills INTO @Bill,@Gross,@NewDiscount,@OldNet,@Balance;
   WHILE @@FETCH_STATUS=0
   BEGIN
    SET @Request=NEWID();
    -- The complete preview is locked; use the balance after our own earlier
    -- corrections. Explicit credit application runs once after all corrections.
    SELECT @Balance=SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges
     WHERE (BillingChargeId=@Bill OR CorrectionOfBillingChargeId=@Bill) AND Status<>N'Cancelled';
    INSERT @Result EXEC dbo.AdjustBillingChargeRate @UserId,@Bill,@Gross,@PaidReductionAction,@Reason,@Request,@OldNet,@Balance,@NewDiscount,0;
    FETCH NEXT FROM Bills INTO @Bill,@Gross,@NewDiscount,@OldNet,@Balance;
   END;
   CLOSE Bills;DEALLOCATE Bills;
   IF @ApplyAvailableCredit=1 EXEC dbo.ApplyStudentCredit_Internal @Admission,@UserId;
  END;
  COMMIT;
  SELECT @StudentFeeAssignmentId StudentFeeAssignmentId,@Admission AdmissionId,@Enrollment EnrollmentId,@From EffectiveFrom,@Through EffectiveThrough,
   @Difference Difference,@Released ReleasedPaidAmount;
 END TRY
 BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.AdjustBillingChargeRate
 @UserId int,@BillingChargeId bigint,@NewAmount decimal(12,2),@PaidReductionAction nvarchar(10),
 @Reason nvarchar(500),@RequestId uniqueidentifier,@ExpectedNet decimal(12,2)=NULL,@ExpectedBalance decimal(12,2)=NULL,@NewDiscount decimal(12,2)=NULL,@ApplyAvailableCredit bit=1
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequireAnyPermission @UserId,N'Fees.Adjust',N'Fees.Configuration.Manage';
 IF (@NewDiscount IS NOT NULL AND (@NewDiscount<0 OR @NewDiscount>@NewAmount OR @NewDiscount<>FLOOR(@NewDiscount)))
  THROW 54920,'Choose a whole-rupee concession no greater than the bill rate.',1;
 IF @NewAmount IS NULL OR @NewAmount<0 OR @NewAmount<>FLOOR(@NewAmount) OR @RequestId IS NULL
  OR @PaidReductionAction IS NULL OR @PaidReductionAction NOT IN(N'Credit',N'Refund') OR NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL
  THROW 54920,'Choose a valid new rate, credit/refund choice and reason.',1;
 DECLARE @Lock int,@Admission int,@Enrollment int,@Fee int,@Assignment int,@Month date,@BaseChargeDate date,@Discount decimal(12,2),@PreviousGross decimal(12,2),@Concession nvarchar(20),
  @OldNet decimal(12,2),@Balance decimal(12,2),@NewNet decimal(12,2),@Difference decimal(12,2),@Released decimal(12,2)=0;
 DECLARE @PreviousCorrection sql_variant=SESSION_CONTEXT(N'FMSC.BillCorrection');
 DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
  BEGIN TRAN;
  EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54745,'Billing is running. Try again shortly.',1;
  IF EXISTS(SELECT 1 FROM dbo.FeeBillCorrections WHERE RequestId=@RequestId)
  BEGIN
   IF NOT EXISTS(SELECT 1 FROM dbo.FeeBillCorrections WHERE RequestId=@RequestId AND BillingChargeId=@BillingChargeId
    AND NewGross=@NewAmount AND PaidReductionAction=@PaidReductionAction AND (@NewDiscount IS NULL OR NewDiscount=@NewDiscount)) THROW 54921,'The correction request belongs to another change.',1;
   COMMIT;
   SELECT BillingChargeId,Difference,ReleasedPaidAmount FROM dbo.FeeBillCorrections WHERE RequestId=@RequestId;RETURN;
  END;
  SELECT @Admission=B.AdmissionId,@Enrollment=B.EnrollmentId,@Fee=B.FeeTypeId,@Assignment=B.StudentFeeAssignmentId,
   @Month=B.BillingMonth,@BaseChargeDate=B.ChargeDate,@Discount=COALESCE(C.NewDiscount,B.DiscountAmount),@PreviousGross=COALESCE(C.NewGross,B.GrossAmount),@Concession=A.ConcessionLabel
  FROM dbo.BillingCharges B WITH(UPDLOCK,HOLDLOCK) JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=B.EnrollmentId
  LEFT JOIN dbo.StudentFeeAssignments A ON A.StudentFeeAssignmentId=B.StudentFeeAssignmentId
  OUTER APPLY(SELECT TOP(1) NewDiscount,NewGross FROM dbo.FeeBillCorrections WHERE BillingChargeId=B.BillingChargeId ORDER BY FeeBillCorrectionId DESC) C
  WHERE B.BillingChargeId=@BillingChargeId AND B.CorrectionOfBillingChargeId IS NULL AND (B.ChargeKind=N'Monthly' OR @NewDiscount IS NOT NULL)
   AND B.Status<>N'Cancelled' AND (E.Status IN(N'Active',N'Pending') OR (@NewDiscount IS NOT NULL AND E.Status IN(N'Promoted',N'Left',N'Cancelled')));
  IF @Admission IS NULL THROW 54922,'Choose a non-reversed bill; use the explicit bill-terms review for a closed placement.',1;
  DECLARE @Resource nvarchar(255)=CONCAT(N'FMSC.StudentCredit.',@Admission);
  EXEC @Lock=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54901,'Student credit is busy. Try again.',1;
  SELECT @OldNet=SUM(NetAmount),@Balance=SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK)
   WHERE (BillingChargeId=@BillingChargeId OR CorrectionOfBillingChargeId=@BillingChargeId) AND Status<>N'Cancelled';
  IF (@ExpectedNet IS NOT NULL AND @ExpectedNet<>@OldNet) OR (@ExpectedBalance IS NOT NULL AND @ExpectedBalance<>@Balance)
   THROW 54923,'A bill or payment changed after preview. Refresh the review; nothing was changed.',1;
  SET @Discount=CASE WHEN @NewDiscount IS NOT NULL THEN @NewDiscount WHEN @Concession IN(N'Free',N'Waived') OR (@Concession=N'Scholarship' AND @Discount=@PreviousGross) THEN @NewAmount WHEN @Discount>@NewAmount THEN @NewAmount ELSE @Discount END;
  SET @NewNet=@NewAmount-@Discount;SET @Difference=@NewNet-@OldNet;
  -- The scoped request authorizes only this bill's correction chain. It is an
  -- operational financial record; it is rolled back with every affected row.
  INSERT dbo.FeeBillCorrections(BillingChargeId,RequestId,PreviousNet,NewGross,NewDiscount,Difference,ReleasedPaidAmount,PaidReductionAction,Reason,CreatedByUserId)
   VALUES(@BillingChargeId,@RequestId,@OldNet,@NewAmount,@Discount,@Difference,@Released,@PaidReductionAction,@Reason,@UserId);
  EXEC sys.sp_set_session_context @key=N'FMSC.BillCorrection',@value=@RequestId;
  IF @Difference>0
  BEGIN
   INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,
    GrossAmount,DiscountAmount,ChargeKind,Remarks,CorrectionOfBillingChargeId)
   VALUES(@Assignment,@Enrollment,@Admission,@Fee,@Month,CASE WHEN @BaseChargeDate>CONVERT(date,GETDATE()) THEN @BaseChargeDate ELSE CONVERT(date,GETDATE()) END,CASE WHEN @BaseChargeDate>CONVERT(date,GETDATE()) THEN @BaseChargeDate ELSE CONVERT(date,GETDATE()) END,@Difference,0,N'Manual',
    LEFT(CONCAT(N'Rate difference for ',CONVERT(nvarchar(7),@Month,120),N': ',@Reason),500),@BillingChargeId);
   IF @ApplyAvailableCredit=1 EXEC dbo.ApplyStudentCredit_Internal @Admission,@UserId;
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
  UPDATE dbo.FeeBillCorrections SET ReleasedPaidAmount=@Released WHERE RequestId=@RequestId;
  EXEC sys.sp_set_session_context @key=N'FMSC.BillCorrection',@value=@PreviousCorrection;
  COMMIT;
  SELECT @BillingChargeId BillingChargeId,@Difference Difference,@Released ReleasedPaidAmount;
 END TRY
 BEGIN CATCH
  BEGIN TRY EXEC sys.sp_set_session_context @key=N'FMSC.BillCorrection',@value=@PreviousCorrection; END TRY BEGIN CATCH END CATCH;
  IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK; THROW;
 END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.CloseStudentAdmission @UserId int,@AdmissionId int,@LeavingDate date,@Reason nvarchar(500),
 @Cancel bit=0,@Preview bit=1,@ExpectedSnapshot varchar(64)=NULL,@ReverseFutureUnpaid bit=0,@PaidReductionAction nvarchar(10)=N'Credit',@MonthlyFeePolicy nvarchar(20)=N'SchoolCutoff',@ReverseFromMonth date=NULL
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 59710,'Enter the reason for leaving or cancellation.',1;
 IF @LeavingDate IS NULL OR @LeavingDate>CONVERT(date,GETDATE()) THROW 59711,'Choose a leaving date that is not in the future.',1;
 IF @MonthlyFeePolicy IS NULL OR @MonthlyFeePolicy NOT IN(N'SchoolCutoff',N'KeepIssued',N'FromMonth') THROW 59721,'Choose how existing monthly fees should be handled.',1;
 DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @State nvarchar(30),@Enrollment int,@Start date,@Snapshot varchar(64),@Raw nvarchar(max),@Lock int;
 SELECT @State=Status FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Enrollment=EnrollmentId,@Start=EnrollmentDate FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status IN(N'Active',N'Pending');
 IF @Enrollment IS NULL OR @State NOT IN(N'Active',N'Pending') THROW 59712,'This record has no open placement to close.',1;
 IF @Cancel=1 AND (@State<>N'Pending' OR EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND AdmissionNumber IS NOT NULL))
  THROW 59720,'An officially admitted student cannot be cancelled. Use Mark as Left for departure, or correct details and fees in their respective workspaces.',1;
 IF @State=N'Pending' AND @Cancel=0 THROW 59713,'Use Cancel Registration for a pending registration.',1;
 IF @LeavingDate<@Start THROW 59714,'Leaving date cannot be before the placement start.',1;
 SELECT @Lock=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Lock=COUNT(*) FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Lock=COUNT(*) FROM dbo.FeePaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF EXISTS(SELECT 1 FROM dbo.LeaveCertificates WHERE EnrollmentId=@Enrollment) THROW 59715,'This placement already has a leaving certificate. Review its recorded departure in Admission.',1;
 IF @Cancel=1 AND EXISTS(SELECT 1 FROM dbo.FeePaymentAllocations A JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
    WHERE A.AdmissionId=@AdmissionId AND P.Status=N'Completed' GROUP BY A.BillingChargeId HAVING SUM(A.Amount)>0)
  THROW 59716,'Payments exist. Review refunds or reversals in Fee Corrections before cancellation; history must be retained. For an admitted student who attended, use Mark as Left.',1;
 IF @Cancel=1 AND (EXISTS(SELECT 1 FROM dbo.SportsCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.RegularStudentCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.NoObjectionCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.DateOfBirthCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.ProvisionalCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.CharacterCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.LeaveCertificates WHERE AdmissionId=@AdmissionId) OR
   EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId AND EnrollmentId<>@Enrollment))
  THROW 59717,'This admission has academic or certificate history. Use Mark as Left rather than cancellation.',1;
 DECLARE @MonthlyBoundary date=dbo.GetMonthlyBillingStartDate(@LeavingDate);
 SET @MonthlyBoundary=DATEFROMPARTS(YEAR(@MonthlyBoundary),MONTH(@MonthlyBoundary),1);
 IF @MonthlyFeePolicy=N'KeepIssued' SET @MonthlyBoundary='99991201';
 IF @MonthlyFeePolicy=N'FromMonth'
 BEGIN
  IF @ReverseFromMonth IS NULL OR DAY(@ReverseFromMonth)<>1 OR @ReverseFromMonth<DATEFROMPARTS(YEAR(@Start),MONTH(@Start),1)
   OR @ReverseFromMonth>DATEADD(month,1,DATEFROMPARTS(YEAR(@LeavingDate),MONTH(@LeavingDate),1))
   THROW 59721,'Choose a first month to waive, from the placement start through the month after leaving.',1;
  SET @MonthlyBoundary=@ReverseFromMonth;
 END;
 DECLARE @FutureCount int,@FutureAmount decimal(12,2),@FuturePaid int,@Blocked int;
 SELECT B.BillingChargeId,B.BillingMonth,X.NetAmount,X.Balance INTO #ClosureBills
 FROM dbo.BillingCharges B CROSS APPLY(SELECT SUM(Q.NetAmount) NetAmount,SUM(dbo.GetFeeChargeBalance(Q.BillingChargeId)) Balance
  FROM dbo.BillingCharges Q WHERE (Q.BillingChargeId=B.BillingChargeId OR Q.CorrectionOfBillingChargeId=B.BillingChargeId) AND Q.Status<>N'Cancelled') X
 WHERE B.EnrollmentId=@Enrollment AND B.Status<>N'Cancelled' AND B.ChargeKind=N'Monthly'
  AND B.CorrectionOfBillingChargeId IS NULL AND B.BillingMonth>=@MonthlyBoundary AND X.NetAmount>0;
 SELECT @FutureCount=COUNT(*),@FutureAmount=COALESCE(SUM(Balance),0),
  @FuturePaid=COALESCE(SUM(CASE WHEN NetAmount>Balance THEN 1 ELSE 0 END),0) FROM #ClosureBills;
 IF @Cancel=0 AND @FutureCount>0 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 SET @Blocked=CASE WHEN @Cancel=0 AND (@PaidReductionAction IS NULL OR @PaidReductionAction NOT IN(N'Credit',N'Refund')) THEN 1 ELSE 0 END;
 DECLARE @PaidReturnAmount decimal(12,2)=(SELECT COALESCE(SUM(NetAmount-Balance),0) FROM #ClosureBills);
 SET @Raw=CONCAT(@UserId,CONVERT(varchar(10),@LeavingDate,120),@Reason,CONVERT(char(1),@Cancel),@MonthlyFeePolicy,CONVERT(varchar(10),@MonthlyBoundary,120),@PaidReductionAction,(SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.FeePayments WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.FeePaymentAllocations WHERE AdmissionId=@AdmissionId FOR JSON PATH));
 SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
 IF @Preview=1
 BEGIN
  SELECT @Snapshot Snapshot,@State CurrentStatus,@Cancel CancelAdmission,@Blocked BlockedCount,@FutureCount FutureCount,@FutureAmount FutureAmount,@FuturePaid FuturePaid,COUNT(*) ChargeCount,@PaidReturnAmount PaidReturnAmount,@MonthlyFeePolicy MonthlyFeePolicy,@MonthlyBoundary ReverseFromMonth,
   COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) Outstanding FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND Status<>N'Cancelled';
  COMMIT;RETURN;
 END;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59719,'The record changed. Review the closure again.',1;
 IF @Blocked>0 THROW 59718,'Review charges after the leaving month. Choose reversal for unpaid future charges, or resolve paid charges in Fee Corrections first.',1;
 IF @Cancel=1
 BEGIN
  EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
  DECLARE @Charge bigint; DECLARE @Reverse TABLE(BillingChargeId bigint,AdmissionId int,FeeTypeName nvarchar(100),BillingMonth date,AmountReversed decimal(12,2),Reason nvarchar(500),Status nvarchar(20));
  DECLARE C CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND Status<>N'Cancelled'
   AND (@Cancel=1 OR COALESCE(BillingMonth,ChargeDate)>DATEFROMPARTS(YEAR(@LeavingDate),MONTH(@LeavingDate),1));
  OPEN C;FETCH NEXT FROM C INTO @Charge;
  WHILE @@FETCH_STATUS=0 BEGIN INSERT @Reverse EXEC dbo.ReverseBillingCharge @UserId,@Charge,@Reason;FETCH NEXT FROM C INTO @Charge;END;
  CLOSE C;DEALLOCATE C;
 END;
 IF @Cancel=0 AND @FutureCount>0
 BEGIN
  DECLARE @MonthlyCharge bigint,@CorrectionRequest uniqueidentifier;
  DECLARE @MonthlyResult TABLE(BillingChargeId bigint,Difference decimal(12,2),ReleasedPaidAmount decimal(12,2));
  DECLARE MonthlyCharges CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId FROM #ClosureBills ORDER BY BillingChargeId;
  OPEN MonthlyCharges;FETCH NEXT FROM MonthlyCharges INTO @MonthlyCharge;
  WHILE @@FETCH_STATUS=0 BEGIN
   SET @CorrectionRequest=NEWID();
   INSERT @MonthlyResult EXEC dbo.AdjustBillingChargeRate @UserId,@MonthlyCharge,0,@PaidReductionAction,@Reason,@CorrectionRequest;
   FETCH NEXT FROM MonthlyCharges INTO @MonthlyCharge;
  END;
  CLOSE MonthlyCharges;DEALLOCATE MonthlyCharges;
 END;
 DECLARE @NewStatus nvarchar(30)=CASE WHEN @Cancel=1 THEN N'Cancelled' ELSE N'Left' END;
 UPDATE dbo.Enrollments SET Status=@NewStatus,LeavingDate=@LeavingDate,BillingReady=0,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@Enrollment;
 UPDATE dbo.Students SET Status=@NewStatus,WithdrawalDate=@LeavingDate,UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId;
 INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,OldStatus,NewStatus,Remarks,UserId) VALUES(@Enrollment,@AdmissionId,N'STATUS_CHANGE',@LeavingDate,@State,@NewStatus,@Reason,@UserId);
 INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,CreatedBy) VALUES(@AdmissionId,@State,@NewStatus,@Reason,@LeavingDate,@UserId);

 COMMIT;
 END TRY
 BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;

GO
CREATE OR ALTER PROCEDURE dbo.AdmitStudent
@UserId INT, @AdmissionDate DATE, @AdmissionCampusId INT, @AcademicYearId INT, @ClassSectionId INT, @StudentName NVARCHAR (500), @FatherName NVARCHAR (500), @DateOfBirth DATE, @PlaceOfBirth NVARCHAR (500), @Gender NVARCHAR (20), @BFormNumber NVARCHAR (50), @Religion NVARCHAR (50), @BloodGroup NVARCHAR (10), @ContactNumber NVARCHAR (30), @AlternateContactNumber NVARCHAR (30), @Address NVARCHAR (500), @PreviousSchool NVARCHAR (500), @FatherOccupation NVARCHAR (250), @Disability NVARCHAR (3), @UsesTransport BIT, @SelectedFeeTypeIds dbo.FeeTypeIdList READONLY, @BiseEnrolmentNumber NVARCHAR (50)=NULL, @CasteTribe NVARCHAR (100)=NULL, @AdmissionDiscounts dbo.AdmissionFeeDiscountType READONLY, @AdmissionNumber VARCHAR (40)=NULL, @QuickRegistration bit=0, @FamilyStudentId int=NULL, @FeeConcessionsJson nvarchar(max)=NULL,@MonthlyDiscountFrom date=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Admit';
    IF @FeeConcessionsJson IS NOT NULL AND ISJSON(@FeeConcessionsJson)<>1
      THROW 51024,'Review the selected fee concessions.',1;
    DECLARE @Concessions TABLE(FeeTypeId int NOT NULL PRIMARY KEY,ConcessionLabel nvarchar(20) NOT NULL);
    IF EXISTS(SELECT 1 FROM OPENJSON(@FeeConcessionsJson) WITH(FeeTypeId int,ConcessionLabel nvarchar(20))
      WHERE FeeTypeId IS NULL OR ConcessionLabel IS NULL OR ConcessionLabel NOT IN(N'Free',N'Scholarship',N'Waived'))
      THROW 51024,'Choose a valid fee and concession.',1;
    INSERT @Concessions SELECT FeeTypeId,ConcessionLabel FROM OPENJSON(@FeeConcessionsJson)
      WITH(FeeTypeId int,ConcessionLabel nvarchar(20));
    IF EXISTS(SELECT 1 FROM @Concessions) EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
    DECLARE @ReviewedDiscounts dbo.AdmissionFeeDiscountType;
    INSERT @ReviewedDiscounts SELECT * FROM @AdmissionDiscounts;
    IF EXISTS (SELECT 1
               FROM   @ReviewedDiscounts)
        EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
    IF EXISTS (SELECT 1
               FROM   @ReviewedDiscounts
               WHERE  DiscountAmount < 0)
        THROW 51023, 'Fee discount cannot be negative.', 1;
    IF @AdmissionDate > CAST (GETDATE() AS DATE)
        THROW 51010, 'Admission date cannot be in the future.', 1;
    IF NULLIF (LTRIM(RTRIM(@StudentName)), N'') IS NULL
        THROW 51011, 'Student name is required.', 1;
    IF LEN(LTRIM(RTRIM(@StudentName))) > 500
        THROW 51021, 'Student name cannot be longer than 500 characters.', 1;
    IF @DateOfBirth IS NOT NULL
       AND @DateOfBirth > @AdmissionDate
        THROW 51012, 'Date of birth cannot be later than the admission date.', 1;
    IF @Disability NOT IN (N'Yes', N'No')
        THROW 51013, 'Disability must be Yes or No.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Campuses
                   WHERE  CampusId = @AdmissionCampusId)
        THROW 51014, 'The selected campus is invalid.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.AcademicYears
                   WHERE  AcademicYearId = @AcademicYearId)
        THROW 51015, 'The selected academic year is invalid.', 1;
    DECLARE @ContextCampusId AS INT, @ClassId AS INT;
    SELECT @ContextCampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @ClassSectionId;
    IF @ContextCampusId IS NULL
        THROW 51017, 'The selected class/section placement does not exist or is inactive.', 1;
    IF @ContextCampusId <> @AdmissionCampusId
        THROW 51018, 'The selected class/section belongs to a different campus.', 1;
    DECLARE @YearStartDate AS DATE, @YearEndDate AS DATE;
    SELECT @YearStartDate = StartDate,
           @YearEndDate = EndDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @AcademicYearId;
    IF @AdmissionDate < @YearStartDate
       OR @AdmissionDate > @YearEndDate
        THROW 51022, 'The admission date must fall within the selected academic year''s date range.', 1;
    DECLARE @MissingMandatoryFees AS NVARCHAR (1000);
    SELECT @MissingMandatoryFees = STRING_AGG(FT.FeeTypeName, N', ')
    FROM   dbo.FeeTypes AS FT
    WHERE  FT.IsMandatory = 1
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.FeeConfigurations AS FC
                           WHERE  FC.CampusId = @AdmissionCampusId
                                  AND FC.ClassId = @ClassId
                                  AND FC.FeeTypeId = FT.FeeTypeId);
    IF @MissingMandatoryFees IS NOT NULL
        BEGIN
            DECLARE @MandatoryFeeErrorMsg AS NVARCHAR (1100) = N'Required fee configuration is missing for the selected class: ' + @MissingMandatoryFees + N'.';
            THROW 51019, @MandatoryFeeErrorMsg, 1;
        END
    DECLARE @BillingStartDate AS DATE;
    SET @BillingStartDate = dbo.GetMonthlyBillingStartDate(@AdmissionDate);
    BEGIN TRANSACTION;
    IF @QuickRegistration=0 EXECUTE dbo.ReserveAdmissionNumber @AdmissionNumber = @AdmissionNumber OUTPUT; ELSE SET @AdmissionNumber=NULL;
    DECLARE @RegistrationNumber AS NVARCHAR (40);
    IF @QuickRegistration=0 EXECUTE dbo.GetNextRegistrationNumber @RegistrationDate = @AdmissionDate, @RegistrationNumber = @RegistrationNumber OUTPUT;
    INSERT  dbo.Students (AdmissionNumber, AdmissionDate, AdmissionCampusId, RegistrationNumber, StudentName, FatherName, DateOfBirth, Gender, BFormNumber, Religion, BloodGroup, ContactNumber, AlternateContactNumber, Address, PreviousSchool, PlaceOfBirth, FatherOccupation, Disability, Status, BiseEnrolmentNumber, CasteTribe)
    VALUES              (@AdmissionNumber, @AdmissionDate, @AdmissionCampusId, @RegistrationNumber, @StudentName, NULLIF (LTRIM(RTRIM(@FatherName)), N''), @DateOfBirth, NULLIF (LTRIM(RTRIM(@Gender)), N''), NULLIF (LTRIM(RTRIM(@BFormNumber)), N''), NULLIF (LTRIM(RTRIM(@Religion)), N''), NULLIF (LTRIM(RTRIM(@BloodGroup)), N''), NULLIF (LTRIM(RTRIM(@ContactNumber)), N''), NULLIF (LTRIM(RTRIM(@AlternateContactNumber)), N''), NULLIF (LTRIM(RTRIM(@Address)), N''), NULLIF (LTRIM(RTRIM(@PreviousSchool)), N''), NULLIF (LTRIM(RTRIM(@PlaceOfBirth)), N''), NULLIF (LTRIM(RTRIM(@FatherOccupation)), N''), @Disability, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, NULLIF (LTRIM(RTRIM(@BiseEnrolmentNumber)), N''), NULLIF (LTRIM(RTRIM(@CasteTribe)), N''));
    DECLARE @AdmissionId AS INT;
    SET @AdmissionId = SCOPE_IDENTITY();
    IF @QuickRegistration=1 UPDATE dbo.Students SET QuickReference=CONCAT('QR-',@AdmissionId) WHERE AdmissionId=@AdmissionId;
    INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, BillingStartDate, Status, UsesTransport, Remarks, BillingReady, PlacementConfirmedAt)
    VALUES                 (@AdmissionId, @AcademicYearId, @ClassSectionId, @AdmissionDate, @BillingStartDate, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, @UsesTransport, N'Created by admission',1,CASE WHEN @QuickRegistration=0 THEN SYSDATETIME() END);
    DECLARE @EnrollmentId AS INT;
    SET @EnrollmentId = SCOPE_IDENTITY();
    INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks)
    SELECT @EnrollmentId,
           @AdmissionId,
           FC.FeeTypeId,
           FC.Amount,
           0,
           FC.Frequency,
           N'Created by admission'
    FROM   dbo.FeeConfigurations AS FC
           INNER JOIN
           dbo.FeeTypes AS FT
           ON FT.FeeTypeId = FC.FeeTypeId
    WHERE  FC.CampusId = @AdmissionCampusId
           AND FC.ClassId = @ClassId
           AND (FT.IsMandatory = 1
                OR (FT.IsTransportLinked = 1
                    AND @UsesTransport = 1)
                OR FC.FeeTypeId IN (SELECT FeeTypeId
                                    FROM   @SelectedFeeTypeIds));
    IF EXISTS(SELECT 1 FROM @Concessions C LEFT JOIN dbo.StudentFeeAssignments A
      ON A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=C.FeeTypeId WHERE A.StudentFeeAssignmentId IS NULL OR A.Frequency<>N'Monthly')
      THROW 51024,'Concessions must refer to an assigned monthly fee.',1;
    UPDATE D SET DiscountAmount=A.Amount FROM @ReviewedDiscounts D JOIN @Concessions C ON C.FeeTypeId=D.FeeTypeId
      JOIN dbo.StudentFeeAssignments A ON A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=C.FeeTypeId
      WHERE C.ConcessionLabel IN(N'Free',N'Waived');
    INSERT @ReviewedDiscounts SELECT C.FeeTypeId,A.Amount FROM @Concessions C
      JOIN dbo.StudentFeeAssignments A ON A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=C.FeeTypeId
      WHERE C.ConcessionLabel IN(N'Free',N'Waived') AND NOT EXISTS(SELECT 1 FROM @ReviewedDiscounts D WHERE D.FeeTypeId=C.FeeTypeId);
    IF EXISTS (SELECT 1
               FROM   @ReviewedDiscounts AS D
                      LEFT OUTER JOIN
                      dbo.StudentFeeAssignments AS SFA
                      ON SFA.EnrollmentId = @EnrollmentId
                         AND SFA.FeeTypeId = D.FeeTypeId
               WHERE  SFA.StudentFeeAssignmentId IS NULL
                      OR D.DiscountAmount > SFA.Amount)
        THROW 51024, 'A fee discount is invalid or exceeds the assigned fee.', 1;
    UPDATE SFA
    SET    DiscountAmount = D.DiscountAmount,
           UpdatedAt      = SYSDATETIME()
    FROM   dbo.StudentFeeAssignments AS SFA
           INNER JOIN
           @ReviewedDiscounts AS D
           ON D.FeeTypeId = SFA.FeeTypeId
    WHERE  SFA.EnrollmentId = @EnrollmentId AND SFA.Frequency<>N'Monthly';
    -- The school chooses the first concession month; legacy callers retain next-month defaults.
    DECLARE @DiscountFrom date=COALESCE(@MonthlyDiscountFrom,DATEADD(month,1,DATEFROMPARTS(YEAR(@AdmissionDate),MONTH(@AdmissionDate),1)));
    IF DAY(@DiscountFrom)<>1 OR @DiscountFrom<DATEFROMPARTS(YEAR(@AdmissionDate),MONTH(@AdmissionDate),1) OR @MonthlyDiscountFrom>@YearEndDate
      THROW 51024,'Choose a first concession month within the admission academic year.',1;
    IF @DiscountFrom=DATEFROMPARTS(YEAR(@AdmissionDate),MONTH(@AdmissionDate),1)
    BEGIN
      UPDATE A SET DiscountAmount=D.DiscountAmount,ConcessionLabel=C.ConcessionLabel
       FROM dbo.StudentFeeAssignments A JOIN @ReviewedDiscounts D ON D.FeeTypeId=A.FeeTypeId
       LEFT JOIN @Concessions C ON C.FeeTypeId=A.FeeTypeId
       WHERE A.EnrollmentId=@EnrollmentId AND A.Frequency=N'Monthly';
    END
    ELSE IF EXISTS(SELECT 1 FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId AND @DiscountFrom<=EndDate)
    BEGIN
      SELECT A.*,D.DiscountAmount ChosenDiscount INTO #AdmissionDiscountVersions
       FROM dbo.StudentFeeAssignments A JOIN @ReviewedDiscounts D ON D.FeeTypeId=A.FeeTypeId
       WHERE A.EnrollmentId=@EnrollmentId AND A.Frequency=N'Monthly' AND D.DiscountAmount>0;
      UPDATE A SET EffectiveTo=DATEADD(month,-1,@DiscountFrom)
       FROM dbo.StudentFeeAssignments A JOIN #AdmissionDiscountVersions V ON V.StudentFeeAssignmentId=A.StudentFeeAssignmentId;
      INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,Remarks,EffectiveFrom,EffectiveTo,ConcessionLabel)
       SELECT V.EnrollmentId,V.AdmissionId,V.FeeTypeId,V.Amount,V.ChosenDiscount,V.Frequency,V.Remarks,@DiscountFrom,NULL,C.ConcessionLabel
       FROM #AdmissionDiscountVersions V LEFT JOIN @Concessions C ON C.FeeTypeId=V.FeeTypeId;
    END;
    IF @FamilyStudentId IS NOT NULL EXEC dbo.AddStudentSibling @AdmissionId,@FamilyStudentId,N'Sibling',@UserId;
    EXEC dbo.PostEnrollmentOpeningCharges @EnrollmentId;
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'ADMISSION', @AdmissionDate, NULL, @ClassSectionId, NULL, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, N'Initial placement created', @UserId);
    INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
    VALUES                          (@AdmissionId, NULL, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, N'Admission', @AdmissionDate, N'Initial student status', @UserId);

COMMIT TRANSACTION;
    SELECT @AdmissionId AS AdmissionId,
           @EnrollmentId AS EnrollmentId,
           @RegistrationNumber AS RegistrationNumber,
           @AdmissionNumber AS AdmissionNumber, (SELECT QuickReference FROM dbo.Students WHERE AdmissionId=@AdmissionId) QuickReference;
END

GO
CREATE OR ALTER PROCEDURE dbo.BulkPromoteStudents
@UserId INT, @SourceAcademicYearId INT, @DestinationAcademicYearId INT, @SourceCampusId INT, @SourceClassId INT, @SourceSectionId INT, @DestinationClassSectionId INT, @EffectiveDate DATE, @Remarks NVARCHAR (500), @Decisions dbo.BulkPromotionDecisionType READONLY, @CarryConcessions bit=0
AS

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
    IF @CarryConcessions=1 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
    IF @SourceAcademicYearId <= 0
        THROW 52501, 'A valid source academic year is required.', 1;
    IF @DestinationAcademicYearId <= 0
        THROW 52502, 'A valid destination academic year is required.', 1;
    IF @DestinationAcademicYearId = @SourceAcademicYearId
        THROW 52503, 'Bulk Promotion must move students into a different academic year. Use Transfer Class for same-year movement.', 1;
    IF @SourceCampusId <= 0
       OR @SourceClassId <= 0
        THROW 52504, 'A valid source campus and class are required.', 1;
    IF EXISTS (SELECT 1
               FROM   @Decisions
               WHERE  Decision = N'PROMOTE')
       AND @DestinationClassSectionId <= 0
        THROW 52506, 'A valid destination class/section is required for promoted students.', 1;
    IF @EffectiveDate IS NULL
        THROW 52508, 'The effective date is required.', 1;
    IF @EffectiveDate > CONVERT (DATE, SYSDATETIME())
        THROW 52509, 'The effective date cannot be in the future.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   @Decisions)
        THROW 52510, 'Select at least one student to process.', 1;
    IF EXISTS (SELECT 1
               FROM   @Decisions
               WHERE  Decision NOT IN (N'PROMOTE', N'HOLD'))
        THROW 52511, 'Bulk promotion contains an invalid decision.', 1;
    DECLARE @SourceStartDate AS DATE, @DestinationStartDate AS DATE, @DestinationEndDate AS DATE, @DestinationCampusId AS INT, @DestinationClassId AS INT;
    SELECT @SourceStartDate = StartDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @SourceAcademicYearId;
    SELECT @DestinationStartDate = StartDate, @DestinationEndDate=EndDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @DestinationAcademicYearId;
    IF @SourceStartDate IS NULL
        THROW 52512, 'The source academic year is invalid.', 1;
    IF @DestinationStartDate IS NULL
        THROW 52513, 'The destination academic year is invalid.', 1;
    IF @DestinationStartDate <= @SourceStartDate
        THROW 52514, 'The destination academic year must be later than the source academic year.', 1;
    IF @EffectiveDate<@DestinationStartDate OR (@DestinationEndDate IS NOT NULL AND @EffectiveDate>@DestinationEndDate)
        THROW 52520,'The effective date must fall within the destination academic year.',1;
    IF EXISTS (SELECT 1
               FROM   @Decisions
               WHERE  Decision = N'PROMOTE')
        BEGIN
            SELECT @DestinationCampusId = CCS.CampusId,
                   @DestinationClassId = CCS.ClassId
            FROM   dbo.CampusClassSections AS CCS
            WHERE  CCS.ClassSectionId = @DestinationClassSectionId;
            IF @DestinationCampusId IS NULL
                THROW 52516, 'The destination class/section is invalid.', 1;
            IF @DestinationCampusId <> @SourceCampusId
                THROW 52517, 'Bulk Promotion cannot move students between campuses.', 1;
        END
    ELSE
        BEGIN
            SET @DestinationClassSectionId = NULL;
        END
    IF EXISTS (SELECT 1
               FROM   @Decisions
               WHERE  Decision = N'PROMOTE')
        BEGIN
            DECLARE @MissingMandatoryFeesDest AS NVARCHAR (1000);
            SELECT @MissingMandatoryFeesDest = STRING_AGG(FT.FeeTypeName, N', ')
            FROM   dbo.FeeTypes AS FT
            WHERE  FT.IsMandatory = 1
                   AND NOT EXISTS (SELECT 1
                                   FROM   dbo.FeeConfigurations AS FC
                                   WHERE  FC.CampusId = @DestinationCampusId
                                          AND FC.ClassId = @DestinationClassId
                                          AND FC.FeeTypeId = FT.FeeTypeId);
            IF @MissingMandatoryFeesDest IS NOT NULL
                BEGIN
                    DECLARE @MandatoryFeeErrorMsgDest AS NVARCHAR (1100) = N'Required fee configuration is missing for the destination class: ' + @MissingMandatoryFeesDest + N'.';
                    THROW 52519, @MandatoryFeeErrorMsgDest, 1;
                END
        END
    DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
    BEGIN TRY
    BEGIN TRANSACTION;
    SELECT E.EnrollmentId,
           E.AdmissionId,
           E.ClassSectionId,
           E.UsesTransport,
           D.Decision,
           C.ClassId AS CurrentClassId,
           CCS.CampusId AS CurrentCampusId
    INTO   #Selected
    FROM   @Decisions AS D
           INNER JOIN
           dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
           ON E.AdmissionId = D.AdmissionId
              AND E.Status = N'Active'
              AND E.AcademicYearId = @SourceAcademicYearId
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
           INNER JOIN
           dbo.Classes AS C
           ON C.ClassId = CCS.ClassId
    WHERE  CCS.CampusId = @SourceCampusId
           AND CCS.ClassId = @SourceClassId
           AND (@SourceSectionId = 0
                OR CCS.SectionId = @SourceSectionId);
    IF EXISTS (SELECT 1
               FROM   @Decisions AS D
                      LEFT OUTER JOIN
                      #Selected AS S
                      ON S.AdmissionId = D.AdmissionId
               WHERE  S.AdmissionId IS NULL)
        THROW 52521, 'One or more selected students are no longer active in the selected source class. Reload the candidates and try again.', 1;
    ALTER TABLE #Selected
        ADD NewClassSectionId INT NULL;
    UPDATE #Selected
    SET    NewClassSectionId = CASE WHEN Decision = N'PROMOTE' THEN @DestinationClassSectionId ELSE ClassSectionId END;
    IF EXISTS (SELECT 1
               FROM   #Selected AS S
                      INNER JOIN
                      dbo.CampusClassSections AS CCS
                      ON CCS.ClassSectionId = S.NewClassSectionId
               WHERE  CCS.CampusId <> @SourceCampusId)
        THROW 52522, 'One or more destination class/sections are invalid for the source campus.', 1;
    DECLARE @BatchLockResult AS INT;
    DECLARE @BatchLockResource AS NVARCHAR (255) = N'FMSC.BulkPromotion.' + CONVERT (NVARCHAR (20), @SourceAcademicYearId) + N'.' + CONVERT (NVARCHAR (20), @SourceClassId) + N'.' + CONVERT (NVARCHAR (20), @SourceCampusId);
    EXECUTE @BatchLockResult = sys.sp_getapplock @Resource = @BatchLockResource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
    IF @BatchLockResult < 0
        THROW 52523, 'Another bulk promotion is currently processing this class. Please try again after it completes.', 1;

    INSERT  dbo.PromotionBatches (SourceAcademicYearId, DestinationAcademicYearId, SourceCampusId, SourceClassId, SourceSectionId, DestinationClassSectionId, EffectiveDate, Remarks, CreatedByUserId)
    VALUES                      (@SourceAcademicYearId, @DestinationAcademicYearId, @SourceCampusId, @SourceClassId, NULLIF (@SourceSectionId, 0), @DestinationClassSectionId, @EffectiveDate, @Remarks, @UserId);
    DECLARE @PromotionBatchId AS INT = SCOPE_IDENTITY();
    IF EXISTS (SELECT 1
               FROM   (SELECT DISTINCT CCS.CampusId,
                                       CCS.ClassId
                       FROM   #Selected AS S
                              INNER JOIN
                              dbo.CampusClassSections AS CCS
                              ON CCS.ClassSectionId = S.NewClassSectionId) AS D
               WHERE  EXISTS (SELECT 1
                              FROM   dbo.FeeTypes AS FT
                              WHERE  FT.IsMandatory = 1
                                     AND NOT EXISTS (SELECT 1
                                                     FROM   dbo.FeeConfigurations AS FC
                                                     WHERE  FC.CampusId = D.CampusId
                                                            AND FC.ClassId = D.ClassId
                                                            AND FC.FeeTypeId = FT.FeeTypeId)))
        THROW 52524, 'Required fee configuration is missing for one or more destination classes.', 1;
    IF EXISTS(SELECT 1 FROM #Selected X JOIN dbo.Students S ON S.AdmissionId=X.AdmissionId WHERE S.LegacySetupPending=1) THROW 59751,'Finish legacy verification and Go Live before promoting these students.',1;
    IF EXISTS(SELECT 1 FROM #Selected S JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId WHERE E.PlacementConfirmedAt IS NULL)
        THROW 59646,'Confirm the source placement in Admission before promotion.',1;
    IF EXISTS(SELECT 1 FROM dbo.BillingCharges B WITH(UPDLOCK,HOLDLOCK)
        JOIN #Selected S ON S.EnrollmentId=B.EnrollmentId
        WHERE B.Status<>N'Cancelled' AND COALESCE(B.BillingMonth,B.ChargeDate)>DATEFROMPARTS(YEAR(DATEADD(day,-1,@EffectiveDate)),MONTH(DATEADD(day,-1,@EffectiveDate)),1))
        THROW 59632,'Charges exist after the promotion effective date. Review unpaid charges in Fee Collection or paid charges in Fee Corrections, then retry. No students were promoted.',1;
    UPDATE E
    SET    Status      = N'Promoted',
           LeavingDate = @EffectiveDate,
           UpdatedAt   = SYSDATETIME(),
           Remarks     = COALESCE (NULLIF (@Remarks, N''), E.Remarks)
    FROM   dbo.Enrollments AS E
           INNER JOIN
           #Selected AS S
           ON S.EnrollmentId = E.EnrollmentId
    WHERE  E.Status = N'Active';
    IF @@ROWCOUNT <> (SELECT COUNT(*)
                      FROM   #Selected)
        THROW 52525, 'The source enrollments could not all be closed as historical enrollments.', 1;
    INSERT dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, BillingStartDate, Status, UsesTransport, BillingReady, Remarks, PlacementConfirmedAt)
    SELECT S.AdmissionId,
           @DestinationAcademicYearId,
           S.NewClassSectionId,
           @EffectiveDate,
           dbo.GetMonthlyBillingStartDate(@EffectiveDate),
           N'Active',
           0,
           0,
           CASE WHEN S.Decision = N'PROMOTE' THEN N'Created by bulk promotion' ELSE N'Created by bulk promotion - held back/repeat class' END, SYSDATETIME()
    FROM   #Selected AS S;
    DECLARE @NewEnrollments TABLE (
        AdmissionId       INT           NOT NULL,
        OldEnrollmentId   INT           NOT NULL,
        NewEnrollmentId   INT           NOT NULL,
        Decision          NVARCHAR (20) NOT NULL,
        NewClassSectionId INT           NOT NULL,
        UsesTransport     BIT           NOT NULL);
    INSERT INTO @NewEnrollments (AdmissionId, OldEnrollmentId, NewEnrollmentId, Decision, NewClassSectionId, UsesTransport)
    SELECT S.AdmissionId,
           S.EnrollmentId,
           E.EnrollmentId,
           S.Decision,
           S.NewClassSectionId,
           0
    FROM   #Selected AS S
           INNER JOIN
           dbo.Enrollments AS E
           ON E.AdmissionId = S.AdmissionId
              AND E.AcademicYearId = @DestinationAcademicYearId
              AND E.Status = N'Active';
    IF (SELECT COUNT(*)
        FROM   @NewEnrollments) <> (SELECT COUNT(*)
                                    FROM   #Selected)
        THROW 52526, 'The new academic-year enrollments could not all be created.', 1;
    INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks)
    SELECT N.NewEnrollmentId,
           N.AdmissionId,
           FC.FeeTypeId,
           FC.Amount,
           0,
           FC.Frequency,
           CASE WHEN N.Decision = N'PROMOTE' THEN N'Created by bulk promotion' ELSE N'Created by bulk promotion - held back/repeat class' END
    FROM   @NewEnrollments AS N
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = N.NewClassSectionId
           INNER JOIN
           dbo.FeeConfigurations AS FC
           ON FC.CampusId = CCS.CampusId
              AND FC.ClassId = CCS.ClassId
           INNER JOIN
           dbo.FeeTypes AS FT
           ON FT.FeeTypeId = FC.FeeTypeId
    WHERE  FT.IsMandatory = 1
           AND FT.IsTransportLinked = 0;
    -- Carry only the monthly agreement in force at the end of the source
    -- placement, never a temporary expired concession or an earlier bill waiver.
    IF @CarryConcessions=1
    UPDATE Target SET
        DiscountAmount=CASE WHEN Source.ConcessionLabel IN(N'Free',N'Waived')
            OR (Source.ConcessionLabel=N'Scholarship' AND Source.DiscountAmount=Source.Amount)
            OR Source.DiscountAmount>Target.Amount THEN Target.Amount ELSE Source.DiscountAmount END,
        ConcessionLabel=Source.ConcessionLabel,
        Remarks=LEFT(CONCAT(Target.Remarks,N' | Monthly concession carried from prior placement'),500)
    FROM dbo.StudentFeeAssignments Target
    JOIN @NewEnrollments N ON N.NewEnrollmentId=Target.EnrollmentId
    JOIN dbo.AcademicYears Y ON Y.AcademicYearId=@SourceAcademicYearId
    CROSS APPLY(SELECT CASE WHEN Y.EndDate<DATEADD(day,-1,@EffectiveDate) THEN Y.EndDate ELSE DATEADD(day,-1,@EffectiveDate) END LastDate) Cutoff
    CROSS APPLY(SELECT DATEFROMPARTS(YEAR(Cutoff.LastDate),MONTH(Cutoff.LastDate),1) LastMonth) M
    CROSS APPLY(SELECT TOP(1) A.Amount,A.DiscountAmount,A.ConcessionLabel
        FROM dbo.StudentFeeAssignments A WITH(UPDLOCK,HOLDLOCK)
        WHERE A.EnrollmentId=N.OldEnrollmentId AND A.FeeTypeId=Target.FeeTypeId AND A.Frequency=N'Monthly' AND A.CancelledAt IS NULL
            AND ISNULL(A.EffectiveFrom,'19000101')<=M.LastMonth AND ISNULL(A.EffectiveTo,'99991231')>=M.LastMonth
        ORDER BY A.EffectiveFrom DESC,A.StudentFeeAssignmentId DESC) Source
    WHERE Target.Frequency=N'Monthly';
    INSERT dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    SELECT N.OldEnrollmentId,
           N.AdmissionId,
           CASE WHEN N.Decision = N'PROMOTE' THEN N'BULK_PROMOTE' ELSE N'BULK_HOLD' END,
           @EffectiveDate,
           S.ClassSectionId,
           N.NewClassSectionId,
           N'Active',
           N'Promoted',
           CASE WHEN N.Decision = N'PROMOTE' THEN N'Bulk promoted to EnrollmentId ' + CONVERT (NVARCHAR (20), N.NewEnrollmentId) ELSE N'Bulk held back/repeated class in new academic year; EnrollmentId ' + CONVERT (NVARCHAR (20), N.NewEnrollmentId) END,
           @UserId
    FROM   @NewEnrollments AS N
           INNER JOIN
           #Selected AS S
           ON S.AdmissionId = N.AdmissionId;
    INSERT dbo.PromotionBatchStudents (PromotionBatchId, AdmissionId, OldEnrollmentId, NewEnrollmentId, Decision, Remarks)
    SELECT @PromotionBatchId,
           N.AdmissionId,
           N.OldEnrollmentId,
           N.NewEnrollmentId,
           N.Decision,
           CASE WHEN N.Decision = N'PROMOTE' THEN N'Promoted to destination class.' ELSE N'Held back/repeated the current class in the destination academic year.' END
    FROM   @NewEnrollments AS N
           INNER JOIN
           #Selected AS S
           ON S.AdmissionId = N.AdmissionId;
    DECLARE @ProcessedCount AS INT = (SELECT COUNT(*)
                                      FROM   @NewEnrollments), @PromotedCount AS INT = (SELECT COUNT(*)
                                                                                        FROM   @NewEnrollments
                                                                                        WHERE  Decision = N'PROMOTE'), @HeldBackCount AS INT = (SELECT COUNT(*)
                                                                                                                                                FROM   @NewEnrollments
                                                                                                                                                WHERE  Decision = N'HOLD');
    UPDATE dbo.PromotionBatches
    SET    ProcessedCount = @ProcessedCount,
           PromotedCount  = @PromotedCount,
           HeldBackCount  = @HeldBackCount
    WHERE  PromotionBatchId = @PromotionBatchId;

COMMIT TRANSACTION;
    SELECT @PromotionBatchId AS PromotionBatchId,
           @ProcessedCount AS ProcessedCount,
           @PromotedCount AS PromotedCount,
           @HeldBackCount AS HeldBackCount;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK;
        THROW;
    END CATCH;
END;
GO
CREATE OR ALTER TRIGGER dbo.GuardClosedEnrollmentCharges ON dbo.BillingCharges AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 -- Closed history stays protected. A reviewed, permission-checked financial
 -- correction may change its own bill chain without reopening the placement.
 IF EXISTS(SELECT 1 FROM inserted I LEFT JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
   JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=I.EnrollmentId
   WHERE E.Status NOT IN(N'Active',N'Pending')
   AND NOT EXISTS(SELECT 1 FROM dbo.FeeBillCorrections C
    JOIN dbo.BillingCharges Original ON Original.BillingChargeId=C.BillingChargeId
    WHERE C.RequestId=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'FMSC.BillCorrection'))
     AND C.BillingChargeId=COALESCE(I.CorrectionOfBillingChargeId,I.BillingChargeId)
     AND ((D.BillingChargeId IS NOT NULL AND I.EnrollmentId=D.EnrollmentId AND I.StudentFeeAssignmentId=D.StudentFeeAssignmentId
       AND I.AdmissionId=D.AdmissionId AND I.FeeTypeId=D.FeeTypeId AND ISNULL(I.BillingMonth,'19000101')=ISNULL(D.BillingMonth,'19000101')
       AND I.ChargeDate=D.ChargeDate AND I.DueDate=D.DueDate AND I.ChargeKind=D.ChargeKind)
      OR (D.BillingChargeId IS NULL AND I.CorrectionOfBillingChargeId=Original.BillingChargeId
       AND I.EnrollmentId=Original.EnrollmentId AND I.StudentFeeAssignmentId=Original.StudentFeeAssignmentId
       AND I.AdmissionId=Original.AdmissionId AND I.FeeTypeId=Original.FeeTypeId AND I.ChargeKind=N'Manual'
       AND ISNULL(I.BillingMonth,'19000101')=ISNULL(Original.BillingMonth,'19000101'))))
   AND (D.BillingChargeId IS NULL OR I.EnrollmentId<>D.EnrollmentId
    OR I.StudentFeeAssignmentId<>D.StudentFeeAssignmentId OR I.AdmissionId<>D.AdmissionId OR I.FeeTypeId<>D.FeeTypeId
    OR I.GrossAmount<>D.GrossAmount OR I.DiscountAmount<>D.DiscountAmount
    OR ISNULL(I.BillingMonth,'19000101')<>ISNULL(D.BillingMonth,'19000101')
    OR I.ChargeDate<>D.ChargeDate OR I.DueDate<>D.DueDate OR I.ChargeKind<>D.ChargeKind
    OR (I.Status<>D.Status AND D.Status=N'Cancelled')))
  THROW 57102,'This enrollment is closed. Its charges cannot be posted, changed or reversed here. Outstanding fees can still be collected.',1;
 IF EXISTS(SELECT 1 FROM deleted D LEFT JOIN inserted I ON I.BillingChargeId=D.BillingChargeId
    JOIN dbo.Enrollments E ON E.EnrollmentId=D.EnrollmentId WHERE I.BillingChargeId IS NULL AND E.Status NOT IN(N'Active',N'Pending'))
  THROW 57102,'Closed enrollment charges must remain in history.',1;
 -- New bills must fit the enrollment and academic year, including backdated closures.
 IF EXISTS(SELECT 1 FROM inserted I LEFT JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
    JOIN dbo.Enrollments E ON E.EnrollmentId=I.EnrollmentId
    JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
    WHERE D.BillingChargeId IS NULL AND I.Status<>N'Cancelled'
      AND NOT EXISTS(SELECT 1 FROM dbo.FeeBillCorrections C JOIN dbo.BillingCharges Original ON Original.BillingChargeId=C.BillingChargeId
       WHERE C.RequestId=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'FMSC.BillCorrection'))
        AND I.CorrectionOfBillingChargeId=Original.BillingChargeId AND I.EnrollmentId=Original.EnrollmentId
        AND I.AdmissionId=Original.AdmissionId AND I.FeeTypeId=Original.FeeTypeId AND I.ChargeKind=N'Manual'
        AND ISNULL(I.BillingMonth,'19000101')=ISNULL(Original.BillingMonth,'19000101'))
      AND ((E.PlacementConfirmedAt IS NULL AND E.Status<>N'Pending') OR EXISTS(SELECT 1 FROM dbo.Students S WHERE S.AdmissionId=E.AdmissionId AND S.LegacySetupPending=1) OR
      (I.ChargeKind=N'Monthly' AND (E.BillingStartDate IS NULL OR COALESCE(I.BillingMonth,I.ChargeDate)<DATEFROMPARTS(YEAR(E.BillingStartDate),MONTH(E.BillingStartDate),1)))
      OR COALESCE(I.BillingMonth,I.ChargeDate)<DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1)
      OR (E.Status=N'Pending' AND I.ChargeKind=N'Monthly' AND COALESCE(I.BillingMonth,I.ChargeDate)<>DATEFROMPARTS(YEAR(COALESCE(E.BillingStartDate,E.EnrollmentDate)),MONTH(COALESCE(E.BillingStartDate,E.EnrollmentDate)),1)) OR (Y.EndDate IS NOT NULL AND COALESCE(I.BillingMonth,I.ChargeDate)>DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1))
      OR (E.LeavingDate IS NOT NULL AND COALESCE(I.BillingMonth,I.ChargeDate)>DATEFROMPARTS(YEAR(E.LeavingDate),MONTH(E.LeavingDate),1))))
    THROW 59633,'The charge period is outside the enrollment or academic year. Correct placement or billing dates in Admission before posting.',1;
 -- Protect payments even if an alternative reversal entry point bypasses the form.
 IF EXISTS(SELECT 1 FROM inserted I JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
   WHERE I.Status=N'Cancelled' AND D.Status<>N'Cancelled' AND EXISTS(
    SELECT 1 FROM dbo.FeePaymentAllocations A WITH(UPDLOCK,HOLDLOCK)
    JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
    WHERE A.BillingChargeId=I.BillingChargeId AND P.Status=N'Completed' GROUP BY A.BillingChargeId HAVING SUM(A.Amount)>0))
  THROW 57103,'A paid or partially paid charge cannot be cancelled. Use the payment correction or refund workflow.',1;
END;

GO
IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>20',@Definition)=0
  THROW 51000,'Unexpected communication callback definition. Review before upgrading.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>20',N'FROM dbo.DatabaseVersion)<>21');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition));
 EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'21',SYSDATETIME(),SUSER_SNAME(),N'Operator-selected concession periods and admission/leaving choices');
