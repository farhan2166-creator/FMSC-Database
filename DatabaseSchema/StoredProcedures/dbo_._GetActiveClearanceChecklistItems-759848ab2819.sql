-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.View';
    SELECT   ItemId,
             ItemText
    FROM     dbo.ClearanceChecklistItems
    ORDER BY DisplayOrder, ItemId;
END
