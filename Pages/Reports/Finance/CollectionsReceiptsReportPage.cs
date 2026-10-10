using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Reports.Finance;
public sealed class CollectionsReceiptsReportPage : CanonicalFinanceReportPage
{
    public CollectionsReceiptsReportPage() : base(
        "Collections & Receipts", "Money received, refunded or corrected, with one canonical receipt count.",
        "GetCollectionsAndReceiptsReport", "FMSC_Collections_Receipts",
        new[] { "Receipts", "Detailed Transactions", "Summary" },
        new[] { "Day", "Week", "Month", "Payment Method", "Class", "Campus", "Fee Type" },
        true, false, true, true)
    { }
    protected override bool IsSummaryView => ViewName == "Summary";
    protected override IEnumerable<SqlParameter> BuildParameters()
    {
        yield return DatabaseHelper.P("@View", ViewName);
        yield return DatabaseHelper.P("@GroupBy", GroupName);
        yield return DatabaseHelper.Date("@FromDate", From.Checked ? From.Value : null);
        yield return DatabaseHelper.Date("@ToDate", To.Checked ? To.Value : null);
        yield return DatabaseHelper.Int("@CampusId", Campus.SelectedId);
        yield return DatabaseHelper.Int("@ClassId", Class.SelectedId);
        yield return DatabaseHelper.Int("@SectionId", Section.SelectedId);
        yield return DatabaseHelper.P("@PaymentMethod", MethodName.Length == 0 ? null : MethodName);
    }
}
