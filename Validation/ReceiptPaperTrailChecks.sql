SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON;
DECLARE @Actor int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator');
DECLARE @Admission int=17,@Enrollment int=(SELECT TOP(1) EnrollmentId FROM Enrollments WHERE AdmissionId=17 ORDER BY EnrollmentId DESC);
IF @Actor IS NULL OR @Enrollment IS NULL THROW 51000,'Admission 17 regression fixture or administrator is missing from the test copy.',1;
DECLARE @Date datetime2(0)=SYSDATETIME();
DECLARE @Issue TABLE(CollectionReceiptId uniqueidentifier,ReceiptNumber nvarchar(40),AdmissionId int,Balance decimal(12,2));
DECLARE @Existing nvarchar(max)=(SELECT SnapshotJson FROM CollectionReceipts WHERE ReceiptNumber=N'FR-2026-00009');
BEGIN TRAN;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Date,N'Fee changes',N'Test acknowledgement',N'[17,18,19,3272]',N'[]',N'[]',0,0,0,1;
DECLARE @Snapshot nvarchar(max)=(SELECT R.SnapshotJson FROM CollectionReceipts R JOIN @Issue I ON I.CollectionReceiptId=R.CollectionReceiptId);
IF EXISTS(SELECT 1 FROM BillingCharges B WHERE B.AdmissionId=17 AND B.BillingChargeId IN(52,53)
 AND NOT EXISTS(SELECT 1 FROM OPENJSON(@Snapshot,'$.Lines') WITH(ChargeId bigint,Discount decimal(12,2),GrossAmount decimal(12,2)) L
 WHERE L.ChargeId=B.BillingChargeId AND L.Discount=B.DiscountAmount AND L.GrossAmount=B.GrossAmount))
 THROW 51000,'Admission 17 fully waived fees disappeared from the acknowledgement.',1;
IF EXISTS(SELECT 1 FROM OPENJSON(@Snapshot,'$.Lines') WITH(GrossAmount decimal(12,2),Discount decimal(12,2),PreviouslyPaid decimal(12,2),Cash decimal(12,2),Credit decimal(12,2),BalanceAfter decimal(12,2)) L
 WHERE GrossAmount-Discount-PreviouslyPaid-Cash-Credit<>BalanceAfter)
 THROW 51000,'Admission 17 account lines do not reconcile.',1;
IF @Existing<>(SELECT SnapshotJson FROM CollectionReceipts WHERE ReceiptNumber=N'FR-2026-00009') THROW 51000,'An existing receipt was rewritten.',1;
SELECT N'PASS: admission 17 acknowledgement retains waivers, prior payments and original receipts.';
ROLLBACK;

-- A small isolated sequence exercises partial payment -> waiver -> settlement,
-- fee increases, credit application, and immutable reprints.
BEGIN TRAN;
INSERT dbo.FeeTypes(FeeTypeCode,FeeTypeName,IsMandatory,IsTransportLinked)
 VALUES(CONCAT(N'PAPER-',LEFT(CONVERT(nvarchar(36),NEWID()),8)),N'Paper trail fixture',0,0);
DECLARE @Fee int=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.StudentFeeAssignments(AdmissionId,EnrollmentId,FeeTypeId,Amount,DiscountAmount,Frequency)
 VALUES(@Admission,@Enrollment,@Fee,1600,400,N'As Needed');
DECLARE @Assignment int=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
 VALUES(@Assignment,@Enrollment,@Admission,@Fee,CONVERT(date,GETDATE()),CONVERT(date,GETDATE()),1600,400,N'Manual');
DECLARE @Bill bigint=CONVERT(bigint,SCOPE_IDENTITY()),@Payment int;
INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Status,CreatedByUserId)
 VALUES(@Admission,@Enrollment,CONVERT(date,GETDATE()),CONCAT(N'PT-',LEFT(CONVERT(nvarchar(36),NEWID()),20)),500,N'Cash',N'Completed',@Actor);
SET @Payment=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@Payment,@Admission,@Bill,500);
DECLARE @Payments nvarchar(max)=CONCAT(N'[',@Payment,N']'),@Lines nvarchar(max)=CONCAT(N'[{"ChargeId":',@Bill,N',"AdmissionId":17,"EnrollmentId":',@Enrollment,N',"AmountBefore":1200,"Discount":0,"Cash":500,"Credit":0}]');
DELETE @Issue;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Date,N'Cash',N'Partial fixture',N'[17]',@Payments,@Lines,500,0,0;
DECLARE @FirstId uniqueidentifier=(SELECT CollectionReceiptId FROM @Issue),@First nvarchar(max)=(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=(SELECT CollectionReceiptId FROM @Issue));
IF NOT EXISTS(SELECT 1 FROM OPENJSON(@First,'$.Lines') WITH(ChargeId bigint,GrossAmount decimal(12,2),Discount decimal(12,2),PreviouslyPaid decimal(12,2),Cash decimal(12,2),BalanceAfter decimal(12,2)) L
 WHERE ChargeId=@Bill AND GrossAmount=1600 AND Discount=400 AND PreviouslyPaid=0 AND Cash=500 AND BalanceAfter=700)
 THROW 51000,'Partial payment lost its original fee or concession.',1;
SELECT N'PASS: a partial collection preserves full fee, existing discount, new cash and remaining balance.';

DECLARE @Request uniqueidentifier=NEWID();
EXEC dbo.AdjustBillingChargeRate @Actor,@Bill,1600,N'Credit',N'One-time fee waiver from Fee Collection',@Request,1200,700,1100,0;
DELETE @Issue;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Date,N'Fee changes',N'Waiver fixture',N'[17]',N'[]',N'[]',0,0,0,1;
SET @Snapshot=(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=(SELECT CollectionReceiptId FROM @Issue));
IF NOT EXISTS(SELECT 1 FROM OPENJSON(@Snapshot,'$.Lines') WITH(ChargeId bigint,GrossAmount decimal(12,2),Discount decimal(12,2),PreviouslyPaid decimal(12,2),BalanceAfter decimal(12,2),ConcessionKind nvarchar(30)) L
 WHERE ChargeId=@Bill AND GrossAmount=1600 AND Discount=1100 AND PreviouslyPaid=500 AND BalanceAfter=0 AND ConcessionKind=N'Waived')
 THROW 51000,'Waiving after a partial payment hid the fee or counted previous cash as a discount.',1;
IF @First<>(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=@FirstId) THROW 51000,'A later waiver changed the original receipt.',1;
SELECT N'PASS: a waiver after partial payment stays visible without refunding cash or changing the old receipt.';

-- Increasing the agreed charge produces a correction charge, not a second fee.
SET @Request=NEWID();
EXEC dbo.AdjustBillingChargeRate @Actor,@Bill,1800,N'Credit',N'Fixture agreed rate increase',@Request,500,0,1100,0;
DELETE @Issue;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Date,N'Fee changes',N'Increase fixture',N'[17]',N'[]',N'[]',0,0,0,1;
SET @Snapshot=(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=(SELECT CollectionReceiptId FROM @Issue));
IF NOT EXISTS(SELECT 1 FROM OPENJSON(@Snapshot,'$.Lines') WITH(ChargeId bigint,GrossAmount decimal(12,2),Discount decimal(12,2),PreviouslyPaid decimal(12,2),BalanceAfter decimal(12,2)) L
 WHERE ChargeId=@Bill AND GrossAmount=1800 AND Discount=1100 AND PreviouslyPaid=500 AND BalanceAfter=200)
 THROW 51000,'A rate increase did not reconcile its correction chain.',1;
SELECT N'PASS: fee increases consolidate their correction charges and retain earlier payment and concession.';

-- Credit settles the correction charge; original cash and receipt stay fixed.
DECLARE @Extra bigint=(SELECT TOP(1) BillingChargeId FROM BillingCharges WHERE CorrectionOfBillingChargeId=@Bill ORDER BY BillingChargeId DESC);
INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Status,CreatedByUserId,IsCreditApplication)
 VALUES(@Admission,@Enrollment,CONVERT(date,GETDATE()),CONCAT(N'PT-CR-',LEFT(CONVERT(nvarchar(36),NEWID()),20)),200,N'Account Credit',N'Completed',@Actor,1);
SET @Payment=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@Payment,@Admission,@Extra,200);
SET @Payments=CONCAT(N'[',@Payment,N']');
SET @Lines=CONCAT(N'[{"ChargeId":',@Extra,N',"AdmissionId":17,"EnrollmentId":',@Enrollment,N',"AmountBefore":200,"Discount":0,"Cash":0,"Credit":200}]');
DELETE @Issue;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Date,N'Account Credit',N'Credit fixture',N'[17]',@Payments,@Lines,0,200,0;
SET @Snapshot=(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=(SELECT CollectionReceiptId FROM @Issue));
IF NOT EXISTS(SELECT 1 FROM OPENJSON(@Snapshot,'$.Lines') WITH(ChargeId bigint,GrossAmount decimal(12,2),Discount decimal(12,2),PreviouslyPaid decimal(12,2),Credit decimal(12,2),BalanceAfter decimal(12,2)) L
 WHERE ChargeId=@Bill AND GrossAmount=1800 AND Discount=1100 AND PreviouslyPaid=500 AND Credit=200 AND BalanceAfter=0)
 THROW 51000,'Credit settlement was not retained separately from cash and discount.',1;
IF @First<>(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=@FirstId) THROW 51000,'A later payment changed the original receipt.',1;
SELECT N'PASS: credit-only collection remains separate from earlier cash, discounts and the original receipt.';

-- A later refund reopens dues; a new acknowledgement discloses that balance
-- without editing any of the original document's amounts.
DECLARE @OriginalCash int=(SELECT TOP(1) P.FeePaymentId FROM FeePayments P JOIN FeePaymentAllocations A ON A.FeePaymentId=P.FeePaymentId
 WHERE A.BillingChargeId=@Bill AND P.IsCreditApplication=0 AND P.TotalAmount=500 AND P.RelatedFeePaymentId IS NULL ORDER BY P.FeePaymentId DESC);
INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Status,CreatedByUserId,RelatedFeePaymentId)
 VALUES(@Admission,@Enrollment,CONVERT(date,GETDATE()),CONCAT(N'PT-RF-',LEFT(CONVERT(nvarchar(36),NEWID()),20)),-100,N'Refund',N'Completed',@Actor,@OriginalCash);
SET @Payment=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@Payment,@Admission,@Bill,-100);
DELETE @Issue;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Date,N'Fee changes',N'Refund fixture',N'[17]',N'[]',N'[]',0,0,0,1;
SET @Snapshot=(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=(SELECT CollectionReceiptId FROM @Issue));
IF NOT EXISTS(SELECT 1 FROM OPENJSON(@Snapshot,'$.Lines') WITH(ChargeId bigint,PreviouslyPaid decimal(12,2),BalanceAfter decimal(12,2)) L
 WHERE ChargeId=@Bill AND PreviouslyPaid=600 AND BalanceAfter=100)
 THROW 51000,'Refund did not reopen the correct due on the new document.',1;
IF @First<>(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=@FirstId) THROW 51000,'Refund changed the original receipt.',1;
SELECT N'PASS: later refunds reopen the correct balance while original receipt figures stay unchanged.';

SET @Request=NEWID();
EXEC dbo.AdjustBillingChargeRate @Actor,@Bill,1800,N'Credit',N'Fixture discount reduced',@Request,700,100,1000,0;
DELETE @Issue;
INSERT @Issue EXEC dbo.IssueCollectionReceipt @Actor,17,@Date,N'Fee changes',N'Discount reduction fixture',N'[17]',N'[]',N'[]',0,0,0,1;
SET @Snapshot=(SELECT SnapshotJson FROM CollectionReceipts WHERE CollectionReceiptId=(SELECT CollectionReceiptId FROM @Issue));
IF NOT EXISTS(SELECT 1 FROM OPENJSON(@Snapshot,'$.Lines') WITH(ChargeId bigint,GrossAmount decimal(12,2),Discount decimal(12,2),PreviouslyPaid decimal(12,2),BalanceAfter decimal(12,2)) L
 WHERE ChargeId=@Bill AND GrossAmount=1800 AND Discount=1000 AND PreviouslyPaid=600 AND BalanceAfter=200)
 THROW 51000,'Reducing a discount did not disclose the revised fee and new due.',1;
SELECT N'PASS: reduced discounts and multiple rate corrections reconcile with refunds and earlier payments.';
ROLLBACK;

-- Refusal must happen before any collection mutation.
DECLARE @Review TABLE(ReviewToken varchar(64));
INSERT @Review EXEC dbo.GetCollectionReviewState @Actor,17;
DECLARE @OldToken varchar(64)=(SELECT ReviewToken FROM @Review);
BEGIN TRAN;
UPDATE dbo.StudentFeeAssignments SET Remarks=CONCAT(COALESCE(Remarks,N''),N' fixture concurrent change') WHERE AdmissionId=17;
BEGIN TRY
 EXEC dbo.GetCollectionReviewState @Actor,17,@OldToken;
 THROW 51000,'Stale collection review was accepted.',1;
END TRY
BEGIN CATCH
 IF ERROR_NUMBER()<>53104 BEGIN IF @@TRANCOUNT>0 ROLLBACK; THROW; END;
 IF @@TRANCOUNT>0 ROLLBACK;
 SELECT N'PASS: a concurrent fee change rejects the stale collection review.';
END CATCH;
