using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Billing;
using FMSC_SMS.Pages.Finance.Collection;
using FMSC_SMS.Pages.Security;

internal static partial class Program
{
    private static void CheckStudentPurchaseCollection()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName,
            CurrentUser.MustChangePassword, Grants: CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(955, "Purchase fixture", "Accountant", false,
                new[] { "Students.View", "Fees.View", "Fees.Collect" });
            int opens = 0;
            Func<Control>? destination = null;
            using var page = new StudentPurchasesPage((factory, module, title) => {
                Check(module == "Finance" && title == "Fee Collection", "Purchase shortcut uses the collection destination");
                destination = factory;
                opens++;
            });
            object Field(string name) => typeof(StudentPurchasesPage).GetField(name, flags)!.GetValue(page)!;
            void Set(string name, object value) => typeof(StudentPurchasesPage).GetField(name, flags)!.SetValue(page, value);
            void Update() => typeof(StudentPurchasesPage).GetMethod("UpdateCollectionAction", flags)!.Invoke(page, null);
            void Open() => typeof(StudentPurchasesPage).GetMethod("OpenCollection", flags)!.Invoke(page, null);
            var button = (ModernButton)Field("collect");
            var picker = (StudentPicker)Field("student");
            var placement = (LookupBox)Field("enrollment");
            var entry = (AdditionalFeeEntry)Field("entry");
            Check(!button.Enabled, "Collection starts disabled without a loaded student");
            picker.SetStudent(41, "Synthetic student");
            using var placements = new DataTable();
            placements.Columns.Add("EnrollmentId", typeof(int));
            placements.Columns.Add("Display", typeof(string));
            placements.Rows.Add(71, "Synthetic placement");
            placement.BindingContext = new BindingContext();
            placement.Bind(placements, "EnrollmentId", "Display");
            placement.SelectedValue = 71;
            Set("loadedEnrollmentId", 71);
            Update(); Open();
            Check(!button.Enabled && opens == 0, "Collection waits for the selected student's load to complete");
            Set("loadedAdmissionId", 41);
            Update(); button.PerformClick();
            Check(button.Enabled && opens == 1 && destination is not null,
                "A loaded student can hand off to collection without posting another charge");
            using (Control target = destination!())
                Check(target is FeeCollectionPage && !target.IsHandleCreated,
                    "Collection is created lazily without initiating a payment or a database read");
            picker.SetStudent(42, "Another student");
            Update(); Open();
            Check(!button.Enabled && opens == 1, "A stale load cannot collect for a newly selected student");
            picker.SetStudent(41, "Synthetic student");
            typeof(AdditionalFeeEntry).GetField("busy", flags)!.SetValue(entry, true);
            Update(); Open();
            Check(!button.Enabled && opens == 1, "Collection cannot interrupt a charge save");
            typeof(AdditionalFeeEntry).GetField("busy", flags)!.SetValue(entry, false);
            var recovery = (AdditionalFeeRecovery)typeof(AdditionalFeeEntry).GetField("recovery", flags)!.GetValue(entry)!;
            typeof(AdditionalFeeRecovery).GetProperty("Blocked", flags)!.SetValue(recovery, true);
            Update(); Open();
            Check(!button.Enabled && opens == 1, "An uncertain save must be checked before collection");
            typeof(AdditionalFeeRecovery).GetProperty("Blocked", flags)!.SetValue(recovery, false);
            Set("loadedEnrollmentId", 0);
            Update(); Open();
            Check(!button.Enabled && opens == 1, "A student without a loaded placement cannot dispatch collection");
            Set("loadedEnrollmentId", 71);
            var remarks = (ModernTextBox)typeof(AdditionalFeeEntry).GetField("reason", flags)!.GetValue(entry)!;
            remarks.Text = "Unposted purchase draft";
            Check(page.HasUnsavedChanges, "The shell can detect an unfinished purchase before navigation");
            for (int mask = 0; mask < 8; mask++)
            {
                string[] required = { "Students.View", "Fees.View", "Fees.Collect" };
                CurrentUser.SignIn(955, "Purchase fixture", "Accountant", false,
                    required.Where((_, index) => (mask & (1 << index)) != 0));
                Update();
                Check(button.Enabled == (mask == 7), "Collection requires all destination permissions");
                int before = opens;
                Open();
                Check(opens == before + (mask == 7 ? 1 : 0), "Direct dispatch rechecks collection permissions");
            }
            CurrentUser.SignIn(955, "No collection", "Accountant", false, new[] { "Students.View", "Fees.View" });
            Check(page.HasUnsavedChanges && remarks.Text == "Unposted purchase draft",
                "Dispatch leaves the purchase draft intact for the shell's discard confirmation");
            int prior = opens;
            Open();
            Check(opens == prior, "A previously enabled shortcut cannot bypass revoked collection permission");
            typeof(StudentPurchasesPage).GetMethod("ClearStudent", flags)!.Invoke(page, new object[] { true });
            Check(!button.Enabled && picker.SelectedId == 0, "Clearing the student also clears the collection hand-off");
        }
        finally { CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName, previous.MustChangePassword, previous.Grants); }
    }
}
