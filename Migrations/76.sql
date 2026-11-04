-- Credit lifecycle: posting bills must never settle them using held advance.
-- Owner decision (4 Nov 2026): use student advance only in an explicitly
-- confirmed Fee Collection transaction with a saved collection receipt.
-- Existing automatic credit settlements and fallback readers remain untouched.
-- Keep this compatibility entry point because older posting/activation procedures
-- still call it; removing callers can be done when those procedures are revised.
SET NOCOUNT ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 76 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>75
 THROW 51000,'Migration 76 requires schema 75.',1;
GO

CREATE OR ALTER PROCEDURE dbo.ApplyStudentCredit_Internal
    @AdmissionId int,
    @UserId int=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @@TRANCOUNT=0
        THROW 54900,'Credit application requires a transaction.',1;
    -- Deliberate no-op: callers can post charges but cannot settle fees here.
    -- Fee Collection alone applies chosen credit via dbo.ApplyCollectionCredit
    -- and issues dbo.IssueCollectionReceipt in the same transaction.
    -- Do not delete historical CR-GUID payments or their legacy read path.
END;
GO


-- Credit is deliberately forbidden on a backdated collection, regardless of
-- how much advance exists today. Cash backdating retains its existing rules.
-- Credit-only collections also obey the same enrollment date guard as cash.
CREATE OR ALTER PROCEDURE dbo.ApplyCollectionCredit
 @UserId int,@TransactionDate date,@CreditsJson nvarchar(max)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 IF @@TRANCOUNT=0 THROW 54900,'Credit application requires the collection transaction.',1;
 IF @TransactionDate IS NULL OR @TransactionDate>CONVERT(date,GETDATE()) OR ISJSON(@CreditsJson)<>1
  THROW 54902,'Choose a valid payment date and credit allocation.',1;
 IF @TransactionDate<CONVERT(date,GETDATE())
  THROW 54902,'Advance cannot be used for a backdated collection. Select today for credit settlement.',1;
 DECLARE @Plan TABLE(ChargeId bigint PRIMARY KEY,AdmissionId int NOT NULL,EnrollmentId int NOT NULL,
  Amount decimal(12,2) NOT NULL,ExpectedBalance decimal(12,2) NOT NULL,ExpectedCredit decimal(12,2) NOT NULL);
 INSERT @Plan SELECT ChargeId,AdmissionId,EnrollmentId,Amount,ExpectedBalance,ExpectedCredit
 FROM OPENJSON(@CreditsJson) WITH(ChargeId bigint,AdmissionId int,EnrollmentId int,
  Amount decimal(12,2),ExpectedBalance decimal(12,2),ExpectedCredit decimal(12,2));
 IF NOT EXISTS(SELECT 1 FROM @Plan) OR EXISTS(SELECT 1 FROM @Plan WHERE Amount<=0 OR ExpectedBalance<Amount OR ExpectedCredit<0)
  THROW 54902,'Invalid credit allocation. Reload the student.',1;
 DECLARE @Lock int,@Resource nvarchar(255),@Student int,@Available decimal(12,2),@Use decimal(12,2),@Expected decimal(12,2);
 EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
 IF @Lock<0 THROW 54901,'Billing is busy. Try again.',1;
 DECLARE Students CURSOR LOCAL FAST_FORWARD FOR
  SELECT AdmissionId,SUM(Amount),MIN(ExpectedCredit) FROM @Plan GROUP BY AdmissionId ORDER BY AdmissionId;
 OPEN Students;FETCH NEXT FROM Students INTO @Student,@Use,@Expected;
 WHILE @@FETCH_STATUS=0
 BEGIN
  SET @Resource=CONCAT(N'FMSC.StudentCredit.',@Student);
  EXEC @Lock=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54901,'The student credit account is busy. Try again.',1;
  SELECT @Available=COALESCE(SUM(Amount),0) FROM dbo.StudentCreditEntries WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Student;
  IF @Available<>@Expected OR @Use>@Available OR EXISTS(SELECT 1 FROM @Plan WHERE AdmissionId=@Student AND ExpectedCredit<>@Expected)
   THROW 53104,'Available credit changed. Reload the student before collecting.',1;
  FETCH NEXT FROM Students INTO @Student,@Use,@Expected;
 END;
 CLOSE Students;DEALLOCATE Students;
 IF EXISTS(SELECT 1 FROM @Plan P LEFT JOIN dbo.BillingCharges B WITH(UPDLOCK,HOLDLOCK) ON B.BillingChargeId=P.ChargeId
  WHERE B.BillingChargeId IS NULL OR B.AdmissionId<>P.AdmissionId OR B.EnrollmentId<>P.EnrollmentId
   OR B.Status=N'Cancelled' OR dbo.GetFeeChargeBalance(B.BillingChargeId)<>P.ExpectedBalance)
  THROW 53104,'A bill changed. Reload the student before collecting.',1;
 -- The collection-only credit path must obey the same enrollment-date boundary
 -- as cash collection. This check runs under the billing transaction lock.
 IF EXISTS(SELECT 1 FROM @Plan P
    JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=P.EnrollmentId
    WHERE @TransactionDate<E.EnrollmentDate)
  THROW 54902,'The collection date cannot be before the selected enrollment date.',1;
 DECLARE @Bill bigint,@Enrollment int,@Payment int,@Request uniqueidentifier;
 DECLARE Bills CURSOR LOCAL FAST_FORWARD FOR SELECT ChargeId,AdmissionId,EnrollmentId,Amount FROM @Plan ORDER BY AdmissionId,ChargeId;
 OPEN Bills;FETCH NEXT FROM Bills INTO @Bill,@Student,@Enrollment,@Use;
 WHILE @@FETCH_STATUS=0
 BEGIN
  SET @Request=NEWID();
  INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Remarks,Status,CreatedByUserId,IsCreditApplication)
   VALUES(@Student,@Enrollment,@TransactionDate,CONCAT(N'CR-',CONVERT(nvarchar(36),@Request)),@Use,N'Account Credit',N'Existing credit applied; no new money received',N'Completed',@UserId,1);
  SET @Payment=CONVERT(int,SCOPE_IDENTITY());
  INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount,Remarks)
   VALUES(@Payment,@Student,@Bill,@Use,N'Existing student credit');
  INSERT dbo.StudentCreditEntries(AdmissionId,Amount,EntryKind,FeePaymentId,RequestId,Reason,CreatedByUserId)
   VALUES(@Student,-@Use,N'Application',@Payment,@Request,N'Existing credit applied to posted fees',@UserId);
  UPDATE dbo.BillingCharges SET Status=CASE WHEN dbo.GetFeeChargeBalance(@Bill)=0 THEN N'Paid' ELSE N'PartiallyPaid' END WHERE BillingChargeId=@Bill;
  FETCH NEXT FROM Bills INTO @Bill,@Student,@Enrollment,@Use;
 END;
 CLOSE Bills;DEALLOCATE Bills;
END;
GO

INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'76',SYSDATETIME(),SUSER_SNAME(),
       N'Apply advance only during collection dated today; never in posting or backdated settlement');
GO
