using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports.Finance;

internal static partial class Program
{
    private static void CheckStatementRegenerationFreshness()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(StudentAccountStatementPage);
        using var page=new StudentAccountStatementPage();
        var grid=(DataGridView)typeof(V2Page).GetField("Grid",flags)!.GetValue(page)!;
        using var old=new DataTable();old.Columns.Add("Previous statement");old.Rows.Add("Retained snapshot");page.BindingContext=new BindingContext();grid.DataSource=old;
        var export=(ModernButton)type.GetField("exportButton",flags)!.GetValue(page)!;export.Enabled=true;type.GetField("_appliedFilterSignature",flags)!.SetValue(page,"Previously current");
        var from=(ModernDatePicker)type.GetField("from",flags)!.GetValue(page)!;var to=(ModernDatePicker)type.GetField("to",flags)!.GetValue(page)!;
        from.Checked=to.Checked=true;from.Value=DateTime.Today;to.Value=DateTime.Today.AddDays(-1);
        export.Enabled=true;type.GetMethod("LoadStatement",flags)!.Invoke(page,null);
        Check(!export.Enabled&&(string)type.GetField("_appliedFilterSignature",flags)!.GetValue(page)! == "","A refused regeneration invalidates the previous statement instead of leaving Export current");
        Check(ReferenceEquals(grid.DataSource,old),"The old statement remains visible for reference while its freshness is invalidated");
    }
}
