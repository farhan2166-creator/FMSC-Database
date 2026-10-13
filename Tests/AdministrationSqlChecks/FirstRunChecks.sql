-- Only a schema-only, explicitly disposable version-3 fixture with seeded reference data.
SET NOCOUNT ON;
IF DB_NAME() NOT LIKE N'FMSC[_]AuditChecks[_]%' THROW 59991,'Use an explicitly disposable audit database.',1;
IF EXISTS(SELECT 1 FROM dbo.Users) THROW 59992,'First-run checks require an empty Users table.',1;
DECLARE @Admin int=(SELECT RoleId FROM dbo.Roles WHERE RoleName=N'Administrator');
IF @Admin IS NULL OR (SELECT COUNT(*) FROM dbo.Permissions)<>28 THROW 59993,'Seed the two roles and supported permission catalog first.',1;
DECLARE @Hash varbinary(64)=CONVERT(varbinary(64),REPLICATE('H',64)),@Salt varbinary(32)=CONVERT(varbinary(32),REPLICATE('S',32));
EXEC dbo.BootstrapFirstAdministrator @Username=N'first-run-audit-fixture',@PasswordHash=@Hash,@PasswordSalt=@Salt;
DECLARE @User int=(SELECT UserId FROM dbo.Users WHERE Username=N'first-run-audit-fixture');
IF (SELECT RoleId FROM dbo.Users WHERE UserId=@User)<>@Admin OR
   (SELECT COUNT(*) FROM dbo.UserRoles WHERE UserId=@User)<>1 OR
   NOT EXISTS(SELECT 1 FROM dbo.UserRoles WHERE UserId=@User AND RoleId=@Admin)
   THROW 59994,'First-run setup must assign Administrator only.',1;
EXEC dbo.RequirePermission @User,N'Any.Future.Action';
EXEC dbo.RequireAnyPermission @User,N'Any.Future.Action';
DECLARE @Permissions TABLE(PermissionId int,PermissionCode nvarchar(100),PermissionName nvarchar(100));
INSERT @Permissions EXEC dbo.GetUserPermissions @User;
IF (SELECT COUNT(*) FROM @Permissions)<>28 THROW 59995,'Initial Administrator must receive every implemented permission.',1;
BEGIN TRY
    EXEC dbo.BootstrapFirstAdministrator @Username=N'blocked-second-fixture',@PasswordHash=@Hash,@PasswordSalt=@Salt;
    THROW 59996,'Repeated bootstrap unexpectedly succeeded.',1;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    IF ERROR_NUMBER()<>57200 THROW;
END CATCH;
IF (SELECT COUNT(*) FROM dbo.Users)<>1 THROW 59997,'Repeated bootstrap changed account state.',1;
SELECT N'PASS: 3 first-run checks: Administrator-only membership, full authority, repeated setup refused' Verification;
