-- Module body only. See companion XML for signatures and relationships.


BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.RequirePermission @UserId, N'System.Setup';

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.Sections
        WHERE SectionId = @SectionId
    )
        THROW 59011, 'The selected section was not found.', 1;

    IF EXISTS (SELECT 1 FROM dbo.CampusClassSections WHERE SectionId = @SectionId)
        THROW 59012, 'This section is assigned to one or more campus/class combinations and cannot be deleted. Deactivate it instead.', 1;

    IF EXISTS (SELECT 1 FROM dbo.PromotionBatches WHERE SourceSectionId = @SectionId)
        THROW 59013, 'This section is referenced by promotion history and cannot be deleted. Deactivate it instead.', 1;

    DELETE FROM dbo.Sections
    WHERE SectionId = @SectionId;
END;
