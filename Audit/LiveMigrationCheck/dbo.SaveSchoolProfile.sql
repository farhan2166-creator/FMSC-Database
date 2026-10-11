
-- SaveSchoolProfile
CREATE   PROCEDURE dbo.SaveSchoolProfile
@UserId INT, @SchoolName NVARCHAR (200), @AddressLine NVARCHAR (500), @City NVARCHAR (100), @ContactNumber NVARCHAR (30), @Email NVARCHAR (150), @LogoImage VARBINARY (MAX), @LogoFileName NVARCHAR (255), @LogoContentType NVARCHAR (100), @FeeProrationCutoffDay INT=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'System.Setup';
    IF @SchoolName IS NULL
       OR LTRIM(RTRIM(@SchoolName)) = N''
        THROW 59000, 'School name is required.', 1;
    IF @FeeProrationCutoffDay IS NOT NULL
       AND @FeeProrationCutoffDay <> 0
       AND @FeeProrationCutoffDay NOT BETWEEN 1 AND 28
        THROW 59001, 'The fee proration cutoff day must be between 1 and 28, or left blank.', 1;
    IF @FeeProrationCutoffDay = 0
        SET @FeeProrationCutoffDay = NULL;
    BEGIN TRANSACTION;
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID(), @OldValues AS NVARCHAR (MAX) = NULL, @NewValues AS NVARCHAR (MAX), @Action AS NVARCHAR (30);
    IF EXISTS (SELECT 1
               FROM   dbo.SchoolProfile
               WHERE  SchoolProfileId = 1)
        BEGIN
            SET @Action = N'UPDATE';
            SELECT @OldValues = (SELECT SchoolName,
                                        AddressLine,
                                        City,
                                        ContactNumber,
                                        Email,
                                        LogoFileName,
                                        LogoContentType,
                                        FeeProrationCutoffDay
                                 FROM   dbo.SchoolProfile
                                 WHERE  SchoolProfileId = 1
                                 FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
            UPDATE dbo.SchoolProfile
            SET    SchoolName            = LTRIM(RTRIM(@SchoolName)),
                   AddressLine           = NULLIF (LTRIM(RTRIM(@AddressLine)), N''),
                   City                  = NULLIF (LTRIM(RTRIM(@City)), N''),
                   ContactNumber         = NULLIF (LTRIM(RTRIM(@ContactNumber)), N''),
                   Email                 = NULLIF (LTRIM(RTRIM(@Email)), N''),
                   LogoImage             = @LogoImage,
                   LogoFileName          = NULLIF (LTRIM(RTRIM(@LogoFileName)), N''),
                   LogoContentType       = NULLIF (LTRIM(RTRIM(@LogoContentType)), N''),
                   FeeProrationCutoffDay = @FeeProrationCutoffDay,
                   UpdatedAt             = SYSDATETIME(),
                   UpdatedByUserId       = @UserId
            WHERE  SchoolProfileId = 1;
        END
    ELSE
        BEGIN
            SET @Action = N'INSERT';
            INSERT  dbo.SchoolProfile (SchoolProfileId, SchoolName, AddressLine, City, ContactNumber, Email, LogoImage, LogoFileName, LogoContentType, FeeProrationCutoffDay, CreatedByUserId, UpdatedByUserId)
            VALUES                   (1, LTRIM(RTRIM(@SchoolName)), NULLIF (LTRIM(RTRIM(@AddressLine)), N''), NULLIF (LTRIM(RTRIM(@City)), N''), NULLIF (LTRIM(RTRIM(@ContactNumber)), N''), NULLIF (LTRIM(RTRIM(@Email)), N''), @LogoImage, NULLIF (LTRIM(RTRIM(@LogoFileName)), N''), NULLIF (LTRIM(RTRIM(@LogoContentType)), N''), @FeeProrationCutoffDay, @UserId, @UserId);
        END
    SELECT @NewValues = (SELECT SchoolName,
                                AddressLine,
                                City,
                                ContactNumber,
                                Email,
                                LogoFileName,
                                LogoContentType,
                                FeeProrationCutoffDay
                         FROM   dbo.SchoolProfile
                         WHERE  SchoolProfileId = 1
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'SCHOOL_PROFILE', N'1');
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'SchoolProfile', @RecordId = N'1', @Action = @Action, @OldValues = @OldValues, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT 1 AS SchoolProfileId;
END



