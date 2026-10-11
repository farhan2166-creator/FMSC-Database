
-- GetAdmissionsReport
CREATE   PROCEDURE dbo.GetAdmissionsReport
@UserId INT, @FromDate DATE, @ToDate DATE, @CampusId INT=0, @ClassId INT=0
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Reports.View';
    SELECT   S.AdmissionId,
             S.AdmissionNumber,
             S.AdmissionDate,
             S.RegistrationNumber,
             S.StudentName,
             S.FatherName,
             S.Gender,
             S.ContactNumber,
             CAMP.CampusName,
             C.ClassName,
             SEC.SectionName,
             E.EnrollmentDate,
             E.Status AS EnrollmentStatus
    FROM     dbo.Students AS S
             LEFT OUTER JOIN
             dbo.Enrollments AS E
             ON E.EnrollmentId = (SELECT   TOP (1) E2.EnrollmentId
                                  FROM     dbo.Enrollments AS E2
                                  WHERE    E2.AdmissionId = S.AdmissionId
                                  ORDER BY CASE WHEN E2.Status = N'Active' THEN 0 ELSE 1 END, E2.EnrollmentDate DESC, E2.EnrollmentId DESC)
             LEFT OUTER JOIN
             dbo.CampusClassSections AS CCS
             ON CCS.ClassSectionId = E.ClassSectionId
             LEFT OUTER JOIN
             dbo.Campuses AS CAMP
             ON CAMP.CampusId = COALESCE (CCS.CampusId, S.AdmissionCampusId)
             LEFT OUTER JOIN
             dbo.Classes AS C
             ON C.ClassId = CCS.ClassId
             LEFT OUTER JOIN
             dbo.Sections AS SEC
             ON SEC.SectionId = CCS.SectionId
    WHERE    S.AdmissionDate BETWEEN @FromDate AND @ToDate
             AND (@CampusId = 0
                  OR S.AdmissionCampusId = @CampusId
                  OR CCS.CampusId = @CampusId)
             AND (@ClassId = 0
                  OR CCS.ClassId = @ClassId)
    ORDER BY S.AdmissionDate, CAMP.CampusName, C.DisplayOrder, S.StudentName;
END



