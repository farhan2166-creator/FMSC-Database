using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Reports.Finance;
using FMSC_SMS.Pages.Reports;
using FMSC_SMS.Pages.Students.Profile;

internal static partial class Program
{
    private static void CheckStudentAccountFlow()
    {
        Console.WriteLine("ACCOUNT_FLOW: begin native account and family checks");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName,
            CurrentUser.MustChangePassword, CurrentUser.GetPermissions().ToArray());
        IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
            .SelectMany(c => new[] { c }.Concat(Descendants(c)));
        try
        {
            foreach (var access in new[] {
                (Permissions: new[] { "Students.View", "Students.Family" }, FamilyRoute: false),
                (Permissions: new[] { "Students.Family" }, FamilyRoute: true),
                (Permissions: new[] { "Students.View" }, FamilyRoute: false) })
            {
                CurrentUser.SignIn(922, "Navigation fixture", "Operator", false, access.Permissions);
                using var shell = new MainDashboardForm((_, _) => { });
                var commands = (List<NavigationCommand>)typeof(MainDashboardForm).GetField("navigationCommands", flags)!.GetValue(shell)!;
                Check(commands.Any(c => c.Caption == "Family / Siblings") == access.FamilyRoute,
                    "Family navigation is consolidated while preserving accounts that have only family access");
            }
            CurrentUser.SignIn(920, "Account fixture", "Administrator", false, Array.Empty<string>());
            using(var reports=new ReportsHomePage((Action<Func<Control>,string,string>)((_,_,_)=>{})))
                foreach(string title in new[]{PageTitles.FamilyGroupStatements,PageTitles.AnnualFeeReceipts,PageTitles.OutstandingBills})
                    Check(Descendants(reports).Any(control=>control.Text==title),"Reports provides a dedicated entry for "+title);
            int calls = 0, readId = 0;
            var reply = new TaskCompletionSource<(DataTable, decimal)>();
            Func<int, DateTime?, DateTime?, Task<(DataTable Transactions, decimal OpeningBalance)>> read = (id, _, _) => {
                calls++; readId = id; return reply.Task;
            };
            var type = typeof(StudentAccountStatementPage);
            using var page = (StudentAccountStatementPage)Activator.CreateInstance(type, flags, null, new object[] { false, read }, null)!;
            T Field<T>(string name) => (T)type.GetField(name, flags)!.GetValue(page)!;
            object? Invoke(string name) => type.GetMethod(name, flags)!.Invoke(page, null);
            using var owner = new Form { ClientSize = new Size(1200, 720), AutoScaleMode = AutoScaleMode.None };
            owner.Controls.Add(page); owner.Show(); Application.DoEvents();
            Console.WriteLine("ACCOUNT_FLOW: SQL-free statement is visible");
            var grid = (DataGridView)typeof(V2Page).GetField("Grid", flags)!.GetValue(page)!;
            var picker = Field<StudentPicker>("studentSearch");
            var selected = Field<HashSet<int>>("manuallySelectedAdmissionIds");
            Check(!Field<bool>("groupMode")&&!Descendants(page).OfType<CheckBox>().Any(), "The student statement has no group-mode switch");
            Check(Field<Label>("closingBalance").Text=="—","An unloaded account does not imply a zero balance");
            Check(!Field<ModernButton>("generateStatementButton").Enabled && !Field<ModernButton>("annualReceiptsButton").Enabled,
                "Statement and receipts require a confirmed student");
            Check(!Descendants(page).OfType<ModernButton>().Any(b => b.Text == "Find Students"&&b.Visible) && !Field<DataGridView>("students").Visible,
                "The normal account view hides group search and Include checkboxes");
            foreach (var size in new[] { new Size(1200, 720), new Size(1000, 650), new Size(850, 580) })
            {
                owner.ClientSize = size; Application.DoEvents();
                Check(grid.Width > page.ClientSize.Width * .8, "Account details use the available width instead of sharing it with a selection grid");
                foreach (var date in new[] { Field<ModernDatePicker>("from"), Field<ModernDatePicker>("to") })
                    Check(date.Visible && date.Height >= AppMetrics.InputControlHeight && date.Width > 0,
                        "Optional dates retain visible native input space across client sizes");
                foreach (var name in new[] { "generateStatementButton", "printAmountsDueButton", "annualReceiptsButton" })
                {
                    var button = Field<ModernButton>(name);
                    Check(button.Visible && button.Bounds.Right <= button.Parent!.ClientSize.Width && button.Bounds.Bottom <= button.Parent.ClientSize.Height,
                        "Account actions fit their row and remain reachable through the scrolling viewport");
                }
            }
            Check(SynchronizationContext.Current is WindowsFormsSynchronizationContext,
                "Native async page checks retain the production WinForms synchronization context after nested event pumping");
            int uiThread = Environment.CurrentManagedThreadId;
            grid.DataBindingComplete += (_, _) => Check(Environment.CurrentManagedThreadId == uiThread,
                "Statement result binding remains on the owning UI thread");
            picker.SetStudent(41, "Synthetic Ava · Admission 41");
            Console.WriteLine("ACCOUNT_FLOW: native sizing verified; choose synthetic student");
            ((EventHandler)typeof(StudentPicker).GetField("UserSelectionChanged", flags)!.GetValue(picker)!)
                .Invoke(picker, EventArgs.Empty);
            Check(calls == 1 && readId == 41 && page.IsBusy && selected.SetEquals(new[] { 41 }),
                "Choosing one student loads exactly that account without an Include checkbox or extra Find step");
            Check(!picker.Enabled, "Account loading holds the selected identity stable");
            var data = new DataTable();
            data.Columns.Add("StudentName"); data.Columns.Add("TransactionType"); data.Columns.Add("TransactionId", typeof(long));
            data.Columns.Add("TransactionDate", typeof(DateTime)); data.Columns.Add("DebitAmount", typeof(decimal)); data.Columns.Add("PaidAmount", typeof(decimal));
            data.Rows.Add("Synthetic Ava", "CHARGE", 1L, new DateTime(2026, 10, 1), 100m, 40m);
            data.Rows.Add("Synthetic Ava", "PAYMENT", 2L, new DateTime(2026, 10, 2), 0m, 40m);
            reply.SetResult((data, 20m));
            PumpUntil(() => !page.IsBusy, "The selected student's account finishes binding");
            Console.WriteLine("ACCOUNT_FLOW: account read and binding completed");
            Check(grid.Rows.Count == 1 && Field<Label>("closingBalance").Text == "80" && Field<ModernButton>("exportButton").Enabled,
                "The simplified view preserves opening balance, charges, payment totals and current-snapshot export");
            Check(Field<Label>("statementContext").Text.Contains("Synthetic Ava") && Field<ModernButton>("annualReceiptsButton").Enabled,
                "The account identifies its student and enables contextual annual receipts");
            Check(Field<ModernButton>("annualReceiptsButton").Parent!=Field<ModernButton>("generateStatementButton").Parent,
                "Related reports are separate from statement generation actions");
            Directory.CreateDirectory("artifacts/student-account");
            owner.ClientSize=new Size(1200,800);Application.DoEvents();
            using(var bitmap=new Bitmap(page.Width,page.Height))
            {page.DrawToBitmap(bitmap,page.ClientRectangle);bitmap.Save(Path.GetFullPath("artifacts/student-account/single.png"));}
            Check(type.GetMethod("PrintThermalDueReport",flags) is null,
                "Statement printing no longer uses the retired drawing-based fee-due report");
            var from = Field<ModernDatePicker>("from"); from.Value = new DateTime(2026, 10, 1); from.Checked = true;
            Console.WriteLine("ACCOUNT_FLOW: owned receipt guard verified");
            Check(!Field<ModernButton>("exportButton").Enabled, "Changing the period invalidates the previous financial snapshot");
            using(var group=(StudentAccountStatementPage)Activator.CreateInstance(type,flags,null,new object[]{false,read,true},null)!)
            {
                owner.Controls.Remove(page);owner.Controls.Add(group);Application.DoEvents();
                T GroupField<T>(string name)=>(T)type.GetField(name,flags)!.GetValue(group)!;
                Check(GroupField<bool>("groupMode")&&GroupField<DataGridView>("students").Visible,
                    "Family/group selection opens in its own dedicated report");
                Check(GroupField<ModernButton?>("annualReceiptsButton") is null&&GroupField<ModernButton?>("printAmountsDueButton") is null,
                    "The group workflow contains only statement actions");
                Check(GroupField<HashSet<int>>("manuallySelectedAdmissionIds").Count==0&&selected.SetEquals(new[]{41}),
                    "Separate reports do not share or silently change selections");
                GroupField<HashSet<int>>("manuallySelectedAdmissionIds").UnionWith(new[]{41,42});
                GroupField<DataGridView>("students").Rows.Add(true,false,41,"Synthetic Ava","Class 10","A");
                GroupField<DataGridView>("students").Rows.Add(true,false,42,"Synthetic Ben","Class 8","A");
                GroupField<Dictionary<int,HashSet<int>>>("familySelections")[41]=new HashSet<int>{41,42};
                type.GetMethod("UpdateSelectionActions",flags)!.Invoke(group,null);
                type.GetMethod("SelectionCountText",flags)!.Invoke(group,null);
                Application.DoEvents();
                Check(GroupField<Label>("statementContext").Text.Contains("2 student(s)")&&GroupField<ModernButton>("generateStatementButton").Enabled,
                    "The group statement counts unique included students and enables generation");
                Check(GroupField<ModernButton>("generateStatementButton").PointToScreen(Point.Empty).Y>=GroupField<DataGridView>("students").PointToScreen(new Point(0,GroupField<DataGridView>("students").Height)).Y,
                    "Group statement generation follows student selection in the visible flow");
                using(var bitmap=new Bitmap(group.Width,group.Height))
                {group.DrawToBitmap(bitmap,group.ClientRectangle);bitmap.Save(Path.GetFullPath("artifacts/student-account/group.png"));}
                owner.Controls.Remove(group);owner.Controls.Add(page);Application.DoEvents();
            }
            Check(selected.SetEquals(new[]{41})&&picker.SelectedId==41&&from.Checked&&from.Value.Date==new DateTime(2026,10,1),
                "Returning from a separate report retains the student account and its dates");
            picker.SetStudent(42, "Synthetic Ben"); Invoke("SelectSingleStudent");
            CurrentUser.SignIn(921, "Restricted fixture", "Operator", false, new[] { "Reports.View", "Fees.View" });
            using(var reports=new ReportsHomePage((Action<Func<Control>,string,string>)((_,_,_)=>{})))
                Check(!Descendants(reports).Any(control=>control.Text==PageTitles.FamilyGroupStatements||control.Text==PageTitles.AnnualFeeReceipts),
                    "Statement report entries respect statement permission");
            Invoke("UpdateSelectionActions"); Invoke("OpenAnnualReceipts");
            Check(!Field<ModernButton>("annualReceiptsButton").Enabled && Application.OpenForms.Count == 1,
                "Direct receipt dispatch cannot bypass its existing statement permission");
            CurrentUser.SignIn(920, "Account fixture", "Administrator", false, Array.Empty<string>());
            using var profile = new StudentProfilePage((_, _, _) => throw new Exception("Family must not replace the profile"), false);
            Console.WriteLine("ACCOUNT_FLOW: separate reports and receipt permission verified");
            var profilePicker = (StudentPicker)typeof(StudentProfilePage).GetField("student", flags)!.GetValue(profile)!;
            profilePicker.SetStudent(41, "Synthetic Ava");
            using var family = (Control)Activator.CreateInstance(typeof(StudentFamilyPage), flags, null, new object[] { 41, false }, null)!;
            Func<Control> createFamily = () => family;
            using var familyWindow = (ApplicationWindowForm)typeof(StudentProfilePage).GetMethod("CreateShortcutDialog", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { "Open Family / Siblings", createFamily })!;
            var actionReply = new TaskCompletionSource(); Func<Task> action = () => actionReply.Task;
            var familyTask = (Task)typeof(StudentFamilyPage).GetMethod("RunFamilyActionAsync", flags)!.Invoke(family, new object[] { action })!;
            var familyClosing = new FormClosingEventArgs(CloseReason.UserClosing, false);
            typeof(Form).GetMethod("OnFormClosing", flags)!.Invoke(familyWindow, new object[] { familyClosing });
            Check(familyClosing.Cancel && !familyWindow.ShowInTaskbar && family.Dock == DockStyle.Fill,
                "Family uses an owned window whose close guard preserves an in-flight link operation");
            actionReply.SetResult(); PumpUntil(() => familyTask.IsCompleted, "Owned family operation settles");
            Check(profilePicker.SelectedId == 41 && profilePicker.Text == "Synthetic Ava" && !family.IsHandleCreated,
                "Family window creation retains profile identity and this fixture never queries a school database");
            owner.Close();
            Console.WriteLine("ACCOUNT_FLOW: all account and owned family checks completed");
        }
        finally { CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName, previous.MustChangePassword, previous.Item5); }
    }
}
