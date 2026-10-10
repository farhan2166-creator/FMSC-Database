SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO


/* ----------------------------------------------------------------------------
   dbo.DeleteAcademicYear - the feature removed
   ---------------------------------------------------------------------------- */

CREATE   PROCEDURE dbo.DeleteAcademicYear
(
    @UserId int,
    @AcademicYearId int
)
AS


BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.RequirePermission @UserId, N'Calendar.Manage';

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.AcademicYears
        WHERE AcademicYearId = @AcademicYearId
    )
        THROW 59041, 'The selected academic year was not found.', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.AcademicYears
        WHERE AcademicYearId = @AcademicYearId
          AND IsCurrent = 1
    )
        THROW 59042, 'The current academic year cannot be deleted. Select another year as current first.', 1;


    IF EXISTS (SELECT 1 FROM dbo.Enrollments WHERE AcademicYearId = @AcademicYearId)
        THROW 59045, 'This academic year has student enrollments and cannot be deleted. Deactivate it instead.', 1;

    IF EXISTS (SELECT 1 FROM dbo.PromotionBatches WHERE SourceAcademicYearId = @AcademicYearId)
        THROW 59047, 'This academic year is referenced by promotion history and cannot be deleted. Deactivate it instead.', 1;

    IF EXISTS (SELECT 1 FROM dbo.PromotionBatches WHERE DestinationAcademicYearId = @AcademicYearId)
        THROW 59048, 'This academic year is referenced as a promotion destination and cannot be deleted. Deactivate it instead.', 1;

    DELETE FROM dbo.AcademicYears
    WHERE AcademicYearId = @AcademicYearId;
END;

GO
