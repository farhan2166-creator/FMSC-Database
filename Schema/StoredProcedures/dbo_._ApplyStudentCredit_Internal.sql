-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.ApplyStudentCredit_Internal @AdmissionId int,@UserId int=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @@TRANCOUNT=0 THROW 54900,'Credit application requires a transaction.',1;
 DECLARE @Credit decimal(12,2),@Lock int,@Resource nvarchar(255)=CONCAT(N'FMSC.StudentCredit.',@AdmissionId),@Actor int;
 EXEC @Lock=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
 IF @Lock<0 THROW 54901,'The student credit account is busy. Try again.',1;
 SELECT @Credit=COALESCE(SUM(Amount),0) FROM dbo.StudentCreditEntries WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF @Credit<=0 RETURN;
 SELECT TOP(1) @Actor=COALESCE(@UserId,CreatedByUserId) FROM dbo.StudentCreditEntries WHERE AdmissionId=@AdmissionId ORDER BY CreditEntryId DESC;
 DECLARE @Charge bigint,@Enrollment int,@Balance decimal(12,2),@Use decimal(12,2),@Payment int,
  @Receipt nvarchar(40),@Request uniqueidentifier;
 -- Apply only to obligations whose charge date has arrived. Never consume credit against a future opening bill early.
 DECLARE Charges CURSOR LOCAL FAST_FORWARD FOR
  SELECT B.BillingChargeId,B.EnrollmentId,dbo.GetFeeChargeBalance(B.BillingChargeId)
  FROM dbo.BillingCharges B WITH(UPDLOCK,HOLDLOCK) JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
  WHERE B.AdmissionId=@AdmissionId AND B.Status<>N'Cancelled' AND B.ChargeDate<=CONVERT(date,GETDATE())
   AND dbo.GetFeeChargeBalance(B.BillingChargeId)>0
  ORDER BY CASE WHEN F.FeeTypeCode=N'TUITION' THEN 0 ELSE 1 END,COALESCE(B.BillingMonth,B.ChargeDate),B.BillingChargeId;
 OPEN Charges; FETCH NEXT FROM Charges INTO @Charge,@Enrollment,@Balance;
 WHILE @@FETCH_STATUS=0 AND @Credit>0
 BEGIN
  SET @Use=CASE WHEN @Credit>@Balance THEN @Balance ELSE @Credit END;
  SET @Request=NEWID();SET @Receipt=CONCAT(N'CR-',CONVERT(nvarchar(36),@Request));
  INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Remarks,Status,CreatedByUserId,IsCreditApplication)
   VALUES(@AdmissionId,@Enrollment,CONVERT(date,GETDATE()),@Receipt,@Use,N'Account Credit',N'Existing credit applied; no new money received',N'Completed',@Actor,1);
  SET @Payment=CONVERT(int,SCOPE_IDENTITY());
  INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount,Remarks)
   VALUES(@Payment,@AdmissionId,@Charge,@Use,N'Existing student credit');
  INSERT dbo.StudentCreditEntries(AdmissionId,Amount,EntryKind,FeePaymentId,RequestId,Reason,CreatedByUserId)
   VALUES(@AdmissionId,-@Use,N'Application',@Payment,@Request,N'Existing credit applied to fees',@Actor);
  UPDATE dbo.BillingCharges SET Status=CASE WHEN dbo.GetFeeChargeBalance(@Charge)=0 THEN N'Paid' ELSE N'PartiallyPaid' END WHERE BillingChargeId=@Charge;
  SET @Credit=@Credit-@Use;
  FETCH NEXT FROM Charges INTO @Charge,@Enrollment,@Balance;
 END;
 CLOSE Charges; DEALLOCATE Charges;
END;
