using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Reports.Finance;
public sealed class CollectionsReceiptsReportPage : CanonicalFinanceReportPage
{
    public CollectionsReceiptsReportPage() : base(
        "Collections & Receipts", "Payments received, refunds and corrections. Credit applied is shown separately.",
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
    protected override DataTable SelectExportRows(DataTable source, bool dataOnly) =>
        dataOnly ? CollectionReceiptGrouping.DetailsOnly(source) : source;
    protected override int ExportRecordCount(DataTable source) => CollectionReceiptGrouping.Layout(source)?.Count(r=>r.Kind=="Detail") ?? source.Rows.Count;
    protected override string CsvExportLabel => "CSV data rows only (*.csv)";
    protected override string GroupingCaption => "Group By";
    protected override IReadOnlyList<ReportColumnDef> DefineReportColumns()
    {
        var columns=base.DefineReportColumns();
        if(ViewName!="Receipts")return columns;
        string[] priority={"Receipt / Reference","Date","Student / Family","Payment Received","Money Received",
            "Credit Applied","Adjusted Collection","Class / Section","Payment Method","Refunds","Corrections","Scope","Received By"};
        return columns.Where(column=>column.Key!="Campus")
            .OrderBy(column=>Array.IndexOf(priority,column.Key) is int index&&index>=0?index:int.MaxValue).ToArray();
    }
    protected override void OnReportPageBound()
    {
        base.OnReportPageBound();
        if(ViewName!="Receipts")return;
        Grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
        Grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.DisplayedCells;
        Grid.ColumnHeadersDefaultCellStyle.WrapMode=DataGridViewTriState.True;
        Grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        Grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
        foreach(DataGridViewColumn column in Grid.Columns)
        {
            if(!column.Visible)continue;
            int width=column.Name switch {
                "Student / Family"=>260,"Class / Section"=>190,"Receipt / Reference"=>155,
                "Date"=>110,"Payment Method"=>125,"Scope"=>80,_=>110
            };
            column.MinimumWidth=DpiLayout.Pixels(width,Grid.DeviceDpi);
            column.FillWeight=width;
            column.AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;
            column.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
        }
        Grid.AutoResizeRows(DataGridViewAutoSizeRowsMode.DisplayedCells);
    }
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
        {
            CollectionReceiptPresentation.Apply(data);
            CollectionReceiptGrouping.Apply(data,
                Convert.ToString(parameters.Single(p => p.ParameterName == "@GroupBy").Value) ?? "Whole School");
        }
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
