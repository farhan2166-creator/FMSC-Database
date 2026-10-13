-- Owner-approved two-role policy: Administrator (always full authority) and Accountant.
-- Preserve existing Accountant grants. Removed roles grant no replacement access.
-- Run after Migrations/2.sql, with a verified recovery backup, while all app sessions are closed.
-- This single batch owns one transaction, including procedure changes and version 3.
-- Repeat execution at version 3 is a no-op; it does not reset later Accountant edits.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @Version nvarchar(20)=(SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion WITH(UPDLOCK,HOLDLOCK) ORDER BY VersionId DESC);
    IF @Version=N'3'
    BEGIN
        COMMIT TRANSACTION;
        RETURN;
    END;
    IF @Version<>N'2' OR @Version IS NULL
        THROW 57400,'Two-role migration requires database version 2. Apply the preceding migration first.',1;
    DECLARE @Administrator int, @Accountant int;
    SELECT @Administrator=RoleId FROM dbo.Roles WITH(TABLOCKX,HOLDLOCK) WHERE RoleName=N'Administrator';
    IF @Administrator IS NULL OR (SELECT COUNT(*) FROM dbo.Roles WHERE RoleName=N'Administrator')<>1
        THROW 57401,'Exactly one Administrator role is required before role cleanup.',1;
    SELECT @Accountant=RoleId FROM dbo.Roles WHERE RoleName=N'Accountant';
    IF @Accountant IS NULL
        THROW 57402,'The existing Accountant role is required; no replacement permissions are guessed.',1;
    DECLARE @LockCount bigint;
    SELECT @LockCount=COUNT_BIG(*) FROM dbo.Permissions WITH(TABLOCKX,HOLDLOCK);
    SELECT @LockCount=COUNT_BIG(*) FROM dbo.RolePermissions WITH(TABLOCKX,HOLDLOCK);
    SELECT @LockCount=COUNT_BIG(*) FROM dbo.Users WITH(TABLOCKX,HOLDLOCK);
    SELECT @LockCount=COUNT_BIG(*) FROM dbo.UserRoles WITH(TABLOCKX,HOLDLOCK);
    DECLARE @Before nvarchar(max)=(SELECT
        JSON_QUERY((SELECT RoleId,RoleName,Description FROM dbo.Roles ORDER BY RoleId FOR JSON PATH,INCLUDE_NULL_VALUES)) Roles,
        JSON_QUERY((SELECT RoleId,PermissionId FROM dbo.RolePermissions ORDER BY RoleId,PermissionId FOR JSON PATH)) Grants,
        JSON_QUERY((SELECT UserId,RoleId FROM dbo.Users ORDER BY UserId FOR JSON PATH)) PrimaryRoles,
        JSON_QUERY((SELECT UserId,RoleId FROM dbo.UserRoles ORDER BY UserId,RoleId FOR JSON PATH)) Memberships
        FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
    DECLARE @Admins TABLE(UserId int PRIMARY KEY);
    INSERT @Admins SELECT U.UserId FROM dbo.Users U WHERE U.RoleId=@Administrator OR EXISTS(
        SELECT 1 FROM dbo.UserRoles UR WHERE UR.UserId=U.UserId AND UR.RoleId=@Administrator);
    DECLARE @Actor int=(SELECT MIN(UserId) FROM @Admins);
    UPDATE U SET RoleId=CASE WHEN A.UserId IS NOT NULL THEN @Administrator ELSE @Accountant END
        FROM dbo.Users U LEFT JOIN @Admins A ON A.UserId=U.UserId
        WHERE U.RoleId<>CASE WHEN A.UserId IS NOT NULL THEN @Administrator ELSE @Accountant END;
    DELETE UR FROM dbo.UserRoles UR LEFT JOIN @Admins A ON A.UserId=UR.UserId
        WHERE UR.RoleId NOT IN(@Administrator,@Accountant) OR A.UserId IS NOT NULL AND UR.RoleId<>@Administrator;
    INSERT dbo.UserRoles(UserId,RoleId) SELECT A.UserId,@Administrator FROM @Admins A
        WHERE NOT EXISTS(SELECT 1 FROM dbo.UserRoles UR WHERE UR.UserId=A.UserId AND UR.RoleId=@Administrator);
    DELETE dbo.RolePermissions WHERE RoleId<>@Accountant;
    DELETE dbo.Roles WHERE RoleId NOT IN(@Administrator,@Accountant);
    DECLARE @Catalog nvarchar(max)=N'[{"Code":"Backup.Create","Name":"Create Backup","Description":"Create a database backup."},{"Code":"Backup.Restore","Name":"Restore Backup","Description":"Restore the database from a backup."},{"Code":"Backup.View","Name":"View Backups","Description":"View database backup information."},{"Code":"Calendar.Manage","Name":"Manage Calendar","Description":"Manage academic terms/calendar"},{"Code":"Certificates.Create","Name":"Create Certificates","Description":"Create certificates."},{"Code":"Certificates.Print","Name":"Print Certificates","Description":"Print certificates."},{"Code":"Enrollment.Manage","Name":"Manage Enrollment","Description":"Manage enrollment records."},{"Code":"Fees.Adjust","Name":"Adjust Fees","Description":"Request/review/complete fee adjustments"},{"Code":"Fees.Collect","Name":"Collect Fee","Description":"Record a student fee payment."},{"Code":"Fees.Configuration.Manage","Name":"Manage Fee Configuration","Description":"Create and modify class fee configuration."},{"Code":"Fees.Reports.View","Name":"View billing status checks","Description":"Check unposted or missing charges and billing cycle status. General report categories use Reports.View."},{"Code":"Fees.Statement.View","Name":"View annual fee receipts","Description":"Open and print annual student fee receipts; also needs report and fee viewing access."},{"Code":"Fees.View","Name":"View Fees","Description":"View student fee information."},{"Code":"Reports.View","Name":"View student and finance reports","Description":"Open student and finance report categories and their exports. Fees.Reports.View is a separate billing-status check."},{"Code":"Staff.Manage","Name":"Manage Staff","Description":"Create and update staff records."},{"Code":"Staff.View","Name":"View Staff","Description":"View staff records."},{"Code":"Students.Administer","Name":"Maintain student records and lifecycle","Description":"Edit saved student details and photos, manage family membership, re-admission, placement/status and legacy/lifecycle maintenance."},{"Code":"Students.Admit","Name":"Admit Student","Description":"Create a new student admission."},{"Code":"Students.Family","Name":"Open family workspace","Description":"View student families; changing members also requires Students.Administer."},{"Code":"Students.Leave","Name":"Leave Student","Description":"Mark a student as leaving the school."},{"Code":"Students.View","Name":"View Students","Description":"View student records and searches."},{"Code":"System.Administration","Name":"Manage system administration","Description":"Manage users, roles, permissions and audit access. The Administrator role separately retains full application authority."},{"Code":"System.Audit","Name":"View Audit Logs","Description":"View administrative audit history."},{"Code":"System.Roles","Name":"View roles and permissions","Description":"Read role definitions and the permission catalog. Editing also requires System.Roles.Manage."},{"Code":"System.Roles.Manage","Name":"Edit Accountant permissions","Description":"Save the Accountant description and permissions; requires role viewing access. Administrator remains protected with full authority."},{"Code":"System.Setup","Name":"Manage School Setup","Description":"Manage school and system configuration."},{"Code":"System.Users","Name":"View users","Description":"Read user accounts. Editing also requires System.Users.Manage and role viewing access."},{"Code":"System.Users.Manage","Name":"Create and edit users","Description":"Save normal user accounts and reset their passwords; requires user and role viewing access."}]';
    DECLARE @Supported TABLE(Code nvarchar(100) PRIMARY KEY,Name nvarchar(100),Description nvarchar(250));
    INSERT @Supported SELECT Code,Name,Description FROM OPENJSON(@Catalog)
        WITH(Code nvarchar(100) '$.Code',Name nvarchar(100) '$.Name',Description nvarchar(250) '$.Description');
    -- No active Accountant capability is remapped to a broader permission.
    DELETE RP FROM dbo.RolePermissions RP JOIN dbo.Permissions P ON P.PermissionId=RP.PermissionId
        WHERE NOT EXISTS(SELECT 1 FROM @Supported S WHERE S.Code=P.PermissionCode);
    DELETE P FROM dbo.Permissions P WHERE NOT EXISTS(SELECT 1 FROM @Supported S WHERE S.Code=P.PermissionCode);
    UPDATE P SET PermissionName=S.Name,Description=S.Description FROM dbo.Permissions P JOIN @Supported S ON S.Code=P.PermissionCode;
    INSERT dbo.Permissions(PermissionCode,PermissionName,Description) SELECT Code,Name,Description FROM @Supported S
        WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions P WHERE P.PermissionCode=S.Code);

    -- RequireAnyPermission
    EXEC sys.sp_executesql N'
-- RequireAnyPermission
ALTER PROCEDURE dbo.RequireAnyPermission
@UserId INT, @PermissionCode1 NVARCHAR (100), @PermissionCode2 NVARCHAR (100)=NULL, @PermissionCode3 NVARCHAR (100)=NULL
AS

BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Users
                   WHERE  UserId = @UserId)
        BEGIN
            THROW 50003, ''The specified user is not authenticated.'', 1;
        END
    IF EXISTS (SELECT 1
               FROM   dbo.Users AS U
                      INNER JOIN
                      dbo.Roles AS R
                      ON R.RoleId = U.RoleId
               WHERE  U.UserId = @UserId
                      AND R.RoleName = N''Administrator'')
        BEGIN
            RETURN;
        END
    IF EXISTS (SELECT 1 FROM dbo.UserRoles UR JOIN dbo.Roles R ON R.RoleId=UR.RoleId
               WHERE UR.UserId=@UserId AND R.RoleName=N''Administrator'')
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
                      AND (P.PermissionCode = @PermissionCode1
                           OR (@PermissionCode2 IS NOT NULL
                               AND P.PermissionCode = @PermissionCode2)
                           OR (@PermissionCode3 IS NOT NULL
                               AND P.PermissionCode = @PermissionCode3)))
        BEGIN
            RETURN;
        END;
    THROW 50003, ''You are not authorized to perform this operation.'', 1;
END';

    -- SaveRole
    EXEC sys.sp_executesql N'
-- SaveRole
ALTER PROCEDURE dbo.SaveRole
@UserId INT, @RoleName NVARCHAR (50), @RoleId INT=0, @Description NVARCHAR (250)=NULL
AS

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequireAnyPermission @UserId, N''System.Roles.Manage'', N''System.Administration'';
    IF @RoleId<=0 OR LTRIM(RTRIM(ISNULL(@RoleName,N'''')))<>N''Accountant''
       OR NOT EXISTS(SELECT 1 FROM dbo.Roles WHERE RoleId=@RoleId AND RoleName=N''Accountant'')
        THROW 57410,''Only the existing Accountant role may be edited. Administrator always has full authority; creating or renaming roles is disabled.'',1;
    DECLARE @IsNewRole AS BIT = CASE WHEN @RoleId = 0 THEN 1 ELSE 0 END;
    DECLARE @OldRoleName AS NVARCHAR (50);
    DECLARE @OldDescription AS NVARCHAR (250);
    IF @IsNewRole = 0
        SELECT @OldRoleName = RoleName,
               @OldDescription = Description
        FROM   dbo.Roles
        WHERE  RoleId = @RoleId;
    IF NULLIF (LTRIM(RTRIM(@RoleName)), N'''') IS NULL
        THROW 57110, ''Role name is required.'', 1;
    IF @RoleId <> 0
       AND EXISTS (SELECT 1
                   FROM   dbo.Roles
                   WHERE  RoleId = @RoleId
                          AND RoleName = N''Administrator'')
        BEGIN
            THROW 57228, ''The Administrator role is protected.'', 1;
        END
    IF EXISTS (SELECT 1
               FROM   dbo.Roles
               WHERE  RoleName = LTRIM(RTRIM(@RoleName))
                      AND RoleId <> @RoleId)
        BEGIN
            THROW 57229, ''A role with this name already exists.'', 1;
        END
    BEGIN TRANSACTION;
    IF @RoleId = 0
        BEGIN
            INSERT  dbo.Roles (RoleName, Description)
            VALUES           (LTRIM(RTRIM(@RoleName)), NULLIF (LTRIM(RTRIM(@Description)), N''''));
            SET @RoleId = CONVERT (INT, SCOPE_IDENTITY());
        END
    ELSE
        BEGIN
            UPDATE dbo.Roles
            SET    RoleName    = LTRIM(RTRIM(@RoleName)),
                   Description = NULLIF (LTRIM(RTRIM(@Description)), N'''')
            WHERE  RoleId = @RoleId;
            IF @@ROWCOUNT = 0
                THROW 57111, ''The selected role was not found.'', 1;
        END
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @AuditRecordId AS NVARCHAR (200) = CAST (@RoleId AS NVARCHAR (200));
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N''ROLE_SAVE'', @AuditRecordId);
    DECLARE @RoleAction AS NVARCHAR (10) = CASE WHEN @IsNewRole = 1 THEN N''INSERT'' ELSE N''UPDATE'' END;
    DECLARE @OldRoleValues AS NVARCHAR (MAX);
    IF @IsNewRole = 0
        SELECT @OldRoleValues = (SELECT @OldRoleName AS RoleName,
                                        @OldDescription AS Description
                                 FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER);
    DECLARE @NewRoleValues AS NVARCHAR (MAX) = (SELECT LTRIM(RTRIM(@RoleName)) AS RoleName,
                                                       NULLIF (LTRIM(RTRIM(@Description)), N'''') AS Description
                                                FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N''Roles'', @RecordId = @AuditRecordId, @Action = @RoleAction, @OldValues = @OldRoleValues, @NewValues = @NewRoleValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @RoleId AS RoleId;
END';

    -- SaveRolePermissions
    EXEC sys.sp_executesql N'
-- SaveRolePermissions
ALTER PROCEDURE dbo.SaveRolePermissions
@UserId INT, @RoleId INT, @PermissionIdsJson NVARCHAR (MAX)
AS

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequireAnyPermission @UserId, N''System.Roles.Manage'', N''System.Administration'';
    IF @RoleId <= 0
        THROW 57300, ''A valid role is required.'', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Roles
                   WHERE  RoleId = @RoleId)
        THROW 57301, ''The selected role was not found.'', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.Roles
               WHERE  RoleId = @RoleId
                      AND RoleName = N''Administrator'')
        THROW 57302, ''Administrator permissions are managed automatically.'', 1;
    IF NOT EXISTS(SELECT 1 FROM dbo.Roles WHERE RoleId=@RoleId AND RoleName=N''Accountant'')
        THROW 57411,''Only Accountant permissions may be changed; Administrator always has full authority.'',1;
    IF ISNULL(ISJSON(@PermissionIdsJson),0) <> 1 OR LEFT(LTRIM(@PermissionIdsJson),1)<>N''[''
        THROW 57303, ''The supplied permission list is not valid JSON.'', 1;
    IF EXISTS(SELECT 1 FROM OPENJSON(@PermissionIdsJson) WHERE [type]<>2 OR TRY_CONVERT(int,[value]) IS NULL OR TRY_CONVERT(int,[value])<=0)
        THROW 57412,''Permission selections must be an array of positive integer IDs.'',1;
    DECLARE @SelectedPermissions TABLE (
        PermissionId INT NOT NULL PRIMARY KEY);
    INSERT INTO @SelectedPermissions (PermissionId)
    SELECT DISTINCT TRY_CONVERT (INT, [value])
    FROM   OPENJSON (@PermissionIdsJson)
    WHERE  TRY_CONVERT (INT, [value]) IS NOT NULL;
    IF EXISTS (SELECT 1
               FROM   @SelectedPermissions AS SP
                      LEFT OUTER JOIN
                      dbo.Permissions AS P
                      ON P.PermissionId = SP.PermissionId
               WHERE  P.PermissionId IS NULL OR P.PermissionCode NOT IN(N''Backup.Create'',N''Backup.Restore'',N''Backup.View'',N''Calendar.Manage'',N''Certificates.Create'',N''Certificates.Print'',N''Enrollment.Manage'',N''Fees.Adjust'',N''Fees.Collect'',N''Fees.Configuration.Manage'',N''Fees.Reports.View'',N''Fees.Statement.View'',N''Fees.View'',N''Reports.View'',N''Staff.Manage'',N''Staff.View'',N''Students.Administer'',N''Students.Admit'',N''Students.Family'',N''Students.Leave'',N''Students.View'',N''System.Administration'',N''System.Audit'',N''System.Roles'',N''System.Roles.Manage'',N''System.Setup'',N''System.Users'',N''System.Users.Manage''))
        THROW 57304, ''One or more selected permissions are invalid.'', 1;
    IF NOT EXISTS(SELECT 1 FROM @SelectedPermissions S JOIN dbo.Permissions P ON P.PermissionId=S.PermissionId WHERE P.PermissionCode=N''System.Administration'')
       AND (
        EXISTS(SELECT 1 FROM @SelectedPermissions S JOIN dbo.Permissions P ON P.PermissionId=S.PermissionId WHERE P.PermissionCode=N''System.Users.Manage'')
        AND (NOT EXISTS(SELECT 1 FROM @SelectedPermissions S JOIN dbo.Permissions P ON P.PermissionId=S.PermissionId WHERE P.PermissionCode=N''System.Users'')
          OR NOT EXISTS(SELECT 1 FROM @SelectedPermissions S JOIN dbo.Permissions P ON P.PermissionId=S.PermissionId WHERE P.PermissionCode=N''System.Roles''))
        OR EXISTS(SELECT 1 FROM @SelectedPermissions S JOIN dbo.Permissions P ON P.PermissionId=S.PermissionId WHERE P.PermissionCode IN(N''System.Roles.Manage'',N''System.Users''))
        AND NOT EXISTS(SELECT 1 FROM @SelectedPermissions S JOIN dbo.Permissions P ON P.PermissionId=S.PermissionId WHERE P.PermissionCode=N''System.Roles''))
        THROW 57414,''User and role management require their viewing permissions. Select the required viewing access before saving.'',1;
    DECLARE @OldPermissionIds TABLE (
        PermissionId INT NOT NULL);
    INSERT INTO @OldPermissionIds (PermissionId)
    SELECT PermissionId
    FROM   dbo.RolePermissions
    WHERE  RoleId = @RoleId;
    BEGIN TRANSACTION;
    DELETE dbo.RolePermissions
    WHERE  RoleId = @RoleId;
    INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
    SELECT @RoleId,
           PermissionId
    FROM   @SelectedPermissions;
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @AuditRecordId AS NVARCHAR (200) = CAST (@RoleId AS NVARCHAR (200));
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N''ROLE_PERMISSIONS_SAVE'', @AuditRecordId);
    DECLARE @OldPermissionIdsJson AS NVARCHAR (MAX);
    DECLARE @NewPermissionIdsJson AS NVARCHAR (MAX);
    SELECT @OldPermissionIdsJson = N''['' + ISNULL(STRING_AGG(CAST (PermissionId AS NVARCHAR (10)), N'','') WITHIN GROUP (ORDER BY PermissionId), N'''') + N'']''
    FROM   @OldPermissionIds;
    SELECT @NewPermissionIdsJson = N''['' + ISNULL(STRING_AGG(CAST (PermissionId AS NVARCHAR (10)), N'','') WITHIN GROUP (ORDER BY PermissionId), N'''') + N'']''
    FROM   @SelectedPermissions;
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N''RolePermissions'', @RecordId = @AuditRecordId, @Action = N''UPDATE'', @OldValues = @OldPermissionIdsJson, @NewValues = @NewPermissionIdsJson;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @RoleId AS RoleId;
END';

    -- SaveUser
    EXEC sys.sp_executesql N'
-- SaveUser
ALTER PROCEDURE dbo.SaveUser
@UserId INT, @Username NVARCHAR (100), @RoleId INT, @TargetUserId INT=0, @MustChangePassword BIT=1, @PasswordHash VARBINARY (64)=NULL, @PasswordSalt VARBINARY (32)=NULL, @RoleIdsJson NVARCHAR (MAX)=NULL
AS

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequireAnyPermission @UserId, N''System.Users.Manage'', N''System.Administration'';
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
    IF NULLIF (LTRIM(RTRIM(@Username)), N'''') IS NULL
        THROW 57101, ''Username is required.'', 1;
    DECLARE @AdministratorRoleId AS INT;
    SELECT   TOP (1) @AdministratorRoleId = RoleId
    FROM     dbo.Roles
    WHERE    RoleName = N''Administrator''
    ORDER BY RoleId;
    IF @AdministratorRoleId IS NULL
        THROW 57222, ''The Administrator role is not available.'', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.Users AS U
               WHERE  U.UserId = @TargetUserId
                      AND (U.RoleId = @AdministratorRoleId OR EXISTS(SELECT 1 FROM dbo.UserRoles UR
                           WHERE UR.UserId=U.UserId AND UR.RoleId=@AdministratorRoleId)))
        BEGIN
            THROW 57223, ''The Administrator account is protected and cannot be edited as a normal user.'', 1;
        END
    DECLARE @SelectedRoles TABLE (
        RoleId INT NOT NULL PRIMARY KEY);
    IF NULLIF (LTRIM(RTRIM(@RoleIdsJson)), N'''') IS NOT NULL
        BEGIN
            IF ISNULL(ISJSON(@RoleIdsJson),0) <> 1 OR LEFT(LTRIM(@RoleIdsJson),1)<>N''[''
                THROW 57224, ''The supplied role list is not valid JSON.'', 1;
            IF EXISTS(SELECT 1 FROM OPENJSON(@RoleIdsJson) WHERE [type]<>2 OR TRY_CONVERT(int,[value]) IS NULL OR TRY_CONVERT(int,[value])<=0)
                THROW 57413,''Role selections must be an array of positive integer IDs.'',1;
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
               WHERE  R.RoleId IS NULL OR R.RoleName NOT IN(N''Administrator'',N''Accountant''))
        THROW 57226, ''One or more selected roles are invalid.'', 1;
    IF EXISTS (SELECT 1
               FROM   @SelectedRoles AS SR
               WHERE  SR.RoleId = @AdministratorRoleId)
        THROW 57227, ''The Administrator role is reserved for the system administrator account.'', 1;
    BEGIN TRANSACTION;
    IF @TargetUserId = 0
        BEGIN
            IF @PasswordHash IS NULL
               OR @PasswordSalt IS NULL
                THROW 57102, ''A password is required when creating a user.'', 1;
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
                        THROW 57228, ''At least one non-Administrator role must exist in the system before a user can be created.'', 1;
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
                THROW 57104, ''The selected user account was not found.'', 1;
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
    VALUES                     (@OperationId, @UserId, N''USER_SAVE'', @AuditRecordId);
    DECLARE @UserAction AS NVARCHAR (10) = CASE WHEN @IsNewUser = 1 THEN N''INSERT'' ELSE N''UPDATE'' END;
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
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N''Users'', @RecordId = @AuditRecordId, @Action = @UserAction, @OldValues = @OldUserValues, @NewValues = @NewUserValues;
    DECLARE @OldRoleIdsJson AS NVARCHAR (MAX);
    DECLARE @NewRoleIdsJson AS NVARCHAR (MAX);
    SELECT @OldRoleIdsJson = N''['' + ISNULL(STRING_AGG(CAST (RoleId AS NVARCHAR (10)), N'','') WITHIN GROUP (ORDER BY RoleId), N'''') + N'']''
    FROM   @OldUserRoles;
    SELECT @NewRoleIdsJson = N''['' + ISNULL(STRING_AGG(CAST (RoleId AS NVARCHAR (10)), N'','') WITHIN GROUP (ORDER BY RoleId), N'''') + N'']''
    FROM   @SelectedRoles;
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N''UserRoles'', @RecordId = @AuditRecordId, @Action = N''UPDATE'', @OldValues = @OldRoleIdsJson, @NewValues = @NewRoleIdsJson;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @TargetUserId AS UserId;
END';

    -- BootstrapFirstAdministrator
    EXEC sys.sp_executesql N'
-- BootstrapFirstAdministrator
ALTER PROCEDURE dbo.BootstrapFirstAdministrator
@Username NVARCHAR (100), @PasswordHash VARBINARY (64), @PasswordSalt VARBINARY (32)
AS

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DECLARE @SetupLockResult AS INT;
    EXECUTE @SetupLockResult = sys.sp_getapplock @Resource = N''FMSC.FirstRunAdministrator'', @LockMode = N''Exclusive'', @LockOwner = N''Transaction'', @LockTimeout = 5000;
    IF @SetupLockResult < 0
        THROW 57207, ''Administrator setup is already in progress in another window. Wait for it to finish, then sign in with the administrator account it creates.'', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.Users)
        THROW 57200, ''Initial administrator setup is already completed. Please sign in with an existing administrator account.'', 1;
    IF NULLIF (LTRIM(RTRIM(@Username)), N'''') IS NULL
        THROW 57201, ''Administrator username is required.'', 1;
    IF LEN(LTRIM(RTRIM(@Username))) > 100
        THROW 57202, ''Administrator username is too long.'', 1;
    IF @PasswordHash IS NULL
       OR DATALENGTH(@PasswordHash) <> 64
        THROW 57203, ''The supplied administrator password hash is invalid.'', 1;
    IF @PasswordSalt IS NULL
       OR DATALENGTH(@PasswordSalt) <> 32
        THROW 57204, ''The supplied administrator password salt is invalid.'', 1;
    DECLARE @RoleId AS INT;
    SELECT   TOP (1) @RoleId = RoleId
    FROM     dbo.Roles
    WHERE    RoleName = N''Administrator''
    ORDER BY RoleId;
    IF @RoleId IS NULL
        THROW 57205, ''The Administrator role is not available. Database reference data is incomplete.'', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.Users
               WHERE  Username = LTRIM(RTRIM(@Username)))
        THROW 57206, ''That administrator username already exists.'', 1;
    INSERT  dbo.Users (Username, PasswordHash, PasswordSalt, RoleId, MustChangePassword)
    VALUES           (LTRIM(RTRIM(@Username)), @PasswordHash, @PasswordSalt, @RoleId, 1);
    DECLARE @UserId AS INT = CONVERT (INT, SCOPE_IDENTITY());
    INSERT dbo.UserRoles (UserId, RoleId)
    SELECT @UserId,
           R.RoleId
    FROM   dbo.Roles AS R
    WHERE  R.RoleId=@RoleId AND NOT EXISTS (SELECT 1
                       FROM   dbo.UserRoles AS UR
                       WHERE  UR.UserId = @UserId
                              AND UR.RoleId = R.RoleId);
    COMMIT TRANSACTION;
    SELECT @UserId AS UserId,
           LTRIM(RTRIM(@Username)) AS Username,
           @RoleId AS RoleId;
END';

    -- AddStudentSibling
    EXEC sys.sp_executesql N'
-- AddStudentSibling
ALTER PROCEDURE dbo.AddStudentSibling
@AdmissionId INT, @SiblingAdmissionId INT, @RelationshipType NVARCHAR (30), @UserId INT
AS

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N''Students.Administer'';
    DECLARE @Relationship AS NVARCHAR (30) = LTRIM(RTRIM(@RelationshipType));
    IF NULLIF (@Relationship, N'''') IS NULL
        THROW 56644, ''A relationship type is required when adding a sibling.'', 1;
    IF @Relationship NOT IN (N''Sibling'', N''Brother'', N''Sister'')
        THROW 56645, ''The relationship type must be Sibling, Brother or Sister.'', 1;
    SET @Relationship = N''Sibling'';
    IF @AdmissionId = @SiblingAdmissionId
        THROW 56640, ''A student cannot be added as their own sibling.'', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Students
                   WHERE  AdmissionId = @AdmissionId)
        THROW 56641, ''The main student was not found.'', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Students
                   WHERE  AdmissionId = @SiblingAdmissionId)
        THROW 56642, ''The sibling student was not found.'', 1;
    BEGIN TRANSACTION;
    DECLARE @FamilyId AS INT;
    SELECT @FamilyId = FamilyId
    FROM   dbo.FamilyMembers WITH (UPDLOCK, HOLDLOCK)
    WHERE  AdmissionId = @AdmissionId;
    DECLARE @SiblingFamilyId AS INT;
    SELECT @SiblingFamilyId = FamilyId
    FROM   dbo.FamilyMembers WITH (UPDLOCK, HOLDLOCK)
    WHERE  AdmissionId = @SiblingAdmissionId;
    IF @FamilyId IS NOT NULL
       AND @SiblingFamilyId IS NOT NULL
       AND @FamilyId <> @SiblingFamilyId
        THROW 56643, ''These students already belong to different families. Resolve the existing family groups before adding the relationship.'', 1;
    IF @FamilyId IS NULL
       AND @SiblingFamilyId IS NULL
        BEGIN
            INSERT  dbo.Families (FamilyCode)
            VALUES              (NULL);
            SET @FamilyId = CONVERT (INT, SCOPE_IDENTITY());
            INSERT  dbo.FamilyMembers (FamilyId, AdmissionId, RelationshipType)
            VALUES                   (@FamilyId, @AdmissionId, @Relationship);
            INSERT  dbo.FamilyMembers (FamilyId, AdmissionId, RelationshipType)
            VALUES                   (@FamilyId, @SiblingAdmissionId, @Relationship);
        END
    ELSE
        IF @FamilyId IS NOT NULL
           AND @SiblingFamilyId IS NULL
            BEGIN
                INSERT  dbo.FamilyMembers (FamilyId, AdmissionId, RelationshipType)
                VALUES                   (@FamilyId, @SiblingAdmissionId, @Relationship);
            END
        ELSE
            IF @FamilyId IS NULL
               AND @SiblingFamilyId IS NOT NULL
                BEGIN
                    INSERT  dbo.FamilyMembers (FamilyId, AdmissionId, RelationshipType)
                    VALUES                   (@SiblingFamilyId, @AdmissionId, @Relationship);
                    SET @FamilyId = @SiblingFamilyId;
                END
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @NewValues AS NVARCHAR (MAX) = (SELECT @FamilyId AS FamilyId,
                                                   @AdmissionId AS AdmissionId,
                                                   @SiblingAdmissionId AS SiblingAdmissionId,
                                                   @Relationship AS RelationshipType
                                            FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    COMMIT TRANSACTION;
    EXECUTE dbo.GetStudentFamily @UserId = @UserId, @AdmissionId = @AdmissionId;
END';

    -- GetPermissions
    EXEC sys.sp_executesql N'
-- GetPermissions
ALTER PROCEDURE dbo.GetPermissions
@UserId INT
AS

BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequireAnyPermission @UserId, N''System.Roles'', N''System.Administration'';
    SELECT   PermissionId,
             PermissionCode,
             PermissionName,
             Description
    FROM     dbo.Permissions
    ORDER BY PermissionCode;
END';

    IF @Actor IS NOT NULL
    BEGIN
        DECLARE @After nvarchar(max)=(SELECT
            JSON_QUERY((SELECT RoleId,RoleName,Description FROM dbo.Roles ORDER BY RoleId FOR JSON PATH,INCLUDE_NULL_VALUES)) Roles,
            JSON_QUERY((SELECT RoleId,PermissionId FROM dbo.RolePermissions ORDER BY RoleId,PermissionId FOR JSON PATH)) Grants,
            JSON_QUERY((SELECT UserId,RoleId FROM dbo.Users ORDER BY UserId FOR JSON PATH)) PrimaryRoles,
            JSON_QUERY((SELECT UserId,RoleId FROM dbo.UserRoles ORDER BY UserId,RoleId FOR JSON PATH)) Memberships
            FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
        DECLARE @Operation uniqueidentifier=NEWID();
        INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote)
            VALUES(@Operation,@Actor,N'ROLE_POLICY_MIGRATION',N'Version 3: Administrator and Accountant');
        EXEC dbo.WriteAuditLog @OperationId=@Operation,@UserId=@Actor,@TableName=N'Roles',@RecordId=N'role-policy-v3',
            @Action=N'UPDATE',@OldValues=@Before,@NewValues=@After;
        DELETE dbo.AuditOperations WHERE OperationId=@Operation AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@Operation);
    END;
    INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
        VALUES(N'3',SYSDATETIME(),SUSER_SNAME(),N'Two roles, consistent supported permissions and protected Administrator authority.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
