-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Users
                   WHERE  UserId = @UserId)
        BEGIN
            THROW 50003, 'The specified user is not authenticated.', 1;
        END
    IF EXISTS (SELECT 1
               FROM   dbo.Users AS U
                      INNER JOIN
                      dbo.Roles AS R
                      ON R.RoleId = U.RoleId
               WHERE  U.UserId = @UserId
                      AND R.RoleName = N'Administrator')
        RETURN;
    IF EXISTS (SELECT 1
               FROM   dbo.Users AS U
                      INNER JOIN
                      dbo.UserRoles AS UR
                      ON UR.UserId = U.UserId
                      INNER JOIN
                      dbo.Roles AS R
                      ON R.RoleId = UR.RoleId
               WHERE  U.UserId = @UserId
                      AND R.RoleName = N'Administrator')
        RETURN;
    IF EXISTS (SELECT 1
               FROM   dbo.UserRoles AS UR
                      INNER JOIN
                      dbo.Roles AS R
                      ON R.RoleId = UR.RoleId
                      INNER JOIN
                      dbo.RolePermissions AS RP
                      ON RP.RoleId = R.RoleId
                      INNER JOIN
                      dbo.Permissions AS P
                      ON P.PermissionId = RP.PermissionId
               WHERE  UR.UserId = @UserId
                      AND P.PermissionCode = @PermissionCode)
        RETURN;
    THROW 50003, 'You are not authorized to perform this operation.', 1;
END
