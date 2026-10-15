-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Students.Administer';

    SELECT E.EnrollmentId, E.AdmissionId, S.RegistrationNumber,
           S.StudentName, S.FatherName, AY.YearName AS AcademicYear,
           C.ClassName, SEC.SectionName, CAMP.CampusName,
           S.RegistrationNumber + N' - ' + S.StudentName + N' - ' +
           C.ClassName + N' / ' + SEC.SectionName AS Display,
           E.EnrollmentDate
    FROM dbo.Enrollments E
    JOIN dbo.Students S ON S.AdmissionId = E.AdmissionId
    JOIN dbo.AcademicYears AY ON AY.AcademicYearId = E.AcademicYearId
    JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId = E.ClassSectionId
    JOIN dbo.Classes C ON C.ClassId = CCS.ClassId
    JOIN dbo.Sections SEC ON SEC.SectionId = CCS.SectionId
    JOIN dbo.Campuses CAMP ON CAMP.CampusId = CCS.CampusId
    WHERE E.Status = N'Active' AND E.BillingReady = 0 AND E.PlacementConfirmedAt IS NOT NULL
      AND S.Status = N'Active'
    ORDER BY S.StudentName, S.RegistrationNumber;
END;
