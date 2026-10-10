-- Disposable database only, with migration 18 applied. All fixtures roll back.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
GO
CREATE PROCEDURE #Fixture @U int OUTPUT,@Campus int OUTPUT,@Y1 int OUTPUT,@Y2 int OUTPUT,@E1 int OUTPUT,@E2 int OUTPUT,@Fee int OUTPUT,@Place int OUTPUT
AS
BEGIN
 DECLARE @Code nvarchar(10)=LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),'-',''),10),@Class int,@Section int,@Student int;
 SELECT TOP(1) @U=U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator';
 SELECT TOP(1) @Class=ClassId,@Section=SectionId FROM dbo.CampusClassSections;
 IF @U IS NULL OR @Class IS NULL THROW 57800,'Tests require an administrator and catalog placement.',1;
 INSERT dbo.Campuses(CampusCode,CampusName) VALUES(@Code,N'Audit test campus'); SET @Campus=SCOPE_IDENTITY();
 INSERT dbo.AcademicYears(YearName,StartDate,EndDate) VALUES(@Code+N'-1','20250101','20251231');SET @Y1=SCOPE_IDENTITY();
 INSERT dbo.AcademicYears(YearName,StartDate,EndDate) VALUES(@Code+N'-2','20260101','20261231');SET @Y2=SCOPE_IDENTITY();
 INSERT dbo.CampusClassSections(CampusId,ClassId,SectionId,IsActive) VALUES(@Campus,@Class,@Section,1);SET @Place=SCOPE_IDENTITY();
 INSERT dbo.FeeTypes(FeeTypeCode,FeeTypeName) VALUES(@Code,N'Audit test monthly fee');SET @Fee=SCOPE_IDENTITY();
 INSERT dbo.Students(AdmissionDate,AdmissionCampusId,StudentName,AdmissionNumber,Status)
 VALUES('20250101',@Campus,N'Audit test older year',CONVERT(varchar(20),9000000000+ABS(CONVERT(bigint,CHECKSUM(NEWID())))),N'Active');SET @Student=SCOPE_IDENTITY();
 INSERT dbo.Enrollments(AdmissionId,AcademicYearId,ClassSectionId,EnrollmentDate,BillingStartDate,BillingReady,Status)
 VALUES(@Student,@Y1,@Place,'20250101','20250101',1,N'Active');SET @E1=SCOPE_IDENTITY();
 INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,Frequency) VALUES(@E1,@Student,@Fee,100,N'Monthly');
 INSERT dbo.Students(AdmissionDate,AdmissionCampusId,StudentName,AdmissionNumber,Status)
 VALUES('20260101',@Campus,N'Audit test selected year',CONVERT(varchar(20),9000000000+ABS(CONVERT(bigint,CHECKSUM(NEWID())))),N'Active');SET @Student=SCOPE_IDENTITY();
 INSERT dbo.Enrollments(AdmissionId,AcademicYearId,ClassSectionId,EnrollmentDate,BillingStartDate,BillingReady,Status)
 VALUES(@Student,@Y2,@Place,'20260101','20260101',1,N'Active');SET @E2=SCOPE_IDENTITY();
 INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,Frequency) VALUES(@E2,@Student,@Fee,100,N'Monthly');
END;
GO
BEGIN TRANSACTION;
DECLARE @U int,@C int,@Y1 int,@Y2 int,@E1 int,@E2 int,@F int,@P int;
EXEC #Fixture @U OUTPUT,@C OUTPUT,@Y1 OUTPUT,@Y2 OUTPUT,@E1 OUTPUT,@E2 OUTPUT,@F OUTPUT,@P OUTPUT;
DECLARE @Types dbo.FeeTypeIdList;
DECLARE @Months TABLE(BillingMonth date,ChargesToPost int,EligibleStudents int,TotalNet decimal(38,2));
INSERT @Types VALUES(@F);
INSERT @Months EXEC dbo.GetPostableMonths @U,@Y2,@C,0,0,@Types;
IF (SELECT COUNT(*) FROM @Months)<>12 OR EXISTS(SELECT 1 FROM @Months WHERE ChargesToPost<>1 OR EligibleStudents<>1 OR TotalNet<>100) THROW 57801,'Month discovery included another enrollment year.',1;
-- Direct preview is restricted to this private campus; inspect both result sets.
EXEC dbo.PreviewMonthlyFees @UserId=@U,@BillingMonth='20260501',@CampusId=@C,@FeeTypeIds=@Types,@AcademicYearId=@Y2;
DECLARE @Selection dbo.PostingSelectionType;
INSERT @Selection VALUES(@E2,@F,'20260501');
EXEC dbo.PostSelectedMonthlyFees @UserId=@U,@BillingMonth='20260501',@Selection=@Selection,@AcademicYearId=@Y2;
IF (SELECT COUNT(*) FROM dbo.BillingCharges WHERE EnrollmentId=@E2 AND BillingMonth='20260501')<>1 OR EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE EnrollmentId=@E1) THROW 57802,'Posting did not isolate the selected year.',1;
EXEC dbo.PostSelectedMonthlyFees @UserId=@U,@BillingMonth='20260501',@Selection=@Selection,@AcademicYearId=@Y2;
IF (SELECT COUNT(*) FROM dbo.BillingCharges WHERE EnrollmentId=@E2 AND BillingMonth='20260501')<>1 THROW 57803,'Repeated posting created a duplicate.',1;
ROLLBACK;
PRINT 'PASS selected-year discovery, preview, posting and idempotency';
GO
BEGIN TRANSACTION;
DECLARE @U int,@C int,@Y1 int,@Y2 int,@E1 int,@E2 int,@F int,@P int;
EXEC #Fixture @U OUTPUT,@C OUTPUT,@Y1 OUTPUT,@Y2 OUTPUT,@E1 OUTPUT,@E2 OUTPUT,@F OUTPUT,@P OUTPUT;
DECLARE @Selection dbo.PostingSelectionType;
INSERT @Selection VALUES(@E1,@F,'20260501'),(@E2,@F,'20260501');
BEGIN TRY
 EXEC dbo.PostSelectedMonthlyFees @UserId=@U,@BillingMonth='20260501',@Selection=@Selection,@AcademicYearId=@Y2;
 THROW 57804,'Cross-year selection was accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>54744 THROW; END CATCH;
IF @@TRANCOUNT<>0 OR EXISTS(SELECT 1 FROM dbo.Campuses WHERE CampusId=@C) THROW 57805,'Rejected posting did not roll back completely.',1;
PRINT 'PASS mixed-year selection rejected atomically at the database boundary';
GO
BEGIN TRANSACTION;
DECLARE @U int,@C int,@Y1 int,@Y2 int,@E1 int,@E2 int,@F int,@P int;
EXEC #Fixture @U OUTPUT,@C OUTPUT,@Y1 OUTPUT,@Y2 OUTPUT,@E1 OUTPUT,@E2 OUTPUT,@F OUTPUT,@P OUTPUT;
DECLARE @Assignments dbo.CampusClassSectionAssignmentType;
INSERT @Assignments SELECT ClassId,SectionId,0 FROM dbo.CampusClassSections WHERE ClassSectionId=@P;
BEGIN TRY
 EXEC dbo.SaveCampusClassSections @U,@C,@Assignments;
 THROW 57806,'Occupied placement was deactivated.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>50106 THROW; END CATCH;
IF @@TRANCOUNT<>0 THROW 57807,'Rejected setup change did not roll back.',1;
PRINT 'PASS occupied placement deactivation blocked with rollback';
GO
BEGIN TRANSACTION;
DECLARE @U int,@C int,@Y1 int,@Y2 int,@E1 int,@E2 int,@F int,@P int,@Unused int;
EXEC #Fixture @U OUTPUT,@C OUTPUT,@Y1 OUTPUT,@Y2 OUTPUT,@E1 OUTPUT,@E2 OUTPUT,@F OUTPUT,@P OUTPUT;
INSERT dbo.CampusClassSections(CampusId,ClassId,SectionId,IsActive)
 SELECT TOP(1) @C,ClassId,SectionId,1 FROM dbo.CampusClassSections WHERE CampusId<>@C AND NOT EXISTS(SELECT 1 FROM dbo.CampusClassSections X WHERE X.CampusId=@C AND X.ClassId=dbo.CampusClassSections.ClassId AND X.SectionId=dbo.CampusClassSections.SectionId);
SET @Unused=SCOPE_IDENTITY();
IF @Unused IS NULL THROW 57808,'Fixture requires a second catalog placement.',1;
DECLARE @Assignments dbo.CampusClassSectionAssignmentType;
INSERT @Assignments SELECT ClassId,SectionId,0 FROM dbo.CampusClassSections WHERE ClassSectionId=@Unused;
EXEC dbo.SaveCampusClassSections @U,@C,@Assignments;
IF NOT EXISTS(SELECT 1 FROM dbo.CampusClassSections WHERE ClassSectionId=@Unused AND IsActive=0) OR NOT EXISTS(SELECT 1 FROM dbo.CampusClassSections WHERE ClassSectionId=@P AND IsActive=1) THROW 57809,'Unused placement handling changed unrelated placement.',1;
UPDATE dbo.Enrollments SET Status=N'Left',LeavingDate=EnrollmentDate WHERE EnrollmentId IN(@E1,@E2);
DELETE @Assignments;
INSERT @Assignments SELECT ClassId,SectionId,0 FROM dbo.CampusClassSections WHERE ClassSectionId=@P;
EXEC dbo.SaveCampusClassSections @U,@C,@Assignments;
IF NOT EXISTS(SELECT 1 FROM dbo.CampusClassSections WHERE ClassSectionId=@P AND IsActive=0) OR (SELECT COUNT(*) FROM dbo.Enrollments WHERE EnrollmentId IN(@E1,@E2))<>2 THROW 57810,'Historical-only retirement lost records.',1;
ROLLBACK;
PRINT 'PASS unused and historical-only placements can retire without deleting enrollments';
GO
BEGIN TRANSACTION;
DECLARE @U int,@C int,@Y1 int,@Y2 int,@E1 int,@E2 int,@F int,@P int;
EXEC #Fixture @U OUTPUT,@C OUTPUT,@Y1 OUTPUT,@Y2 OUTPUT,@E1 OUTPUT,@E2 OUTPUT,@F OUTPUT,@P OUTPUT;
INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,GrossAmount,ChargeKind)
 SELECT A.StudentFeeAssignmentId,E.EnrollmentId,E.AdmissionId,@F,'20260501','20260501',100,K.Kind FROM dbo.Enrollments E JOIN dbo.StudentFeeAssignments A ON A.EnrollmentId=E.EnrollmentId CROSS JOIN(VALUES(N'Monthly'),(N'Annual'),(N'Manual')) K(Kind) WHERE E.EnrollmentId IN(@E1,@E2);
UPDATE dbo.Enrollments SET Status=N'Left',LeavingDate='20260601' WHERE EnrollmentId=@E1;
DECLARE @Types dbo.FeeTypeIdList;
DECLARE @Months TABLE(BillingMonth date,ChargesAffected int,ReversibleCharges int,TotalPosted decimal(38,2),TotalCollected decimal(38,2));
INSERT @Types VALUES(@F);
INSERT @Months EXEC dbo.GetReversibleMonths @U,@C,0,0,@Types;
IF (SELECT COUNT(*) FROM @Months)<>1 OR EXISTS(SELECT 1 FROM @Months WHERE ChargesAffected<>3 OR ReversibleCharges<>3 OR TotalPosted<>300) THROW 57811,'Reversal discovery included closed enrollment charges or omitted charge kinds.',1;
EXEC dbo.PreviewPostingReversal @U,'20260501',@C,0,0,@Types;
EXEC dbo.ReverseMonthlyFees @UserId=@U,@BillingMonth='20260501',@CampusId=@C,@FeeTypeIds=@Types,@Reason=N'Audit all-kind month reversal test';
IF (SELECT COUNT(*) FROM dbo.BillingCharges WHERE EnrollmentId=@E2 AND Status=N'Cancelled')<>3 OR (SELECT COUNT(*) FROM dbo.BillingCharges WHERE EnrollmentId=@E1 AND Status=N'Open')<>3 THROW 57812,'Reversal scope or closed-enrollment protection changed.',1;
ROLLBACK;
PRINT 'PASS month discovery agrees with active eligibility; all three charge kinds reverse; closed charges retained';
GO
-- Historical horizons above 100 months must work without changing the 24-month future cap.
DECLARE @MonthsBack int=12;
WHILE @MonthsBack<=130
BEGIN
 BEGIN TRANSACTION;
 DECLARE @U int,@C int,@Y1 int,@Y2 int,@E1 int,@E2 int,@F int,@P int,@Start date=DATEADD(month,-@MonthsBack,CONVERT(date,SYSDATETIME()));
 EXEC #Fixture @U OUTPUT,@C OUTPUT,@Y1 OUTPUT,@Y2 OUTPUT,@E1 OUTPUT,@E2 OUTPUT,@F OUTPUT,@P OUTPUT;
 UPDATE dbo.Students SET AdmissionDate=@Start WHERE AdmissionId IN(SELECT AdmissionId FROM dbo.Enrollments WHERE EnrollmentId=@E1);
 UPDATE dbo.Enrollments SET EnrollmentDate=@Start,BillingStartDate=@Start WHERE EnrollmentId=@E1;
 DECLARE @Projection TABLE(ExpectedBillingMonth date,FeeTypeId int,FeeTypeName nvarchar(200),Frequency nvarchar(30),ExpectedAmount decimal(18,2),ExpectedDiscountAmount decimal(18,2),ExpectedNetAmount decimal(18,2),StudentFeeAssignmentId int);
 INSERT @Projection EXEC dbo.GetStudentFeeProjection @U,@E1,NULL,30;
 IF (SELECT COUNT(*) FROM @Projection)<>24 OR EXISTS(SELECT 1 FROM @Projection WHERE ExpectedNetAmount<>100) THROW 57813,'Projection horizon/count changed.',1;
 DECLARE @Missing TABLE(EnrollmentId int,AdmissionId int,RegistrationNumber nvarchar(100),StudentName nvarchar(200),CampusName nvarchar(200),ClassName nvarchar(200),SectionName nvarchar(200),FeeTypeId int,FeeTypeName nvarchar(200),ExpectedBillingMonth date,ExpectedAmount decimal(18,2),StudentFeeAssignmentId int);
 INSERT @Missing EXEC dbo.GetMissingChargeCandidates @U,@Y1,@C;
 IF (SELECT COUNT(*) FROM @Missing)<>@MonthsBack+1 THROW 57814,'Historical missing-charge months were lost or duplicated.',1;
 DELETE @Projection; DELETE @Missing;
 ROLLBACK;
 PRINT CONCAT('PASS historical reports: ',@MonthsBack,' months, exact history and preserved future cap');
 SET @MonthsBack=CASE @MonthsBack WHEN 12 THEN 99 WHEN 99 THEN 101 WHEN 101 THEN 130 ELSE 131 END;
END;
GO
