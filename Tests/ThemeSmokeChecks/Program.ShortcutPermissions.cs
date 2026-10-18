using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Dashboard;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Profile;

internal static partial class Program
{
    private static void CheckShortcutPermissions()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName,
            CurrentUser.MustChangePassword, CurrentUser.GetPermissions().ToArray());
        IEnumerable<Control> Children(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Children(c)));
        try
        {
            foreach (bool reportAccess in new[] { false, true })
            {
                var permissions = reportAccess ? new[] { "Students.View", "Reports.View", "Fees.View", "Fees.Statement.View", "Fees.Collect" } : new[] { "Students.View" };
                CurrentUser.SignIn(901, "Fixture", "Accountant", false, permissions);
                using var dashboard = new WelcomePage(_ => { }, null, (_, _) => Task.FromResult(new DataTable()), _ => Task.FromResult(new DataTable()));
                Check(Children(dashboard).OfType<ModernButton>().Any(b => b.Text == "Student Reports") == reportAccess,
                    "Dashboard Student Reports requires student and report access together");
                int opens = 0;
                using var profile = new StudentProfilePage((_, _, _) => opens++, false);
                ((StudentPicker)typeof(StudentProfilePage).GetField("student", flags)!.GetValue(profile)!).SetStudent(41, "Synthetic student");
                typeof(StudentProfilePage).GetMethod("UpdateStudentShortcuts", flags)!.Invoke(profile, null);
                var buttons = Children(profile).OfType<ModernButton>().ToArray();
                ModernButton Button(string caption) => buttons.Single(b => b.Text == caption);
                Check(!Button("Manage Photo").Enabled && !Button("Open Family / Siblings").Enabled,
                    "Viewing-only account is not offered unauthorized photo or family management");
                Check(Button("Open " + PageTitles.StudentRecordChecks).Enabled && Button("Open Admissions").Enabled,
                    "Student viewing retains admission-detail and read-only data-review destinations");
                Check(Button("Open " + PageTitles.AnnualFeeReceipts).Enabled == reportAccess,
                    "Annual receipts follow their actual statement permission");
                typeof(Control).GetMethod("OnClick", flags)!.Invoke(Button("Manage Photo"), new object[] { EventArgs.Empty });
                Check(opens == 0, "Direct action dispatch cannot bypass profile destination permission");
                Button("Open " + PageTitles.StudentRecordChecks).PerformClick();
                Check(opens == 1, "A permitted read-only review shortcut still dispatches normally");
                CurrentUser.SignIn(902, "Family viewer", "Accountant", false, new[] { "Students.View", "Students.Family" });
                typeof(StudentProfilePage).GetMethod("UpdateStudentShortcuts", flags)!.Invoke(profile, null);
                Check(Button("Open Family / Siblings").Enabled && !Button("Manage Photo").Enabled,
                    "Family viewing does not require or imply photo administration");
            }
            CurrentUser.SignIn(903, "Fixture admin", "Administrator", false, Array.Empty<string>());
            CheckProfileShortcutContext();
        }
        finally { CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName, previous.MustChangePassword, previous.Item5); }
    }
}
