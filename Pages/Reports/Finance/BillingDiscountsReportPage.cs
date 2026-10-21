using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Reports.Finance;
public sealed class BillingDiscountsReportPage : CanonicalFinanceReportPage
{
    public BillingDiscountsReportPage() : base(
        "Billing & Discounts", "Review billed fees, discounts and net amounts payable.",
        "GetBillingAndDiscountsReport", "FMSC_Billing_Discounts",
        new[] { "Billing Summary", "Billing Detail", "Discount Detail", "Concession Assignments" },
        new[] { "Day", "Week", "Month", "Class", "Campus", "Fee Type" },
        true, false, false, true)
    { }
    protected override bool IsSummaryView => ViewName == "Billing Summary";
    protected override void PrepareReportData(DataTable data, SqlParameter[] parameters)
    {
        if (!Equals(parameters.Single(p => p.ParameterName == "@View").Value, "Billing Summary")) return;
        string group = Convert.ToString(parameters.Single(p => p.ParameterName == "@GroupBy").Value) ?? "";
        string heading = group switch {
            "Day" => "Charge Date",
            "Week" => "Charge Week Starting",
            "Month" => "Charge Month",
            _ => group
        };
        foreach (var (name, label) in new[] {
            ("Group", heading), ("Charges", "Charge Count"),
            ("Gross Charges", "Gross Billed"), ("Discounts Applied", "Discounts"),
            ("Net Charges", "Net Billed") })
            if (data.Columns.Contains(name)) data.Columns[name]!.ColumnName = label;
    }
    protected override IEnumerable<SqlParameter> BuildParameters()
    {
        yield return DatabaseHelper.P("@View", ViewName);
        yield return DatabaseHelper.P("@GroupBy", GroupName);
        yield return DatabaseHelper.Date("@FromDate", From.Checked ? From.Value : null);
        yield return DatabaseHelper.Date("@ToDate", To.Checked ? To.Value : null);
        yield return DatabaseHelper.Int("@CampusId", Campus.SelectedId);
        yield return DatabaseHelper.Int("@ClassId", Class.SelectedId);
        yield return DatabaseHelper.Int("@SectionId", Section.SelectedId);
    }
}
