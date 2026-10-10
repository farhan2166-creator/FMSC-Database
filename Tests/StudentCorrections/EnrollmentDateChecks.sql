-- Execute only against a disposable copy of the established database. Apply Migrations/18.sql first.
-- Synthetic fixtures are rolled back; no school records are modified.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
GO
CREATE TABLE #AuditBaseline(Logs bigint,Operations bigint);
GO
CREATE PROCEDURE #DateFixture @User int OUTPUT,@Student int OUTPUT,@Enrollment int OUTPUT,@Assignment int OUTPUT,@Bill bigint OUTPUT,@NewDate date OUTPUT
AS
BEGIN
 DECLARE @Year int,@Start date,@Placement int,@Campus int,@Fee int;
 SELECT TOP(1) @User=U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator';
 SELECT TOP(1) @Year=AcademicYearId,@Start=StartDate FROM dbo.AcademicYears WHERE DATEDIFF(day,StartDate,EndDate)>100 AND StartDate<DATEADD(day,-100,GETDATE()) ORDER BY StartDate DESC;
 SELECT TOP(1) @Placement=ClassSectionId,@Campus=CampusId FROM dbo.CampusClassSections;
 SELECT TOP(1) @Fee=FeeTypeId FROM dbo.FeeTypes;
 IF @User IS NULL OR @Year IS NULL OR @Placement IS NULL OR @Fee IS NULL THROW 57900,'Test fixtures require an administrator, historical year, placement and fee type.',1;
 SET @NewDate=DATEADD(day,5,@Start);
 INSERT dbo.Students(AdmissionDate,AdmissionCampusId,StudentName,AdmissionNumber,Status,WithdrawalDate)
 VALUES(@Start,@Campus,N'Correction test',CONVERT(varchar(20),9000000000+ABS(CONVERT(bigint,CHECKSUM(NEWID())))),N'Left',DATEADD(day,50,@Start));
 SET @Student=SCOPE_IDENTITY();
 INSERT dbo.Enrollments(AdmissionId,AcademicYearId,ClassSectionId,EnrollmentDate,LeavingDate,Status,BillingStartDate)
 VALUES(@Student,@Year,@Placement,@Start,DATEADD(day,50,@Start),N'Active',@Start);
 SET @Enrollment=SCOPE_IDENTITY();
 INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,Frequency) VALUES(@Enrollment,@Student,@Fee,100,N'Monthly');
 SET @Assignment=SCOPE_IDENTITY();
 INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,GrossAmount,ChargeKind)
 VALUES(@Assignment,@Enrollment,@Student,@Fee,DATEADD(month,1,DATEFROMPARTS(YEAR(@NewDate),MONTH(@NewDate),1)),DATEADD(day,40,@Start),100,N'Monthly');
 SET @Bill=SCOPE_IDENTITY();
 UPDATE dbo.Enrollments SET LeavingDate=NULL,BillingReady=1 WHERE EnrollmentId=@Enrollment;
 UPDATE dbo.Students SET Status=N'Active',WithdrawalDate=NULL WHERE AdmissionId=@Student;
 DELETE FROM #AuditBaseline;
 INSERT #AuditBaseline SELECT (SELECT COUNT_BIG(*) FROM dbo.AuditLogs),(SELECT COUNT_BIG(*) FROM dbo.AuditOperations);
END;
GO
-- Read-only preview and coordinated date/charge save.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date,@Start date,@Billing date,@Snapshot nvarchar(max),@Cancel int,@Add int;
EXEC #DateFixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
SELECT @Start=EnrollmentDate FROM dbo.Enrollments WHERE EnrollmentId=@E;
SET @Billing=DATEADD(month,2,DATEFROMPARTS(YEAR(@Start),MONTH(@Start),1));
DECLARE @Plan TABLE(Snapshot nvarchar(max),CancelCount int,AddCount int,CancelledAmount decimal(18,2),AddedAmount decimal(18,2),BlockedCount int,ThroughDate date,ReturnDate datetime2);
INSERT @Plan EXEC dbo.ReconcileEnrollmentDates @U,@E,@Start,@Billing,N'Date correction test', @WriteAudit=0;
SELECT @Snapshot=Snapshot,@Cancel=CancelCount,@Add=AddCount FROM @Plan;
IF @Cancel<>1 OR @Add<1 THROW 57800,'Preview did not identify expected cancellation/additions.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE BillingChargeId=@B AND Status=N'Open') THROW 57801,'Preview changed the existing bill.',1;
IF (SELECT COUNT(*) FROM dbo.BillingCharges WHERE EnrollmentId=@E)<>1 THROW 57802,'Preview created bills.',1;
EXEC dbo.ReconcileEnrollmentDates @U,@E,@Start,@Billing,N'Date correction test',0,0,@Snapshot, @WriteAudit=0;
IF NOT EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@E AND BillingStartDate=@Billing AND Status=N'Active') THROW 57803,'Dates/status incorrect.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE BillingChargeId=@B AND Status=N'Cancelled') THROW 57804,'Old unpaid bill not retained as cancelled.',1;
IF (SELECT COUNT(*) FROM dbo.BillingCharges WHERE EnrollmentId=@E AND Status<>N'Cancelled')<>@Add THROW 57805,'Added count differs from preview.',1;
IF EXISTS(SELECT 1 FROM dbo.FeePayments WHERE EnrollmentId=@E) THROW 57806,'Payment unexpectedly created.',1;
IF EXISTS(SELECT 1 FROM #AuditBaseline WHERE Logs<>(SELECT COUNT_BIG(*) FROM dbo.AuditLogs) OR Operations<>(SELECT COUNT_BIG(*) FROM dbo.AuditOperations)) THROW 57790,'Console correction created audit data.',1;
ROLLBACK;
PRINT 'PASS read-only preview, accurate impact counts and atomic date/charge reconciliation';
GO
-- Payment history blocks the save, including a bill marked paid without an allocation.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date,@Start date,@Billing date,@Snapshot nvarchar(max);
EXEC #DateFixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
SELECT @Start=EnrollmentDate FROM dbo.Enrollments WHERE EnrollmentId=@E;
SET @Billing=DATEADD(month,2,DATEFROMPARTS(YEAR(@Start),MONTH(@Start),1));
UPDATE dbo.BillingCharges SET Status=N'Paid' WHERE BillingChargeId=@B;
DECLARE @Plan TABLE(Snapshot nvarchar(max),CancelCount int,AddCount int,CancelledAmount decimal(18,2),AddedAmount decimal(18,2),BlockedCount int,ThroughDate date,ReturnDate datetime2);
INSERT @Plan EXEC dbo.ReconcileEnrollmentDates @U,@E,@Start,@Billing,N'Date correction test', @WriteAudit=0;
IF NOT EXISTS(SELECT 1 FROM @Plan WHERE BlockedCount=1) THROW 57807,'Paid bill not flagged in preview.',1;
SELECT @Snapshot=Snapshot FROM @Plan;
BEGIN TRY
 EXEC dbo.ReconcileEnrollmentDates @U,@E,@Start,@Billing,N'Date correction test',0,0,@Snapshot, @WriteAudit=0;
 THROW 57808,'Paid bill was reconciled.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>57410 THROW; END CATCH;
IF @@TRANCOUNT>0 ROLLBACK;
PRINT 'PASS payment blocker preview and save protection';
GO
-- Financial changes after review invalidate the complete save.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date,@Start date,@Billing date,@Snapshot nvarchar(max);
EXEC #DateFixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
SELECT @Start=EnrollmentDate FROM dbo.Enrollments WHERE EnrollmentId=@E;
SET @Billing=DATEADD(month,2,DATEFROMPARTS(YEAR(@Start),MONTH(@Start),1));
DECLARE @Plan TABLE(Snapshot nvarchar(max),CancelCount int,AddCount int,CancelledAmount decimal(18,2),AddedAmount decimal(18,2),BlockedCount int,ThroughDate date,ReturnDate datetime2);
INSERT @Plan EXEC dbo.ReconcileEnrollmentDates @U,@E,@Start,@Billing,N'Date correction test', @WriteAudit=0;
SELECT @Snapshot=Snapshot FROM @Plan;
UPDATE dbo.BillingCharges SET GrossAmount=101 WHERE BillingChargeId=@B;
BEGIN TRY
 EXEC dbo.ReconcileEnrollmentDates @U,@E,@Start,@Billing,N'Date correction test',0,0,@Snapshot, @WriteAudit=0;
 THROW 57809,'Stale financial review accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>57408 THROW; END CATCH;
IF @@TRANCOUNT>0 ROLLBACK;
IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@S) THROW 57810,'Failed reconciliation did not roll back.',1;
PRINT 'PASS stale financial review rejected with full rollback';
GO
-- A confirmed enrollment still requires a billing start.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date,@Start date;
EXEC #DateFixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
SELECT @Start=EnrollmentDate FROM dbo.Enrollments WHERE EnrollmentId=@E;
BEGIN TRY
 EXEC dbo.ReconcileEnrollmentDates @U,@E,@Start,NULL,N'Date correction test', @WriteAudit=0;
 THROW 57811,'Confirmed billing start was cleared.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>57404 THROW; END CATCH;
IF @@TRANCOUNT>0 ROLLBACK;
PRINT 'PASS established confirmed-billing-start restriction';
GO
-- Closed enrollments stay excluded from active reconciliation.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date,@Start date;
EXEC #DateFixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
SELECT @Start=EnrollmentDate FROM dbo.Enrollments WHERE EnrollmentId=@E;
UPDATE dbo.Enrollments SET Status=N'Left',LeavingDate=@D WHERE EnrollmentId=@E;
BEGIN TRY
 EXEC dbo.ReconcileEnrollmentDates @U,@E,@Start,@Start,N'Date correction test', @WriteAudit=0;
 THROW 57812,'Closed enrollment reconciled.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>57403 THROW; END CATCH;
IF @@TRANCOUNT>0 ROLLBACK;
PRINT 'PASS established closed-enrollment restriction';
GO
