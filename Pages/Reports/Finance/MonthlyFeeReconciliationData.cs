using System.Data;

namespace FMSC_SMS.Pages.Reports.Finance;

internal static class MonthlyFeeReconciliationData
{
    // A billing-month cohort with CURRENT settlements, not a historical cash-flow statement.
    // Completed allocations are signed, exactly as in dbo.GetFeeChargeBalance.
    internal const string Query = """
        EXEC dbo.RequirePermission @UserId,N'Reports.View';
        ;WITH Charges AS (
            SELECT B.BillingChargeId,DATEFROMPARTS(YEAR(M.FeePeriod),MONTH(M.FeePeriod),1) BillingMonth,B.GrossAmount,B.DiscountAmount,B.NetAmount,
                P.CampusId,P.ClassId,P.SectionId,B.FeeTypeId,
                COALESCE(CA.CampusName,N'Unassigned') Campus,COALESCE(CL.ClassName,N'Unassigned') Class,
                COALESCE(SC.SectionName,N'Unassigned') Section,F.FeeTypeName [Fee Type]
            FROM dbo.BillingCharges B
            JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId AND E.AdmissionId=B.AdmissionId
            JOIN dbo.CampusClassSections P ON P.ClassSectionId=E.ClassSectionId
            LEFT JOIN dbo.Campuses CA ON CA.CampusId=P.CampusId
            LEFT JOIN dbo.Classes CL ON CL.ClassId=P.ClassId
            LEFT JOIN dbo.Sections SC ON SC.SectionId=P.SectionId
            JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
            CROSS APPLY (SELECT COALESCE(B.BillingMonth,B.ChargeDate) FeePeriod) M
            WHERE B.Status<>N'Cancelled'
              AND (@FromDate IS NULL OR M.FeePeriod>=DATEFROMPARTS(YEAR(@FromDate),MONTH(@FromDate),1))
              AND (@ToDate IS NULL OR M.FeePeriod<=EOMONTH(@ToDate))
              AND (@AcademicYearId=0 OR E.AcademicYearId=@AcademicYearId)
              AND (@CampusId=0 OR P.CampusId=@CampusId) AND (@ClassId=0 OR P.ClassId=@ClassId)
              AND (@SectionId=0 OR P.SectionId=@SectionId)
        ), Settlements AS (
            SELECT A.BillingChargeId,
                SUM(CASE WHEN P.IsCreditApplication=0 THEN A.Amount ELSE CONVERT(decimal(12,2),0) END) MoneyAllocated,
                SUM(CASE WHEN P.IsCreditApplication=1 THEN A.Amount ELSE CONVERT(decimal(12,2),0) END) CreditApplied
            FROM dbo.FeePaymentAllocations A JOIN Charges B ON B.BillingChargeId=A.BillingChargeId
            JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId AND P.Status=N'Completed'
            GROUP BY A.BillingChargeId
        )
        SELECT B.*,COALESCE(S.MoneyAllocated,0) [Money Allocated],COALESCE(S.CreditApplied,0) [Credit Applied],
            CASE WHEN B.NetAmount-COALESCE(S.MoneyAllocated,0)-COALESCE(S.CreditApplied,0)>0
                THEN B.NetAmount-COALESCE(S.MoneyAllocated,0)-COALESCE(S.CreditApplied,0) ELSE 0 END Outstanding,
            CASE WHEN COALESCE(S.MoneyAllocated,0)+COALESCE(S.CreditApplied,0)>B.NetAmount
                THEN COALESCE(S.MoneyAllocated,0)+COALESCE(S.CreditApplied,0)-B.NetAmount ELSE 0 END [Excess Settlement]
        FROM Charges B LEFT JOIN Settlements S ON S.BillingChargeId=B.BillingChargeId
        ORDER BY B.BillingMonth,B.CampusId,B.ClassId,B.SectionId,B.FeeTypeId,B.BillingChargeId;
        """;

    internal static readonly string[] Groupings = { "Whole School", "Campus", "Class", "Section", "Fee Type" };
    private static readonly (string Source, string Label)[] Metrics = {
        ("GrossAmount","Gross Fees"),("DiscountAmount","Discounts"),("NetAmount","Net Fees"),
        ("Money Allocated","Money Allocated"),("Credit Applied","Credit Applied"),
        ("Outstanding","Outstanding"),("Excess Settlement","Excess Settlement")
    };

    internal static (DataTable Rows, DataTable Totals) Create(DataTable source, string grouping)
    {
        if (!Groupings.Contains(grouping)) throw new ArgumentException("Unsupported reconciliation grouping.",nameof(grouping));
        var rows = new DataTable();
        rows.Columns.Add("Billing Month"); rows.Columns.Add("Group"); rows.Columns.Add("Charges",typeof(int));
        foreach (var metric in Metrics) rows.Columns.Add(metric.Label,typeof(decimal));
        string Key(DataRow r) => grouping switch {
            "Campus" => $"{r["CampusId"]}", "Class" => $"{r["CampusId"]}:{r["ClassId"]}",
            "Section" => $"{r["CampusId"]}:{r["ClassId"]}:{r["SectionId"]}",
            "Fee Type" => $"{r["FeeTypeId"]}", _ => "All"
        };
        string Label(DataRow r) => grouping switch {
            "Campus" => Convert.ToString(r["Campus"])!, "Class" => $"{r["Campus"]} / {r["Class"]}",
            "Section" => $"{r["Campus"]} / {r["Class"]} / {r["Section"]}",
            "Fee Type" => Convert.ToString(r["Fee Type"])!, _ => "Whole School"
        };
        var groups = source.AsEnumerable().GroupBy(r => (
            Month: new DateTime(Convert.ToDateTime(r["BillingMonth"]).Year,Convert.ToDateTime(r["BillingMonth"]).Month,1),Scope: Key(r)));
        foreach (var group in groups.OrderBy(g => g.Key.Month).ThenBy(g => Label(g.First()),StringComparer.CurrentCultureIgnoreCase).ThenBy(g => g.Key.Scope,StringComparer.Ordinal))
        {
            var row = rows.NewRow(); row["Billing Month"]=group.Key.Month.ToString("MMM yyyy"); row["Group"]=Label(group.First()); row["Charges"]=group.Count();
            foreach (var metric in Metrics) row[metric.Label]=group.Sum(r => Convert.ToDecimal(r[metric.Source]));
            rows.Rows.Add(row);
        }
        var totals = new DataTable();
        foreach (string metric in new[] { "Net Fees", "Settled", "Outstanding", "Excess Settlement" }) totals.Columns.Add(metric,typeof(decimal));
        decimal Sum(string column) => rows.AsEnumerable().Sum(r => r.Field<decimal>(column));
        totals.Rows.Add(Sum("Net Fees"),Sum("Money Allocated")+Sum("Credit Applied"),Sum("Outstanding"),Sum("Excess Settlement"));
        return (rows,totals);
    }
}
