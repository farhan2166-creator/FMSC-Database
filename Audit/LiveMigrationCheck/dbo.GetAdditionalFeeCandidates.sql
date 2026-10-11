CREATE   PROCEDURE dbo.GetAdditionalFeeCandidates
    @UserId int,
    @AcademicYearId int,
    @CampusId int = 0,
    @ClassId int = 0,
    @SectionId int = 0
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Fees.Collect';
    IF NOT EXISTS (SELECT 1 FROM dbo.AcademicYears
                   WHERE AcademicYearId = @AcademicYearId AND IsCurrent = 1)
        THROW 53040, 'Additional fees must use the current academic year. Reload the page if it has changed.', 1;

    SELECT E.EnrollmentId, S.AdmissionNumber, S.RegistrationNumber, S.StudentName,
           S.FatherName, C.CampusName, CL.ClassName, SEC.SectionName,
           E.BillingStartDate
    FROM dbo.Enrollments E
    JOIN dbo.Students S ON S.AdmissionId = E.AdmissionId
    JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId = E.ClassSectionId
    JOIN dbo.Campuses C ON C.CampusId = CCS.CampusId
    JOIN dbo.Classes CL ON CL.ClassId = CCS.ClassId
    JOIN dbo.Sections SEC ON SEC.SectionId = CCS.SectionId
    WHERE E.AcademicYearId = @AcademicYearId
      AND (@ClassId = 0 OR CCS.ClassId = @ClassId)
      AND (@CampusId = 0 OR CCS.CampusId = @CampusId)
      AND (@SectionId = 0 OR CCS.SectionId = @SectionId)
      AND E.Status = N'Active' AND E.BillingReady = 1
    ORDER BY C.CampusName, CL.DisplayOrder, SEC.DisplayOrder,
             S.AdmissionNumberBase, S.AdmissionNumberSuffix, S.StudentName;
END;

