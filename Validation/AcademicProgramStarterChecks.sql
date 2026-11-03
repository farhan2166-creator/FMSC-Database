-- Run from the repository root with sqlcmd on a disposable schema-70 database:
-- sqlcmd -S <test instance> -d <test database> -E -b -i Validation/AcademicProgramStarterChecks.sql
-- All inserts, including the migration version marker, are rolled back.
:ON ERROR EXIT
SET XACT_ABORT ON;
BEGIN TRANSACTION;
SELECT * INTO #ProgramsBefore FROM dbo.AcademicPrograms;
SELECT ClassId,AcademicProgramId INTO #ClassProgramsBefore FROM dbo.Classes;
GO
:r Migrations/71.sql
GO
IF EXISTS
(
 SELECT * FROM #ProgramsBefore
 EXCEPT SELECT * FROM dbo.AcademicPrograms
)
 THROW 51000,'An existing program was changed or removed.',1;
IF EXISTS
(
 SELECT ClassId,AcademicProgramId FROM #ClassProgramsBefore
 EXCEPT SELECT ClassId,AcademicProgramId FROM dbo.Classes
)
 THROW 51000,'An existing class program assignment was changed.',1;
IF EXISTS
(
 SELECT * FROM (VALUES
 (N'School',N'Primary'),(N'School',N'Middle'),(N'School',N'Secondary'),
 (N'College',N'F.Sc (Pre-Medical)'),(N'College',N'F.Sc (Pre-Engineering)'),
 (N'College',N'ICS'),(N'College',N'I.Com'),(N'College',N'FA')) S(ProgramType,ProgramName)
 WHERE NOT EXISTS(SELECT 1 FROM dbo.AcademicPrograms P
  WHERE P.ProgramType=S.ProgramType AND LTRIM(RTRIM(P.ProgramName))=S.ProgramName)
)
 THROW 51000,'An approved starter program is missing.',1;
IF (SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC)<>71
 THROW 51000,'The migration version was not recorded.',1;
ROLLBACK TRANSACTION;
PRINT 'PASS: all eight starter programs are available and existing programs and class links are unchanged. Test rolled back.';
