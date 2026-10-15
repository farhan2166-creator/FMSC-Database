-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Reports.View';

    ;WITH Counts AS
    (
        SELECT
            CCS.CampusId, CAMP.CampusName,
            CCS.ClassId, C.ClassName,
            CCS.SectionId, SEC.SectionName,
            E.AcademicYearId,
            COUNT(*) AS StudentCount
        FROM dbo.Enrollments E
        INNER JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId = E.ClassSectionId
        INNER JOIN dbo.Campuses CAMP ON CAMP.CampusId = CCS.CampusId
        INNER JOIN dbo.Classes C ON C.ClassId = CCS.ClassId
        INNER JOIN dbo.Sections SEC ON SEC.SectionId = CCS.SectionId
        WHERE E.Status = N'Active'
          AND E.AcademicYearId IN (@CurrentAcademicYearId, @CompareAcademicYearId)
          AND (@CampusId = 0 OR CCS.CampusId = @CampusId)
          AND (@ClassId = 0 OR CCS.ClassId = @ClassId)
          AND (@SectionId = 0 OR CCS.SectionId = @SectionId)
        GROUP BY CCS.CampusId, CAMP.CampusName, CCS.ClassId, C.ClassName, CCS.SectionId, SEC.SectionName, E.AcademicYearId
    )
    SELECT
        ISNULL(Cur.CampusId, Cmp.CampusId) AS CampusId,
        ISNULL(Cur.CampusName, Cmp.CampusName) AS CampusName,
        ISNULL(Cur.ClassId, Cmp.ClassId) AS ClassId,
        ISNULL(Cur.ClassName, Cmp.ClassName) AS ClassName,
        ISNULL(Cur.SectionId, Cmp.SectionId) AS SectionId,
        ISNULL(Cur.SectionName, Cmp.SectionName) AS SectionName,
        ISNULL(Cur.StudentCount, 0) AS CurrentStrength,
        CASE WHEN @CompareAcademicYearId = 0 THEN NULL ELSE ISNULL(Cmp.StudentCount, 0) END AS CompareStrength,
        CASE WHEN @CompareAcademicYearId = 0 THEN NULL ELSE ISNULL(Cur.StudentCount, 0) - ISNULL(Cmp.StudentCount, 0) END AS Delta
    FROM (SELECT * FROM Counts WHERE AcademicYearId = @CurrentAcademicYearId) Cur
    FULL OUTER JOIN (SELECT * FROM Counts WHERE AcademicYearId = @CompareAcademicYearId) Cmp
        ON Cur.CampusId = Cmp.CampusId AND Cur.ClassId = Cmp.ClassId AND Cur.SectionId = Cmp.SectionId
    ORDER BY CampusName, ClassName, SectionName;
END;
