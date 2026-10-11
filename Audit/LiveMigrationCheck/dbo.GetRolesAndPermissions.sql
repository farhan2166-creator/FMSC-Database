

-- Viewing the full role -> permission matrix (used to populate the
-- checkbox state when a role is selected on the Roles page).
CREATE   PROCEDURE dbo.GetRolesAndPermissions
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequireAnyPermission
        @UserId,
        N'System.Roles',
        N'System.Administration';

    SELECT
        R.RoleId,
        R.RoleName,
        P.PermissionId,
        P.PermissionCode,
        P.PermissionName
    FROM dbo.Roles R
    LEFT JOIN dbo.RolePermissions RP ON RP.RoleId=R.RoleId
    LEFT JOIN dbo.Permissions P ON P.PermissionId=RP.PermissionId
    ORDER BY R.RoleName,P.PermissionCode;
END;

