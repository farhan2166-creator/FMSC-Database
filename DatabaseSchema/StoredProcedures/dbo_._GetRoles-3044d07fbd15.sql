-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequireAnyPermission @UserId, N'System.Roles', N'System.Users', N'System.Administration';
    SELECT   RoleId,
             RoleName,
             Description
    FROM     dbo.Roles
    ORDER BY RoleName;
END
