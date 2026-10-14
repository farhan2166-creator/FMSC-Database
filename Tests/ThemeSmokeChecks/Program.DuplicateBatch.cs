using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Maintenance;

internal static partial class Program
{
    private static void CheckDuplicateBatchSerialization()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var page=new DuplicateStudentAuditPage();page.BindingContext=new BindingContext();
        var type=typeof(DuplicateStudentAuditPage);var grid=(DataGridView)typeof(V2Page).GetField("Grid",flags)!.GetValue(page)!;
        using var rows=new DataTable();rows.Columns.Add("ReviewId",typeof(int));rows.Rows.Add(1);rows.Rows.Add(2);grid.DataSource=rows;grid.Rows[0].Selected=true;
        var update=type.GetMethod("UpdateResolveButtons",flags)!;update.Invoke(page,null);
        var confirm=(ModernButton)type.GetField("_confirmButton",flags)!.GetValue(page)!;var dismiss=(ModernButton)type.GetField("_dismissButton",flags)!.GetValue(page)!;
        Check(confirm.Enabled&&dismiss.Enabled,"A selected duplicate pair initially offers both deliberate decisions");
        var reply=new TaskCompletionSource();Func<Task> operation=()=>reply.Task;
        var pending=(Task<bool>)typeof(V2Page).GetMethod("RunBusyAsync",flags)!.Invoke(page,new object[]{"Batch fixture",operation,new Control[]{page}})!;
        Check(((IBusyPage)page).IsBusy,"Duplicate scan/batch exposes its active state to its workspace");
        grid.Rows[1].Selected=true;update.Invoke(page,null);
        Check(!confirm.Enabled&&!dismiss.Enabled,"Changing selection cannot re-enable either duplicate decision during a batch");
        foreach(var status in new[]{"Confirmed","Dismissed"})
        {
            var refused=(Task)type.GetMethod("ResolveSelectedAsync",flags)!.Invoke(page,new object[]{status})!;
            Check(refused.IsCompletedSuccessfully,"Both decision handlers refuse overlap before prompting or submitting SQL");
        }
        var scan=(Task)type.GetMethod("ScanAsync",flags)!.Invoke(page,null)!;
        Check(scan.IsCompletedSuccessfully,"A duplicate scan cannot overlap a resolution batch");
        reply.SetResult();PumpUntil(()=>pending.IsCompleted,"Duplicate batch fixture settles");update.Invoke(page,null);
        Check(confirm.Enabled&&dismiss.Enabled&&page.Enabled,"Settled batch restores decisions from the actual remaining selection");
        Check(!page.IsHandleCreated,"Duplicate concurrency fixture invokes no SQL scan or resolution");
    }
    private static void CheckStudentReviewBusyHost()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var window=new Form{ClientSize=new Size(960,640)};
        using var host=new StudentDataReviewPage((_,_,_)=>{});
        var tabs=(TabControl)typeof(StudentDataReviewPage).GetField("tabs",flags)!.GetValue(host)!;
        // Supply inert cached pages so the real host runs without starting SQL.
        tabs.TabPages[0].Controls.Add(new Panel());
        var child=new ModalGuardFixture{IsBusy=true};tabs.TabPages[1].Controls.Add(child);
        window.Controls.Add(host);window.Show();Application.DoEvents();
        Check(host.IsBusy&&host.BusyMessage==child.BusyMessage,"Student Review forwards an active operation from every cached child, including a hidden tab");
        tabs.SelectedIndex=1;Application.DoEvents();
        Check(tabs.SelectedIndex==0,"Student Review refuses tab switches while any cached child operation is pending");
        child.IsBusy=false;tabs.SelectedIndex=1;Application.DoEvents();
        Check(tabs.SelectedIndex==1&&!host.IsBusy,"Student Review resumes tab navigation after the cached operation settles");
        window.Close();
    }

}
