-- Credit selected on Fee Collection settles the reviewed posted bills (including
-- future months) in the SAME transaction as cash and discount. No new cash.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 22 requires the installer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>21
 THROW 51000,'Migration 22 requires schema 21.',1;
GO
CREATE OR ALTER PROCEDURE dbo.ApplyCollectionCredit
 @UserId int,@TransactionDate date,@CreditsJson nvarchar(max)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 IF @@TRANCOUNT=0 THROW 54900,'Credit application requires the collection transaction.',1;
 IF @TransactionDate IS NULL OR @TransactionDate>CONVERT(date,GETDATE()) OR ISJSON(@CreditsJson)<>1
  THROW 54902,'Choose a valid payment date and credit allocation.',1;
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
IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>21',@Definition)=0
  THROW 51000,'Unexpected communication callback definition. Review before upgrading.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>21',N'FROM dbo.DatabaseVersion)<>22');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition));
 EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'22',SYSDATETIME(),SUSER_SNAME(),N'Apply reviewed available credit with cash and discount on Fee Collection');