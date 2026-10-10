SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* ---------------------------------------------------------------------------
   6/18  dbo.GetClassUsageCount  ->  System.Setup
   --------------------------------------------------------------------------- */
CREATE   PROCEDURE dbo.GetClassUsageCount
(
    @UserId int,
    @ClassId int
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
    WHERE CCS.ClassId = @ClassId
      AND E.Status = N'Active';
END;

GO
