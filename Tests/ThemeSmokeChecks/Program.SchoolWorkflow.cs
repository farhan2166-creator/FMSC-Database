using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Billing;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Admissions;

internal static partial class Program
{
    private static void CheckSchoolWorkflow()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName, CurrentUser.MustChangePassword,
            Permissions: CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(7777, "School workflow fixture", "Administrator", false, Array.Empty<string>());
            using var admission = (WorkspacePage)AdmissionsWorkspace.Create((_, _, _) => { });
            var tabs = (TabControl)typeof(WorkspacePage).GetField("_tabs", flags)!.GetValue(admission)!;
            Check(tabs.TabPages.Cast<TabPage>().Select(t => t.Text).SequenceEqual(new[] {
                "Admit / Edit Student", "Registration & Pending", "Existing Student Setup" }),
                "An administrator can reach direct admission, pending registrations and existing-student activation from one workspace");
            Check(!admission.IsHandleCreated, "Admission route inspection starts no SQL loaders");

            var data = new DataSet();
            var summary = data.Tables.Add();
            summary.Columns.Add("BillingMonth", typeof(DateTime)); summary.Columns.Add("CurrentCharges", typeof(int)); summary.Columns.Add("CurrentAmount", typeof(decimal));
            summary.Rows.Add(DateTime.Today, 0, 0m);
            data.Tables.Add();
            var students = data.Tables.Add(); students.Columns.Add("AdmissionId", typeof(int)); students.Columns.Add("IsLegacyReference", typeof(bool)); students.Columns.Add("StudentName");
            students.Rows.Add(42, false, "Fictional Student");
            string? destination = null;
            using var status = new BillingStatusPage((_, _, title) => destination = title, () => Task.FromResult(data), false);
            typeof(BillingStatusPage).GetField("review", flags)!.SetValue(status, data);
            var view = (ModernComboBox)typeof(BillingStatusPage).GetField("view", flags)!.GetValue(status)!;
            using var window = new Form { ClientSize = new Size(1100, 700) };
            window.Controls.Add(status); window.Show(); Application.DoEvents();
            view.SelectedIndex = 1;
            var grid = (DataGridView)typeof(V2Page).GetField("Grid", flags)!.GetValue(status)!;
            status.BindingContext = new BindingContext(); grid.BindingContext = status.BindingContext;
            grid.DataSource = students.DefaultView;
            if (grid.Rows.Count > 0) grid.CurrentCell = grid.Rows[0].Cells["StudentName"];
            typeof(BillingStatusPage).GetMethod("OpenStudent", flags)!.Invoke(status, new object[] { false });
            Check(destination == PageTitles.Admissions, "A billing exception opens the selected student's admission setup without starting a write");
            destination = null;
            typeof(BillingStatusPage).GetMethod("OpenStudent", flags)!.Invoke(status, new object[] { true });
            Check(destination == "Fee Collection", "A billing exception can open the selected student's collection account");
            CurrentUser.SignIn(7778, "Read-only fixture", "Accountant", false, new[] { "Fees.View", "Students.View" });
            destination = null;
            typeof(BillingStatusPage).GetMethod("OpenStudent", flags)!.Invoke(status, new object[] { false });
            typeof(BillingStatusPage).GetMethod("OpenStudent", flags)!.Invoke(status, new object[] { true });
            Check(destination is null, "Billing action dispatch rechecks current permissions");
        }
        finally { CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName, previous.MustChangePassword, previous.Permissions); }
    }
}
