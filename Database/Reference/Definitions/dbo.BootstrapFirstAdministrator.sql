SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- BootstrapFirstAdministrator
CREATE   PROCEDURE dbo.BootstrapFirstAdministrator
@Username NVARCHAR (100), @PasswordHash VARBINARY (64), @PasswordSalt VARBINARY (32)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DECLARE @SetupLockResult AS INT;
    EXECUTE @SetupLockResult = sys.sp_getapplock @Resource = N'FMSC.FirstRunAdministrator', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;
    IF @SetupLockResult < 0
        THROW 57207, 'Administrator setup is already in progress in another window. Wait for it to finish, then sign in with the administrator account it creates.', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.Users)
        THROW 57200, 'Initial administrator setup is already completed. Please sign in with an existing administrator account.', 1;
    IF NULLIF (LTRIM(RTRIM(@Username)), N'') IS NULL
        THROW 57201, 'Administrator username is required.', 1;
    IF LEN(LTRIM(RTRIM(@Username))) > 100
        THROW 57202, 'Administrator username is too long.', 1;
    IF @PasswordHash IS NULL
       OR DATALENGTH(@PasswordHash) <> 64
        THROW 57203, 'The supplied administrator password hash is invalid.', 1;
    IF @PasswordSalt IS NULL
       OR DATALENGTH(@PasswordSalt) <> 32
        THROW 57204, 'The supplied administrator password salt is invalid.', 1;
    DECLARE @RoleId AS INT;
    SELECT   TOP (1) @RoleId = RoleId
    FROM     dbo.Roles
    WHERE    RoleName = N'Administrator'
    ORDER BY RoleId;
    IF @RoleId IS NULL
        THROW 57205, 'The Administrator role is not available. Database reference data is incomplete.', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.Users
               WHERE  Username = LTRIM(RTRIM(@Username)))
        THROW 57206, 'That administrator username already exists.', 1;
    INSERT  dbo.Users (Username, PasswordHash, PasswordSalt, RoleId, MustChangePassword)
    VALUES           (LTRIM(RTRIM(@Username)), @PasswordHash, @PasswordSalt, @RoleId, 1);
    DECLARE @UserId AS INT = CONVERT (INT, SCOPE_IDENTITY());
    INSERT dbo.UserRoles (UserId, RoleId)
    SELECT @UserId,
           R.RoleId
    FROM   dbo.Roles AS R
    WHERE  NOT EXISTS (SELECT 1
                       FROM   dbo.UserRoles AS UR
                       WHERE  UR.UserId = @UserId
                              AND UR.RoleId = R.RoleId);
    COMMIT TRANSACTION;
    SELECT @UserId AS UserId,
           LTRIM(RTRIM(@Username)) AS Username,
           @RoleId AS RoleId;
END



GO
