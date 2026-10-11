
-- GetPermissions
CREATE   PROCEDURE dbo.GetPermissions
@UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequireAnyPermission @UserId, N'System.Permissions', N'System.Roles', N'System.Administration';
    SELECT   PermissionId,
             PermissionCode,
             PermissionName,
             Description
    FROM     dbo.Permissions
    ORDER BY PermissionCode;
END



