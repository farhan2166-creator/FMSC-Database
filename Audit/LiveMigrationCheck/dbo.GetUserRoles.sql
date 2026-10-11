
-- GetUserRoles
CREATE   PROCEDURE dbo.GetUserRoles
@UserId INT, @TargetUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequireAnyPermission @UserId, N'System.Users', N'System.Administration';
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Users
                   WHERE  UserId = @TargetUserId)
        THROW 57221, 'The selected user account was not found.', 1;
    DECLARE @AdministratorRoleId AS INT;
    SELECT   TOP (1) @AdministratorRoleId = RoleId
    FROM     dbo.Roles
    WHERE    RoleName = N'Administrator'
    ORDER BY RoleId;
    IF EXISTS (SELECT 1
               FROM   dbo.Users AS U
               WHERE  U.UserId = @TargetUserId
                      AND U.RoleId = @AdministratorRoleId)
        BEGIN
            SELECT   R.RoleId,
                     R.RoleName,
                     R.Description,
                     CAST (1 AS BIT) AS IsAssigned
            FROM     dbo.Roles AS R
            ORDER BY CASE WHEN R.RoleId = @AdministratorRoleId THEN 0 ELSE 1 END, R.RoleName;
            RETURN;
        END
    SELECT   R.RoleId,
             R.RoleName,
             R.Description,
             CAST (CASE WHEN UR.UserId IS NULL THEN 0 ELSE 1 END AS BIT) AS IsAssigned
    FROM     dbo.Roles AS R
             LEFT OUTER JOIN
             dbo.UserRoles AS UR
             ON UR.UserId = @TargetUserId
                AND UR.RoleId = R.RoleId
    ORDER BY R.RoleName;
END



