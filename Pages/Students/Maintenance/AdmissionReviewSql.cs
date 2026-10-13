namespace FMSC_SMS.Pages.Students.Maintenance;

internal static class AdmissionReviewSql
{
    internal const string Query = """
                EXEC dbo.RequirePermission @UserId,N'Students.View';
                SELECT S.AdmissionId,COALESCE(S.AdmissionNumber,S.QuickReference) Reference,S.StudentName,S.FatherName,S.ContactNumber,S.Status,
                 CASE WHEN S.LegacyVerifiedAt IS NULL THEN N'Needs Review' ELSE N'Verified' END Verification,
                 Y.YearName,C.ClassName,SEC.SectionName,E.EnrollmentDate PlacementStart,E.BillingStartDate BillingStart,
                 COALESCE((SELECT SUM(dbo.GetFeeChargeBalance(B.BillingChargeId)) FROM dbo.BillingCharges B WHERE B.AdmissionId=S.AdmissionId AND B.Status<>N'Cancelled'),0) Outstanding
                FROM dbo.Students S OUTER APPLY(SELECT TOP(1) * FROM dbo.Enrollments H WHERE H.AdmissionId=S.AdmissionId ORDER BY H.EnrollmentId DESC) E
                LEFT JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId LEFT JOIN dbo.CampusClassSections CS ON CS.ClassSectionId=E.ClassSectionId
                LEFT JOIN dbo.Classes C ON C.ClassId=CS.ClassId LEFT JOIN dbo.Sections SEC ON SEC.SectionId=CS.SectionId
                WHERE (@Pending=1 AND S.Status=N'Pending' AND S.QuickReference IS NOT NULL AND S.AdmissionNumber IS NULL) OR (@Pending=0 AND S.LegacySetupPending=1) ORDER BY S.LegacyVerifiedAt,S.AdmissionId;
                """;
}
