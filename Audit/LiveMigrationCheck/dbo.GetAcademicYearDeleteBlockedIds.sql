CREATE   PROCEDURE dbo.GetAcademicYearDeleteBlockedIds
    @UserId int
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Calendar.Manage';

    SELECT AY.AcademicYearId AS Id
    FROM dbo.AcademicYears AY
    WHERE AY.IsCurrent = 1
       OR EXISTS (SELECT 1 FROM dbo.Enrollments X WHERE X.AcademicYearId = AY.AcademicYearId)
       OR EXISTS (SELECT 1 FROM dbo.PromotionBatches X WHERE X.SourceAcademicYearId = AY.AcademicYearId)
       OR EXISTS (SELECT 1 FROM dbo.PromotionBatches X WHERE X.DestinationAcademicYearId = AY.AcademicYearId);
END;
