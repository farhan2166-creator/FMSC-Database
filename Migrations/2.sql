-- Optional section descriptions; preserve existing records and setup permissions.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF (SELECT TOP (1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC) <> N'1'
        THROW 57402, 'Section descriptions require database schema version 1.', 1;

    ALTER TABLE dbo.Sections ADD Description nvarchar(250) NULL;

    EXEC sys.sp_executesql N'
ALTER PROCEDURE dbo.SaveSection
    @UserId int, @SectionName nvarchar(50), @SectionId int,
    @SectionCode nvarchar(30), @Description nvarchar(250) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @OrderLock int;
        EXEC @OrderLock = sys.sp_getapplock
            @Resource = N''SetupOrder:Sections'', @LockMode = ''Exclusive'',
            @LockOwner = ''Transaction'', @LockTimeout = 10000;
        IF @OrderLock < 0 THROW 57401, ''Unable to lock setup ordering. Please try again.'', 1;
        EXEC dbo.RequirePermission @UserId, N''System.Setup'';
        SET @Description = NULLIF(LTRIM(RTRIM(@Description)), N'''');
        IF @SectionId = 0
        BEGIN
            DECLARE @NextOrder int;
            SELECT @NextOrder = ISNULL(MAX(DisplayOrder), 0) + 1
            FROM dbo.Sections WITH (UPDLOCK, HOLDLOCK);
            INSERT dbo.Sections (DisplayOrder, SectionCode, SectionName, Description)
            VALUES (@NextOrder, @SectionCode, @SectionName, @Description);
            SET @SectionId = CONVERT(int, SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            UPDATE dbo.Sections
            SET SectionCode = @SectionCode, SectionName = @SectionName,
                Description = @Description
            WHERE SectionId = @SectionId;
            IF @@ROWCOUNT = 0 THROW 56221, ''The selected section was not found.'', 1;
        END
        COMMIT TRANSACTION;
        SELECT @SectionId AS SectionId;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END';

    EXEC sys.sp_executesql N'
ALTER PROCEDURE dbo.GetSections @UserId int
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequireAuthenticatedUser @UserId;
    SELECT DisplayOrder, SectionId, SectionCode, SectionName, Description
    FROM dbo.Sections
    ORDER BY DisplayOrder, SectionId;
END';

    INSERT dbo.DatabaseVersion (VersionNumber, AppliedOn, AppliedBy, Notes)
    VALUES (N'2', SYSDATETIME(), SUSER_SNAME(), N'Add optional section descriptions.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
