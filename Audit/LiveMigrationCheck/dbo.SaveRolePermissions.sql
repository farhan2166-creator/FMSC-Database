
-- SaveRolePermissions
CREATE   PROCEDURE dbo.SaveRolePermissions
@UserId INT, @RoleId INT, @PermissionIdsJson NVARCHAR (MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequireAnyPermission @UserId, N'System.Roles', N'System.Roles.Manage', N'System.Administration';
    IF @RoleId <= 0
        THROW 57300, 'A valid role is required.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Roles
                   WHERE  RoleId = @RoleId)
        THROW 57301, 'The selected role was not found.', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.Roles
               WHERE  RoleId = @RoleId
                      AND RoleName = N'Administrator')
        THROW 57302, 'Administrator permissions are managed automatically.', 1;
    IF ISJSON(@PermissionIdsJson) <> 1
        THROW 57303, 'The supplied permission list is not valid JSON.', 1;
    DECLARE @SelectedPermissions TABLE (
        PermissionId INT NOT NULL PRIMARY KEY);
    INSERT INTO @SelectedPermissions (PermissionId)
    SELECT DISTINCT TRY_CONVERT (INT, [value])
    FROM   OPENJSON (@PermissionIdsJson)
    WHERE  TRY_CONVERT (INT, [value]) IS NOT NULL;
    IF EXISTS (SELECT 1
               FROM   @SelectedPermissions AS SP
                      LEFT OUTER JOIN
                      dbo.Permissions AS P
                      ON P.PermissionId = SP.PermissionId
               WHERE  P.PermissionId IS NULL)
        THROW 57304, 'One or more selected permissions are invalid.', 1;
    DECLARE @OldPermissionIds TABLE (
        PermissionId INT NOT NULL);
    INSERT INTO @OldPermissionIds (PermissionId)
    SELECT PermissionId
    FROM   dbo.RolePermissions
    WHERE  RoleId = @RoleId;
    BEGIN TRANSACTION;
    DELETE dbo.RolePermissions
    WHERE  RoleId = @RoleId;
    INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
    SELECT @RoleId,
           PermissionId
    FROM   @SelectedPermissions;
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @AuditRecordId AS NVARCHAR (200) = CAST (@RoleId AS NVARCHAR (200));
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'ROLE_PERMISSIONS_SAVE', @AuditRecordId);
    DECLARE @OldPermissionIdsJson AS NVARCHAR (MAX);
    DECLARE @NewPermissionIdsJson AS NVARCHAR (MAX);
    SELECT @OldPermissionIdsJson = N'[' + ISNULL(STRING_AGG(CAST (PermissionId AS NVARCHAR (10)), N',') WITHIN GROUP (ORDER BY PermissionId), N'') + N']'
    FROM   @OldPermissionIds;
    SELECT @NewPermissionIdsJson = N'[' + ISNULL(STRING_AGG(CAST (PermissionId AS NVARCHAR (10)), N',') WITHIN GROUP (ORDER BY PermissionId), N'') + N']'
    FROM   @SelectedPermissions;
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'RolePermissions', @RecordId = @AuditRecordId, @Action = N'UPDATE', @OldValues = @OldPermissionIdsJson, @NewValues = @NewPermissionIdsJson;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @RoleId AS RoleId;
END



