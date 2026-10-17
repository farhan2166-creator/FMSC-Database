using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Reports;
internal static class GridPrintJob
{
    public static void ShowPreview(IWin32Window? owner, string institution, string reportTitle,
        string filterSummary, DataTable table, string printedBy, string moneyFormat = "#,##0.##") =>
        GridReportPreview.Show(owner, institution, reportTitle, filterSummary, table, printedBy, moneyFormat);
}
