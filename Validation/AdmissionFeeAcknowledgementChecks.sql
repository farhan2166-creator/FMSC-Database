SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
-- Choose an existing, untouched legacy placeholder; never persist test activation.
DECLARE @Student int=(SELECT TOP(1) S.AdmissionId FROM dbo.Students S WHERE LegacySetupPending=1 AND S.AdmissionDate<='20261001' AND NOT EXISTS(SELECT 1 FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId) ORDER BY S.AdmissionId DESC);
IF @Student IS NULL THROW 51000,'No pending no-placement test fixture is available.',1;
DECLARE @Fees nvarchar(max)=N'[{"FeeTypeId":2,"Amount":400,"DiscountAmount":50,"Frequency":"Monthly"}]';
DECLARE @Preview TABLE(Snapshot varchar(64),OpeningTotal decimal(12,2),ChargeCount int);
BEGIN TRAN;
INSERT @Preview EXEC dbo.ActivateStudentBilling @UserId=1,@AdmissionId=@Student,@YearId=28,@PlacementId=1,@StartDate='20260401',@BillingStart='20261001',@StudentName=N'Rollback fee test',@FatherName=N'Rollback parent',@Fees=@Fees,@Reason=N'Admission fee already paid test',@AdmissionFeeAlreadyPaid=1;
IF (SELECT OpeningTotal FROM @Preview)<>350 OR (SELECT ChargeCount FROM @Preview)<>1 THROW 51001,'Already-paid admission fee was included in preview.',1;
DECLARE @Snapshot varchar(64)=(SELECT Snapshot FROM @Preview);
EXEC dbo.ActivateStudentBilling @UserId=1,@AdmissionId=@Student,@YearId=28,@PlacementId=1,@StartDate='20260401',@BillingStart='20261001',@StudentName=N'Rollback fee test',@FatherName=N'Rollback parent',@Fees=@Fees,@Reason=N'Admission fee already paid test',@AdmissionFeeAlreadyPaid=1,@Preview=0,@ExpectedSnapshot=@Snapshot;
IF NOT EXISTS(SELECT 1 FROM dbo.StudentAdmissionFeeAcknowledgements WHERE AdmissionId=@Student AND RecordedBy=1) THROW 51002,'Missing prior-payment acknowledgement.',1;
IF EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE AdmissionId=@Student AND FeeTypeId=1) OR EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments WHERE AdmissionId=@Student AND FeeTypeId=1 AND CancelledAt IS NULL) THROW 51003,'Admission charge or assignment was created.',1;
IF EXISTS(SELECT 1 FROM dbo.FeePayments WHERE AdmissionId=@Student) THROW 51004,'A false payment receipt was created.',1;
IF (SELECT SUM(NetAmount) FROM dbo.BillingCharges WHERE AdmissionId=@Student)<>350 THROW 51005,'Incorrect tuition charge.',1;
ROLLBACK;
IF EXISTS(SELECT 1 FROM dbo.StudentAdmissionFeeAcknowledgements WHERE AdmissionId=@Student) OR EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AdmissionId=@Student) THROW 51006,'Test changes were not rolled back.',1;
PRINT 'PASS: prior-paid admission acknowledgement; no admission charge/assignment/receipt; tuition-only opening; full rollback.';
-- An acknowledgement must not be combined with charging the same admission fee.
DECLARE @Rejected bit=0;
BEGIN TRY
 EXEC dbo.ActivateStudentBilling @UserId=1,@AdmissionId=@Student,@YearId=28,@PlacementId=1,@StartDate='20260401',@BillingStart='20261001',@StudentName=N'Rollback fee test',@FatherName=N'Rollback parent',@Fees=N'[{"FeeTypeId":1,"Amount":4000,"DiscountAmount":0,"Frequency":"Yearly"},{"FeeTypeId":2,"Amount":400,"DiscountAmount":0,"Frequency":"Monthly"}]',@Reason=N'Conflicting decision test',@AdmissionFeeAlreadyPaid=1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>59971 THROW; SET @Rejected=1; END CATCH;
IF @Rejected=0 THROW 51007,'Conflicting admission fee decision was accepted.',1;
SET @Rejected=0;
BEGIN TRY
 EXEC dbo.ActivateStudentBilling @UserId=1,@AdmissionId=@Student,@YearId=28,@PlacementId=1,@StartDate='20260401',@BillingStart='20261001',@StudentName=N'Rollback fee test',@FatherName=N'Rollback parent',@Fees=@Fees,@Reason=N'Missing admission fee test',@AdmissionFeeAlreadyPaid=0;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>59968 THROW; SET @Rejected=1; END CATCH;
IF @Rejected=0 THROW 51008,'Admission fee was omitted without acknowledgement.',1;
PRINT 'PASS: conflicting choice and missing mandatory admission fee rejected.';
