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
IF @Definition IS NULL OR CHARINDEX(@Anchor,@Definition)=0
 OR CHARINDEX(@Anchor,@Definition,CHARINDEX(@Anchor,@Definition)+LEN(@Anchor))<>0
 OR CHARINDEX(N'THROW 52311',@Definition)<>0
 THROW 51000,'Transfer Class differs from expected schema 68; refusing an unsafe change.',1;
SET @Definition=REPLACE(@Definition,@Anchor,
 @Anchor+CHAR(10)+N'    IF @OldClassId = @NewClassId
        THROW 52311, ''This is a section-only movement. Use Transfer Section; no class-transfer fees were changed.'', 1;');
EXEC sys.sp_executesql @Definition;
GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'69',SYSDATETIME(),SUSER_SNAME(),
 N'Require Transfer Class to change ClassId; same-class section movement uses Transfer Section');
GO
