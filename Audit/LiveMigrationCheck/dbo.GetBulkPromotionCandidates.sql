
-- GetBulkPromotionCandidates
CREATE   PROCEDURE dbo.GetBulkPromotionCandidates
@UserId INT, @SourceAcademicYearId INT, @SourceCampusId INT, @SourceClassId INT, @SourceSectionId INT=0
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
    IF @SourceAcademicYearId <= 0
        THROW 52401, 'A valid source academic year is required.', 1;
    IF @SourceCampusId <= 0
        THROW 52402, 'A valid source campus is required.', 1;
    IF @SourceClassId <= 0
        THROW 52403, 'A valid source class is required.', 1;
    SELECT   E.EnrollmentId,
             E.AdmissionId,
             S.RegistrationNumber,
             S.StudentName,
             S.FatherName,
             E.ClassSectionId,
             C.ClassId,
             C.ClassName,
             SEC.SectionId,
             SEC.SectionName,
             CAMP.CampusId,
             CAMP.CampusName,
             E.UsesTransport,
             N'PROMOTE' AS SuggestedDecision
    FROM     dbo.Enrollments AS E
             INNER JOIN
             dbo.Students AS S
             ON S.AdmissionId = E.AdmissionId
             INNER JOIN
             dbo.CampusClassSections AS CCS
             ON CCS.ClassSectionId = E.ClassSectionId
             INNER JOIN
             dbo.Campuses AS CAMP
             ON CAMP.CampusId = CCS.CampusId
             INNER JOIN
             dbo.Classes AS C
             ON C.ClassId = CCS.ClassId
             INNER JOIN
             dbo.Sections AS SEC
             ON SEC.SectionId = CCS.SectionId
    WHERE    E.Status = N'Active'
             AND E.AcademicYearId = @SourceAcademicYearId
             AND CCS.CampusId = @SourceCampusId
             AND CCS.ClassId = @SourceClassId
             AND (@SourceSectionId = 0
                  OR CCS.SectionId = @SourceSectionId)
    ORDER BY C.DisplayOrder, SEC.SectionName, SEC.SectionId, S.StudentName, E.EnrollmentId;
END



