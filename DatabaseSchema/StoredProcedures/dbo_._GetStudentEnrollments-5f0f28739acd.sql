-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.View';
    IF @AdmissionId <= 0
        THROW 52001, 'A valid student is required.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Students
                   WHERE  AdmissionId = @AdmissionId)
        THROW 52002, 'The selected student was not found.', 1;
    SELECT   E.EnrollmentId,
             E.AdmissionId,
             E.AcademicYearId,
             AY.YearName,
             E.ClassSectionId,
             C.ClassName,
             SEC.SectionName,
             E.EnrollmentDate,
             E.LeavingDate,
             E.Status,
             CAST (ISNULL(SUM(CASE WHEN BC.Status <> N'Cancelled' THEN dbo.GetFeeChargeBalance(BC.BillingChargeId) ELSE 0 END), 0) AS DECIMAL (12, 2)) AS OutstandingAmount
    FROM     dbo.Enrollments AS E
             INNER JOIN
             dbo.AcademicYears AS AY
             ON AY.AcademicYearId = E.AcademicYearId
             INNER JOIN
             dbo.CampusClassSections AS CCS
             ON CCS.ClassSectionId = E.ClassSectionId
             INNER JOIN
             dbo.Classes AS C
             ON C.ClassId = CCS.ClassId
             INNER JOIN
             dbo.Sections AS SEC
             ON SEC.SectionId = CCS.SectionId
             LEFT OUTER JOIN
             dbo.BillingCharges AS BC
             ON BC.EnrollmentId = E.EnrollmentId
    WHERE    E.AdmissionId = @AdmissionId
    GROUP BY E.EnrollmentId, E.AdmissionId, E.AcademicYearId, AY.YearName, E.ClassSectionId, C.ClassName, SEC.SectionName, E.EnrollmentDate, E.LeavingDate, E.Status
    HAVING   E.Status = N'Active'
             OR ISNULL(SUM(CASE WHEN BC.Status <> N'Cancelled' THEN dbo.GetFeeChargeBalance(BC.BillingChargeId) ELSE 0 END), 0) > 0
             OR EXISTS (SELECT 1
                        FROM   dbo.StudentFeeAssignments AS SFA
                        WHERE  SFA.EnrollmentId = E.EnrollmentId
                               AND SFA.CancelledAt IS NULL)
    ORDER BY CASE WHEN E.Status = N'Active' THEN 0 ELSE 1 END, CASE WHEN E.Status = N'Active' THEN E.EnrollmentId END DESC, OutstandingAmount DESC, E.EnrollmentId DESC;
END
