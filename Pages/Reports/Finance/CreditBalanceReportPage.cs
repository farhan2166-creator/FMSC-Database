using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Reports.Finance;

public sealed class CreditBalanceReportPage : CanonicalFinanceReportPage
{
    private readonly ModernNumericTextBox minimum = new() { Minimum = 0, Maximum = 1000000000m, ZeroAsPlaceholder = true };
    public CreditBalanceReportPage() : base("Student Credit Balance Register",
        "Advance credit held for students. This is not outstanding fees or new income. Location filters use the current or latest placement.",
        "CreditBalanceRegister", "FMSC_Credit_Balances", new[] { "Students", "Families" }, Array.Empty<string>(),false,false,false,false)
    {
        minimum.TextChanged += (_, _) => FiltersChanged();
    }
    protected override IEnumerable<(string, Control)> AdditionalReportFilters() => new[] { ("Minimum credit per student (Rs.)",(Control)minimum) };
    protected override string AdditionalFilterSignature => NumericFilterSignature(minimum.Text);
    protected override string AdditionalFilterDescription => $"Minimum credit per student: Rs. {minimum.Value:N0}";
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
