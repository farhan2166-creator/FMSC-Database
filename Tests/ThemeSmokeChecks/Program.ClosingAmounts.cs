using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;

internal static partial class Program
{
    private static void CheckClosingWholeAmounts()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var page = new DailyCollectionClosingPage();
        using var amounts = new DataTable(); amounts.Columns.Add("Payment Method");
        foreach (string column in new[] { "Recorded Amount", "Actual Amount", "Difference" }) amounts.Columns.Add(column, typeof(decimal));
        amounts.Rows.Add("Cash", 100m, 100m, 0m);
        page.BindingContext = new BindingContext();
        var grid = (DataGridView)typeof(V2Page).GetField("Grid", flags)!.GetValue(page)!;
        grid.DataSource = amounts; grid.ReadOnly = false; grid.Columns["Actual Amount"].ReadOnly = false;
        bool Refused(string text)
        {
            var args = (DataGridViewCellValidatingEventArgs)Activator.CreateInstance(typeof(DataGridViewCellValidatingEventArgs), flags, null, new object[] { grid.Columns["Actual Amount"].Index, 0, text }, null)!;
            typeof(DataGridView).GetMethod("OnCellValidating", flags)!.Invoke(grid, new object[] { args }); return args.Cancel;
        }
        Check(Refused("100.50") && Refused("100.999"), "Closing grid refuses fractions without rounding");
        Check(!Refused("100") && !Refused("0") && !Refused(""), "Closing grid accepts whole amounts, explicit zero and unchecked blank");
        Check(!Refused("-10"), "Whole-rupee validation preserves existing signed net semantics");
        // Defense at Submit protects programmatically supplied or previously
        // stored fractions even if grid validation did not run.
        var type = page.GetType();
        type.GetField("amounts", flags)!.SetValue(page, amounts);
        type.GetField("loadedDay", flags)!.SetValue(page, DateTime.Today);
        using var status = new DataTable(); status.Columns.Add("Unused"); status.Rows.Add("Synthetic");
        type.GetField("state", flags)!.SetValue(page, status.Rows[0]);
        amounts.Rows[0]["Actual Amount"] = 100.50m;
        var pending = (Task)type.GetMethod("SubmitAsync", flags)!.Invoke(page, null)!;
        Check(pending.IsCompletedSuccessfully && !page.IsBusy && (decimal)amounts.Rows[0]["Actual Amount"] == 100.50m,
            "Submit rejects a fractional actual before SQL and leaves the original value for correction");
    }
}
