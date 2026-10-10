SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- GetUserPermissions
CREATE   PROCEDURE dbo.GetUserPermissions
@UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1
               FROM   dbo.Users AS U
                      INNER JOIN
                      dbo.Roles AS R
                      ON R.RoleId = U.RoleId
               WHERE  U.UserId = @UserId
                      AND R.RoleName = N'Administrator')
       OR EXISTS (SELECT 1
                  FROM   dbo.Users AS U
                         INNER JOIN
                         dbo.UserRoles AS UR
                         ON UR.UserId = U.UserId
                         INNER JOIN
                         dbo.Roles AS R
                         ON R.RoleId = UR.RoleId
                  WHERE  U.UserId = @UserId
                         AND R.RoleName = N'Administrator')
        BEGIN
            SELECT   P.PermissionId,
                     P.PermissionCode,
                     P.PermissionName
            FROM     dbo.Permissions AS P
            ORDER BY P.PermissionCode;
            RETURN;
        END
    SELECT   DISTINCT P.PermissionId,
                      P.PermissionCode,
                      P.PermissionName
    FROM     dbo.Users AS U
             INNER JOIN
             dbo.UserRoles AS UR
             ON UR.UserId = U.UserId
             INNER JOIN
             dbo.Roles AS R
             ON R.RoleId = UR.RoleId
             INNER JOIN
             dbo.RolePermissions AS RP
             ON RP.RoleId = R.RoleId
             INNER JOIN
             dbo.Permissions AS P
             ON P.PermissionId = RP.PermissionId
    WHERE    U.UserId = @UserId
    ORDER BY P.PermissionCode;
END



GO
