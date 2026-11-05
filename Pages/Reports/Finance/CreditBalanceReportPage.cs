using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Reports.Finance;

public sealed class CreditBalanceReportPage : CanonicalFinanceReportPage
{
    private readonly ModernNumericTextBox minimum = new() { Minimum = 0, Maximum = 1000000000m, ZeroAsPlaceholder = true };
    public CreditBalanceReportPage() : base("Student Advance Balance Register",
        "Unused advance held for students. Location filters use each student's current or latest placement.",
        "CreditBalanceRegister", "FMSC_Credit_Balances", new[] { "Students", "Families" }, Array.Empty<string>(),false,false,false,false)
    {
        minimum.TextChanged += (_, _) => FiltersChanged();
    }
    protected override IEnumerable<(string, Control)> AdditionalReportFilters() => new[] { ("Minimum advance per student (Rs.)",(Control)minimum) };
    protected override string AdditionalFilterSignature => NumericFilterSignature(minimum.Text);
    protected override string AdditionalFilterDescription => (minimum.Value>0?$"Minimum advance per student: Rs. {minimum.Value:#,##0.##}. ":"")+"Only students matching the filters are included.";
    protected override IReadOnlyList<ReportColumnDef> DefineReportColumns()=>base.DefineReportColumns().Select(column=>column with {
        Header=column.Key switch {
            "Available Credit"=>ViewName=="Families"?"Family Advance":"Available Advance",
            "Students"=>"Students with Advance","Members"=>"Student Details","Family / Student"=>"Family",
            "Last Activity"=>"Last Advance Activity",_=>column.Header
        }
    }).ToArray();
    protected override void OnReportPageBound()
    {
        base.OnReportPageBound();
        Grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
        Grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.DisplayedCells;
        Grid.ColumnHeadersDefaultCellStyle.WrapMode=DataGridViewTriState.True;
        Grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        foreach(DataGridViewColumn column in Grid.Columns)
            column.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
        Grid.AutoResizeRows(DataGridViewAutoSizeRowsMode.DisplayedCells);
    }
    protected override bool AppendReportTotals => false;
    protected override IEnumerable<SqlParameter> BuildParameters()
    {
        yield return DatabaseHelper.Int("@UserId",CurrentUser.UserId);
        yield return DatabaseHelper.Int("@CampusId",Campus.SelectedId);
        yield return DatabaseHelper.Int("@ClassId",Class.SelectedId);
        yield return DatabaseHelper.Int("@SectionId",Section.SelectedId);
    }
    protected override async Task<DataSet> ReadReportAsync(SqlParameter[] parameters, CancellationToken token)
    {
        var rows = await Task.Run(() => DatabaseHelper.QuerySql(CreditBalanceReportData.Query,parameters),token);
        var result = new DataSet(); result.Tables.Add(rows); return result;
    }
    protected override Func<DataTable, DataTable?, (DataTable Rows, DataTable? Totals)> CaptureResultTransform(SqlParameter[] parameters)
    {
        bool families = ViewName == "Families"; decimal amount = minimum.Value;
        return (rows, _) => { var result = CreditBalanceReportData.Create(rows,families,amount); return (result.Rows,result.Totals); };
    }
}
