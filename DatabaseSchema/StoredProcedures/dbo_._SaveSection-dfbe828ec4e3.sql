-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @OrderLock int;
        EXEC @OrderLock = sys.sp_getapplock
            @Resource = N'SetupOrder:Sections', @LockMode = 'Exclusive',
            @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @OrderLock < 0 THROW 57401, 'Unable to lock setup ordering. Please try again.', 1;
        EXEC dbo.RequirePermission @UserId, N'System.Setup';
        SET @Description = NULLIF(LTRIM(RTRIM(@Description)), N'');
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
            IF @@ROWCOUNT = 0 THROW 56221, 'The selected section was not found.', 1;
        END
        COMMIT TRANSACTION;
        SELECT @SectionId AS SectionId;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
