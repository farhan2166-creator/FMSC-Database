
-- GetUserForLogin
CREATE   PROCEDURE dbo.GetUserForLogin
@Username NVARCHAR (100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT U.UserId,
           U.Username,
           U.PasswordHash,
           U.PasswordSalt,
           U.MustChangePassword,
           U.FailedLoginCount,
           U.LockedUntil,
           CASE WHEN U.RoleId = (SELECT   TOP (1) R.RoleId
                                 FROM     dbo.Roles AS R
                                 WHERE    R.RoleName = N'Administrator'
                                 ORDER BY R.RoleId) THEN N'Administrator' ELSE ISNULL(STUFF((SELECT   N', ' + R2.RoleName
                                                                                             FROM     dbo.UserRoles AS UR2
                                                                                                      INNER JOIN
                                                                                                      dbo.Roles AS R2
                                                                                                      ON R2.RoleId = UR2.RoleId
                                                                                             WHERE    UR2.UserId = U.UserId
                                                                                             ORDER BY R2.RoleName
                                                                                             FOR      XML PATH (N''), TYPE).value(N'.', N'nvarchar(max)'), 1, 2, N''), N'No Roles') END AS RoleName
    FROM   dbo.Users AS U
    WHERE  U.Username = @Username;
END



