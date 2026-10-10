using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Reports.Finance;
public sealed class BillingDiscountsReportPage : CanonicalFinanceReportPage
{
    public BillingDiscountsReportPage() : base(
        "Billing & Discounts", "Review fee charges and their discounts.",
        "GetBillingAndDiscountsReport", "FMSC_Billing_Discounts",
        new[] { "Billing Summary", "Billing Detail", "Discount Detail" },
        new[] { "Day", "Week", "Month", "Class", "Campus", "Fee Type" },
        true, false, false, true)
    { }
    protected override bool IsSummaryView => ViewName == "Billing Summary";
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
