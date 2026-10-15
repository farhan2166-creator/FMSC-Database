-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'System.Setup';
    SELECT C.CampusId AS Id
    FROM dbo.Campuses C
    WHERE EXISTS (SELECT 1 FROM dbo.CampusClassSections X WHERE X.CampusId = C.CampusId)
       OR EXISTS (SELECT 1 FROM dbo.FeeConfigurations X WHERE X.CampusId = C.CampusId)
       OR EXISTS (SELECT 1 FROM dbo.PromotionBatches X WHERE X.SourceCampusId = C.CampusId)

       OR EXISTS (SELECT 1 FROM dbo.Students X WHERE X.AdmissionCampusId = C.CampusId);
END;
