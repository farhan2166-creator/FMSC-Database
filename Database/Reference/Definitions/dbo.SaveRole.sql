SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- SaveRole
CREATE   PROCEDURE dbo.SaveRole
@UserId INT, @RoleName NVARCHAR (50), @RoleId INT=0, @Description NVARCHAR (250)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequireAnyPermission @UserId, N'System.Roles.Manage', N'System.Administration';
    DECLARE @IsNewRole AS BIT = CASE WHEN @RoleId = 0 THEN 1 ELSE 0 END;
    DECLARE @OldRoleName AS NVARCHAR (50);
    DECLARE @OldDescription AS NVARCHAR (250);
    IF @IsNewRole = 0
        SELECT @OldRoleName = RoleName,
               @OldDescription = Description
        FROM   dbo.Roles
        WHERE  RoleId = @RoleId;
    IF NULLIF (LTRIM(RTRIM(@RoleName)), N'') IS NULL
        THROW 57110, 'Role name is required.', 1;
    IF @RoleId <> 0
       AND EXISTS (SELECT 1
                   FROM   dbo.Roles
                   WHERE  RoleId = @RoleId
                          AND RoleName = N'Administrator')
        BEGIN
            THROW 57228, 'The Administrator role is protected.', 1;
        END
    IF EXISTS (SELECT 1
               FROM   dbo.Roles
               WHERE  RoleName = LTRIM(RTRIM(@RoleName))
                      AND RoleId <> @RoleId)
        BEGIN
            THROW 57229, 'A role with this name already exists.', 1;
        END
    BEGIN TRANSACTION;
    IF @RoleId = 0
        BEGIN
            INSERT  dbo.Roles (RoleName, Description)
            VALUES           (LTRIM(RTRIM(@RoleName)), NULLIF (LTRIM(RTRIM(@Description)), N''));
            SET @RoleId = CONVERT (INT, SCOPE_IDENTITY());
        END
    ELSE
        BEGIN
            UPDATE dbo.Roles
            SET    RoleName    = LTRIM(RTRIM(@RoleName)),
                   Description = NULLIF (LTRIM(RTRIM(@Description)), N'')
            WHERE  RoleId = @RoleId;
            IF @@ROWCOUNT = 0
                THROW 57111, 'The selected role was not found.', 1;
        END
    BEGIN
        DECLARE @AdministratorRoleId AS INT;
        SELECT   TOP (1) @AdministratorRoleId = RoleId
        FROM     dbo.Roles
        WHERE    RoleName = N'Administrator'
        ORDER BY RoleId;
        INSERT INTO dbo.UserRoles (UserId, RoleId)
        SELECT U.UserId,
               @RoleId
        FROM   dbo.Users AS U
        WHERE  U.RoleId = @AdministratorRoleId
               AND NOT EXISTS (SELECT 1
                               FROM   dbo.UserRoles AS UR
                               WHERE  UR.UserId = U.UserId
                                      AND UR.RoleId = @RoleId);
    END
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @AuditRecordId AS NVARCHAR (200) = CAST (@RoleId AS NVARCHAR (200));
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'ROLE_SAVE', @AuditRecordId);
    DECLARE @RoleAction AS NVARCHAR (10) = CASE WHEN @IsNewRole = 1 THEN N'INSERT' ELSE N'UPDATE' END;
    DECLARE @OldRoleValues AS NVARCHAR (MAX);
    IF @IsNewRole = 0
        SELECT @OldRoleValues = (SELECT @OldRoleName AS RoleName,
                                        @OldDescription AS Description
                                 FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER);
    DECLARE @NewRoleValues AS NVARCHAR (MAX) = (SELECT LTRIM(RTRIM(@RoleName)) AS RoleName,
                                                       NULLIF (LTRIM(RTRIM(@Description)), N'') AS Description
                                                FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Roles', @RecordId = @AuditRecordId, @Action = @RoleAction, @OldValues = @OldRoleValues, @NewValues = @NewRoleValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @RoleId AS RoleId;
END



GO
