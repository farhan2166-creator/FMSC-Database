-- Module body only. See companion XML for signatures and relationships.


BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequireAnyPermission @UserId, N'System.Users.Manage', N'System.Administration';
    DECLARE @IsNewUser AS BIT = CASE WHEN @TargetUserId = 0 THEN 1 ELSE 0 END;
    DECLARE @OldUsername AS NVARCHAR (100);
    DECLARE @OldMustChangePassword AS BIT;
    DECLARE @OldUserRoles TABLE (
        RoleId INT NOT NULL);
    IF @IsNewUser = 0
        BEGIN
            SELECT @OldUsername = Username,
                   @OldMustChangePassword = MustChangePassword
            FROM   dbo.Users
            WHERE  UserId = @TargetUserId;
            INSERT INTO @OldUserRoles (RoleId)
            SELECT RoleId
            FROM   dbo.UserRoles
            WHERE  UserId = @TargetUserId;
        END
    IF NULLIF (LTRIM(RTRIM(@Username)), N'') IS NULL
        THROW 57101, 'Username is required.', 1;
    DECLARE @AdministratorRoleId AS INT;
    SELECT   TOP (1) @AdministratorRoleId = RoleId
    FROM     dbo.Roles
    WHERE    RoleName = N'Administrator'
    ORDER BY RoleId;
    IF @AdministratorRoleId IS NULL
        THROW 57222, 'The Administrator role is not available.', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.Users AS U
               WHERE  U.UserId = @TargetUserId
                      AND (U.RoleId = @AdministratorRoleId OR EXISTS(SELECT 1 FROM dbo.UserRoles UR
                           WHERE UR.UserId=U.UserId AND UR.RoleId=@AdministratorRoleId)))
        BEGIN
            THROW 57223, 'The Administrator account is protected and cannot be edited as a normal user.', 1;
        END
    DECLARE @SelectedRoles TABLE (
        RoleId INT NOT NULL PRIMARY KEY);
    IF NULLIF (LTRIM(RTRIM(@RoleIdsJson)), N'') IS NOT NULL
        BEGIN
            IF ISNULL(ISJSON(@RoleIdsJson),0) <> 1 OR LEFT(LTRIM(@RoleIdsJson),1)<>N'['
                THROW 57224, 'The supplied role list is not valid JSON.', 1;
            IF EXISTS(SELECT 1 FROM OPENJSON(@RoleIdsJson) WHERE [type]<>2 OR TRY_CONVERT(int,[value]) IS NULL OR TRY_CONVERT(int,[value])<=0)
                THROW 57413,'Role selections must be an array of positive integer IDs.',1;
            INSERT INTO @SelectedRoles (RoleId)
            SELECT DISTINCT TRY_CONVERT (INT, [value])
            FROM   OPENJSON (@RoleIdsJson)
            WHERE  TRY_CONVERT (INT, [value]) IS NOT NULL;
        END
    ELSE
        IF @RoleId IS NOT NULL
           AND @RoleId > 0
            BEGIN
                INSERT  INTO @SelectedRoles (RoleId)
                VALUES                     (@RoleId);
            END
    IF EXISTS (SELECT 1
               FROM   @SelectedRoles AS SR
                      LEFT OUTER JOIN
                      dbo.Roles AS R
                      ON R.RoleId = SR.RoleId
               WHERE  R.RoleId IS NULL OR R.RoleName NOT IN(N'Administrator',N'Accountant'))
        THROW 57226, 'One or more selected roles are invalid.', 1;
    IF EXISTS (SELECT 1
               FROM   @SelectedRoles AS SR
               WHERE  SR.RoleId = @AdministratorRoleId)
        THROW 57227, 'The Administrator role is reserved for the system administrator account.', 1;
    BEGIN TRANSACTION;
    IF @TargetUserId = 0
        BEGIN
            IF @PasswordHash IS NULL
               OR @PasswordSalt IS NULL
                THROW 57102, 'A password is required when creating a user.', 1;
            DECLARE @PrimaryRoleId AS INT;
            SELECT   TOP (1) @PrimaryRoleId = RoleId
            FROM     @SelectedRoles
            ORDER BY RoleId;
            IF @PrimaryRoleId IS NULL
                BEGIN
                    SELECT   TOP (1) @PrimaryRoleId = RoleId
                    FROM     dbo.Roles
                    WHERE    RoleId <> @AdministratorRoleId
                    ORDER BY RoleId;
                    IF @PrimaryRoleId IS NULL
                        THROW 57228, 'At least one non-Administrator role must exist in the system before a user can be created.', 1;
                END
            INSERT  dbo.Users (Username, PasswordHash, PasswordSalt, RoleId, MustChangePassword)
            VALUES           (LTRIM(RTRIM(@Username)), @PasswordHash, @PasswordSalt, @PrimaryRoleId, @MustChangePassword);
            SET @TargetUserId = CONVERT (INT, SCOPE_IDENTITY());
        END
    ELSE
        BEGIN
            UPDATE dbo.Users
            SET    Username           = LTRIM(RTRIM(@Username)),
                   RoleId             = COALESCE ((SELECT   TOP (1) RoleId
                                                   FROM     @SelectedRoles
                                                   ORDER BY RoleId), RoleId),
                   MustChangePassword = @MustChangePassword,
                   PasswordHash       = COALESCE (@PasswordHash, PasswordHash),
                   PasswordSalt       = COALESCE (@PasswordSalt, PasswordSalt),
                   UpdatedAt          = SYSDATETIME()
            WHERE  UserId = @TargetUserId;
            IF @@ROWCOUNT = 0
                THROW 57104, 'The selected user account was not found.', 1;
        END
    DELETE dbo.UserRoles
    WHERE  UserId = @TargetUserId;
    INSERT INTO dbo.UserRoles (UserId, RoleId)
    SELECT @TargetUserId,
           RoleId
    FROM   @SelectedRoles;
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @AuditRecordId AS NVARCHAR (200) = CAST (@TargetUserId AS NVARCHAR (200));
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'USER_SAVE', @AuditRecordId);
    DECLARE @UserAction AS NVARCHAR (10) = CASE WHEN @IsNewUser = 1 THEN N'INSERT' ELSE N'UPDATE' END;
    DECLARE @OldUserValues AS NVARCHAR (MAX);
    DECLARE @NewUserValues AS NVARCHAR (MAX);
    IF @IsNewUser = 0
        SELECT @OldUserValues = (SELECT @OldUsername AS Username,
                                        @OldMustChangePassword AS MustChangePassword
                                 FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER);
    SELECT @NewUserValues = (SELECT LTRIM(RTRIM(@Username)) AS Username,
                                    @MustChangePassword AS MustChangePassword,
                                    CASE WHEN @PasswordHash IS NOT NULL
                                              AND @PasswordSalt IS NOT NULL THEN CAST (1 AS BIT) END AS PasswordChanged
                             FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Users', @RecordId = @AuditRecordId, @Action = @UserAction, @OldValues = @OldUserValues, @NewValues = @NewUserValues;
    DECLARE @OldRoleIdsJson AS NVARCHAR (MAX);
    DECLARE @NewRoleIdsJson AS NVARCHAR (MAX);
    SELECT @OldRoleIdsJson = N'[' + ISNULL(STRING_AGG(CAST (RoleId AS NVARCHAR (10)), N',') WITHIN GROUP (ORDER BY RoleId), N'') + N']'
    FROM   @OldUserRoles;
    SELECT @NewRoleIdsJson = N'[' + ISNULL(STRING_AGG(CAST (RoleId AS NVARCHAR (10)), N',') WITHIN GROUP (ORDER BY RoleId), N'') + N']'
    FROM   @SelectedRoles;
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'UserRoles', @RecordId = @AuditRecordId, @Action = N'UPDATE', @OldValues = @OldRoleIdsJson, @NewValues = @NewRoleIdsJson;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @TargetUserId AS UserId;
END
