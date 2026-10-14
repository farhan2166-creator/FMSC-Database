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
        export.Enabled=true;var refused=(Task)type.GetMethod("LoadStatement",flags)!.Invoke(page,null)!;PumpUntil(()=>refused.IsCompleted,"Rejected statement regeneration settles");
        Check(!export.Enabled&&(string)type.GetField("_appliedFilterSignature",flags)!.GetValue(page)! == "","A refused regeneration invalidates the previous statement instead of leaving Export current");
        Check(ReferenceEquals(grid.DataSource,old),"The old statement remains visible for reference while its freshness is invalidated");
    }
    private static void CheckAsyncStatementAssembly()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(StudentAccountStatementPage);
        var pending=new Queue<TaskCompletionSource<(DataTable,decimal)>>();
        Func<int,DateTime?,DateTime?,Task<(DataTable Transactions,decimal OpeningBalance)>> read=(id,_,_)=>{var reply=new TaskCompletionSource<(DataTable,decimal)>();pending.Enqueue(reply);return reply.Task;};
        using var page=(StudentAccountStatementPage)Activator.CreateInstance(type,flags,null,new object[]{false,read},null)!;page.BindingContext=new BindingContext();
        var selected=(HashSet<int>)type.GetField("manuallySelectedAdmissionIds",flags)!.GetValue(page)!;selected.UnionWith(new[]{1,2});
        DataTable Rows(int id){var rows=new DataTable();rows.Columns.Add("StudentName");rows.Columns.Add("TransactionType");rows.Columns.Add("TransactionId",typeof(long));rows.Columns.Add("TransactionDate",typeof(DateTime));rows.Columns.Add("DebitAmount",typeof(decimal));rows.Columns.Add("PaidAmount",typeof(decimal));rows.Rows.Add("Student "+id,"CHARGE",id,DateTime.Today,100m,0m);rows.Rows.Add("Student "+id,"PAYMENT",id+10,DateTime.Today,0m,40m);return rows;}
        var task=(Task)type.GetMethod("LoadStatement",flags)!.Invoke(page,null)!;
        Check(!task.IsCompleted&&page.IsBusy&&!page.Enabled,"Statement generation returns while SQL is pending and holds a stable selection");
        pending.Dequeue().SetResult((Rows(1),10m));PumpUntil(()=>pending.Count==1,"Second statement read begins only after first assembly");
        pending.Dequeue().SetResult((Rows(2),10m));PumpUntil(()=>task.IsCompleted,"Complete multi-student statement binds");
        Check(task.IsCompletedSuccessfully&&!page.IsBusy&&page.Enabled,"Async statement releases its operation guard after complete binding");
        var balance=(Label)type.GetField("closingBalance",flags)!.GetValue(page)!;var grid=(DataGridView)typeof(V2Page).GetField("Grid",flags)!.GetValue(page)!;
        Check(balance.Text=="140"&&grid.Rows.Count==2,"Off-thread assembly preserves opening, charge, payment totals and excludes duplicate payment display rows");
        Check(((ModernButton)type.GetField("exportButton",flags)!.GetValue(page)!).Enabled,"Only a complete current async statement becomes exportable");
    }

}
