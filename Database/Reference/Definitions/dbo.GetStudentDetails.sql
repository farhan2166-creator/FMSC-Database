SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER OFF;
GO

-- Canonical single-student details, status and current placement read.

CREATE PROCEDURE dbo.GetStudentDetails
(
    @UserId int,
    @AdmissionId int
)
AS

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Students.View';

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Students
        WHERE AdmissionId = @AdmissionId
    )
        THROW 51700, 'The selected student was not found.', 1;

    SELECT
        S.AdmissionId,
        S.AdmissionNumber,
        S.RegistrationNumber,
        S.BiseEnrolmentNumber,
        S.CasteTribe,
        S.StudentName,
        S.FatherName,
        S.DateOfBirth,
        S.Gender,
        S.BFormNumber,
        S.Religion,
        S.BloodGroup,
        S.ContactNumber,
        S.AlternateContactNumber,
        S.Address,
        S.PreviousSchool,
        S.PlaceOfBirth,
        S.FatherOccupation,
        S.Disability,
        S.Status,
        S.WithdrawalDate,
        S.AdmissionDate,

        S.AdmissionCampusId,
        AC.CampusName AS AdmissionCampusName,

        E.EnrollmentId,
        E.EnrollmentDate,
        E.AcademicYearId,
        AY.YearName,
        E.ClassSectionId,
        E.UsesTransport,
        E.Status AS EnrollmentStatus,

        CCS.CampusId AS CurrentCampusId,
        CC.CampusName AS CurrentCampusName,

        C.ClassId,
        C.ClassCode,
        C.ClassName,

        SEC.SectionId,
        SEC.SectionName

    FROM dbo.Students AS S

    INNER JOIN dbo.Campuses AS AC
        ON AC.CampusId = S.AdmissionCampusId

    OUTER APPLY
    (
        SELECT TOP (1) *
        FROM dbo.Enrollments AS EE
        WHERE EE.AdmissionId = S.AdmissionId
        ORDER BY
            CASE WHEN EE.Status = N'Active' THEN 0 ELSE 1 END,
            EE.EnrollmentDate DESC,
            EE.EnrollmentId DESC
    ) AS E

    LEFT JOIN dbo.AcademicYears AS AY
        ON AY.AcademicYearId = E.AcademicYearId


    LEFT JOIN dbo.CampusClassSections AS CCS
        ON CCS.ClassSectionId = E.ClassSectionId

    LEFT JOIN dbo.Campuses AS CC
        ON CC.CampusId = CCS.CampusId

    LEFT JOIN dbo.Classes AS C
        ON C.ClassId = CCS.ClassId

    LEFT JOIN dbo.Sections AS SEC
        ON SEC.SectionId = CCS.SectionId

    WHERE S.AdmissionId = @AdmissionId;
END;


GO
