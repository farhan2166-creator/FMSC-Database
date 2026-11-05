-- Owner decision, 1 Nov 2026: same-campus, same-class, different-section
-- movement is Transfer Section, never Transfer Class.
-- A direct class-transfer call must not write TRANSFER_CLASS history or
-- synchronize destination class fees for a section-only move.
-- Forward-only migration. No existing history or fee data is rewritten.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 69 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>68
 THROW 51000,'Migration 69 requires schema 68.',1;
GO
DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.TransferEnrollmentClass',N'P'));
DECLARE @Anchor nvarchar(max)=
  N'IF @OldClassSectionId = @NewClassSectionId
        THROW 52308, ''The destination class/section is the same as the current placement.'', 1;';
-- SQL exports may preserve LF while the migration file uses CRLF.
-- Normalize both before matching the otherwise exact expected guard.
SET @Definition=REPLACE(@Definition,CHAR(13)+CHAR(10),CHAR(10));
SET @Anchor=REPLACE(@Anchor,CHAR(13)+CHAR(10),CHAR(10));
IF @Definition IS NULL OR CHARINDEX(@Anchor,@Definition)=0
 OR CHARINDEX(@Anchor,@Definition,CHARINDEX(@Anchor,@Definition)+LEN(@Anchor))<>0
 OR CHARINDEX(N'THROW 52311',@Definition)<>0
 THROW 51000,'Transfer Class differs from expected schema 68; refusing an unsafe change.',1;
SET @Definition=REPLACE(@Definition,@Anchor,
 @Anchor+CHAR(10)+N'    IF @OldClassId = @NewClassId
        THROW 52311, ''This is a section-only movement. Use Transfer Section; no class-transfer fees were changed.'', 1;');
-- An existing module may retain a CREATE/CREATE OR ALTER definition text.
-- Re-execute it as ALTER, never CREATE, to preserve the procedure and grants.
-- Read only the beginning of the module definition (including optional comments).
-- SQL Server may preserve formatting around CREATE [OR ALTER] PROCEDURE.
DECLARE @ModuleText nvarchar(max)=UPPER(@Definition);
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
    SET @Definition=STUFF(@Definition,@HeaderStart,@ProcAt-@HeaderStart,N'ALTER ');
ELSE IF @Prefix<>N'ALTER'
    THROW 51007,'Procedure definition has an unexpected header; refusing an unsafe change.',1;
EXEC sys.sp_executesql @Definition;
GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'69',SYSDATETIME(),SUSER_SNAME(),
 N'Require Transfer Class to change ClassId; same-class section movement uses Transfer Section');
GO
