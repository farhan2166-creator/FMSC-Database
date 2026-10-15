-- Module body only. See companion XML for signatures and relationships.


BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequireAnyPermission @UserId, N'System.Roles', N'System.Administration';
    SELECT   PermissionId,
             PermissionCode,
             PermissionName,
             Description
    FROM     dbo.Permissions
    ORDER BY PermissionCode;
END
