-- Communication depends on its own required APIs, not the main application's
-- migration number. Preserve webhook permissions, idempotency and delivery data.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 41 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>40
 THROW 51000,'Migration 41 requires schema 40.',1;
IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 DECLARE @Guard nvarchar(max)=N' IF (SELECT MAX(TRY_CONVERT(int,VersionNumber)) FROM dbo.DatabaseVersion)<>40 THROW 51000,''Incompatible base database version.'',1;';
 IF @Definition IS NULL OR CHARINDEX(@Guard,@Definition)=0 THROW 51000,'Unexpected communication callback definition. Review before upgrading.',1;
 SET @Definition=REPLACE(@Definition,@Guard,N'');
 -- Retire only the known audit-only callback write if this snapshot retains it.
 SET @Definition=REPLACE(@Definition,N' INSERT comm.Audit(Actor,Operation,Detail) VALUES(@Actor,''SignedMetaCallback'',N''Validated external gateway callback persisted; duplicates are idempotent'');',N'');
 IF CHARINDEX(N'dbo.DatabaseVersion',@Definition)>0 OR CHARINDEX(N'INSERT comm.Audit',@Definition)>0
  THROW 51000,'Unexpected callback version or audit work remains. Review before upgrading.',1;
 DECLARE @At int=CHARINDEX(N'PROCEDURE',@Definition);
 IF @At=0 THROW 51000,'Cannot identify communication callback procedure.',1;
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,@At,LEN(@Definition));
 EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'41',SYSDATETIME(),SUSER_SNAME(),N'Decouple communication callback from the main database migration version');
GO
