-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Reports.View';

    SELECT PB.*
    FROM dbo.PromotionBatches PB
    WHERE (@SourceAcademicYearId = 0 OR PB.SourceAcademicYearId = @SourceAcademicYearId)
      AND (@SourceClassId = 0 OR PB.SourceClassId = @SourceClassId)
      AND (@SourceSectionId = 0 OR PB.SourceSectionId = @SourceSectionId)
    ORDER BY PB.PromotionBatchId DESC;
END;
