using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Dashboard;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Profile;
using FMSC_SMS.Pages.Reports.Finance;

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
            // Student Fee Account has two alternative entry permissions, but its
            // GetStudents-backed picker requires Students.View in every scenario.
            for (int mask=0;mask<8;mask++)
            {
                bool students=(mask&1)!=0, reports=(mask&2)!=0, collect=(mask&4)!=0;
                var grants=new List<string>();
                if(students)grants.Add("Students.View");
                if(reports)grants.Add("Reports.View");
                if(collect)grants.Add("Fees.Collect");
                CurrentUser.SignIn(970+mask,"Account access fixture","Accountant",false,grants);
                bool expected=students&&(reports||collect);
                Check(StudentAccountStatementPage.CanOpenAccount==expected,
                    "Account entry needs Students.View plus Reports.View or Fees.Collect");
                using(var home=new WelcomePage(_=>{},null,_=>Task.FromResult(new DataTable()),_=>Task.FromResult(new DataTable())))
                    Check(Children(home).OfType<ModernButton>().Any(b=>b.Text==PageTitles.StudentFeeAccount)==expected,
                        "Dashboard account shortcut matches the full account access rule");
                if(mask is 2 or 5)
                {
                    using var shell=new FMSC_SMS.MainDashboardForm((_,_)=>{});
                    var commands=(List<NavigationCommand>)typeof(FMSC_SMS.MainDashboardForm)
                        .GetField("navigationCommands",flags)!.GetValue(shell)!;
                    Check(commands.Any(c=>c.Caption==PageTitles.StudentFeeAccount)==expected,
                        "Sidebar account entry matches the full account access rule");
                }
            }
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
            // Certificate types have separate SQL write/read authority, while the
            // student picker and detail readers need Students.View for both.
            foreach(var scenario in new (string Name, string[] Grants, int General, int Leaving)[] {
                ("Nothing", Array.Empty<string>(), 0, 0),
                ("Student viewer", new[]{"Students.View"}, 0, 0),
                ("General without student reads", new[]{"Certificates.Create"}, 0, 0),
                ("Leaving without student reads", new[]{"Students.Leave"}, 0, 0),
                ("Ordinary certificates", new[]{"Students.View","Certificates.Create"}, 6, 0),
                ("Leaving certificates", new[]{"Students.View","Students.Leave"}, 0, 2),
                ("Both categories", new[]{"Students.View","Students.Leave","Certificates.Create"}, 6, 2)
            }){
                CurrentUser.SignIn(910,scenario.Name,"Accountant",false,scenario.Grants);
                bool hubAllowed=scenario.General+scenario.Leaving>0;
                Check(CertificateWorkspaceAccess.CanOpenHub(CurrentUser.HasPermission)==hubAllowed,
                    scenario.Name+": certificate hub requires a usable SQL-backed category");
                using var dashboard = new WelcomePage(_ => { }, null, (_, _) => Task.FromResult(new DataTable()), _ => Task.FromResult(new DataTable()));
                Check(Children(dashboard).OfType<ModernButton>().Any(b => b.Text == PageTitles.StudentCertificates)==hubAllowed,
                    scenario.Name+": dashboard certificate shortcut follows SQL-required permissions");
                using var hub = new FMSC_SMS.Pages.Students.Certificates.StudentCertificatesPage((_,_,_)=>{});
                var displayed=Children(hub).OfType<Label>()
                    .Where(label=>label.AccessibleName?.EndsWith(" certificate",StringComparison.Ordinal)==true)
                    .Select(label=>label.AccessibleName!).ToArray();
                Check(displayed.Length==scenario.General+scenario.Leaving &&
                      displayed.Contains("Leave certificate")== (scenario.Leaving>0) &&
                      displayed.Contains("College Leaving certificate")== (scenario.Leaving>0) &&
                      displayed.Contains("Regular Student certificate")== (scenario.General>0),
                    scenario.Name+": only SQL-authorized certificate cards appear");
                Check(!hub.IsHandleCreated,"Certificate card access checks do not start student lookups");
            }
            int certificateOpens=0;
            CurrentUser.SignIn(911,"Leaving operator","Accountant",false,new[]{"Students.View","Students.Leave"});
            using(var hub=new FMSC_SMS.Pages.Students.Certificates.StudentCertificatesPage((_,_,_)=>certificateOpens++))
            {
                var open=typeof(FMSC_SMS.Pages.Students.Certificates.StudentCertificatesPage).GetMethod("Open",flags)!;
                Func<Control> neverBuild=()=>throw new InvalidOperationException("Navigation must not instantiate a certificate page");
                open.Invoke(hub,new object[]{neverBuild,"Leave",true});
                Check(certificateOpens==1,"Leaving-only authority can navigate to a leaving certificate");
                CurrentUser.SignIn(912,"General operator","Accountant",false,new[]{"Students.View","Certificates.Create"});
                open.Invoke(hub,new object[]{neverBuild,"Leave",true});
                Check(certificateOpens==1,"A stale certificate card cannot dispatch after losing leaving permission");
                open.Invoke(hub,new object[]{neverBuild,"Regular Student",false});
                Check(certificateOpens==2,"An authorized ordinary certificate can still dispatch");
            }
            CurrentUser.SignIn(903, "Fixture admin", "Administrator", false, Array.Empty<string>());
            CheckProfileShortcutContext();
        }
        finally { CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName, previous.MustChangePassword, previous.Item5); }
    }
}
