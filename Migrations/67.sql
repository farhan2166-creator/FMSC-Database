-- Forward-only owner decision (1 Nov 2026): class-section IsActive is
-- checked for NEW choices in C#, not as a permanent SQL history prohibition.
-- Replace only the two known current hard stops, retaining permissions,
-- relationships, transaction locks, financial rules and review snapshots.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 67 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>66
 THROW 51000,'Migration 67 requires schema 66.',1;
GO
DECLARE @Reopen nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.ReopenPendingRegistration',N'P'));
DECLARE @Expected nvarchar(150)=N'WHERE ClassSectionId=@ClassSectionId AND IsActive=1';
IF @Reopen IS NULL OR CHARINDEX(@Expected,@Reopen)=0
 THROW 51000,'Reopening procedure differs from expected schema 66; no SQL was altered.',1;
SET @Reopen=REPLACE(@Reopen,@Expected,N'WHERE ClassSectionId=@ClassSectionId');
SET @Reopen=REPLACE(@Reopen,
 N'Choose an active placement and a joining date within its academic year',
 N'Choose a valid placement and a joining date within its academic year');
-- An existing module may retain a CREATE/CREATE OR ALTER definition text.
-- Re-execute it as ALTER, never CREATE, to preserve the procedure and grants.
DECLARE @ModuleHeader nvarchar(80)=UPPER(LEFT(LTRIM(@Reopen),80));
IF @ModuleHeader LIKE N'CREATE OR ALTER PROC%'
    SET @Reopen=STUFF(@Reopen,CHARINDEX(N'CREATE OR ALTER',UPPER(@Reopen)),15,N'ALTER');
ELSE IF @ModuleHeader LIKE N'CREATE PROC%'
    SET @Reopen=STUFF(@Reopen,CHARINDEX(N'CREATE',UPPER(@Reopen)),6,N'ALTER');
ELSE IF @ModuleHeader NOT LIKE N'ALTER PROC%'
    THROW 51000,'Procedure definition has an unexpected header; refusing an unsafe change.',1;
EXEC sys.sp_executesql @Reopen;
GO
DECLARE @Plan nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.ApplyStudentPlacementPlan',N'P'));
DECLARE @Expected nvarchar(150)=N'WHERE ClassSectionId=@Target AND IsActive=1';
IF @Plan IS NULL OR CHARINDEX(@Expected,@Plan)=0
 THROW 51000,'Placement plan procedure differs from expected schema 66; no SQL was altered.',1;
SET @Plan=REPLACE(@Plan,@Expected,N'WHERE ClassSectionId=@Target');
SET @Plan=REPLACE(@Plan,
 N'Choose an active destination for every selected student.',
 N'Choose a valid destination for every selected student.');
-- An existing module may retain a CREATE/CREATE OR ALTER definition text.
-- Re-execute it as ALTER, never CREATE, to preserve the procedure and grants.
DECLARE @ModuleHeader nvarchar(80)=UPPER(LEFT(LTRIM(@Plan),80));
IF @ModuleHeader LIKE N'CREATE OR ALTER PROC%'
    SET @Plan=STUFF(@Plan,CHARINDEX(N'CREATE OR ALTER',UPPER(@Plan)),15,N'ALTER');
ELSE IF @ModuleHeader LIKE N'CREATE PROC%'
    SET @Plan=STUFF(@Plan,CHARINDEX(N'CREATE',UPPER(@Plan)),6,N'ALTER');
ELSE IF @ModuleHeader NOT LIKE N'ALTER PROC%'
    THROW 51000,'Procedure definition has an unexpected header; refusing an unsafe change.',1;
EXEC sys.sp_executesql @Plan;
GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'67',SYSDATETIME(),SUSER_SNAME(),
 N'Move class-section IsActive choice to C# without altering SQL history protections');
GO
