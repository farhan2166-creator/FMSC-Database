SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- GetRoles
CREATE   PROCEDURE dbo.GetRoles
@UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequireAnyPermission @UserId, N'System.Roles', N'System.Users', N'System.Administration';
    SELECT   RoleId,
             RoleName,
             Description
    FROM     dbo.Roles
    ORDER BY RoleName;
END



GO
