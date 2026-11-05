using System.Reflection;
using FMSC_SMS;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using DashboardNavButton = FMSC_SMS.MainDashboardForm.DashboardNavButton;

internal static partial class Program
{
    private static void CheckSidebarGroups()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName,
            CurrentUser.MustChangePassword, CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(990, "Navigation fixture", "Administrator", false, Array.Empty<string>());
            using var shell = new FMSC_SMS.MainDashboardForm((_, _) => { });
            var type = shell.GetType();
            void VerifyMenus()
            {
                var sidebar = (Panel)type.GetField("sidebar", flags)!.GetValue(shell)!;
                var headers = DescendantControls(sidebar).OfType<DashboardNavButton>().Where(b => b.IsModule).ToArray();
                Check(headers.Take(3).Select(b => b.Text).SequenceEqual(new[] { "Daily Work", "Fees", "Students" }),
                    "Fees and Students follow Daily Work in the main navigation");
                string[] Links(string title) => headers.Single(b => b.Text == title).Parent!.Controls
                    .OfType<FlowLayoutPanel>().Single().Controls.OfType<DashboardNavButton>().Select(b => b.Text).ToArray();
                Check(Links("Daily Work").SequenceEqual(new[] { PageTitles.Admissions, "Pending Registrations", "Student Profile",
                    "Fee Collection", PageTitles.OutstandingBills, "Daily Collection Closing" }),
                    "Daily Work stays limited to six shortcuts and excludes billing/posting");
                Check(Links("Fees").Contains(PageTitles.FeePosting) && Links("Fees").Contains(PageTitles.FeeRateSetup),
                    "Billing and fee setup have their home in Fees");
                Check(new[] { PageTitles.Admissions, "Student Profile", PageTitles.StudentRecordChecks,
                    PageTitles.StudentCertificates, "Family / Siblings", PageTitles.AnnualPromotion, PageTitles.PlacementHistory }
                    .All(Links("Students").Contains), "Student and placement work is grouped under Students");
                var commands = (List<NavigationCommand>)type.GetField("navigationCommands", flags)!.GetValue(shell)!;
                Check(commands.Select(c => c.Caption).Distinct().Count() == commands.Count,
                    "Daily aliases do not duplicate command-palette destinations");
                Check(commands.Count(c => c.Caption == "Fee Collection") == 1 &&
                    commands.Count(c => c.Caption == PageTitles.Admissions) == 1,
                    "Common shortcuts share one route command");
                var targets = (Dictionary<Keys, DashboardNavButton>)type.GetField("navigationShortcutTargets", flags)!.GetValue(shell)!;
                Check(targets[Keys.Alt | Keys.D3].Text == "Fees" && targets[Keys.Alt | Keys.D4].Text == "Students",
                    "Fees and Students have distinct navigation shortcuts");
            }
            VerifyMenus();
            type.GetMethod("RebuildSidebar", flags)!.Invoke(shell, null);
            VerifyMenus();
            Check(!shell.IsHandleCreated, "Sidebar checks never start application database loaders");
            CurrentUser.SignIn(991, "Restricted fixture", "Operator", false, Array.Empty<string>());
            using var restricted = new FMSC_SMS.MainDashboardForm((_, _) => { });
            var restrictedCommands = (List<NavigationCommand>)type.GetField("navigationCommands", flags)!.GetValue(restricted)!;
            Check(!restrictedCommands.Any(c => c.Caption is "Fee Collection" or PageTitles.FeePosting or PageTitles.Admissions),
                "Neither full menus nor daily aliases expose unauthorized destinations");
        }
        finally { CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName, previous.MustChangePassword, previous.Item5); }
    }
}
