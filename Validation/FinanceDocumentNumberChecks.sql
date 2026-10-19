-- Run only against a disposable schema-24 copy with two active siblings.
:ON ERROR EXIT
SET NOCOUNT ON; SET XACT_ABORT ON;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
IF DB_NAME() NOT LIKE N'FMSC[_]Documents[_]Test[_]%'
 THROW 51000,'These checks require a disposable finance-document database.',1;
IF (SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC)<>N'24'
 THROW 51000,'These checks require schema 24.',1;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator');
DECLARE @Today date=CONVERT(date,GETDATE()),@N nvarchar(40),@Checks int=0;
BEGIN TRANSACTION;
EXEC dbo.GetNextFinanceDocumentNumber 'B','2090-01-01',@N OUTPUT;
IF @N<>N'B-2090-00001' THROW 51000,'Individual bill prefix/padding failed.',1;
EXEC dbo.GetNextFinanceDocumentNumber 'FB','2090-01-01',@N OUTPUT;
IF @N<>N'FB-2090-00001' THROW 51000,'Family bill sequence is not independent.',1;
EXEC dbo.GetNextFeeReceiptNumber '2090-01-01',@N OUTPUT;
IF @N<>N'R-2090-00001' THROW 51000,'Individual receipt prefix/padding failed.',1;
EXEC dbo.GetNextFinanceDocumentNumber 'FR','2090-01-01',@N OUTPUT;
IF @N<>N'FR-2090-00001' THROW 51000,'Family receipt sequence is not independent.',1;
EXEC dbo.GetNextFinanceDocumentNumber 'R','2090-12-31',@N OUTPUT;
IF @N<>N'R-2090-00002' THROW 51000,'Receipt sequence did not increment.',1;
EXEC dbo.GetNextFinanceDocumentNumber 'R','2091-01-01',@N OUTPUT;
IF @N<>N'R-2091-00001' THROW 51000,'Calendar-year sequence reset failed.',1;
UPDATE dbo.FinanceDocumentCounters SET LastNumber=99999 WHERE DocumentType='R' AND DocumentYear=2091;
EXEC dbo.GetNextFeeReceiptNumber '2091-01-01',@N OUTPUT;
IF @N<>N'R-2091-100000' THROW 51000,'Long sequences were truncated.',1;
SET @Checks+=7;

DECLARE @Family int=(SELECT TOP(1) F.FamilyId FROM dbo.FamilyMembers F JOIN dbo.Enrollments E ON E.AdmissionId=F.AdmissionId
 WHERE E.Status=N'Active' AND E.BillingReady=1 GROUP BY F.FamilyId HAVING COUNT(DISTINCT F.AdmissionId)>1 ORDER BY F.FamilyId);
DECLARE @Fixture TABLE(ChargeId bigint,AdmissionId int,EnrollmentId int);
INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,GrossAmount,DiscountAmount,Status,ChargeKind,ManualRequestId,Remarks)
 OUTPUT inserted.BillingChargeId,inserted.AdmissionId,inserted.EnrollmentId INTO @Fixture
 SELECT TOP(2) A.StudentFeeAssignmentId,A.EnrollmentId,A.AdmissionId,A.FeeTypeId,NULL,@Today,1000,0,N'Open',N'Manual',NEWID(),N'Disposable document-number fixture'
 FROM dbo.FamilyMembers F JOIN dbo.Enrollments E ON E.AdmissionId=F.AdmissionId AND E.Status=N'Active' AND E.BillingReady=1
 CROSS APPLY(SELECT TOP(1) * FROM dbo.StudentFeeAssignments X WHERE X.EnrollmentId=E.EnrollmentId AND X.CancelledAt IS NULL AND X.Frequency=N'Monthly' ORDER BY X.StudentFeeAssignmentId) A
 WHERE F.FamilyId=@Family ORDER BY F.AdmissionId;
IF (SELECT COUNT(DISTINCT AdmissionId) FROM @Fixture)<>2 THROW 51000,'Two valid sibling placements are required for the fixture.',1;
DECLARE @Primary int=(SELECT MIN(AdmissionId) FROM @Fixture),@Ids nvarchar(max),@Source nvarchar(max),@Xml xml;
SELECT @Ids=N'['+STRING_AGG(CONVERT(nvarchar(max),AdmissionId),N',')+N']',@Source=STRING_AGG(CONVERT(nvarchar(max),ChargeId),N',') FROM @Fixture;
SELECT @Xml=(SELECT N'Bill' DocumentKind,1000 Amount,2000 TotalPaid,@Source BillSourceKey FROM @Fixture ORDER BY AdmissionId FOR XML PATH('FeeReceiptData'),ROOT('NewDataSet'),TYPE);
DECLARE @BillResult TABLE(BillNumber nvarchar(40),IssuedAt datetime2(0),SnapshotXml nvarchar(max));
DECLARE @BeforePayments int=(SELECT COUNT(*) FROM dbo.FeePayments),@BeforeCredit int=(SELECT COUNT(*) FROM dbo.StudentCreditEntries);
INSERT @BillResult EXEC dbo.IssueFeeBill @User,@Primary,@Ids,@Xml;
DECLARE @Bill nvarchar(40)=(SELECT BillNumber FROM @BillResult),@Issued datetime2(0)=(SELECT IssuedAt FROM @BillResult);
IF @Bill NOT LIKE N'FB-%-00001' THROW 51000,'Two students did not receive a family bill number.',1;
DELETE @BillResult;
INSERT @BillResult EXEC dbo.IssueFeeBill @User,@Primary,@Ids,@Xml;
IF (SELECT BillNumber FROM @BillResult)<>@Bill OR (SELECT IssuedAt FROM @BillResult)<>@Issued
 THROW 51000,'Reprinting changed a bill number or issue date.',1;
IF @BeforePayments<>(SELECT COUNT(*) FROM dbo.FeePayments) OR @BeforeCredit<>(SELECT COUNT(*) FROM dbo.StudentCreditEntries)
 THROW 51000,'Issuing a bill changed financial movements.',1;
SET @Checks+=3;

DECLARE @OneId nvarchar(max)=CONCAT(N'[',@Primary,N']'),@OneCharge bigint=(SELECT ChargeId FROM @Fixture WHERE AdmissionId=@Primary),@Enrollment int=(SELECT EnrollmentId FROM @Fixture WHERE AdmissionId=@Primary);
SET @Source=CONVERT(nvarchar(max),@OneCharge);
SET @Xml=(SELECT N'Bill' DocumentKind,1000 Amount,1000 TotalPaid,@Source BillSourceKey FOR XML PATH('FeeReceiptData'),ROOT('NewDataSet'),TYPE);
DELETE @BillResult;
INSERT @BillResult EXEC dbo.IssueFeeBill @User,@Primary,@OneId,@Xml;
IF (SELECT BillNumber FROM @BillResult) NOT LIKE N'B-%-00001' THROW 51000,'One student did not receive an individual bill number.',1;
SET @Checks+=1;

DECLARE @Alloc dbo.FamilyFeeAllocationType;
INSERT @Alloc SELECT AdmissionId,EnrollmentId,ChargeId,10 FROM @Fixture;
DECLARE @BeforePayment int=COALESCE((SELECT MAX(FeePaymentId) FROM dbo.FeePayments),0);
EXEC dbo.ProcessFamilyFeeTransaction @TransactionType=N'Collection',@TransactionDate=@Today,@PaymentMethod=N'Cash',@ReferenceNumber=NULL,
 @Remarks=N'Disposable fixture',@Allocations=@Alloc,@UserId=@User,@ReceivedAmount=20,@IncludedStudentsJson=@Ids;
IF (SELECT COUNT(DISTINCT ReceiptNumber) FROM dbo.FeePayments WHERE FeePaymentId>@BeforePayment)<>1
 OR EXISTS(SELECT 1 FROM dbo.FeePayments WHERE FeePaymentId>@BeforePayment AND ReceiptNumber NOT LIKE N'FR-%-00001')
 THROW 51000,'Family payments did not share their FR number.',1;
DECLARE @PayIds nvarchar(max)=(SELECT N'['+STRING_AGG(CONVERT(nvarchar(max),FeePaymentId),N',')+N']' FROM dbo.FeePayments WHERE FeePaymentId>@BeforePayment);
DECLARE @Lines nvarchar(max)=(SELECT ChargeId,AdmissionId,EnrollmentId,1000 AmountBefore,0 Discount,10 Cash,0 Credit FROM @Fixture FOR JSON PATH);
DECLARE @Receipt TABLE(Id uniqueidentifier,ReceiptNumber nvarchar(40),AdmissionId int,Balance decimal(12,2));
INSERT @Receipt EXEC dbo.IssueCollectionReceipt @User,@Primary,@Today,N'Cash',N'Fixture',@Ids,@PayIds,@Lines,20,0,0;
IF (SELECT ReceiptNumber FROM @Receipt) NOT LIKE N'FR-%-00001'
 OR EXISTS(SELECT 1 FROM @Fixture WHERE dbo.GetFeeChargeBalance(ChargeId)<>990) THROW 51000,'Family receipt changed the payment or its balances.',1;
SET @Checks+=2;

DECLARE @Single dbo.FeePaymentAllocationType;INSERT @Single VALUES(@OneCharge,10);
SET @BeforePayment=COALESCE((SELECT MAX(FeePaymentId) FROM dbo.FeePayments),0);
EXEC dbo.ProcessFeeTransaction @GroupReceiptNumber=NULL,@AdmissionId=@Primary,@EnrollmentId=@Enrollment,@TransactionType=N'Collection',@Amount=10,
 @TransactionDate=@Today,@PaymentMethod=N'Cash',@ReferenceNumber=NULL,@Remarks=N'Fixture',@FeePaymentId=NULL,@BillingChargeId=NULL,@Allocations=@Single,@UserId=@User;
SET @PayIds=(SELECT N'['+STRING_AGG(CONVERT(nvarchar(max),FeePaymentId),N',')+N']' FROM dbo.FeePayments WHERE FeePaymentId>@BeforePayment);
SET @Lines=(SELECT @OneCharge ChargeId,@Primary AdmissionId,@Enrollment EnrollmentId,990 AmountBefore,0 Discount,10 Cash,0 Credit FOR JSON PATH);
DELETE @Receipt;
INSERT @Receipt EXEC dbo.IssueCollectionReceipt @User,@Primary,@Today,N'Cash',N'Fixture',@OneId,@PayIds,@Lines,10,0,0;
IF (SELECT ReceiptNumber FROM @Receipt) NOT LIKE N'R-%-00001' OR dbo.GetFeeChargeBalance(@OneCharge)<>980
 THROW 51000,'Individual cash receipt prefix or balance is wrong.',1;
SET @Checks+=1;

DECLARE @Request uniqueidentifier=NEWID();
EXEC dbo.ProcessStudentCredit @UserId=@User,@AdmissionId=@Primary,@Action=N'Deposit',@Amount=100,@RequestId=@Request,@Reason=N'Disposable fixture',@TransactionDate=@Today;
IF NOT EXISTS(SELECT 1 FROM dbo.StudentCreditEntries C JOIN dbo.FeePayments P ON P.FeePaymentId=C.FeePaymentId
 WHERE C.RequestId=@Request AND P.ReceiptNumber LIKE N'R-%-00002') THROW 51000,'Explicit credit deposits did not use the individual receipt sequence.',1;
DECLARE @Available decimal(12,2)=(SELECT SUM(Amount) FROM dbo.StudentCreditEntries WHERE AdmissionId=@Primary);
DECLARE @Credits nvarchar(max)=(SELECT @OneCharge ChargeId,@Primary AdmissionId,@Enrollment EnrollmentId,10 Amount,980 ExpectedBalance,@Available ExpectedCredit FOR JSON PATH);
SET @BeforePayment=COALESCE((SELECT MAX(FeePaymentId) FROM dbo.FeePayments),0);
EXEC dbo.ApplyCollectionCredit @UserId=@User,@TransactionDate=@Today,@CreditsJson=@Credits;
SET @PayIds=(SELECT N'['+STRING_AGG(CONVERT(nvarchar(max),FeePaymentId),N',')+N']' FROM dbo.FeePayments WHERE FeePaymentId>@BeforePayment);
SET @Lines=(SELECT @OneCharge ChargeId,@Primary AdmissionId,@Enrollment EnrollmentId,980 AmountBefore,0 Discount,0 Cash,10 Credit FOR JSON PATH);
DELETE @Receipt;
INSERT @Receipt EXEC dbo.IssueCollectionReceipt @User,@Primary,@Today,N'Account Credit',N'Fixture',@OneId,@PayIds,@Lines,0,10,0;
IF (SELECT ReceiptNumber FROM @Receipt) NOT LIKE N'R-%-00003' OR dbo.GetFeeChargeBalance(@OneCharge)<>970
 THROW 51000,'Credit-only settlement did not receive a normal receipt number.',1;
SET @Checks+=2;

DECLARE @Other int=(SELECT AdmissionId FROM @Fixture WHERE AdmissionId<>@Primary),@OtherCharge bigint=(SELECT ChargeId FROM @Fixture WHERE AdmissionId<>@Primary);
SET @Request=NEWID();
EXEC dbo.ProcessStudentCredit @UserId=@User,@AdmissionId=@Other,@Action=N'Deposit',@Amount=100,@RequestId=@Request,@Reason=N'Disposable fixture',@TransactionDate=@Today;
SET @BeforePayment=COALESCE((SELECT MAX(FeePaymentId) FROM dbo.FeePayments),0);
SET @Credits=(SELECT F.ChargeId,F.AdmissionId,F.EnrollmentId,10 Amount,dbo.GetFeeChargeBalance(F.ChargeId) ExpectedBalance,
 (SELECT SUM(Amount) FROM dbo.StudentCreditEntries WHERE AdmissionId=F.AdmissionId) ExpectedCredit FROM @Fixture F FOR JSON PATH);
EXEC dbo.ApplyCollectionCredit @UserId=@User,@TransactionDate=@Today,@CreditsJson=@Credits;
SET @PayIds=(SELECT N'['+STRING_AGG(CONVERT(nvarchar(max),FeePaymentId),N',')+N']' FROM dbo.FeePayments WHERE FeePaymentId>@BeforePayment);
SET @Lines=(SELECT ChargeId,AdmissionId,EnrollmentId,CASE WHEN AdmissionId=@Primary THEN 970 ELSE 990 END AmountBefore,0 Discount,0 Cash,10 Credit FROM @Fixture FOR JSON PATH);
DELETE @Receipt;
INSERT @Receipt EXEC dbo.IssueCollectionReceipt @User,@Primary,@Today,N'Account Credit',N'Fixture',@Ids,@PayIds,@Lines,0,20,0;
IF (SELECT ReceiptNumber FROM @Receipt) NOT LIKE N'FR-%-00002' THROW 51000,'Credit-only family settlement did not receive an FR number.',1;
SET @Checks+=1;

SET @BeforePayment=COALESCE((SELECT MAX(FeePaymentId) FROM dbo.FeePayments),0);
SET @Credits=(SELECT F.ChargeId,F.AdmissionId,F.EnrollmentId,5 Amount,dbo.GetFeeChargeBalance(F.ChargeId) ExpectedBalance,
 (SELECT SUM(Amount) FROM dbo.StudentCreditEntries WHERE AdmissionId=F.AdmissionId) ExpectedCredit FROM @Fixture F WHERE AdmissionId=@Other FOR JSON PATH);
EXEC dbo.ApplyCollectionCredit @UserId=@User,@TransactionDate=@Today,@CreditsJson=@Credits;
DELETE @Alloc;INSERT @Alloc SELECT AdmissionId,EnrollmentId,ChargeId,5 FROM @Fixture WHERE AdmissionId=@Primary;
EXEC dbo.ProcessFamilyFeeTransaction @TransactionType=N'Collection',@TransactionDate=@Today,@PaymentMethod=N'Cash',@ReferenceNumber=NULL,
 @Remarks=N'Disposable fixture',@Allocations=@Alloc,@UserId=@User,@ReceivedAmount=5,@IncludedStudentsJson=@Ids;
SET @PayIds=(SELECT N'['+STRING_AGG(CONVERT(nvarchar(max),FeePaymentId),N',')+N']' FROM dbo.FeePayments WHERE FeePaymentId>@BeforePayment);
SET @Lines=(SELECT ChargeId,AdmissionId,EnrollmentId,CASE WHEN AdmissionId=@Primary THEN 960 ELSE 980 END AmountBefore,0 Discount,
 CASE WHEN AdmissionId=@Primary THEN 5 ELSE 0 END Cash,CASE WHEN AdmissionId=@Other THEN 5 ELSE 0 END Credit FROM @Fixture FOR JSON PATH);
DELETE @Receipt;
INSERT @Receipt EXEC dbo.IssueCollectionReceipt @User,@Primary,@Today,N'Cash',N'Fixture',@Ids,@PayIds,@Lines,5,5,0;
IF (SELECT ReceiptNumber FROM @Receipt) NOT LIKE N'FR-%-00003' OR
 EXISTS(SELECT 1 FROM dbo.FeePayments WHERE FeePaymentId>@BeforePayment AND IsCreditApplication=0 AND ReceiptNumber NOT LIKE N'FR-%-00003')
 THROW 51000,'Mixed family receipt with only one cash-paying child was misclassified.',1;
IF (SELECT LastNumber FROM dbo.FinanceDocumentCounters WHERE DocumentType='R' AND DocumentYear=YEAR(@Today))<>4
 THROW 51000,'Mixed family collection consumed an individual receipt number.',1;
SET @Checks+=2;

SET @Xml=(SELECT N'Bill' DocumentKind,955 Amount,955 TotalPaid,@Source BillSourceKey FOR XML PATH('FeeReceiptData'),ROOT('NewDataSet'),TYPE);
DELETE @BillResult;
INSERT @BillResult EXEC dbo.IssueFeeBill @User,@Primary,@OneId,@Xml;
IF (SELECT BillNumber FROM @BillResult) NOT LIKE N'B-%-00002' THROW 51000,'Changed dues reused the earlier bill.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.IssuedFeeBills WHERE BillNumber LIKE N'B-%-00001'
 AND SnapshotXml.value('(/*/FeeReceiptData/TotalPaid/text())[1]','decimal(12,2)')=1000)
 THROW 51000,'The earlier issued bill was altered by later collection.',1;
SET @Checks+=2;
-- A stale client snapshot must be rejected before it can become an issued bill.
SET @Xml=(SELECT N'Bill' DocumentKind,1000 Amount,1000 TotalPaid,@Source BillSourceKey FOR XML PATH('FeeReceiptData'),ROOT('NewDataSet'),TYPE);
BEGIN TRY
 EXEC dbo.IssueFeeBill @User,@Primary,@OneId,@Xml;
 THROW 51000,'A stale bill balance was accepted.',1;
END TRY
BEGIN CATCH
 IF ERROR_NUMBER()<>54942 THROW;
 SET @Checks+=1;
END CATCH;
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
IF EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE Remarks=N'Disposable document-number fixture')
 THROW 51000,'The disposable financial fixture did not roll back.',1;
PRINT CONCAT(N'PASS: ',@Checks+1,N' SQL document-number, bill and collection checks; financial fixture rolled back.');
