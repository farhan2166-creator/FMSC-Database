
-- RequireAnyPermission
CREATE   PROCEDURE dbo.RequireAnyPermission
@UserId INT, @PermissionCode1 NVARCHAR (100), @PermissionCode2 NVARCHAR (100)=NULL, @PermissionCode3 NVARCHAR (100)=NULL
AS
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
        BEGIN
            RETURN;
        END
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
                      AND (P.PermissionCode = @PermissionCode1
                           OR (@PermissionCode2 IS NOT NULL
                               AND P.PermissionCode = @PermissionCode2)
                           OR (@PermissionCode3 IS NOT NULL
                               AND P.PermissionCode = @PermissionCode3)))
        BEGIN
            RETURN;
        END;
    THROW 50003, 'You are not authorized to perform this operation.', 1;
END



