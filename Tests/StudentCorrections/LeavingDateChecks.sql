-- Execute only against a disposable copy of the established database. No migration is needed.
-- Synthetic fixtures are rolled back; no school records are modified.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
GO
CREATE PROCEDURE #Fixture @User int OUTPUT,@Student int OUTPUT,@Enrollment int OUTPUT,@Assignment int OUTPUT,@Bill bigint OUTPUT,@NewDate date OUTPUT
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
 UPDATE dbo.Enrollments SET Status=N'Left' WHERE EnrollmentId=@Enrollment;
 INSERT dbo.LeaveCertificates(AdmissionId,EnrollmentId,StudentName,ClassStudied,LeaveDate,CreatedByUserId)
 VALUES(@Student,@Enrollment,N'Correction test',N'Test class',DATEADD(day,50,@Start),@User);
END;
GO
CREATE PROCEDURE #Repair @User int,@Student int,@Enrollment int,@Date date,@Snapshot nvarchar(max),@Keep bit=1,@Withdrawal bit=1
AS
BEGIN
 DECLARE @Changes nvarchar(max)=(SELECT EnrollmentId,EnrollmentDate,
  CASE WHEN EnrollmentId=@Enrollment THEN @Date ELSE LeavingDate END LeavingDate,BillingStartDate
  FROM dbo.Enrollments WHERE AdmissionId=@Student ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
 @Certificates nvarchar(max)=(SELECT LeaveRecordId,@Date LeaveDate FROM dbo.LeaveCertificates WHERE EnrollmentId=@Enrollment FOR JSON PATH);
 EXEC dbo.RepairLifecycleDates @User,@Student,@Snapshot,@Changes,N'Integration test',0,@Keep,@Certificates,@Withdrawal,@Date;
END;
GO
-- Related dates change together, while financial and original history records remain.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date,@Snapshot nvarchar(max),@Created datetime2;
EXEC #Fixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
SELECT @Created=CreatedAt FROM dbo.LeaveCertificates WHERE EnrollmentId=@E;
UPDATE dbo.BillingCharges SET Status=N'Paid' WHERE BillingChargeId=@B;
EXEC dbo.GetLifecycleRepairSnapshot @S,@Snapshot OUTPUT;
EXEC #Repair @U,@S,@E,@D,@Snapshot;
IF NOT EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@E AND LeavingDate=@D) THROW 57902,'Enrollment not corrected.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@S AND WithdrawalDate=@D) THROW 57903,'Withdrawal not corrected.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.LeaveCertificates WHERE EnrollmentId=@E AND LeaveDate=@D AND CreatedAt=@Created) THROW 57904,'Certificate not corrected or creation changed.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE BillingChargeId=@B AND Status=N'Paid' AND GrossAmount=100) THROW 57905,'Financial history changed.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.EnrollmentHistory WHERE EnrollmentId=@E AND ActionType=N'CORRECTION') THROW 57906,'Correction history missing.',1;
ROLLBACK;
PRINT 'PASS linked dates corrected together; paid bills and creation/history preserved';
GO
-- Historical departure correction must not overwrite a later withdrawal.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date,@Snapshot nvarchar(max),@LaterDate date;
EXEC #Fixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
SELECT @LaterDate=DATEADD(day,60,EnrollmentDate) FROM dbo.Enrollments WHERE EnrollmentId=@E;
INSERT dbo.Enrollments(AdmissionId,AcademicYearId,ClassSectionId,EnrollmentDate,LeavingDate,Status)
 SELECT AdmissionId,AcademicYearId,ClassSectionId,@LaterDate,DATEADD(day,5,@LaterDate),N'Left' FROM dbo.Enrollments WHERE EnrollmentId=@E;
UPDATE dbo.Students SET WithdrawalDate=DATEADD(day,5,@LaterDate) WHERE AdmissionId=@S;
EXEC dbo.GetLifecycleRepairSnapshot @S,@Snapshot OUTPUT;
EXEC #Repair @U,@S,@E,@D,@Snapshot,1,0;
IF NOT EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@S AND WithdrawalDate=DATEADD(day,5,@LaterDate)) THROW 57907,'Later withdrawal overwritten.',1;
ROLLBACK;
PRINT 'PASS earlier departure preserves later withdrawal';
GO
-- Stale linked records invalidate the whole operation.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date,@Snapshot nvarchar(max);
EXEC #Fixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
EXEC dbo.GetLifecycleRepairSnapshot @S,@Snapshot OUTPUT;
UPDATE dbo.LeaveCertificates SET LeaveDate=DATEADD(day,1,LeaveDate) WHERE EnrollmentId=@E;
BEGIN TRY
 EXEC #Repair @U,@S,@E,@D,@Snapshot;
 THROW 57908,'Stale review accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>57202 THROW; END CATCH;
IF @@TRANCOUNT>0 ROLLBACK;
IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@S) THROW 57909,'Full rollback failed.',1;
PRINT 'PASS stale linked records rejected with full rollback';
GO
-- Financial acknowledgement remains mandatory under the existing database contract.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date,@Snapshot nvarchar(max);
EXEC #Fixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
EXEC dbo.GetLifecycleRepairSnapshot @S,@Snapshot OUTPUT;
BEGIN TRY
 EXEC #Repair @U,@S,@E,@D,@Snapshot,0;
 THROW 57910,'Missing financial acknowledgement accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>57209 THROW; END CATCH;
IF @@TRANCOUNT>0 ROLLBACK;
PRINT 'PASS financial-history acknowledgement enforced';
GO
-- Existing trigger still prevents cancellation of closed bills, even after departure.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date;
EXEC #Fixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
BEGIN TRY
 UPDATE dbo.BillingCharges SET Status=N'Cancelled' WHERE BillingChargeId=@B;
 THROW 57912,'Closed bill cancellation accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>57102 THROW; END CATCH;
IF @@TRANCOUNT>0 ROLLBACK;
PRINT 'PASS established closed-bill cancellation restriction retained';
GO
-- Existing trigger still prevents amount changes on closed bills.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date;
EXEC #Fixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
BEGIN TRY
 UPDATE dbo.BillingCharges SET GrossAmount=200 WHERE BillingChargeId=@B;
 THROW 57913,'Closed bill amount rewritten.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>57102 THROW; END CATCH;
IF @@TRANCOUNT>0 ROLLBACK;
PRINT 'PASS established closed-bill amount protection retained';
GO
-- Invalid chronological correction rolls back all linked edits.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date,@Snapshot nvarchar(max);
EXEC #Fixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
SELECT @D=DATEADD(day,-1,EnrollmentDate) FROM dbo.Enrollments WHERE EnrollmentId=@E;
EXEC dbo.GetLifecycleRepairSnapshot @S,@Snapshot OUTPUT;
BEGIN TRY
 EXEC #Repair @U,@S,@E,@D,@Snapshot;
 THROW 57914,'Departure before start accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>57204 THROW; END CATCH;
IF @@TRANCOUNT>0 ROLLBACK;
PRINT 'PASS departure before start rejected';
GO
BEGIN TRY
 EXEC #Repair -1,-1,-1,'20200101',N'';
 THROW 57911,'Unauthenticated caller accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>50003 THROW; END CATCH;
PRINT 'PASS unauthenticated caller rejected';
GO

-- Status correction returns confirmed outcomes and closes an active enrollment coherently.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date;
EXEC #Fixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
UPDATE dbo.Students SET Status=N'Active',WithdrawalDate=NULL WHERE AdmissionId=@S;
UPDATE dbo.Enrollments SET Status=N'Active',LeavingDate=NULL WHERE EnrollmentId=@E;
SELECT @D=DATEADD(day,70,EnrollmentDate) FROM dbo.Enrollments WHERE EnrollmentId=@E;
DECLARE @Outcome TABLE(AdmissionId int,Status nvarchar(30),WithdrawalDate date,Changed bit);
INSERT @Outcome EXEC dbo.CorrectStudentStatus @U,@S,N'Left',@D,0,N'Status integration test';
IF NOT EXISTS(SELECT 1 FROM @Outcome WHERE Status=N'Left' AND WithdrawalDate=@D AND Changed=1) THROW 57917,'Status outcome not confirmed.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@E AND Status=N'Left' AND LeavingDate=@D) THROW 57918,'Active enrollment not closed.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE BillingChargeId=@B AND Status=N'Open') THROW 57919,'Status correction altered a bill.',1;
DELETE @Outcome;
INSERT @Outcome EXEC dbo.CorrectStudentStatus @U,@S,N'Left',@D,0,N'Status integration test';
IF NOT EXISTS(SELECT 1 FROM @Outcome WHERE Changed=0) THROW 57920,'Unchanged status reported as changed.',1;
ROLLBACK;
PRINT 'PASS confirmed status result, coordinated closure and no-op reporting';
GO
-- Status-only reactivation still needs an active enrollment.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date;
EXEC #Fixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
BEGIN TRY
 EXEC dbo.CorrectStudentStatus @U,@S,N'Active',NULL,1,N'Status integration test';
 THROW 57921,'Student reactivated without an active enrollment.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>57008 THROW; END CATCH;
IF @@TRANCOUNT>0 ROLLBACK;
PRINT 'PASS established reactivation restriction retained';
GO
-- Existing creation fails audit validation for a plain-English reason; changes roll back.
-- Preserve this regression evidence without changing established database procedures.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date,@Year int,@Placement int,@NewStart date;
EXEC #Fixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
SELECT @Year=AcademicYearId,@Placement=ClassSectionId,@NewStart=DATEADD(day,1,LeavingDate) FROM dbo.Enrollments WHERE EnrollmentId=@E;
BEGIN TRY
 EXEC dbo.CorrectEnrollmentStatus @UserId=@U,@AdmissionId=@S,@NewStatus=N'Active',@NewEnrollmentDate=@NewStart,@ClearLeavingDate=1,
  @NewClassSectionId=@Placement,@AcademicYearId=@Year,@Reason=N'Missing record integration test';
 THROW 57922,'Expected existing audit validation failure was not raised; revisit this regression check.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>59941 THROW; END CATCH;
IF @@TRANCOUNT<>0 THROW 57923,'Failed creation did not roll back its transaction.',1;
IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@S) THROW 57924,'Failed creation left transaction data behind.',1;
PRINT 'PASS existing missing-record audit blocker reproduced with complete rollback';
GO
-- A second active enrollment remains forbidden.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date,@Year int,@Placement int;
EXEC #Fixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
SELECT @Year=AcademicYearId,@Placement=ClassSectionId FROM dbo.Enrollments WHERE EnrollmentId=@E;
UPDATE dbo.Enrollments SET Status=N'Active',LeavingDate=NULL WHERE EnrollmentId=@E;
BEGIN TRY
 EXEC dbo.CorrectEnrollmentStatus @UserId=@U,@AdmissionId=@S,@NewStatus=N'Active',@NewEnrollmentDate=@D,@ClearLeavingDate=1,
  @NewClassSectionId=@Placement,@AcademicYearId=@Year,@Reason=N'Missing record integration test';
 THROW 57927,'Second active enrollment accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>57216 THROW; END CATCH;
IF @@TRANCOUNT>0 ROLLBACK;
PRINT 'PASS established duplicate-active-enrollment restriction';
GO
-- Missing Left records still require a leaving date.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date,@Year int,@Placement int;
EXEC #Fixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
SELECT @Year=AcademicYearId,@Placement=ClassSectionId FROM dbo.Enrollments WHERE EnrollmentId=@E;
BEGIN TRY
 EXEC dbo.CorrectEnrollmentStatus @UserId=@U,@AdmissionId=@S,@NewStatus=N'Left',@NewEnrollmentDate=@D,@ClearLeavingDate=1,
  @NewClassSectionId=@Placement,@AcademicYearId=@Year,@Reason=N'Missing record integration test';
 THROW 57928,'Left record without departure date accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>57117 THROW; END CATCH;
IF @@TRANCOUNT>0 ROLLBACK;
PRINT 'PASS established missing-Left-record date requirement';
GO
-- Existing-enrollment closure coordinates student status and preserves related records.
BEGIN TRANSACTION;
DECLARE @U int,@S int,@E int,@A int,@B bigint,@D date,@Leave date,@Placement int;
EXEC #Fixture @U OUTPUT,@S OUTPUT,@E OUTPUT,@A OUTPUT,@B OUTPUT,@D OUTPUT;
SELECT @Leave=LeavingDate,@Placement=ClassSectionId FROM dbo.Enrollments WHERE EnrollmentId=@E;
UPDATE dbo.Enrollments SET Status=N'Active',LeavingDate=NULL WHERE EnrollmentId=@E;
UPDATE dbo.Students SET Status=N'Active',WithdrawalDate=NULL WHERE AdmissionId=@S;
DECLARE @Result TABLE(EnrollmentId int,AdmissionId int,Status nvarchar(30),Changed bit);
INSERT @Result EXEC dbo.CorrectEnrollmentStatus @UserId=@U,@EnrollmentId=@E,@NewStatus=N'Left',@NewLeavingDate=@Leave,@NewClassSectionId=@Placement,@Reason=N'Existing enrollment review test';
IF NOT EXISTS(SELECT 1 FROM @Result WHERE EnrollmentId=@E AND Status=N'Left' AND Changed=1) THROW 57929,'Confirmed closure result missing.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@S AND Status=N'Left' AND WithdrawalDate=@Leave) THROW 57930,'Student closure was not coordinated.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE BillingChargeId=@B AND Status=N'Open' AND NetAmount=100) THROW 57931,'Existing bill changed.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@A AND Amount=100) THROW 57932,'Existing assignment changed.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.LeaveCertificates WHERE EnrollmentId=@E AND LeaveDate=@Leave) THROW 57933,'Existing certificate changed.',1;
DELETE FROM @Result;
INSERT @Result EXEC dbo.CorrectEnrollmentStatus @UserId=@U,@EnrollmentId=@E,@NewStatus=N'Left',@NewLeavingDate=@Leave,@NewClassSectionId=@Placement,@Reason=N'Existing enrollment review test';
IF NOT EXISTS(SELECT 1 FROM @Result WHERE Changed=0) THROW 57934,'No-op result missing.',1;
ROLLBACK;
PRINT 'PASS existing enrollment closure, confirmed result, related-record preservation and no-op';
GO
