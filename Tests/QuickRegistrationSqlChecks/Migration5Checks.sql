DECLARE @Bodies nvarchar(max)=CONCAT(OBJECT_DEFINITION(OBJECT_ID(N'dbo.CompleteQuickAdmission')),OBJECT_DEFINITION(OBJECT_ID(N'dbo.AddStudentFeeAssignment')),OBJECT_DEFINITION(OBJECT_ID(N'dbo.PostEnrollmentOpeningCharges')));
DECLARE @Before nvarchar(max)=(SELECT CONCAT(
 (SELECT * FROM dbo.Students ORDER BY AdmissionId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.BillingCharges ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.FeePayments ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.RolePermissions ORDER BY RoleId,PermissionId FOR JSON PATH,INCLUDE_NULL_VALUES)));
IF (SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC)<>N'4' THROW 57920,'Unexpected migration-5 fixture',1;
EXEC(N'CREATE TRIGGER dbo.FMSC_QR_FailMigration5 ON dbo.DatabaseVersion AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted WHERE VersionNumber=N''5'') THROW 57921,''Injected migration-5 final failure'',1; END;');
BEGIN TRY EXEC sys.sp_executesql @Migration; THROW 57922,'Injected failure did not fire',1; END TRY
BEGIN CATCH IF ERROR_NUMBER()<>57921 THROW; END CATCH;
IF @Bodies<>CONCAT(OBJECT_DEFINITION(OBJECT_ID(N'dbo.CompleteQuickAdmission')),OBJECT_DEFINITION(OBJECT_ID(N'dbo.AddStudentFeeAssignment')),OBJECT_DEFINITION(OBJECT_ID(N'dbo.PostEnrollmentOpeningCharges')))
 OR (SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC)<>N'4' THROW 57923,'Failed migration changed procedures or version',1;
PRINT 'PASS: migration-5 final failure rolls back all three procedure changes and version';
DROP TRIGGER dbo.FMSC_QR_FailMigration5;
EXEC sys.sp_executesql @Migration;
IF (SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC)<>N'5' THROW 57924,'Migration 5 failed',1;
PRINT 'PASS: successful migration 5 installs version 5';
DECLARE @Versions int=(SELECT COUNT(*) FROM dbo.DatabaseVersion);
EXEC sys.sp_executesql @Migration;
IF (SELECT COUNT(*) FROM dbo.DatabaseVersion)<>@Versions THROW 57925,'Migration 5 rerun wrote another version',1;
PRINT 'PASS: migration 5 rerun is a no-op';
DECLARE @After nvarchar(max)=(SELECT CONCAT(
 (SELECT * FROM dbo.Students ORDER BY AdmissionId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.BillingCharges ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.FeePayments ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES),
 (SELECT * FROM dbo.RolePermissions ORDER BY RoleId,PermissionId FOR JSON PATH,INCLUDE_NULL_VALUES)));
IF @Before<>@After THROW 57926,'Migration 5 modified school or permission records',1;
PRINT 'PASS: migration 5 and retry preserve students, charges, payments and permission assignments';
