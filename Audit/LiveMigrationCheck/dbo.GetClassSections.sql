
CREATE PROCEDURE dbo.GetClassSections
    @UserId int, @CampusId int, @ClassId int, @SectionId int
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequireAuthenticatedUser @UserId;
    IF @CampusId <> 0 AND NOT EXISTS (SELECT 1 FROM dbo.Campuses WHERE CampusId = @CampusId)
        THROW 50102, 'Please select a valid campus.', 1;
    IF @ClassId <> 0 AND NOT EXISTS (SELECT 1 FROM dbo.Classes WHERE ClassId = @ClassId)
        THROW 50104, 'Please select a valid class.', 1;
    IF @SectionId <> 0 AND NOT EXISTS (SELECT 1 FROM dbo.Sections WHERE SectionId = @SectionId)
        THROW 50105, 'Please select a valid section.', 1;
    SELECT CCS.ClassSectionId, CCS.CampusId, CMP.CampusName,
           CCS.ClassId, C.ClassName, CCS.SectionId, S.SectionName
    FROM dbo.CampusClassSections CCS
    JOIN dbo.Classes C ON C.ClassId = CCS.ClassId
    JOIN dbo.Sections S ON S.SectionId = CCS.SectionId
    JOIN dbo.Campuses CMP ON CMP.CampusId = CCS.CampusId
    WHERE CCS.IsActive = 1
        AND (@CampusId = 0 OR CCS.CampusId = @CampusId)
        AND (@ClassId = 0 OR CCS.ClassId = @ClassId)
        AND (@SectionId = 0 OR CCS.SectionId = @SectionId)
    ORDER BY CMP.CampusName, C.ClassName, S.SectionName, S.SectionId;
END
