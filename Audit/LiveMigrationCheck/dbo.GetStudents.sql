
-- GetStudents
CREATE   PROCEDURE dbo.GetStudents
@UserId INT, @CampusId INT=0, @AcademicYearId INT=0, @ClassId INT=0, @ClassSectionId INT=0, @Search NVARCHAR (200)=NULL, @IncludeFormerStudents BIT=0, @ProgramType NVARCHAR (20)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.View';
    SELECT   S.AdmissionId,
             S.AdmissionNumber,
             S.RegistrationNumber,
             S.StudentName,
             S.FatherName,
             S.DateOfBirth,
             S.AdmissionDate,
             S.AdmissionCampusId,
             AC.CampusName AS AdmissionCampusName,
             S.Status,
             E.EnrollmentId,
             E.AcademicYearId,
             AY.YearName,
             AY.EndDate AS AcademicYearEndDate,
             E.ClassSectionId,
             C.ClassId,
             C.ClassName,
             SEC.SectionId,
             SEC.SectionName,
             COALESCE (CCS.CampusId, S.AdmissionCampusId) AS CurrentCampusId,
             COALESCE (CC.CampusName, AC.CampusName) AS CurrentCampusName,
             E.Status AS EnrollmentStatus,
             C.AcademicProgramId,
             AP.ProgramType
    FROM     dbo.Students AS S
             INNER JOIN
             dbo.Campuses AS AC
             ON AC.CampusId = S.AdmissionCampusId OUTER APPLY (SELECT   TOP (1) E1.*
                                                               FROM     dbo.Enrollments AS E1
                                                               WHERE    E1.AdmissionId = S.AdmissionId
                                                               ORDER BY CASE WHEN E1.Status = N'Active' THEN 0 ELSE 1 END, E1.EnrollmentDate DESC, E1.EnrollmentId DESC) AS E
             LEFT OUTER JOIN
             dbo.AcademicYears AS AY
             ON AY.AcademicYearId = E.AcademicYearId
             LEFT OUTER JOIN
             dbo.CampusClassSections AS CCS
             ON CCS.ClassSectionId = E.ClassSectionId
             LEFT OUTER JOIN
             dbo.Campuses AS CC
             ON CC.CampusId = CCS.CampusId
             LEFT OUTER JOIN
             dbo.Classes AS C
             ON C.ClassId = CCS.ClassId
             LEFT OUTER JOIN
             dbo.Sections AS SEC
             ON SEC.SectionId = CCS.SectionId
             LEFT OUTER JOIN
             dbo.AcademicPrograms AS AP
             ON AP.AcademicProgramId = C.AcademicProgramId
    WHERE    (@CampusId = 0
              OR COALESCE (CCS.CampusId, S.AdmissionCampusId) = @CampusId)
             AND (@AcademicYearId = 0
                  OR E.AcademicYearId = @AcademicYearId)
             AND (@ClassId = 0
                  OR CCS.ClassId = @ClassId)
             AND (@ClassSectionId = 0
                  OR E.ClassSectionId = @ClassSectionId)
             AND (@IncludeFormerStudents = 1
                  OR S.Status = N'Active')
             AND (NULLIF (LTRIM(RTRIM(@ProgramType)), N'') IS NULL
                  OR AP.ProgramType = @ProgramType)
             AND (NULLIF (LTRIM(RTRIM(@Search)), N'') IS NULL
                  OR S.StudentName LIKE N'%' + LTRIM(RTRIM(@Search)) + N'%'
                  OR S.FatherName LIKE N'%' + LTRIM(RTRIM(@Search)) + N'%'
                  OR S.RegistrationNumber LIKE N'%' + LTRIM(RTRIM(@Search)) + N'%'
                  OR S.AdmissionNumber = LTRIM(RTRIM(@Search)))
    ORDER BY S.AdmissionNumberBase, S.AdmissionNumberSuffix, S.AdmissionId;
END



