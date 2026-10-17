SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
-- All charge/payment mutations below are rollback-only test fixtures.
DECLARE @Types dbo.FeeTypeIdList,@Snapshot varchar(64),@Year int,@Blocked bit=0;
DECLARE @Months TABLE(BillingMonth date,ChargesAffected int,ReversibleCharges int,TotalPosted decimal(12,2),TotalCollected decimal(12,2));
DECLARE @Fixture TABLE(EnrollmentId int,AdmissionId int,Assignment int,FeeTypeId int);
;WITH Candidates AS(SELECT E.EnrollmentId,E.AdmissionId,F.StudentFeeAssignmentId,F.FeeTypeId,ROW_NUMBER() OVER(PARTITION BY E.EnrollmentId ORDER BY F.StudentFeeAssignmentId) Position
 FROM dbo.Enrollments E JOIN dbo.StudentFeeAssignments F ON F.EnrollmentId=E.EnrollmentId
 JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId WHERE Y.IsCurrent=1 AND E.Status=N'Active' AND E.BillingReady=1 AND F.Frequency=N'Monthly' AND F.CancelledAt IS NULL AND F.EffectiveTo IS NULL AND COALESCE(E.BillingStartDate,E.EnrollmentDate)<='20261101')
INSERT @Fixture SELECT TOP(2) EnrollmentId,AdmissionId,StudentFeeAssignmentId,FeeTypeId FROM Candidates WHERE Position=1 ORDER BY EnrollmentId;
IF (SELECT COUNT(*) FROM @Fixture)<>2 THROW 51000,'Two activated monthly-fee fixtures are required.',1;
IF EXISTS(SELECT 1 FROM dbo.BillingCharges B JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId WHERE Y.IsCurrent=1 AND B.Status<>N'Cancelled' AND B.BillingMonth>'20261001') THROW 51001,'This fixture requires no later-than-October bills before testing.',1;
DECLARE @Base int=(SELECT COUNT(*) FROM dbo.BillingCharges),@BasePayments int=(SELECT COUNT(*) FROM dbo.FeePayments);
BEGIN TRAN;
INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind,Remarks)
 SELECT F.Assignment,F.EnrollmentId,F.AdmissionId,F.FeeTypeId,M.Month,M.Month,M.Month,500,0,N'Monthly',N'RollbackLatestMonthFixture' FROM @Fixture F CROSS JOIN(VALUES(CONVERT(date,'20261101')),(CONVERT(date,'20261201'))) M(Month);
INSERT @Months EXEC dbo.GetReversibleMonths @UserId=1,@FeeTypeIds=@Types;
IF (SELECT COUNT(*) FROM @Months)<>1 OR NOT EXISTS(SELECT 1 FROM @Months WHERE BillingMonth='20261201' AND ChargesAffected=2) THROW 51002,'Only latest December should be offered.',1;
EXEC dbo.PreviewPostingReversal @UserId=1,@BillingMonth='20261201',@FeeTypeIds=@Types;
EXEC dbo.RequireWholeMonthReversal 1,'20261201',@Year OUTPUT,@Snapshot OUTPUT;
EXEC dbo.ReverseMonthlyFees @UserId=1,@BillingMonth='20261201',@Reason=N'Rollback latest-month validation',@FeeTypeIds=@Types,@ExpectedSnapshot=@Snapshot;
IF EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE Remarks LIKE N'%RollbackLatestMonthFixture%' AND BillingMonth='20261201' AND Status<>N'Cancelled') THROW 51003,'December was not reversed completely.',1;
DELETE @Months;
INSERT @Months EXEC dbo.GetReversibleMonths @UserId=1,@FeeTypeIds=@Types;
IF NOT EXISTS(SELECT 1 FROM @Months WHERE BillingMonth='20261101' AND ChargesAffected=2) THROW 51004,'November did not become eligible after December.',1;
EXEC dbo.RequireWholeMonthReversal 1,'20261101',@Year OUTPUT,@Snapshot OUTPUT;
EXEC dbo.ReverseMonthlyFees @UserId=1,@BillingMonth='20261101',@Reason=N'Rollback preceding-month validation',@FeeTypeIds=@Types,@ExpectedSnapshot=@Snapshot;
ROLLBACK;
PRINT 'PASS: only latest month offered; whole December then whole November reversed; all fixtures rolled back.';
-- An earlier month cannot bypass later postings.
BEGIN TRY
 BEGIN TRAN;
 INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
 SELECT F.Assignment,F.EnrollmentId,F.AdmissionId,F.FeeTypeId,M.Month,M.Month,M.Month,500,0,N'Monthly' FROM @Fixture F CROSS JOIN(VALUES(CONVERT(date,'20261101')),(CONVERT(date,'20261201'))) M(Month);
 EXEC dbo.ReverseMonthlyFees @UserId=1,@BillingMonth='20261101',@Reason=N'Invalid order rollback test',@FeeTypeIds=@Types,@ExpectedSnapshot='invalid';
 ROLLBACK;
END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; IF ERROR_NUMBER()<>54803 THROW; SET @Blocked=1; END CATCH;
IF @Blocked=0 THROW 51005,'Earlier month reversal was accepted.',1;
-- A payment arriving after preview blocks all charges, across all students.
SET @Blocked=0;
BEGIN TRY
 BEGIN TRAN;
 INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
 SELECT Assignment,EnrollmentId,AdmissionId,FeeTypeId,'20261201','20261201','20261201',500,0,N'Monthly' FROM @Fixture;
 EXEC dbo.RequireWholeMonthReversal 1,'20261201',@Year OUTPUT,@Snapshot OUTPUT;
 DECLARE @E int,@A int,@Charge bigint,@Payment int;
 SELECT TOP(1) @E=EnrollmentId,@A=AdmissionId FROM @Fixture;
 SELECT @Charge=BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=@E AND BillingMonth='20261201' AND Status<>N'Cancelled';
 INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,CreatedByUserId)
 VALUES(@A,@E,'20261017',CONVERT(nvarchar(36),NEWID()),1,N'Cash',1);
 SET @Payment=CONVERT(int,SCOPE_IDENTITY());
 INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@Payment,@A,@Charge,1);
 DELETE @Months;
 INSERT @Months EXEC dbo.GetReversibleMonths @UserId=1,@FeeTypeIds=@Types;
 IF EXISTS(SELECT 1 FROM @Months) THROW 51006,'Partially collected latest month was offered.',1;
 EXEC dbo.ReverseMonthlyFees @UserId=1,@BillingMonth='20261201',@Reason=N'Payment after preview rollback test',@FeeTypeIds=@Types,@ExpectedSnapshot=@Snapshot;
 ROLLBACK;
END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; IF ERROR_NUMBER()<>54804 THROW; SET @Blocked=1; END CATCH;
IF @Blocked=0 THROW 51007,'Payment did not block whole month.',1;
SET @Blocked=0;
BEGIN TRY
 EXEC dbo.PreviewPostingReversal @UserId=1,@BillingMonth='20250401',@FeeTypeIds=@Types;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>54803 THROW; SET @Blocked=1; END CATCH;
IF @Blocked=0 THROW 51008,'Historical year month was accepted.',1;
SET @Blocked=0;
BEGIN TRY
 EXEC dbo.ReverseMonthlyFees @UserId=1,@BillingMonth='20261001',@Reason=N'Partial scope validation',@CampusId=1,@FeeTypeIds=@Types;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>54805 THROW; SET @Blocked=1; END CATCH;
IF @Blocked=0 THROW 51009,'Partial school reversal was accepted.',1;
IF (SELECT COUNT(*) FROM dbo.BillingCharges)<>@Base OR (SELECT COUNT(*) FROM dbo.FeePayments)<>@BasePayments THROW 51010,'Fixture data was not rolled back.',1;
PRINT 'PASS: reverse order, payments after preview, historical scope, and partial filters are protected. No test data persisted.';
-- A changed posting cannot reuse the earlier preview, even if it is still the latest month.
SET @Blocked=0;
BEGIN TRY
 BEGIN TRAN;
 INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
 SELECT Assignment,EnrollmentId,AdmissionId,FeeTypeId,'20261101','20261101','20261101',500,0,N'Monthly' FROM @Fixture;
 EXEC dbo.RequireWholeMonthReversal 1,'20261101',@Year OUTPUT,@Snapshot OUTPUT;
 UPDATE dbo.BillingCharges SET Remarks=N'Changed after preview test' WHERE EnrollmentId=(SELECT MIN(EnrollmentId) FROM @Fixture) AND BillingMonth='20261101' AND Status<>N'Cancelled';
 EXEC dbo.ReverseMonthlyFees @UserId=1,@BillingMonth='20261101',@Reason=N'Stale preview validation',@FeeTypeIds=@Types,@ExpectedSnapshot=@Snapshot;
 ROLLBACK;
END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; IF ERROR_NUMBER()<>54806 THROW; SET @Blocked=1; END CATCH;
IF @Blocked=0 THROW 51011,'A stale preview was accepted.',1;
IF (SELECT COUNT(*) FROM dbo.BillingCharges)<>@Base OR (SELECT COUNT(*) FROM dbo.FeePayments)<>@BasePayments THROW 51012,'Stale-preview fixtures were not rolled back.',1;
PRINT 'PASS: changed posting rejects stale preview; no fixtures persisted.';
