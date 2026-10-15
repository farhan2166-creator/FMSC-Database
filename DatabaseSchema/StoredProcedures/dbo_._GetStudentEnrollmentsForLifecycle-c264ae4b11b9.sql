-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    -- Matches the permission already required by three of this page's
    -- four operations (TransferEnrollmentClassSection/Class/Campus);
    -- Re-Admit itself is gated separately by dbo.ReAdmitStudent's own
    -- Students.Administer check when it runs.
    EXEC dbo.RequirePermission
        @UserId,
        N'Enrollment.Manage';

    IF @AdmissionId <= 0
        THROW 52001, 'A valid student is required.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Students
        WHERE AdmissionId = @AdmissionId
    )
        THROW 52002, 'The selected student was not found.', 1;

    -- No fee-balance filtering, unlike dbo.GetStudentEnrollments - every
    -- enrollment the student has ever had is relevant here, so the page
    -- can always show a Left student's last known placement.
    SELECT
        E.EnrollmentId,
        E.AdmissionId,
        E.AcademicYearId,
        AY.YearName,
        E.ClassSectionId,
        CCS.CampusId, CMP.CampusName, C.ClassId, SEC.SectionId,
        C.ClassName,
        SEC.SectionName,
        E.EnrollmentDate,
        E.LeavingDate, E.BillingStartDate, E.PlacementConfirmedAt, E.BillingReady,
        E.Status

    FROM dbo.Enrollments E

    INNER JOIN dbo.AcademicYears AY
        ON AY.AcademicYearId = E.AcademicYearId

    INNER JOIN dbo.CampusClassSections CCS
        ON CCS.ClassSectionId = E.ClassSectionId

    INNER JOIN dbo.Campuses CMP ON CMP.CampusId = CCS.CampusId
    INNER JOIN dbo.Classes C
        ON C.ClassId = CCS.ClassId

    INNER JOIN dbo.Sections SEC
        ON SEC.SectionId = CCS.SectionId

    WHERE E.AdmissionId = @AdmissionId

    ORDER BY
        CASE
            WHEN E.Status = N'Active' THEN 0
            ELSE 1
        END,

        E.EnrollmentId DESC;
END;
