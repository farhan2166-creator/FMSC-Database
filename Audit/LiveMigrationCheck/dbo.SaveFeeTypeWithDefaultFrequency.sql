
-- SaveFeeTypeWithDefaultFrequency
CREATE   PROCEDURE dbo.SaveFeeTypeWithDefaultFrequency
@UserId INT, @FeeTypeId INT, @FeeTypeCode NVARCHAR (30), @FeeTypeName NVARCHAR (100), @Description NVARCHAR (250), @DisplayOrder INT, @DefaultFrequency NVARCHAR (20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Configuration.Manage';
    IF @DefaultFrequency IS NULL
       OR @DefaultFrequency NOT IN (N'Monthly', N'Yearly', N'As Needed')
        THROW 59251, 'Default frequency must be Monthly, Yearly, or As Needed.', 1;
    DECLARE @Saved TABLE (
        FeeTypeId INT);
    DECLARE @SavedFeeTypeId AS INT;
    BEGIN TRANSACTION;
    INSERT INTO @Saved (FeeTypeId)
    EXECUTE dbo.SaveFeeType @UserId = @UserId, @FeeTypeId = @FeeTypeId, @FeeTypeCode = @FeeTypeCode, @FeeTypeName = @FeeTypeName, @Description = @Description, @DisplayOrder = @DisplayOrder;
    SELECT @SavedFeeTypeId = FeeTypeId
    FROM   @Saved;
    IF @SavedFeeTypeId IS NULL
       OR @SavedFeeTypeId <= 0
        THROW 59252, 'The fee type was not saved, so nothing has been stored.', 1;
    EXECUTE dbo.SetFeeTypeDefaultFrequency @UserId = @UserId, @FeeTypeId = @SavedFeeTypeId, @DefaultFrequency = @DefaultFrequency;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.FeeTypes
                   WHERE  FeeTypeId = @SavedFeeTypeId
                          AND DefaultFrequency = @DefaultFrequency)
        THROW 59253, 'The default frequency was not stored with the fee type, so the whole save has been rolled back.', 1;
    COMMIT TRANSACTION;
    SELECT @SavedFeeTypeId AS FeeTypeId;
END



