-- Disposable schema-72 database only. Supply an administrator's UserId.
-- sqlcmd -S <test instance> -d <test database> -E -b -v UserId=1 -i Validation/AcademicProgramDeleteChecks.sql
-- The unused fixture is rolled back. The in-use test uses an existing class.
:ON ERROR EXIT
SET XACT_ABORT ON;
DECLARE @UserId int=$(UserId),@UnusedId int,@UsedId int,@Blocked bit=0;
EXEC dbo.RequirePermission @UserId,N'System.Setup';
SELECT TOP(1) @UsedId=AcademicProgramId FROM dbo.Classes WHERE AcademicProgramId IS NOT NULL;
IF @UsedId IS NULL THROW 51000,'This disposable test database needs a class linked to a program.',1;

-- An in-use program must remain, even when called directly, bypassing the UI.
BEGIN TRY
 EXEC dbo.DeleteAcademicProgram @UserId=@UserId,@AcademicProgramId=@UsedId;
END TRY
BEGIN CATCH
 IF ERROR_NUMBER()<>56423 THROW;
 SET @Blocked=1;
END CATCH;
IF @Blocked=0 THROW 51000,'In-use deletion was accepted.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.AcademicPrograms WHERE AcademicProgramId=@UsedId)
 THROW 51000,'The linked program was removed.',1;

-- An unused program can be deleted, and a stale/repeated request is refused.
BEGIN TRANSACTION;
INSERT dbo.AcademicPrograms(ProgramType,ProgramCode,ProgramName,DisplayOrder,CreatedAt)
VALUES(N'School',NULL,N'Disposable unused program',2147483000,SYSUTCDATETIME());
SET @UnusedId=CONVERT(int,SCOPE_IDENTITY());
EXEC dbo.DeleteAcademicProgram @UserId=@UserId,@AcademicProgramId=@UnusedId;
IF EXISTS(SELECT 1 FROM dbo.AcademicPrograms WHERE AcademicProgramId=@UnusedId)
 THROW 51000,'Unused deletion did not remove the program.',1;
ROLLBACK TRANSACTION;
SET @Blocked=0;
BEGIN TRY
 EXEC dbo.DeleteAcademicProgram @UserId=@UserId,@AcademicProgramId=@UnusedId;
END TRY
BEGIN CATCH
 IF ERROR_NUMBER()<>56422 THROW;
 SET @Blocked=1;
END CATCH;
IF @Blocked=0 THROW 51000,'Stale deletion was accepted.',1;
PRINT 'PASS: linked programs are protected, unused programs can be deleted, and stale requests are refused.';
