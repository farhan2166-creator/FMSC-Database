SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* Staff assignment feature intentionally retired 2026-10-03; retain all other guards. */

/* Campus deletion retains every safeguard for the active school data. */
CREATE PROCEDURE dbo.DeleteCampus
    @UserId int,
    @CampusId int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.RequirePermission @UserId, N'System.Setup';

    IF NOT EXISTS (SELECT 1 FROM dbo.Campuses WHERE CampusId = @CampusId)
        THROW 59031, 'The selected campus was not found.', 1;
    IF EXISTS (SELECT 1 FROM dbo.CampusClassSections WHERE CampusId = @CampusId)
        THROW 59032, 'This campus has class/section assignments and cannot be deleted. Deactivate it instead.', 1;
    IF EXISTS (SELECT 1 FROM dbo.FeeConfigurations WHERE CampusId = @CampusId)
        THROW 59033, 'This campus has fee configuration and cannot be deleted. Deactivate it instead.', 1;
    IF EXISTS (SELECT 1 FROM dbo.PromotionBatches WHERE SourceCampusId = @CampusId)
        THROW 59034, 'This campus is referenced by promotion history and cannot be deleted. Deactivate it instead.', 1;
    IF EXISTS (SELECT 1 FROM dbo.Students WHERE AdmissionCampusId = @CampusId)
        THROW 59036, 'This campus is assigned to students and cannot be deleted. Deactivate it instead.', 1;

    DELETE FROM dbo.Campuses WHERE CampusId = @CampusId;
END;


GO
