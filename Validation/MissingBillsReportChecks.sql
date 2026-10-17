-- Read-only regression checks for the current test records.
SET NOCOUNT ON;
DECLARE @Rows TABLE(EnrollmentId int,AdmissionId int,RegistrationNumber nvarchar(100),YearName nvarchar(100),StudentName nvarchar(500),CampusName nvarchar(500),ClassName nvarchar(500),SectionName nvarchar(500),FeeTypeId int,FeeTypeName nvarchar(100),ExpectedBillingMonth date,ExpectedAmount decimal(12,2),StudentFeeAssignmentId int);
INSERT @Rows EXEC dbo.GetMissingChargeCandidates @UserId=1,@AcademicYearId=0,@AsOfDate='20261017';
IF EXISTS(SELECT 1 FROM @Rows R JOIN dbo.Enrollments E ON E.EnrollmentId=R.EnrollmentId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId WHERE R.ExpectedBillingMonth<DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1) OR R.ExpectedBillingMonth>DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1)) THROW 51000,'Out-of-year bill candidates remain.',1;
IF EXISTS(SELECT 1 FROM @Rows R JOIN dbo.Enrollments E ON E.EnrollmentId=R.EnrollmentId WHERE R.ExpectedBillingMonth<DATEFROMPARTS(YEAR(COALESCE(E.BillingStartDate,E.EnrollmentDate)),MONTH(COALESCE(E.BillingStartDate,E.EnrollmentDate)),1)) THROW 51001,'Pre-billing-start month reported.',1;
IF NOT EXISTS(SELECT 1 FROM @Rows WHERE AdmissionId=6504 AND ExpectedBillingMonth='20261001' AND ExpectedAmount=500) THROW 51002,'Real October gap for 6504 was lost.',1;
IF EXISTS(SELECT 1 FROM @Rows WHERE AdmissionId=6504 AND ExpectedBillingMonth<'20261001') THROW 51003,'Existing September bill was reported as missing.',1;
IF (SELECT COUNT(*) FROM @Rows WHERE AdmissionId=6594)<>11 OR (SELECT COUNT(*) FROM @Rows WHERE AdmissionId=6595)<>11 THROW 51004,'Valid historical gaps were lost.',1;
SELECT YearName,COUNT(*) MissingBillCount,MIN(ExpectedBillingMonth) FirstMonth,MAX(ExpectedBillingMonth) LastMonth FROM @Rows GROUP BY YearName;
DELETE @Rows;
INSERT @Rows EXEC dbo.GetMissingChargeCandidates @UserId=1,@AcademicYearId=28,@AsOfDate='20261017';
IF (SELECT COUNT(*) FROM @Rows)<>1 OR NOT EXISTS(SELECT 1 FROM @Rows WHERE AdmissionId=6504) THROW 51005,'Current-year report does not match the actual missing bill.',1;
PRINT 'PASS: current-year gap, valid historical gaps, academic-year bounds, billing-start bounds and exclusion of existing bills.';
