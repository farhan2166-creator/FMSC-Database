-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Configuration.Manage';
    BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @OrderLock int;
    EXEC @OrderLock = sys.sp_getapplock @Resource=N'SetupOrder:FeeTypes', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
    IF @OrderLock < 0 THROW 57401, 'Unable to lock setup ordering. Please try again.', 1;
    IF NULLIF (LTRIM(RTRIM(@FeeTypeCode)), N'') IS NULL
        THROW 56302, 'Fee type code is required.', 1;
    IF NULLIF (LTRIM(RTRIM(@FeeTypeName)), N'') IS NULL
        THROW 56303, 'Fee type name is required.', 1;
    IF @FeeTypeId = 0
        BEGIN
            SELECT @DisplayOrder = ISNULL(MAX(DisplayOrder), 0) + 1
                    FROM   dbo.FeeTypes WITH (UPDLOCK,HOLDLOCK);
            INSERT  dbo.FeeTypes (FeeTypeCode, FeeTypeName, Description, DisplayOrder)
            VALUES              (@FeeTypeCode, @FeeTypeName, @Description, @DisplayOrder);

    COMMIT TRANSACTION;
    SELECT CONVERT (INT, SCOPE_IDENTITY()) AS FeeTypeId;
        END
    ELSE
        BEGIN
            UPDATE dbo.FeeTypes
            SET    FeeTypeName  = @FeeTypeName,
                   Description  = @Description
            WHERE  FeeTypeId = @FeeTypeId;
            IF @@ROWCOUNT = 0
                THROW 56301, 'The selected fee type was not found.', 1;

    COMMIT TRANSACTION;
    SELECT @FeeTypeId AS FeeTypeId;
        END
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
