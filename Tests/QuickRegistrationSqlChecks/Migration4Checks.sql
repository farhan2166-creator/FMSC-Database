DECLARE @Before nvarchar(max)=(SELECT CONCAT(
 (SELECT * FROM dbo.Students ORDER BY AdmissionId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.Enrollments ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.BillingCharges ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.FeePayments ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.Users ORDER BY UserId FOR JSON PATH,INCLUDE_NULL_VALUES)));
IF (SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC)<>N'3' OR OBJECT_ID(N'dbo.QuickRegistrationRequests') IS NOT NULL THROW 57900,'Unexpected migration fixture',1;
PRINT 'PASS: migration starts at version 3 without a request table';
UPDATE dbo.DatabaseVersion SET VersionNumber=N'2' WHERE VersionId=(SELECT MAX(VersionId) FROM dbo.DatabaseVersion);
BEGIN TRY EXEC sys.sp_executesql @Migration; THROW 57901,'Wrong version was accepted',1; END TRY
BEGIN CATCH IF ERROR_NUMBER()<>57500 THROW; END CATCH;
IF OBJECT_ID(N'dbo.QuickRegistrationRequests') IS NOT NULL THROW 57902,'Older-version refusal changed the schema',1;
PRINT 'PASS: older version is refused without creating recovery objects';
UPDATE dbo.DatabaseVersion SET VersionNumber=N'3' WHERE VersionId=(SELECT MAX(VersionId) FROM dbo.DatabaseVersion);
EXEC(N'CREATE TRIGGER dbo.FMSC_QR_FailMigration ON dbo.DatabaseVersion AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted WHERE VersionNumber=N''4'') THROW 57903,''Injected final migration failure'',1; END;');
BEGIN TRY EXEC sys.sp_executesql @Migration; THROW 57904,'Injected failure did not fire',1; END TRY
BEGIN CATCH IF ERROR_NUMBER()<>57903 THROW; END CATCH;
PRINT 'PASS: final version-write failure is surfaced';
IF OBJECT_ID(N'dbo.QuickRegistrationRequests') IS NOT NULL OR EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_QuickRegistrationScope') OR (SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC)<>N'3' THROW 57905,'Failed migration did not roll back',1;
PRINT 'PASS: final failure rolls back table, keys, foreign keys and schema version';
DROP TRIGGER dbo.FMSC_QR_FailMigration;
EXEC sys.sp_executesql @Migration;
IF (SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC)<>N'4' OR OBJECT_ID(N'dbo.QuickRegistrationRequests') IS NULL THROW 57906,'Successful migration failed',1;
PRINT 'PASS: successful migration creates recovery table and records version 4';
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.QuickRegistrationRequests') AND is_disabled=0 AND is_not_trusted=0)<>2 OR (SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.QuickRegistrationRequests') AND is_unique=1 AND is_disabled=0)<>2 THROW 57907,'Recovery safeguards missing',1;
PRINT 'PASS: recovery has two trusted foreign keys and two enabled uniqueness safeguards';
DECLARE @Versions int=(SELECT COUNT(*) FROM dbo.DatabaseVersion),@Scope uniqueidentifier=(SELECT TRY_CONVERT(uniqueidentifier,value) FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_QuickRegistrationScope');
IF @Scope IS NULL OR @Scope='00000000-0000-0000-0000-000000000000' THROW 57910,'Recovery identity missing',1;
EXEC sys.sp_executesql @Migration;
IF @Scope<>(SELECT TRY_CONVERT(uniqueidentifier,value) FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_QuickRegistrationScope') OR (SELECT COUNT(*) FROM dbo.DatabaseVersion)<>@Versions THROW 57908,'Migration rerun recorded another version',1;
PRINT 'PASS: version-4 rerun is a no-op';
DECLARE @After nvarchar(max)=(SELECT CONCAT(
 (SELECT * FROM dbo.Students ORDER BY AdmissionId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.Enrollments ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.BillingCharges ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.FeePayments ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.Users ORDER BY UserId FOR JSON PATH,INCLUDE_NULL_VALUES)));
IF @Before<>@After THROW 57909,'Migration modified school records',1;
PRINT 'PASS: migration and retry preserve students, enrollments, charges, payments and account credentials';
