SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* ---------------------------------------------------------------------------
   10/18  dbo.GetSectionUsageCount  ->  System.Setup
   --------------------------------------------------------------------------- */
CREATE   PROCEDURE dbo.GetSectionUsageCount
(
    @UserId int,
    @SectionId int
)
AS

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'System.Setup';

    SELECT COUNT(*)
    FROM dbo.Enrollments E
    INNER JOIN dbo.CampusClassSections CCS
        ON CCS.ClassSectionId = E.ClassSectionId
    WHERE CCS.SectionId = @SectionId
      AND E.Status = N'Active';
END;

GO
