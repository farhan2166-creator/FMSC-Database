using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.StudentAccounts;

internal static class FeeDiscountPeriod
{
    public static DataRow Context(int id) => DatabaseHelper.QuerySql("""
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        SELECT S.ConcessionLabel, S.Frequency, S.EffectiveFrom, S.EffectiveTo, A.StartDate, A.EndDate, E.EnrollmentDate,E.Status,
            (SELECT MAX(B.BillingMonth) FROM dbo.BillingCharges B
             WHERE B.StudentFeeAssignmentId=S.StudentFeeAssignmentId) LastBilledMonth
        FROM dbo.StudentFeeAssignments S
        JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId
        JOIN dbo.AcademicYears A ON A.AcademicYearId=E.AcademicYearId
        WHERE S.StudentFeeAssignmentId=@Id
        """, DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@Id", id)).Rows[0];

    public static DataRow BillContext(long id) => DatabaseHelper.QuerySql("""
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        SELECT S.StudentFeeAssignmentId,S.ConcessionLabel,S.Frequency,A.StartDate,A.EndDate,E.EnrollmentDate,E.Status,
            B.BillingChargeId,COALESCE(B.BillingMonth,DATEFROMPARTS(YEAR(B.ChargeDate),MONTH(B.ChargeDate),1)) BillingMonth,
            F.FeeTypeName,COALESCE(C.NewGross,B.GrossAmount) StandardAmount,COALESCE(C.NewDiscount,B.DiscountAmount) DiscountAmount
        FROM dbo.BillingCharges Selected
        JOIN dbo.BillingCharges B ON B.BillingChargeId=COALESCE(Selected.CorrectionOfBillingChargeId,Selected.BillingChargeId)
        JOIN dbo.StudentFeeAssignments S ON S.StudentFeeAssignmentId=B.StudentFeeAssignmentId
        JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId
        JOIN dbo.AcademicYears A ON A.AcademicYearId=E.AcademicYearId
        JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
        OUTER APPLY(SELECT TOP(1) NewGross,NewDiscount FROM dbo.FeeBillCorrections WHERE BillingChargeId=B.BillingChargeId ORDER BY FeeBillCorrectionId DESC) C
        WHERE Selected.BillingChargeId=@Id AND B.Status<>N'Cancelled'
        """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.P("@Id",id)).Rows[0];

}
