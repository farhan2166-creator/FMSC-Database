using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Maintenance;

internal static partial class Program
{
    private sealed class ToolbarFixture : V2Page
    {
        internal readonly ModernButton Link, Print;
        internal ToolbarFixture() : base("Registration fixture", "")
        {
            Link=AddAction("Link Existing Parent",(_,_)=>{});
            Print=AddAction("Print Bill",(_,_)=>{});
            SetContent(new Panel());
        }
        internal void ChangeWhileHidden()=>SetStatus("Registration loaded while pending list is visible.");
    }
    private static void CheckHiddenRegistrationToolbar()
    {
        using var window=new Form{ClientSize=new Size(960,640)};
        using var page=new ToolbarFixture{Dock=DockStyle.Fill};window.Controls.Add(page);window.Show();Application.DoEvents();
        Check(page.Link.Visible&&page.Print.Visible,"Registration fixture initially shows its actions");
        page.Hide();page.ChangeWhileHidden();page.Show();Application.DoEvents();
        Check(page.Link.Visible&&page.Print.Visible&&page.Link.Parent!.Height>=page.Link.Height,
            "Loading status in a hidden registration tab cannot collapse its actions on return");
        window.Close();
    }
    private static void CheckPendingSelection()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var page=new AdmissionReviewListPage((_,_,_)=>{},true);
        page.BindingContext=new BindingContext();
        var grid=(DataGridView)typeof(V2Page).GetField("Grid",flags)!.GetValue(page)!;
        var confirm=(ModernButton)typeof(AdmissionReviewListPage).GetField("reviewRegistration",flags)!.GetValue(page)!;
        using var rows=new DataTable();rows.Columns.Add("AdmissionId",typeof(int));rows.Rows.Add(41);rows.Rows.Add(42);
        grid.DataSource=new DataView(rows);
        typeof(AdmissionReviewListPage).GetMethod("ClearSelection",flags)!.Invoke(page,null);
        Check(!confirm.Enabled&&grid.CurrentCell is null,"A refreshed pending grid starts without a confirmation target");
        grid.CurrentCell=grid.Rows[1].Cells[0];grid.Rows[1].Selected=true;
        Check(confirm.Enabled,"An explicitly selected pending registration enables review");
        grid.ClearSelection();
        Check(grid.CurrentRow is not null&&!confirm.Enabled,"A stale CurrentRow cannot enable confirmation when selection is cleared");
        grid.Rows[1].Selected=true;
        typeof(Control).GetMethod("OnMouseDown",flags)!.Invoke(page.Controls[0],new object[]{new MouseEventArgs(MouseButtons.Left,1,0,0,0)});
        Check(!confirm.Enabled&&grid.SelectedRows.Count==0,"A click outside the grid clears the visible confirmation target");
        Check(!page.IsHandleCreated,"Pending-selection fixture never starts its real SQL loader");
    }
    private static void CheckQuickRegistrationTabGuard()
    {
        var previous=(FMSC_SMS.Pages.Security.CurrentUser.UserId,FMSC_SMS.Pages.Security.CurrentUser.Username,FMSC_SMS.Pages.Security.CurrentUser.RoleName,FMSC_SMS.Pages.Security.CurrentUser.MustChangePassword,Permissions:FMSC_SMS.Pages.Security.CurrentUser.GetPermissions().ToArray());
        try
        {
            FMSC_SMS.Pages.Security.CurrentUser.SignIn(8881,"Fixture","Administrator",false,Array.Empty<string>());
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var type=typeof(FMSC_SMS.Pages.Students.Admissions.QuickRegistrationPage);
            using var page=new FMSC_SMS.Pages.Students.Admissions.QuickRegistrationPage((_,_,_)=>{},0,false);
            var tabs=(TabControl)type.GetField("tabs",flags)!.GetValue(page)!;
            var form=(Control)type.GetField("form",flags)!.GetValue(page)!;var formType=form.GetType();
            TabControlCancelEventArgs Attempt(int target=1)
            {
                var args=new TabControlCancelEventArgs(tabs.TabPages[target],target,false,TabControlAction.Selecting);
                typeof(TabControl).GetMethod("OnSelecting",flags)!.Invoke(tabs,new object[]{args});return args;
            }
            foreach(int student in new[]{0,41})
            {
                formType.GetField("savedStudent",flags)!.SetValue(form,student);
                formType.GetField("busy",flags)!.SetValue(form,true);
                Check(page.IsBusy&&Attempt().Cancel&&tabs.SelectedIndex==0,"New and saved registration write states refuse internal tab selection without moving context");
                formType.GetField("busy",flags)!.SetValue(form,false);
                Check(!Attempt().Cancel,"A settled registration operation permits tab navigation again");
            }
            foreach(string loading in new[]{"loadingFees","loadingFamily"})
            {
                formType.GetField(loading,flags)!.SetValue(form,true);Check(Attempt().Cancel,"Fee/family reads retain their tab context until settled");formType.GetField(loading,flags)!.SetValue(form,false);
            }
            formType.GetField("recoveryBlocked",flags)!.SetValue(form,true);
            Check(!page.IsBusy&&page.HasUnsavedChanges&&Attempt().Cancel,"Unknown save outcomes block sibling workflows while remaining a retained recovery draft for dashboard leave");
            Check(!Attempt(0).Cancel,"An unresolved record still permits returning to Registration Form to check its exact outcome");
            formType.GetField("recoveryBlocked",flags)!.SetValue(form,false);
            using var child=new ReturnBusyFixture();tabs.TabPages[1].Controls.Add(child);child.Active=true;
            Check(page.IsBusy&&Attempt().Cancel,"The registration host aggregates busy contracts from every loaded child");child.Active=false;
            Check(!Attempt().Cancel&&!page.IsHandleCreated&&!form.IsHandleCreated,"Clean tab decisions require no SQL, profile recovery files or native page handle");
        }
        finally{FMSC_SMS.Pages.Security.CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}
    }
    private sealed class ReturnBusyFixture:UserControl,IBusyPage
    {
        internal bool Active;public bool IsBusy=>Active;public string BusyMessage=>"Wait for the pending registration operation.";
    }

}
