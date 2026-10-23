using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Pages.Reports.Finance;

public sealed class MonthlyFeeReconciliationPage : CanonicalFinanceReportPage
{
    public MonthlyFeeReconciliationPage() : base("Monthly Fee Reconciliation",
        "Billing months with current settlements, including later payments. Money allocated is net of refunds and corrections; advance credit is separate. Location follows the charge's enrollment.",
        "MonthlyFeeReconciliation","FMSC_Monthly_Fee_Reconciliation",new[] { "Monthly Summary" },
        MonthlyFeeReconciliationData.Groupings,true,true,false,true)
    {
        foreach (var picker in new[] { From,To })
        {
            picker.CustomFormat="MMM yyyy";
            picker.Value=new DateTime(picker.Value.Year,picker.Value.Month,1);
            picker.Checked=false;
            picker.ValueChanged += (_,_) => {
                var month=new DateTime(picker.Value.Year,picker.Value.Month,1);
                if (picker.Value!=month)
                {
                    bool useDate=picker.Checked;
                    picker.Value=month;
                    picker.Checked=useDate;
                }
            };
        }
    }
    protected override bool IsSummaryView => true;
    protected override bool AppendReportTotals => false;
    protected override string AdditionalFilterDescription => "Whole billing months; missing billing month uses charge date. Current settlements across all payment dates. Net fees + excess settlement = money allocated + credit applied + outstanding.";
    protected override IEnumerable<SqlParameter> BuildParameters()
    {
        yield return DatabaseHelper.Int("@UserId",CurrentUser.UserId);
        yield return DatabaseHelper.P("@FromDate",From.Checked ? From.Value.Date : null);
        yield return DatabaseHelper.P("@ToDate",To.Checked ? To.Value.Date : null);
        yield return DatabaseHelper.Int("@AcademicYearId",Year.SelectedId);
        yield return DatabaseHelper.Int("@CampusId",Campus.SelectedId);
        yield return DatabaseHelper.Int("@ClassId",Class.SelectedId);
        yield return DatabaseHelper.Int("@SectionId",Section.SelectedId);
    }
    protected override async Task<DataSet> ReadReportAsync(SqlParameter[] parameters,CancellationToken token)
    {
        var rows=await Task.Run(() => DatabaseHelper.QuerySql(MonthlyFeeReconciliationData.Query,parameters),token);
        var result=new DataSet(); result.Tables.Add(rows); return result;
    }
    protected override Func<DataTable,DataTable?,(DataTable Rows,DataTable? Totals)> CaptureResultTransform(SqlParameter[] parameters)
    {
        string grouping=GroupName;
        return (rows,_) => MonthlyFeeReconciliationData.Create(rows,grouping);
    }
}
