-- Module body only. See companion XML for signatures and relationships.

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
