SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- GetActiveClearanceChecklistItems
CREATE   PROCEDURE dbo.GetActiveClearanceChecklistItems
@UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.View';
    SELECT   ItemId,
             ItemText
    FROM     dbo.ClearanceChecklistItems
    ORDER BY DisplayOrder, ItemId;
END



GO
