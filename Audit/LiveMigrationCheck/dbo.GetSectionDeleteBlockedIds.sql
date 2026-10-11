CREATE   PROCEDURE dbo.GetSectionDeleteBlockedIds
    @UserId int
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'System.Setup';

    SELECT S.SectionId AS Id
    FROM dbo.Sections S
    WHERE EXISTS (SELECT 1 FROM dbo.CampusClassSections X WHERE X.SectionId = S.SectionId)
       OR EXISTS (SELECT 1 FROM dbo.PromotionBatches X WHERE X.SourceSectionId = S.SectionId);
END;
