-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SELECT CASE WHEN EXISTS (SELECT 1
                             FROM   dbo.Users) THEN CAST (0 AS BIT) ELSE CAST (1 AS BIT) END AS IsFirstRun,
           (SELECT COUNT(*)
            FROM   dbo.Users) AS UserCount,
           (SELECT COUNT(*)
            FROM   dbo.Campuses) AS ActiveCampusCount,
           (SELECT COUNT(*)
            FROM   dbo.AcademicYears) AS ActiveAcademicYearCount,
           (SELECT COUNT(*)
            FROM   dbo.Classes) AS ActiveClassCount,
           (SELECT COUNT(*)
            FROM   dbo.Sections) AS ActiveSectionCount,
           (SELECT COUNT(*)
            FROM   dbo.CampusClassSections) AS ActiveClassSectionCount,
           (SELECT COUNT(*)
            FROM   dbo.Permissions) AS ActivePermissionCount,
           (SELECT COUNT(*)
            FROM   dbo.Roles) AS ActiveRoleCount;
END
