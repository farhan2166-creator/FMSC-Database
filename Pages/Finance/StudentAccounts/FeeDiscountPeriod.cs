using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.StudentAccounts;

internal static class FeeDiscountPeriod
{
    public static DataTable IssuedBills(int enrollment,int? feeType=null) => DatabaseHelper.QuerySql("""
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        SELECT B.BillingChargeId,CONCAT(N'B-',B.BillingChargeId) [Charge Ref],F.FeeTypeName [Fee],B.ChargeDate [Issued],
            X.NetAmount [Bill Total],X.NetAmount-X.Balance [Settled],X.Balance [Due],B.Remarks [Description]
        FROM dbo.BillingCharges B JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
        CROSS APPLY(SELECT SUM(Q.NetAmount) NetAmount,SUM(dbo.GetFeeChargeBalance(Q.BillingChargeId)) Balance
            FROM dbo.BillingCharges Q WHERE (Q.BillingChargeId=B.BillingChargeId OR Q.CorrectionOfBillingChargeId=B.BillingChargeId)
                AND Q.Status<>N'Cancelled') X
        WHERE B.EnrollmentId=@Enrollment AND (@FeeType IS NULL OR B.FeeTypeId=@FeeType) AND B.CorrectionOfBillingChargeId IS NULL AND B.Status<>N'Cancelled'
        ORDER BY B.ChargeDate DESC,B.BillingChargeId DESC;
        """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@Enrollment",enrollment),DatabaseHelper.P("@FeeType",feeType));

    public static DataRow Context(int id) => DatabaseHelper.QuerySql("""
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        SELECT S.ConcessionLabel, S.Frequency, S.EffectiveFrom, S.EffectiveTo, A.StartDate, A.EndDate, E.EnrollmentDate,E.Status,ST.StudentName,ST.AdmissionNumber,A.YearName,
            (SELECT MAX(B.BillingMonth) FROM dbo.BillingCharges B
             WHERE B.StudentFeeAssignmentId=S.StudentFeeAssignmentId) LastBilledMonth
        FROM dbo.StudentFeeAssignments S
        JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId
        JOIN dbo.Students ST ON ST.AdmissionId=E.AdmissionId
        JOIN dbo.AcademicYears A ON A.AcademicYearId=E.AcademicYearId
        WHERE S.StudentFeeAssignmentId=@Id
        """, DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@Id", id)).Rows[0];

    public static DataRow BillContext(long id) => DatabaseHelper.QuerySql("""
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        SELECT S.StudentFeeAssignmentId,
            CASE WHEN X.NetAmount=0 AND G.GrossAmount>0 THEN COALESCE(S.ConcessionLabel,N'Waived')
                WHEN G.GrossAmount>X.NetAmount THEN N'Scholarship' END ConcessionLabel,
            S.Frequency,A.StartDate,A.EndDate,E.EnrollmentDate,E.Status,ST.StudentName,ST.AdmissionNumber,A.YearName,
            B.BillingChargeId,COALESCE(B.BillingMonth,DATEFROMPARTS(YEAR(B.ChargeDate),MONTH(B.ChargeDate),1)) BillingMonth,
            F.FeeTypeName,G.GrossAmount StandardAmount,G.GrossAmount-X.NetAmount DiscountAmount
        FROM dbo.BillingCharges Selected
        JOIN dbo.BillingCharges B ON B.BillingChargeId=COALESCE(Selected.CorrectionOfBillingChargeId,Selected.BillingChargeId)
        JOIN dbo.StudentFeeAssignments S ON S.StudentFeeAssignmentId=B.StudentFeeAssignmentId
        JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId
        JOIN dbo.Students ST ON ST.AdmissionId=E.AdmissionId
        JOIN dbo.AcademicYears A ON A.AcademicYearId=E.AcademicYearId
        JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
        OUTER APPLY(SELECT TOP(1) NewGross FROM dbo.FeeBillCorrections WHERE BillingChargeId=B.BillingChargeId ORDER BY FeeBillCorrectionId DESC) C
        CROSS APPLY(SELECT COALESCE(C.NewGross,B.GrossAmount) GrossAmount) G
        CROSS APPLY(SELECT SUM(Q.NetAmount) NetAmount FROM dbo.BillingCharges Q
            WHERE (Q.BillingChargeId=B.BillingChargeId OR Q.CorrectionOfBillingChargeId=B.BillingChargeId) AND Q.Status<>N'Cancelled') X
        WHERE Selected.BillingChargeId=@Id AND B.Status<>N'Cancelled'
        """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.P("@Id",id)).Rows[0];

}
