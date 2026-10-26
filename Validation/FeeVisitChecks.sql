SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON;
DECLARE @Stage nvarchar(100)=N'visit financial sequence';
BEGIN TRY
-- The actual three visits supplied by the owner, projected without rewriting them.
SELECT R.ReceiptNumber,V.* INTO #ActualVisits FROM dbo.CollectionReceipts R
CROSS APPLY dbo.FeeVisitLines(JSON_QUERY(R.SnapshotJson,'$.Lines'),R.VisitSequence) V
WHERE R.ReceiptNumber IN(N'FR-2026-00012',N'FR-2026-00013',N'FR-2026-00014');
IF (SELECT COUNT(DISTINCT ReceiptNumber) FROM #ActualVisits)<>3 THROW 51000,'The three actual visit fixtures are missing.',1;
IF (SELECT SUM(GrossAmount) FROM #ActualVisits WHERE ReceiptNumber=N'FR-2026-00012')<>23800
 OR (SELECT SUM(Discount) FROM #ActualVisits WHERE ReceiptNumber=N'FR-2026-00012')<>5400
 OR (SELECT SUM(BalanceAfter) FROM #ActualVisits WHERE ReceiptNumber=N'FR-2026-00012')<>14400
 THROW 51000,'The actual first family visit did not reconcile.',1;
IF (SELECT SUM(GrossAmount) FROM #ActualVisits WHERE ReceiptNumber=N'FR-2026-00013')<>14400
 OR (SELECT SUM(Discount) FROM #ActualVisits WHERE ReceiptNumber=N'FR-2026-00013')<>0
 OR (SELECT SUM(BalanceAfter) FROM #ActualVisits WHERE ReceiptNumber=N'FR-2026-00013')<>7400
 THROW 51000,'The actual second visit repeated settled fees or old concessions.',1;
IF (SELECT SUM(GrossAmount) FROM #ActualVisits WHERE ReceiptNumber=N'FR-2026-00014')<>7400
 OR (SELECT SUM(Discount) FROM #ActualVisits WHERE ReceiptNumber=N'FR-2026-00014')<>5400
 OR (SELECT SUM(BalanceAfter) FROM #ActualVisits WHERE ReceiptNumber=N'FR-2026-00014')<>0
 THROW 51000,'The actual third visit did not show only further concessions.',1;
IF NOT EXISTS(SELECT 1 FROM #ActualVisits WHERE ReceiptNumber=N'FR-2026-00014' AND AdmissionId=3944 AND BaseAmount=600 AND Discount=200 AND Cash=400)
 OR (SELECT SUM(BaseAmount) FROM #ActualVisits WHERE ReceiptNumber=N'FR-2026-00014' AND AdmissionId=4623)<>6800
 OR (SELECT SUM(Discount) FROM #ActualVisits WHERE ReceiptNumber=N'FR-2026-00014' AND AdmissionId=4623)<>5200
 OR (SELECT SUM(Cash) FROM #ActualVisits WHERE ReceiptNumber=N'FR-2026-00014' AND AdmissionId=4623)<>1600
 OR EXISTS(SELECT 1 FROM #ActualVisits WHERE ReceiptNumber=N'FR-2026-00014' AND AdmissionId=5239)
 THROW 51000,'The real student carry-forward figures are incorrect.',1;
SELECT N'PASS: actual visits 12/13/14 carry 14400 -> 7400 -> 0, with only new concessions.';

DECLARE @Actor int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator');
DECLARE @Admission int=17,@Enrollment int=(SELECT TOP(1) EnrollmentId FROM Enrollments WHERE AdmissionId=17 ORDER BY EnrollmentId DESC),@Now datetime2(0)=SYSDATETIME();
DECLARE @Issue TABLE(CollectionReceiptId uniqueidentifier,ReceiptNumber nvarchar(40),AdmissionId int,Balance decimal(12,2));
DECLARE @Adjustment TABLE(BillingChargeId bigint,Difference decimal(12,2),ReleasedPaidAmount decimal(12,2));
BEGIN TRAN;
INSERT dbo.FeeTypes(FeeTypeCode,FeeTypeName,IsMandatory,IsTransportLinked)
 VALUES(CONCAT(N'VIS-',LEFT(CONVERT(nvarchar(36),NEWID()),8)),N'Visit fixture fee',0,0);
DECLARE @Fee int=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.StudentFeeAssignments(AdmissionId,EnrollmentId,FeeTypeId,Amount,DiscountAmount,Frequency)
 VALUES(@Admission,@Enrollment,@Fee,1600,400,N'As Needed');
DECLARE @Assignment int=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
 VALUES(@Assignment,@Enrollment,@Admission,@Fee,CONVERT(date,GETDATE()),CONVERT(date,GETDATE()),1600,400,N'Manual');
DECLARE @Bill bigint=CONVERT(bigint,SCOPE_IDENTITY()),@Payment int;
INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Status,CreatedByUserId)
 VALUES(@Admission,@Enrollment,CONVERT(date,GETDATE()),CONCAT(N'VIS-',LEFT(CONVERT(nvarchar(36),NEWID()),20)),500,N'Cash',N'Completed',@Actor);
SET @Payment=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@Payment,@Admission,@Bill,500);
DECLARE @Payments nvarchar(max)=CONCAT(N'[',@Payment,N']'),@Lines nvarchar(max)=CONCAT(N'[{"ChargeId":',@Bill,N',"AdmissionId":17,"EnrollmentId":',@Enrollment,N',"AmountBefore":1200,"Discount":0,"Cash":500,"Credit":0}]');
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Now,N'Cash',N'First visit fixture',N'[17]',@Payments,@Lines,500,0,0;
DECLARE @FirstId uniqueidentifier=(SELECT CollectionReceiptId FROM @Issue),@First nvarchar(max)=(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=(SELECT CollectionReceiptId FROM @Issue));
IF NOT EXISTS(SELECT 1 FROM OPENJSON(@First,'$.Lines') WITH(ChargeId bigint,GrossAmount decimal(12,2),Discount decimal(12,2),Cash decimal(12,2),BalanceAfter decimal(12,2)) V
 WHERE ChargeId=@Bill AND GrossAmount=1600 AND Discount=400 AND Cash=500 AND BalanceAfter=700)
 THROW 51000,'First visit did not show the new fee, concession and partial payment.',1;
SELECT N'PASS: first visit retains the full new fee and its initial concession.';

DECLARE @Request uniqueidentifier=NEWID();
INSERT @Adjustment EXEC dbo.AdjustBillingChargeRate @Actor,@Bill,1600,N'Credit',N'Further visit discount',@Request,1200,700,600,0;
INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Status,CreatedByUserId)
 VALUES(@Admission,@Enrollment,CONVERT(date,GETDATE()),CONCAT(N'VIS-',LEFT(CONVERT(nvarchar(36),NEWID()),20)),300,N'Cash',N'Completed',@Actor);
SET @Payment=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@Payment,@Admission,@Bill,300);
SET @Payments=CONCAT(N'[',@Payment,N']');SET @Lines=CONCAT(N'[{"ChargeId":',@Bill,N',"AdmissionId":17,"EnrollmentId":',@Enrollment,N',"AmountBefore":500,"Discount":0,"Cash":300,"Credit":0}]');
DELETE @Issue;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Now,N'Cash',N'Second visit fixture',N'[17]',@Payments,@Lines,300,0,0;
DECLARE @Snapshot nvarchar(max)=(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=(SELECT CollectionReceiptId FROM @Issue));
IF NOT EXISTS(SELECT 1 FROM OPENJSON(@Snapshot,'$.Lines') WITH(ChargeId bigint,BaseAmount decimal(12,2),GrossAmount decimal(12,2),Discount decimal(12,2),Cash decimal(12,2),BalanceAfter decimal(12,2)) V
 WHERE ChargeId=@Bill AND BaseAmount=700 AND GrossAmount=700 AND Discount=200 AND Cash=300 AND BalanceAfter=200)
 THROW 51000,'Second visit repeated the original fee or its original concession.',1;
SELECT N'PASS: second visit starts at 700, deducts only the additional 200, receives 300 and carries 200.';

INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
 VALUES(@Assignment,@Enrollment,@Admission,@Fee,CONVERT(date,GETDATE()),CONVERT(date,GETDATE()),900,100,N'Manual');
DECLARE @NewBill bigint=CONVERT(bigint,SCOPE_IDENTITY());
INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Status,CreatedByUserId)
 VALUES(@Admission,@Enrollment,CONVERT(date,GETDATE()),CONCAT(N'VIS-',LEFT(CONVERT(nvarchar(36),NEWID()),20)),700,N'Cash',N'Completed',@Actor);
SET @Payment=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@Payment,@Admission,@Bill,200),(@Payment,@Admission,@NewBill,500);
SET @Payments=CONCAT(N'[',@Payment,N']');
SET @Lines=CONCAT(N'[{"ChargeId":',@Bill,N',"AdmissionId":17,"EnrollmentId":',@Enrollment,N',"AmountBefore":200,"Discount":0,"Cash":200,"Credit":0},{"ChargeId":',@NewBill,N',"AdmissionId":17,"EnrollmentId":',@Enrollment,N',"AmountBefore":800,"Discount":0,"Cash":500,"Credit":0}]');
DELETE @Issue;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Now,N'Cash',N'Third visit fixture',N'[17]',@Payments,@Lines,700,0,0;
SET @Snapshot=(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=(SELECT CollectionReceiptId FROM @Issue));
IF (SELECT SUM(GrossAmount) FROM OPENJSON(@Snapshot,'$.Lines') WITH(ChargeId bigint,GrossAmount decimal(12,2)) V WHERE ChargeId IN(@Bill,@NewBill))<>1100
 OR (SELECT SUM(Discount) FROM OPENJSON(@Snapshot,'$.Lines') WITH(ChargeId bigint,Discount decimal(12,2)) V WHERE ChargeId IN(@Bill,@NewBill))<>100
 OR (SELECT SUM(BalanceAfter) FROM OPENJSON(@Snapshot,'$.Lines') WITH(ChargeId bigint,BalanceAfter decimal(12,2)) V WHERE ChargeId IN(@Bill,@NewBill))<>300
 THROW 51000,'New fees were not combined correctly with carried outstanding.',1;
SELECT N'PASS: a new 900 fee and 100 concession combine with the old 200 balance; old concessions stay in history.';

SET @Request=NEWID();DELETE @Adjustment;
INSERT @Adjustment EXEC dbo.AdjustBillingChargeRate @Actor,@NewBill,900,N'Credit',N'One-time fee waiver from Fee Collection',@Request,800,300,400,0;
DELETE @Issue;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Now,N'Fee changes',N'Waiver acknowledgement',N'[17]',N'[]',N'[]',0,0,0,1;
SET @Snapshot=(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=(SELECT CollectionReceiptId FROM @Issue));
IF NOT EXISTS(SELECT 1 FROM OPENJSON(@Snapshot,'$.Lines') WITH(ChargeId bigint,BaseAmount decimal(12,2),Discount decimal(12,2),BalanceAfter decimal(12,2)) V WHERE ChargeId=@NewBill AND BaseAmount=300 AND Discount=300 AND BalanceAfter=0)
 OR EXISTS(SELECT 1 FROM OPENJSON(@Snapshot,'$.Lines') WITH(ChargeId bigint) V WHERE ChargeId=@Bill)
 THROW 51000,'Waiving the remaining due repeated a settled fee or an old concession.',1;
DECLARE @Accounts nvarchar(max)=(SELECT * FROM dbo.FeeDocumentAccounts(N'[17]',N'[]',N'[]') FOR JSON PATH),@Before bigint=(SELECT MAX(VisitSequence)+1 FROM CollectionReceipts);
IF EXISTS(SELECT 1 FROM dbo.FeeVisitLines(@Accounts,@Before) WHERE ChargeId IN(@Bill,@NewBill)) THROW 51000,'A settled fee or waiver reappeared on the next bill.',1;
SELECT N'PASS: a final waiver shows only the remaining 300; settled fees disappear from subsequent bills.';

SET @Request=NEWID();DELETE @Adjustment;
INSERT @Adjustment EXEC dbo.AdjustBillingChargeRate @Actor,@NewBill,1100,N'Credit',N'Rate increased after settlement',@Request,500,0,400,0;
SET @Request=NEWID();DELETE @Adjustment;
INSERT @Adjustment EXEC dbo.AdjustBillingChargeRate @Actor,@NewBill,1100,N'Credit',N'Discount reduced after settlement',@Request,700,200,300,0;
SET @Accounts=(SELECT * FROM dbo.FeeDocumentAccounts(N'[17]',N'[]',N'[]') FOR JSON PATH);
IF NOT EXISTS(SELECT 1 FROM dbo.FeeVisitLines(@Accounts,@Before) WHERE ChargeId=@NewBill AND BaseAmount=0 AND RateAdjustment=200 AND DiscountReversed=100 AND GrossAmount=300 AND Discount=0 AND BalanceAfter=300)
 THROW 51000,'A later increase or withdrawn concession did not reopen the correct due.',1;
IF @First<>(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=@FirstId) THROW 51000,'Later visits changed the original snapshot.',1;
SELECT N'PASS: later rate increases and withdrawn discounts reopen only the additional due; original receipts remain immutable.';
-- A freshly issued bill must validate against the same visit values as the receipt.
DECLARE @BillXml xml=(SELECT (SELECT STRING_AGG(CONVERT(nvarchar(max),ChargeId),N',') FROM dbo.FeeVisitLines(@Accounts,@Before)) BillSourceKey,
 (SELECT SUM(BalanceAfter) FROM dbo.FeeVisitLines(@Accounts,@Before)) TotalPaid,
 ChargeId BillChargeId,CONVERT(bit,1) BillIsVisit,GrossAmount BillGrossAmount,Discount BillLineDiscount,
 AccountGrossAmount BillAccountGross,AccountDiscountAmount BillAccountDiscount,BalanceAfter Amount
 FROM dbo.FeeVisitLines(@Accounts,@Before) FOR XML PATH('FeeReceiptData'),ROOT('NewDataSet'),TYPE);
DECLARE @SavedBill TABLE(BillNumber nvarchar(40),IssuedAt datetime2(0),SnapshotXml nvarchar(max));
INSERT @SavedBill EXEC dbo.IssueFeeBill @Actor,17,N'[17]',@BillXml;
IF (SELECT COUNT(*) FROM @SavedBill)<>1 THROW 51000,'The visit-based bill did not issue successfully.',1;
SELECT N'PASS: SQL issues a bill using carried due and subsequent adjustments, matching the receipt rules.';

DECLARE @ExtraCash bigint=(SELECT MIN(BillingChargeId) FROM BillingCharges WHERE CorrectionOfBillingChargeId=@NewBill),
 @ExtraCredit bigint=(SELECT MAX(BillingChargeId) FROM BillingCharges WHERE CorrectionOfBillingChargeId=@NewBill),@CashPayment int,@CreditPayment int;
INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Status,CreatedByUserId)
 VALUES(@Admission,@Enrollment,CONVERT(date,GETDATE()),CONCAT(N'VIS-',LEFT(CONVERT(nvarchar(36),NEWID()),20)),200,N'Cash',N'Completed',@Actor);
SET @CashPayment=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@CashPayment,@Admission,@ExtraCash,200);
INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Status,CreatedByUserId,IsCreditApplication)
 VALUES(@Admission,@Enrollment,CONVERT(date,GETDATE()),CONCAT(N'VIS-CR-',LEFT(CONVERT(nvarchar(36),NEWID()),20)),100,N'Account Credit',N'Completed',@Actor,1);
SET @CreditPayment=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@CreditPayment,@Admission,@ExtraCredit,100);
SET @Payments=CONCAT(N'[',@CashPayment,N',',@CreditPayment,N']');
SET @Lines=CONCAT(N'[{"ChargeId":',@ExtraCash,N',"AdmissionId":17,"EnrollmentId":',@Enrollment,N',"AmountBefore":200,"Discount":0,"Cash":200,"Credit":0},{"ChargeId":',@ExtraCredit,N',"AdmissionId":17,"EnrollmentId":',@Enrollment,N',"AmountBefore":100,"Discount":0,"Cash":0,"Credit":100}]');
DELETE @Issue;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Now,N'Cash',N'Mixed settlement fixture',N'[17]',@Payments,@Lines,200,100,0;
SET @Snapshot=(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=(SELECT CollectionReceiptId FROM @Issue));
IF NOT EXISTS(SELECT 1 FROM OPENJSON(@Snapshot,'$.Lines') WITH(ChargeId bigint,GrossAmount decimal(12,2),Discount decimal(12,2),Cash decimal(12,2),Credit decimal(12,2),BalanceAfter decimal(12,2)) V
 WHERE ChargeId=@NewBill AND GrossAmount=300 AND Discount=0 AND Cash=200 AND Credit=100 AND BalanceAfter=0)
 THROW 51000,'Mixed cash and credit did not settle only the reopened due.',1;
SELECT N'PASS: mixed cash and credit settle only new due without repeating historical concessions.';

INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Status,CreatedByUserId,RelatedFeePaymentId)
 VALUES(@Admission,@Enrollment,CONVERT(date,GETDATE()),CONCAT(N'VIS-RF-',LEFT(CONVERT(nvarchar(36),NEWID()),20)),-50,N'Refund',N'Completed',@Actor,@CashPayment);
SET @Payment=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@Payment,@Admission,@ExtraCash,-50);
DELETE @Issue;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Now,N'Fee changes',N'Refund reopening fixture',N'[17]',N'[]',N'[]',0,0,0,1;
SET @Snapshot=(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=(SELECT CollectionReceiptId FROM @Issue));
IF NOT EXISTS(SELECT 1 FROM OPENJSON(@Snapshot,'$.Lines') WITH(ChargeId bigint,BaseAmount decimal(12,2),SettlementAdjustment decimal(12,2),Discount decimal(12,2),BalanceAfter decimal(12,2)) V
 WHERE ChargeId=@NewBill AND BaseAmount=0 AND SettlementAdjustment=50 AND Discount=0 AND BalanceAfter=50)
 THROW 51000,'The refund did not reopen only its new balance.',1;
SELECT N'PASS: refund reopens only the refunded 50 and does not revive settled fees or discounts.';

SET @Request=NEWID();DELETE @Adjustment;
INSERT @Adjustment EXEC dbo.AdjustBillingChargeRate @Actor,@NewBill,1050,N'Credit',N'Rate reduction clears remaining due',@Request,800,50,300,0;
DELETE @Issue;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Now,N'Fee changes',N'Rate reduction fixture',N'[17]',N'[]',N'[]',0,0,0,1;
SET @Request=NEWID();DELETE @Adjustment;
INSERT @Adjustment EXEC dbo.AdjustBillingChargeRate @Actor,@NewBill,1000,N'Credit',N'Paid reduction released to credit',@Request,750,0,300,0;
DELETE @Issue;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Now,N'Fee changes',N'Released credit fixture',N'[17]',N'[]',N'[]',0,0,0,1;
SET @Snapshot=(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=(SELECT CollectionReceiptId FROM @Issue));
IF NOT EXISTS(SELECT 1 FROM OPENJSON(@Snapshot,'$.Lines') WITH(ChargeId bigint,BaseAmount decimal(12,2),RateAdjustment decimal(12,2),SettlementAdjustment decimal(12,2),GrossAmount decimal(12,2),BalanceAfter decimal(12,2)) V
 WHERE ChargeId=@NewBill AND BaseAmount=0 AND RateAdjustment=-50 AND SettlementAdjustment=50 AND GrossAmount=0 AND BalanceAfter=0)
 THROW 51000,'A paid reduction did not disclose both the rate change and released settlement.',1;
IF @First<>(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=@FirstId) THROW 51000,'Later adjustments changed the first receipt.',1;
SELECT N'PASS: a paid reduction preserves offsetting fee/settlement adjustments without inventing a cash payment.';

-- Existing zero-posting policy: disclose a fully waived monthly agreement once
-- for each period, even when there is deliberately no BillingCharges row.
DECLARE @Month date=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1);
UPDATE dbo.Enrollments SET BillingStartDate=@Month,BillingReady=1,PlacementConfirmedAt=COALESCE(PlacementConfirmedAt,SYSDATETIME()) WHERE EnrollmentId=@Enrollment;
INSERT dbo.FeeTypes(FeeTypeCode,FeeTypeName,IsMandatory,IsTransportLinked)
 VALUES(CONCAT(N'FREE-',LEFT(CONVERT(nvarchar(36),NEWID()),8)),N'Visit fully waived plan',0,0);
DECLARE @FreeType int=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.StudentFeeAssignments(AdmissionId,EnrollmentId,FeeTypeId,Amount,DiscountAmount,Frequency,EffectiveFrom,ConcessionLabel)
 VALUES(@Admission,@Enrollment,@FreeType,500,500,N'Monthly',@Month,N'Waived');
DECLARE @FreeAssignment int=CONVERT(int,SCOPE_IDENTITY());
SET @Accounts=(SELECT * FROM dbo.FeeDocumentAccounts(N'[17]',N'[]',N'[]') FOR JSON PATH);
SET @Before=(SELECT MAX(VisitSequence)+1 FROM CollectionReceipts);
IF NOT EXISTS(SELECT 1 FROM dbo.FeeVisitLines(@Accounts,@Before) WHERE ChargeId=-@FreeAssignment AND GrossAmount=500 AND Discount=500 AND BalanceAfter=0)
 THROW 51000,'The fully waived unposted monthly fee was hidden.',1;
DELETE @Issue;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Now,N'Fee changes',N'Waived plan fixture',N'[17]',N'[]',N'[]',0,0,0,1;
SET @Accounts=(SELECT * FROM dbo.FeeDocumentAccounts(N'[17]',N'[]',N'[]') FOR JSON PATH);
SET @Before=(SELECT MAX(VisitSequence)+1 FROM CollectionReceipts);
IF EXISTS(SELECT 1 FROM dbo.FeeVisitLines(@Accounts,@Before) WHERE ChargeId=-@FreeAssignment)
 THROW 51000,'An acknowledged monthly waiver was repeated in the same period.',1;
DECLARE @NextMonth nvarchar(max)=(SELECT -CONVERT(bigint,@FreeAssignment) ChargeId,@Admission AdmissionId,@Enrollment EnrollmentId,
 CONVERT(decimal(12,2),500) GrossAmount,CONVERT(decimal(12,2),500) Discount,CONVERT(decimal(12,2),0) PreviouslyPaid,
 CONVERT(decimal(12,2),0) Cash,CONVERT(decimal(12,2),0) Credit,CONVERT(decimal(12,2),0) BalanceAfter,
 DATEADD(month,1,@Month) FeePeriod,N'Visit fully waived plan' FeeTypeName FOR JSON PATH);
IF NOT EXISTS(SELECT 1 FROM dbo.FeeVisitLines(@NextMonth,@Before) WHERE ChargeId=-@FreeAssignment AND GrossAmount=500 AND Discount=500)
 THROW 51000,'The next monthly waiver was incorrectly consumed by the earlier month.',1;
SELECT N'PASS: fully waived unposted plans are disclosed once per month, with no repeated same-month concession.';

-- An older version-1 receipt omitted cumulative concession fields. Its recorded
-- balance and allocations still identify a further discount accurately.
INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
 VALUES(@Assignment,@Enrollment,@Admission,@Fee,CONVERT(date,GETDATE()),CONVERT(date,GETDATE()),1400,200,N'Manual');
DECLARE @LegacyBill bigint=CONVERT(bigint,SCOPE_IDENTITY()),@LegacyId uniqueidentifier=NEWID(),@LegacyNumber nvarchar(40)=CONCAT(N'LEG-',LEFT(CONVERT(nvarchar(36),NEWID()),20));
INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Status,CreatedByUserId)
 VALUES(@Admission,@Enrollment,CONVERT(date,GETDATE()),@LegacyNumber,500,N'Cash',N'Completed',@Actor);
SET @Payment=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@Payment,@Admission,@LegacyBill,500);
DECLARE @LegacyJson nvarchar(max)=CONCAT(N'{"Students":[{"AdmissionId":17,"AdmissionNumber":"17","StudentName":"Legacy fixture","FatherName":"Fixture","CashApplied":500,"CreditApplied":0,"DiscountApplied":0,"BalanceRemaining":700,"CreditRemaining":50}],"Lines":[{"ChargeId":',@LegacyBill,N',"AdmissionId":17,"EnrollmentId":',@Enrollment,N',"AmountBefore":1200,"Discount":0,"Cash":500,"Credit":0,"BalanceAfter":700}]}');
INSERT dbo.CollectionReceipts(CollectionReceiptId,ReceiptNumber,PrimaryAdmissionId,TransactionDate,CreatedByUserId,CashReceived,CashApplied,CreditApplied,DiscountApplied,BalanceRemaining,CreditRemaining,PaymentMethod,SnapshotJson,SnapshotVersion)
 VALUES(@LegacyId,@LegacyNumber,@Admission,@Now,@Actor,500,500,0,0,700,50,N'Cash',@LegacyJson,1);
INSERT dbo.CollectionReceiptPayments(CollectionReceiptId,FeePaymentId) VALUES(@LegacyId,@Payment);
UPDATE dbo.BillingCharges SET DiscountAmount=300 WHERE BillingChargeId=@LegacyBill;
INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Status,CreatedByUserId)
 VALUES(@Admission,@Enrollment,CONVERT(date,GETDATE()),CONCAT(N'VIS-',LEFT(CONVERT(nvarchar(36),NEWID()),20)),600,N'Cash',N'Completed',@Actor);
SET @Payment=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@Payment,@Admission,@LegacyBill,600);
SET @Payments=CONCAT(N'[',@Payment,N']');
SET @Lines=CONCAT(N'[{"ChargeId":',@LegacyBill,N',"AdmissionId":17,"EnrollmentId":',@Enrollment,N',"AmountBefore":600,"Discount":0,"Cash":600,"Credit":0}]');
DELETE @Issue;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Now,N'Cash',N'Legacy carry fixture',N'[17]',@Payments,@Lines,600,0,0;
SET @Snapshot=(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=(SELECT CollectionReceiptId FROM @Issue));
IF NOT EXISTS(SELECT 1 FROM OPENJSON(@Snapshot,'$.Lines') WITH(ChargeId bigint,BaseAmount decimal(12,2),Discount decimal(12,2),SettlementAdjustment decimal(12,2),Cash decimal(12,2),BalanceAfter decimal(12,2)) V
 WHERE ChargeId=@LegacyBill AND BaseAmount=700 AND Discount=100 AND SettlementAdjustment=0 AND Cash=600 AND BalanceAfter=0)
 THROW 51000,'Legacy carry-forward confused the further discount with an earlier payment.',1;
SELECT N'PASS: a legacy receipt carries 700, shows only a further 100 discount, receives 600 and clears correctly.';
ROLLBACK;

DECLARE @Review TABLE(ReviewToken varchar(64));
SET @Stage=N'concurrent acknowledgement after settlement';
INSERT @Review EXEC dbo.GetCollectionReviewState @Actor,17;
DECLARE @Token varchar(64)=(SELECT ReviewToken FROM @Review);
BEGIN TRAN;
DELETE @Issue;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Now,N'Fee changes',N'Concurrent acknowledgement',N'[17]',N'[]',N'[]',0,0,0,1;
BEGIN TRY
 EXEC dbo.GetCollectionReviewState @Actor,17,@Token;
 THROW 51000,'A changed visit boundary did not invalidate the loaded review.',1;
END TRY
BEGIN CATCH
 IF ERROR_NUMBER()<>53104 BEGIN IF @@TRANCOUNT>0 ROLLBACK; THROW; END;
 IF @@TRANCOUNT>0 ROLLBACK;
 SELECT N'PASS: a concurrent acknowledgement invalidates the old visit boundary before collection.';
END CATCH;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 DECLARE @Failure nvarchar(2048)=CONCAT(@Stage,N': ',ERROR_MESSAGE());
 THROW 51000,@Failure,1;
END CATCH;
