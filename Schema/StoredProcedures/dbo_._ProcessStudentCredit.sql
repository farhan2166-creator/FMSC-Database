-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.ProcessStudentCredit
 @UserId int,@AdmissionId int,@Action nvarchar(20),@Amount decimal(12,2),@RequestId uniqueidentifier,
 @Reason nvarchar(500),@TargetAdmissionId int=NULL,@PaymentMethod nvarchar(30)=N'Cash',@ReceiptNumber nvarchar(40)=NULL,@TransactionDate date=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 SET @TransactionDate=COALESCE(@TransactionDate,CONVERT(date,GETDATE()));
 IF @TransactionDate>CONVERT(date,GETDATE()) THROW 54902,'Credit transactions cannot be dated in the future.',1;
 IF @Action IS NULL OR @Action NOT IN(N'Deposit',N'Refund',N'Transfer') OR @Amount IS NULL OR @Amount<=0 OR @Amount<>FLOOR(@Amount)
   OR @RequestId IS NULL OR NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL
  THROW 54902,'Choose a credit action, whole-rupee amount and reason.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId) THROW 54903,'Student not found.',1;
 IF @Action=N'Deposit' AND @PaymentMethod IN(N'Refund',N'Correction',N'Account Credit') THROW 54902,'Select a money-received payment method.',1;
 IF @Action=N'Transfer' AND (@TargetAdmissionId IS NULL OR @TargetAdmissionId=@AdmissionId OR NOT EXISTS(
   SELECT 1 FROM dbo.FamilyMembers A JOIN dbo.FamilyMembers B ON B.FamilyId=A.FamilyId
    WHERE A.AdmissionId=@AdmissionId AND B.AdmissionId=@TargetAdmissionId)) THROW 54904,'Select a different student in the same family.',1;
 DECLARE @Lock int,@Resource nvarchar(255),@First int=@AdmissionId,@Second int,@Available decimal(12,2),@Enrollment int,@Payment int,@Existing int;
 IF @Action=N'Transfer' BEGIN SET @First=CASE WHEN @AdmissionId<@TargetAdmissionId THEN @AdmissionId ELSE @TargetAdmissionId END;SET @Second=CASE WHEN @First=@AdmissionId THEN @TargetAdmissionId ELSE @AdmissionId END;END;
 DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
  BEGIN TRAN;
  SET @Resource=CONCAT(N'FMSC.StudentCredit.',@First);
  EXEC @Lock=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54901,'Credit account is busy. Try again.',1;
  IF @Second IS NOT NULL BEGIN
   SET @Resource=CONCAT(N'FMSC.StudentCredit.',@Second);
   EXEC @Lock=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
   IF @Lock<0 THROW 54901,'Sibling credit account is busy. Try again.',1;
  END;
  IF EXISTS(SELECT 1 FROM dbo.StudentCreditEntries WHERE RequestId=@RequestId)
  BEGIN
   IF NOT EXISTS(SELECT 1 FROM dbo.StudentCreditEntries WHERE RequestId=@RequestId AND AdmissionId=@AdmissionId
    AND Amount=CASE WHEN @Action=N'Deposit' THEN @Amount ELSE -@Amount END
    AND EntryKind=CASE WHEN @Action=N'Transfer' THEN N'TransferOut' ELSE @Action END
    AND ISNULL(CounterpartyAdmissionId,0)=ISNULL(@TargetAdmissionId,0)) THROW 54905,'This request belongs to another credit operation.',1;
   COMMIT;
   SELECT E.FeePaymentId,P.ReceiptNumber,@AdmissionId AdmissionId,COALESCE((SELECT SUM(Amount) FROM dbo.StudentCreditEntries WHERE AdmissionId=@AdmissionId),0) AvailableCredit
    FROM dbo.StudentCreditEntries E LEFT JOIN dbo.FeePayments P ON P.FeePaymentId=E.FeePaymentId
    WHERE E.RequestId=@RequestId AND E.AdmissionId=@AdmissionId;RETURN;
  END;
  SELECT @Available=COALESCE(SUM(Amount),0) FROM dbo.StudentCreditEntries WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  IF @Action<>N'Deposit' AND @Amount>@Available THROW 54906,'The amount exceeds available student credit. Refresh the account.',1;
  IF @Action IN(N'Deposit',N'Refund')
  BEGIN
   SELECT TOP(1) @Enrollment=EnrollmentId FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId ORDER BY CASE WHEN Status=N'Active' THEN 0 ELSE 1 END,EnrollmentId DESC;
   IF @Enrollment IS NULL THROW 54907,'A student placement is required to issue a credit receipt.',1;
   IF @ReceiptNumber IS NULL BEGIN DECLARE @Date date=@TransactionDate; EXEC dbo.GetNextFeeReceiptNumber @ReceiptDate=@Date,@ReceiptNumber=@ReceiptNumber OUTPUT; END;
   INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Remarks,Status,IsGroupPayment,CreatedByUserId)
    VALUES(@AdmissionId,@Enrollment,@TransactionDate,@ReceiptNumber,CASE WHEN @Action=N'Deposit' THEN @Amount ELSE -@Amount END,
     CASE WHEN @Action=N'Deposit' THEN @PaymentMethod ELSE N'Refund' END,@Reason,N'Completed',1,@UserId);
   SET @Payment=CONVERT(int,SCOPE_IDENTITY());
  END;
  INSERT dbo.StudentCreditEntries(AdmissionId,Amount,EntryKind,FeePaymentId,RequestId,CounterpartyAdmissionId,Reason,CreatedByUserId)
   VALUES(@AdmissionId,CASE WHEN @Action=N'Deposit' THEN @Amount ELSE -@Amount END,
    CASE WHEN @Action=N'Transfer' THEN N'TransferOut' ELSE @Action END,@Payment,@RequestId,@TargetAdmissionId,@Reason,@UserId);
  IF @Action=N'Transfer'
   INSERT dbo.StudentCreditEntries(AdmissionId,Amount,EntryKind,RequestId,CounterpartyAdmissionId,Reason,CreatedByUserId)
    VALUES(@TargetAdmissionId,@Amount,N'TransferIn',@RequestId,@AdmissionId,@Reason,@UserId);
  COMMIT;
  SELECT @Payment FeePaymentId,@ReceiptNumber ReceiptNumber,@AdmissionId AdmissionId,
   COALESCE((SELECT SUM(Amount) FROM dbo.StudentCreditEntries WHERE AdmissionId=@AdmissionId),0) AvailableCredit;
 END TRY
 BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
