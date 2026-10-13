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
}
