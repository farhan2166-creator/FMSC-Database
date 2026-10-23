using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Reports.Finance;
public sealed class CollectionsReceiptsReportPage : CanonicalFinanceReportPage
{
    public CollectionsReceiptsReportPage() : base(
        "Collections & Receipts", "Money received, refunds and corrections. Credit applied is separate and is not new money.",
        "GetCollectionsAndReceiptsReport", "FMSC_Collections_Receipts",
        new[] { "Receipts", "Detailed Transactions", "Summary" },
        new[] { "Day", "Week", "Month", "Payment Method", "Class", "Campus", "Fee Type" },
        true, false, true, true)
    {
        Grid.DataBindingComplete += (_, _) => {
            if (ViewName == "Receipts") LockGridOrder();
        };
    }
    private protected override ReportPaginator.Page CreateReportPage(DataTable source, int index, int pageSize) =>
        CollectionReceiptGrouping.Page(source, index, pageSize);
    protected override DataTable SelectExportRows(DataTable source, bool asCsv) =>
        asCsv ? CollectionReceiptGrouping.DetailsOnly(source) : source;
    protected override int ExportRecordCount(DataTable source) => CollectionReceiptGrouping.Layout(source)?.Count(r=>r.Kind=="Detail") ?? source.Rows.Count;
    protected override string CsvExportLabel => "CSV data rows only (*.csv)";
    protected override void WritePdfExport(string path, string title, string subtitle, DataTable table, Color accent)
    {
        if (CollectionReceiptGrouping.Layout(table) is null) { base.WritePdfExport(path, title, subtitle, table, accent); return; }
        var report = GridReportPreview.Build("", title, subtitle, table, CurrentUser.Username);
        using var local = new Microsoft.Reporting.WinForms.LocalReport();
        local.LoadReportDefinition(new StringReader(report.Definition));
        local.DataSources.Add(new Microsoft.Reporting.WinForms.ReportDataSource("ReportData", report.Data));
        byte[] bytes = local.Render("PDF");
        AtomicExportFile.Publish(path, temp => File.WriteAllBytes(temp, bytes), PdfTableExporter.ValidatePdf);
    }
    protected override string GroupingCaption => "Group By";
    protected override void UpdateViewControls()
    {
        base.UpdateViewControls();
        bool receipts = ViewName == "Receipts";
        string[] options = receipts ? new[] { "Whole School", "Campus", "Class", "Section" }
            : new[] { "Day", "Week", "Month", "Payment Method", "Class", "Campus", "Fee Type" };
        if (!GroupBy.Items.Cast<string>().SequenceEqual(options))
        {
            GroupBy.Items.Clear();
            GroupBy.Items.AddRange(options);
            GroupBy.SelectedIndex = 0;
        }
        GroupBy.Enabled = receipts || IsSummaryView;
    }
    protected override void PrepareReportData(DataTable data, SqlParameter[] parameters)
    {
        if (Equals(parameters.Single(p => p.ParameterName == "@View").Value, "Receipts"))
            CollectionReceiptGrouping.Apply(data,
                Convert.ToString(parameters.Single(p => p.ParameterName == "@GroupBy").Value) ?? "Whole School");
    }
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
