using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.StudentAccounts;

internal static class FeeDiscountPeriod
{
    public static DataRow Context(int id) => DatabaseHelper.QuerySql("""
        SELECT S.ConcessionLabel, S.Frequency, S.EffectiveFrom, S.EffectiveTo, A.StartDate, A.EndDate, E.EnrollmentDate,
            (SELECT MAX(B.BillingMonth) FROM dbo.BillingCharges B
             WHERE B.StudentFeeAssignmentId=S.StudentFeeAssignmentId) LastBilledMonth
        FROM dbo.StudentFeeAssignments S
        JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId
        JOIN dbo.AcademicYears A ON A.AcademicYearId=E.AcademicYearId
        WHERE S.StudentFeeAssignmentId=@Id AND S.CancelledAt IS NULL
        """, DatabaseHelper.Int("@Id", id)).Rows[0];

}
