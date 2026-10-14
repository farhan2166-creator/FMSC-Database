IF (SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC)<>N'5' THROW 57940,'Expected a disposable schema-5 fixture',1;
DECLARE @Before nvarchar(max)=(SELECT CONCAT(
 (SELECT * FROM dbo.FeePayments ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.BankAccountOpeningCertificates ORDER BY BankAccountOpeningCertificateId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.RolePermissions ORDER BY RoleId,PermissionId FOR JSON PATH,INCLUDE_NULL_VALUES)));
EXEC(N'CREATE TRIGGER dbo.FMSC_FailMutationMigration ON dbo.DatabaseVersion AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted WHERE VersionNumber=N''6'') THROW 57941,''Injected final migration failure'',1; END;');
BEGIN TRY EXEC sys.sp_executesql @Migration;THROW 57942,'Migration failure did not fire',1;END TRY
BEGIN CATCH IF ERROR_NUMBER()<>57941 THROW;END CATCH;
IF OBJECT_ID(N'dbo.MutationRequests') IS NOT NULL OR (SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC)<>N'5' THROW 57943,'Failed migration was not atomic',1;
PRINT 'PASS: migration-6 final failure rolls back table, constraints and version';
DROP TRIGGER dbo.FMSC_FailMutationMigration;
EXEC sys.sp_executesql @Migration;
IF (SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC)<>N'6' THROW 57944,'Migration 6 did not finish',1;
PRINT 'PASS: migration 6 installs version 6';
DECLARE @Versions int=(SELECT COUNT(*) FROM dbo.DatabaseVersion);
EXEC sys.sp_executesql @Migration;
IF (SELECT COUNT(*) FROM dbo.DatabaseVersion)<>@Versions THROW 57945,'Migration rerun recorded another version',1;
PRINT 'PASS: migration 6 rerun is a no-op';
DECLARE @After nvarchar(max)=(SELECT CONCAT(
 (SELECT * FROM dbo.FeePayments ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.BankAccountOpeningCertificates ORDER BY BankAccountOpeningCertificateId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.RolePermissions ORDER BY RoleId,PermissionId FOR JSON PATH,INCLUDE_NULL_VALUES)));
IF @Before<>@After THROW 57946,'Migration changed business records or permissions',1;
PRINT 'PASS: migration 6 preserves payments, bank certificates and permissions';
