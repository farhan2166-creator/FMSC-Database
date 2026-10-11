CREATE   PROCEDURE dbo.ReorderFeeTypes
    @UserId int, @Items dbo.DisplayOrderItem READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.RequirePermission @UserId, N'Fees.Configuration.Manage';
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @OrderLock int;
        EXEC @OrderLock=sys.sp_getapplock @Resource=N'SetupOrder:FeeTypes',
            @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
        IF @OrderLock<0 THROW 57401, 'Unable to lock setup ordering. Please try again.', 1;
        DECLARE @Count int;
        SELECT @Count=COUNT(*) FROM dbo.FeeTypes WITH(UPDLOCK,HOLDLOCK);
        IF @Count<>(SELECT COUNT(*) FROM @Items)
            THROW 57600, 'The supplied order must contain every entry in the list.', 1;
        IF EXISTS(SELECT ItemId FROM @Items GROUP BY ItemId HAVING COUNT(*)>1)
            THROW 57601, 'Duplicate entries were supplied.', 1;
        IF EXISTS(SELECT DisplayOrder FROM @Items GROUP BY DisplayOrder HAVING COUNT(*)>1)
            OR EXISTS(SELECT 1 FROM @Items WHERE DisplayOrder<1)
            THROW 57602, 'Invalid ordering positions were supplied.', 1;
        IF EXISTS(SELECT 1 FROM @Items I LEFT JOIN dbo.FeeTypes T ON T.FeeTypeId=I.ItemId
            WHERE T.FeeTypeId IS NULL)
            THROW 57603, 'An entry is invalid or belongs to another list.', 1;
        DECLARE @Ranked TABLE(ItemId int PRIMARY KEY, Position int);
        INSERT @Ranked SELECT ItemId, ROW_NUMBER() OVER(ORDER BY DisplayOrder,ItemId) FROM @Items;
        UPDATE T SET DisplayOrder=-R.Position FROM dbo.FeeTypes T JOIN @Ranked R ON R.ItemId=T.FeeTypeId;
        UPDATE T SET DisplayOrder=R.Position FROM dbo.FeeTypes T JOIN @Ranked R ON R.ItemId=T.FeeTypeId;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;

