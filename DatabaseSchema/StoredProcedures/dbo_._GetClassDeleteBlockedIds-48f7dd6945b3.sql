-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'System.Setup';
    SELECT C.ClassId AS Id
    FROM dbo.Classes C
    WHERE EXISTS (SELECT 1 FROM dbo.CampusClassSections X WHERE X.ClassId = C.ClassId)
       OR EXISTS (SELECT 1 FROM dbo.FeeConfigurations X WHERE X.ClassId = C.ClassId)
       OR EXISTS (SELECT 1 FROM dbo.PromotionBatches X WHERE X.SourceClassId = C.ClassId);
END;
