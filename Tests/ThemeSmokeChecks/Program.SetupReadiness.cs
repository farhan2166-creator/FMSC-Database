using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Setup;

internal static partial class Program
{
    private static void CheckSetupReadinessActions()
    {
        var prior = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName,
            CurrentUser.MustChangePassword, Permissions: CurrentUser.GetPermissions().ToArray());
        var pending = new TaskCompletionSource<DataTable>(TaskCreationOptions.RunContinuationsAsynchronously);
        DataTable Issues()
        {
            var data = new DataTable(); data.Columns.Add("Area"); data.Columns.Add("Issue"); data.Columns.Add("PageKey");
            data.Rows.Add("Profile", "Fictional profile missing", "Profile");
            data.Rows.Add("Fees", "Fictional rates missing", "Fees");
            return data;
        }
        try
        {
            CurrentUser.SignIn(7777, "Readiness fixture", "Operator", false, new[] { "System.Setup" });
            int navigations = 0; string destination = "";
            using var page = new SetupReadinessPage((_, _, title) => { navigations++; destination = title; }, () => pending.Task, false);
            using var window = new Form { ClientSize = new Size(1050, 700) };
            window.Controls.Add(page); window.Show(); Application.DoEvents();
            var children = MessageChildren(page).ToArray();
            var open = children.OfType<ModernButton>().Single(button => button.Text == "Open Selected Setup");
            var check = children.OfType<ModernButton>().Single(button => button.Text == "Check Again");
            var grid = children.OfType<DataGridView>().Single();
            Check(!open.Enabled, "Setup navigation is disabled before a finding is selected");
            check.PerformClick();
            Check(!open.Enabled, "Setup navigation remains disabled during an incomplete check");
            pending.SetResult(Issues());
            PumpUntil(() => check.Enabled && grid.Rows.Count == 2, "Readiness check completes with fictional findings");
            void Select(string key)
            {
                var row = grid.Rows.Cast<DataGridViewRow>().Single(item => Convert.ToString(item.Cells["PageKey"].Value) == key);
                grid.CurrentCell = row.Cells["Area"]; Application.DoEvents();
            }
            Select("Fees");
            Check(!open.Enabled && open.AccessibleDescription!.Contains("administrator", StringComparison.OrdinalIgnoreCase), "Unavailable setup destinations are disabled with an accessible explanation");
            var doubleClick = typeof(DataGridView).GetMethod("OnCellDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
            doubleClick.Invoke(grid, new object[] { new DataGridViewCellEventArgs(grid.Columns["Area"].Index, grid.CurrentRow!.Index) });
            Check(navigations == 0, "Double-click cannot bypass a destination permission");
            Select("Profile"); open.PerformClick();
            Check(navigations == 1 && destination == "School Profile", "An allowed destination opens from the selected finding");
            CurrentUser.SignIn(7777, "Readiness fixture", "Administrator", false, Array.Empty<string>());
            Select("Fees"); open.PerformClick();
            Check(navigations == 2 && destination == PageTitles.FeeRateSetup, "Administrator retains full setup authority without explicit grants");
            pending = new TaskCompletionSource<DataTable>(TaskCreationOptions.RunContinuationsAsynchronously);
            check.PerformClick(); Select("Profile");
            open.Enabled = true; // Exercise stale enabled-state restoration while the read is pending.
            Check(!open.Enabled, "Selection or state restoration cannot re-enable navigation during a check");
            doubleClick.Invoke(grid, new object[] { new DataGridViewCellEventArgs(grid.Columns["Area"].Index, grid.CurrentRow!.Index) });
            Check(navigations == 2, "A double-click during refresh cannot navigate using old findings");
            pending.SetResult(Issues().Clone());
            PumpUntil(() => check.Enabled && grid.Rows.Count == 0, "Empty readiness check replaces old findings");
            Check(!open.Enabled, "An empty result leaves no enabled dead-end navigation action");
        }
        finally { CurrentUser.SignIn(prior.UserId, prior.Username, prior.RoleName, prior.MustChangePassword, prior.Permissions); }
    }
}
