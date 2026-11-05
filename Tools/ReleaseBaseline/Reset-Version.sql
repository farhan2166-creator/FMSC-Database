-- One-time owner-approved first-release reset. The complete approved schema
-- and every school/business record remain unchanged. Not a runtime migration.
SET NOCOUNT ON;
IF @@TRANCOUNT = 0 THROW 51000,'Baseline reset requires an outer transaction and a verified database backup.',1;
DECLARE @Current int = (SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC);
IF @Current IS NULL OR @Current NOT IN (1,76) THROW 51000,'Only the fully updated pre-release database or baseline 1 can be reset.',1;
IF OBJECT_ID(N'dbo.ApplyCollectionCredit',N'P') IS NULL
 OR OBJECT_ID(N'dbo.IssueCollectionReceipt',N'P') IS NULL
 OR COL_LENGTH(N'dbo.Students',N'AdmissionNumber') IS NULL
 THROW 51000,'The current approved database schema is required; a version label cannot upgrade an older schema.',1;
DELETE FROM dbo.DatabaseVersion;
DBCC CHECKIDENT ('dbo.DatabaseVersion',RESEED,0) WITH NO_INFOMSGS;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'1',SYSDATETIME(),SUSER_SNAME(),N'First production baseline; complete existing schema and school data preserved');
