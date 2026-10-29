using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Reports.Finance;

internal static partial class Program
{
    private sealed class StatementCheckIdentity : IDisposable
    {
        private readonly (int Id,string Name,string Role,bool MustChange,string[] Permissions) previous=
            (CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,CurrentUser.GetPermissions().ToArray());
        internal StatementCheckIdentity()=>CurrentUser.SignIn(924,"Statement fixture","Operator",false,new[]{"Reports.View","Fees.View"});
        public void Dispose()=>CurrentUser.SignIn(previous.Id,previous.Name,previous.Role,previous.MustChange,previous.Permissions);
    }
    private static Task<StudentFeeAccountData> ReadEmptyFeeAccount(int admissionId) =>
        Task.FromResult(new StudentFeeAccountData(new DataTable(),new DataTable(),0m,0m));

    private static void CheckStatementRegenerationFreshness()
    {
        using var identity=new StatementCheckIdentity();
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(StudentAccountStatementPage);
        using var page=new StudentAccountStatementPage(null) { ReadAccount=ReadEmptyFeeAccount };
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
        using var identity=new StatementCheckIdentity();
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(StudentAccountStatementPage);
        var pending=new Queue<TaskCompletionSource<(DataTable,decimal)>>();
        Func<int,DateTime?,DateTime?,Task<(DataTable Transactions,decimal OpeningBalance)>> read=(id,_,_)=>{var reply=new TaskCompletionSource<(DataTable,decimal)>();pending.Enqueue(reply);return reply.Task;};
        using var page=new StudentAccountStatementPage(read) { ReadAccount=ReadEmptyFeeAccount };page.BindingContext=new BindingContext();
        using var dialog=(ApplicationWindowForm)type.GetMethod("CreateRelatedReportDialog",BindingFlags.Static|BindingFlags.NonPublic)!
            .Invoke(null,new object[]{PageTitles.StudentFeeAccount,page})!;
        var selected=(HashSet<int>)type.GetField("manuallySelectedAdmissionIds",flags)!.GetValue(page)!;selected.Add(1);((Dictionary<int,HashSet<int>>)type.GetField("familySelections",flags)!.GetValue(page)!)[1]=new(){1,2};
        DataTable Rows(int id){var rows=new DataTable();rows.Columns.Add("StudentName");rows.Columns.Add("TransactionType");rows.Columns.Add("TransactionId",typeof(long));rows.Columns.Add("TransactionDate",typeof(DateTime));rows.Columns.Add("DebitAmount",typeof(decimal));rows.Columns.Add("PaidAmount",typeof(decimal));rows.Rows.Add("Student "+id,"CHARGE",id,DateTime.Today,100m,0m);rows.Rows.Add("Student "+id,"PAYMENT",id+10,DateTime.Today,0m,40m);return rows;}
        var task=(Task)type.GetMethod("LoadStatement",flags)!.Invoke(page,null)!;
        Check(!task.IsCompleted&&page.IsBusy&&!page.Enabled,"Statement generation returns while SQL is pending and holds a stable selection");
        var closing=new FormClosingEventArgs(CloseReason.UserClosing,false);
        typeof(Form).GetMethod("OnFormClosing",flags)!.Invoke(dialog,new object[]{closing});
        Check(closing.Cancel,"A related statement window cannot close while its report is being assembled");
        pending.Dequeue().SetResult((Rows(1),10m));PumpUntil(()=>pending.Count==1,"Second statement read begins only after first assembly");
        pending.Dequeue().SetResult((Rows(2),10m));PumpUntil(()=>task.IsCompleted,"Complete sibling statement binds");
        Check(task.IsCompletedSuccessfully&&!page.IsBusy&&page.Enabled,"Async statement releases its operation guard after complete binding");
        var balance=(Label)type.GetField("closingBalance",flags)!.GetValue(page)!;var grid=(DataGridView)typeof(V2Page).GetField("Grid",flags)!.GetValue(page)!;
        Check(balance.Text=="140"&&grid.Rows.Count==2,"Off-thread assembly preserves opening, charge, payment totals and excludes duplicate payment display rows");
        Check(((ModernButton)type.GetField("exportButton",flags)!.GetValue(page)!).Enabled,"Only a complete current async statement becomes exportable");
    }

}
