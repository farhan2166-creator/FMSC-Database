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
-- Read only the beginning of the module definition (including optional comments).
-- SQL Server may preserve formatting around CREATE [OR ALTER] PROCEDURE.
DECLARE @ModuleText nvarchar(max)=UPPER(@Reopen);
DECLARE @HeaderStart int=1;
WHILE @HeaderStart <= LEN(@ModuleText)
BEGIN
    WHILE @HeaderStart<=LEN(@ModuleText)
          AND UNICODE(SUBSTRING(@ModuleText,@HeaderStart,1)) IN (9,10,13,32)
        SET @HeaderStart+=1;
    IF SUBSTRING(@ModuleText,@HeaderStart,2)=N'--'
    BEGIN
        DECLARE @LineEnd int=CHARINDEX(CHAR(10),@ModuleText,@HeaderStart+2);
        IF @LineEnd=0 THROW 51007,'Procedure module comment was not terminated.',1;
        SET @HeaderStart=@LineEnd+1;
        CONTINUE;
    END;
    IF SUBSTRING(@ModuleText,@HeaderStart,2)=N'/*'
    BEGIN
        DECLARE @CommentEnd int=CHARINDEX(N'*/',@ModuleText,@HeaderStart+2);
        IF @CommentEnd=0 THROW 51007,'Procedure module comment was not terminated.',1;
        SET @HeaderStart=@CommentEnd+2;
        CONTINUE;
    END;
    BREAK;
END;
DECLARE @ProcAt int=CHARINDEX(N'PROC',@ModuleText,@HeaderStart);
IF @ProcAt=0 OR @ProcAt-@HeaderStart>100
    THROW 51007,'Procedure definition has an unexpected header; refusing an unsafe change.',1;
DECLARE @Prefix nvarchar(100)=SUBSTRING(@ModuleText,@HeaderStart,@ProcAt-@HeaderStart);
SET @Prefix=REPLACE(REPLACE(REPLACE(@Prefix,CHAR(9),N' '),CHAR(10),N' '),CHAR(13),N' ');
WHILE CHARINDEX(N'  ',@Prefix)>0 SET @Prefix=REPLACE(@Prefix,N'  ',N' ');
SET @Prefix=LTRIM(RTRIM(@Prefix));
IF @Prefix IN (N'CREATE',N'CREATE OR ALTER')
    SET @Reopen=STUFF(@Reopen,@HeaderStart,@ProcAt-@HeaderStart,N'ALTER ');
ELSE IF @Prefix<>N'ALTER'
    THROW 51007,'Procedure definition has an unexpected header; refusing an unsafe change.',1;
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
-- Read only the beginning of the module definition (including optional comments).
-- SQL Server may preserve formatting around CREATE [OR ALTER] PROCEDURE.
DECLARE @ModuleText nvarchar(max)=UPPER(@Plan);
DECLARE @HeaderStart int=1;
WHILE @HeaderStart <= LEN(@ModuleText)
BEGIN
    WHILE @HeaderStart<=LEN(@ModuleText)
          AND UNICODE(SUBSTRING(@ModuleText,@HeaderStart,1)) IN (9,10,13,32)
        SET @HeaderStart+=1;
    IF SUBSTRING(@ModuleText,@HeaderStart,2)=N'--'
    BEGIN
        DECLARE @LineEnd int=CHARINDEX(CHAR(10),@ModuleText,@HeaderStart+2);
        IF @LineEnd=0 THROW 51007,'Procedure module comment was not terminated.',1;
        SET @HeaderStart=@LineEnd+1;
        CONTINUE;
    END;
    IF SUBSTRING(@ModuleText,@HeaderStart,2)=N'/*'
    BEGIN
        DECLARE @CommentEnd int=CHARINDEX(N'*/',@ModuleText,@HeaderStart+2);
        IF @CommentEnd=0 THROW 51007,'Procedure module comment was not terminated.',1;
        SET @HeaderStart=@CommentEnd+2;
        CONTINUE;
    END;
    BREAK;
END;
DECLARE @ProcAt int=CHARINDEX(N'PROC',@ModuleText,@HeaderStart);
IF @ProcAt=0 OR @ProcAt-@HeaderStart>100
    THROW 51007,'Procedure definition has an unexpected header; refusing an unsafe change.',1;
DECLARE @Prefix nvarchar(100)=SUBSTRING(@ModuleText,@HeaderStart,@ProcAt-@HeaderStart);
SET @Prefix=REPLACE(REPLACE(REPLACE(@Prefix,CHAR(9),N' '),CHAR(10),N' '),CHAR(13),N' ');
WHILE CHARINDEX(N'  ',@Prefix)>0 SET @Prefix=REPLACE(@Prefix,N'  ',N' ');
SET @Prefix=LTRIM(RTRIM(@Prefix));
IF @Prefix IN (N'CREATE',N'CREATE OR ALTER')
    SET @Plan=STUFF(@Plan,@HeaderStart,@ProcAt-@HeaderStart,N'ALTER ');
ELSE IF @Prefix<>N'ALTER'
    THROW 51007,'Procedure definition has an unexpected header; refusing an unsafe change.',1;
EXEC sys.sp_executesql @Plan;
GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'67',SYSDATETIME(),SUSER_SNAME(),
 N'Move class-section IsActive choice to C# without altering SQL history protections');
GO
