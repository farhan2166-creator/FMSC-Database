-- Align the optional communication module with the release's base schema, and
-- retire unused role-read and payment-list APIs. No school, payment or permission rows change.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 20 requires the installer transaction.',1;
IF (SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC)<>19
    THROW 51000,'Migration 20 requires schema 19.',1;
IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
    DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
    IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>6',@Definition)=0
        THROW 51000,'Unexpected communication callback definition. Review before upgrading.',1;
    SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>6',N'FROM dbo.DatabaseVersion)<>20');
    -- Retire audit writing only in this revised procedure. Inbox hashes,
    -- pending receipts and delivery state remain authoritative recovery records.
    SET @Definition=REPLACE(@Definition,NCHAR(13)+NCHAR(10),NCHAR(10));
    SET @Definition=REPLACE(@Definition,N' INSERT comm.Audit(Actor,Operation,Detail) VALUES(@Actor,''SignedMetaCallback'',N''Validated external gateway callback persisted; duplicates are idempotent'');'+NCHAR(10),N'');
    DECLARE @At int=CHARINDEX(N'PROCEDURE',@Definition);
    IF @At=0 THROW 51000,'Cannot identify communication callback procedure.',1;
    SET @Definition=N'ALTER '+SUBSTRING(@Definition,@At,LEN(@Definition));
    EXEC sys.sp_executesql @Definition;
END;
DROP PROCEDURE IF EXISTS dbo.GetAdjustableFeePayments;
DROP PROCEDURE IF EXISTS dbo.GetRolesAndPermissions;
DROP PROCEDURE IF EXISTS dbo.GetUserRoles;
DROP PROCEDURE IF EXISTS dbo.GetRoles;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'20',SYSDATETIME(),SUSER_SNAME(),N'Align communication schema and retire unused read APIs');
