
-- GetUsers
CREATE   PROCEDURE dbo.GetUsers
@UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequireAnyPermission @UserId, N'System.Users', N'System.Audit', N'System.Administration';
    SELECT   U.UserId,
             U.Username,
             U.MustChangePassword,
             U.CreatedAt,
             U.UpdatedAt,
             CASE WHEN U.RoleId = (SELECT   TOP (1) R.RoleId
                                   FROM     dbo.Roles AS R
                                   WHERE    R.RoleName = N'Administrator'
                                   ORDER BY R.RoleId) THEN CAST (1 AS BIT) ELSE CAST (0 AS BIT) END AS IsAdministrator,
             CASE WHEN U.RoleId = (SELECT   TOP (1) R.RoleId
                                   FROM     dbo.Roles AS R
                                   WHERE    R.RoleName = N'Administrator'
                                   ORDER BY R.RoleId) THEN N'All Roles' ELSE ISNULL(STUFF((SELECT   N', ' + R2.RoleName
                                                                                           FROM     dbo.UserRoles AS UR2
                                                                                                    INNER JOIN
                                                                                                    dbo.Roles AS R2
                                                                                                    ON R2.RoleId = UR2.RoleId
                                                                                           WHERE    UR2.UserId = U.UserId
                                                                                           ORDER BY R2.RoleName
                                                                                           FOR      XML PATH (N''), TYPE).value(N'.', N'nvarchar(max)'), 1, 2, N''), N'No Roles') END AS RoleNames
    FROM     dbo.Users AS U
    ORDER BY CASE WHEN U.RoleId = (SELECT   TOP (1) R.RoleId
                                   FROM     dbo.Roles AS R
                                   WHERE    R.RoleName = N'Administrator'
                                   ORDER BY R.RoleId) THEN 0 ELSE 1 END, U.Username;
END



