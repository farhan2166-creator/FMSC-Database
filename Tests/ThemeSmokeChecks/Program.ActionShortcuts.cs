using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Billing;
using FMSC_SMS.Pages.Finance.Collection;
using FMSC_SMS.Pages.Security;
using System.Reflection;

internal static partial class Program
{
    private static void CheckActionShortcuts()
    {
        Check(AppHotKeys.PageActions.Select(s => s.KeyData).Distinct().Count() == AppHotKeys.PageActions.Length,
            "Application action shortcuts have no duplicate keys");
        Check(AppHotKeys.PageActions.All(s => AppHotKeys.IsValidShortcut(s.KeyData) && (s.KeyData & Keys.Modifiers) != Keys.None),
            "Every page action uses one modifier and one key");
        using var tip = new ToolTip();
        using var form = new Form();
        var controller = new PageActionShortcutController(form, tip);
        var left = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 220 };
        var right = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var firstInput = new TextBox();
        var secondInput = new TextBox();
        var fee = new ModernButton { Text = "Add Fee", SavesChanges = true };
        var charge = new ModernButton { Text = "Add Charge", SavesChanges = true };
        var add = new ModernButton { Text = "Add Row" };
        int fees = 0, charges = 0, rows = 0;
        fee.Click += (_, _) => fees++;
        charge.Click += (_, _) => charges++;
        add.Click += (_, _) => rows++;
        left.Controls.AddRange(new Control[] { firstInput, fee, add });
        right.Controls.AddRange(new Control[] { secondInput, charge });
        form.Controls.AddRange(new Control[] { right, left });
        form.Show();
        firstInput.Focus();
        Check(controller.TryHandle(Keys.Control | Keys.S) && fees == 1 && charges == 0,
            "Ctrl+S commits Add Fee in the focused editor");
        Check(controller.AvailableHints().Any(h => h.Target == fee && h.Key == "Ctrl+S"),
            "Save hints include the semantic Add Fee action");
        Check(controller.TryHandle(Keys.Alt | Keys.A) && rows == 1 && fees == 1,
            "Alt+A adds a draft row without invoking a commit button");
        secondInput.Focus();
        Check(controller.TryHandle(Keys.Control | Keys.S) && charges == 1 && fees == 1,
            "Ctrl+S follows focus to Add Charge");
        charge.Enabled = false;
        Check(!controller.TryHandle(Keys.Control | Keys.S) && fees == 1 && charges == 1,
            "Disabled save cannot fall through to another editor");
        charge.Enabled = true;
        var duplicate = new ModernButton { Text = "Save Other" };
        right.Controls.Add(duplicate);
        Check(!controller.TryHandle(Keys.Control | Keys.S), "Ambiguous saves do not dispatch");
        Check(!controller.TryHandle(Keys.Insert), "Insert no longer commits data");
        duplicate.Visible = false;
        charge.Text = "Save Changes";
        controller.RefreshHints();
        Check(controller.TryHandle(Keys.Control | Keys.S) && charges == 2,
            "Caption changes retain save semantics without duplicate matching");
        using var entry = new AdditionalFeeEntry();
        Check(entry.PostButton.SavesChanges, "Real shared charge entry declares save semantics");
        form.Close();
        foreach (var shortcut in AppHotKeys.PageActions)
        foreach (string caption in shortcut.ButtonPrefixes)
        {
            using var host = new Form();
            var command = new ModernButton { Text = caption };
            host.Controls.Add(command);
            var commands = new PageActionShortcutController(host, tip);
            int clicks = 0;
            command.Click += (_, _) => clicks++;
            host.Show();
            Check(commands.TryHandle(shortcut.KeyData) && clicks == 1,
                $"{shortcut.DisplayKey} dispatches {shortcut.Action} exactly once");
            Check(commands.AvailableHints().Any(h => h.Target == command && h.Key == shortcut.DisplayKey),
                $"{shortcut.Action} hint matches its dispatch key");
            command.Enabled = false;
            Check(!commands.TryHandle(shortcut.KeyData) && clicks == 1,
                $"{shortcut.DisplayKey} cannot invoke disabled {caption}");
            Check(!commands.AvailableHints().Any(h => h.Target == command),
                $"Disabled {caption} has no available shortcut hint");
            command.Enabled = true;
            command.Visible = false;
            Check(!commands.TryHandle(shortcut.KeyData) && clicks == 1,
                $"{shortcut.DisplayKey} cannot invoke hidden {caption}");
            host.Close();
        }
        using var printHost = new Form();
        var receipt = new ModernButton { Text = "Print Receipt", Top = 0 };
        var studentBill = new ModernButton { Text = "Print Student Bill", Top = 40 };
        var familyBill = new ModernButton { Text = "Print Family Bill", Top = 80 };
        printHost.Controls.AddRange(new Control[] { receipt, studentBill, familyBill });
        var printing = new PageActionShortcutController(printHost, tip);
        int receipts = 0, studentBills = 0, familyBills = 0;
        receipt.Click += (_, _) => receipts++;
        studentBill.Click += (_, _) => studentBills++;
        familyBill.Click += (_, _) => familyBills++;
        printHost.Show();
        Check(!printing.TryHandle(Keys.Control | Keys.P), "Generic Print refuses three competing print actions");
        Check(printing.TryHandle(Keys.Alt | Keys.P) && receipts == 1 && studentBills == 0 && familyBills == 0,
            "Receipt key selects only receipt among multiple print buttons");
        Check(printing.TryHandle(Keys.Control | Keys.B) && studentBills == 1 && familyBills == 0,
            "Student bill key does not print a family bill");
        Check(printing.TryHandle(Keys.Alt | Keys.B) && familyBills == 1 && studentBills == 1,
            "Family bill key does not print a student bill");
        receipt.Enabled = false;
        Check(!printing.TryHandle(Keys.Alt | Keys.P) && receipts == 1,
            "Unavailable receipt does not fall through to another print action");
        printHost.Close();
        CheckCollectionShortcutTargets();
        CheckNewShortcutCoverage();
    }

    private static void CheckCollectionShortcutTargets()
    {
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName,
            CurrentUser.MustChangePassword, Permissions: CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(7779, "Shortcut fixture", "Administrator", false, Array.Empty<string>());
            using var page = new FeeCollectionPage(false);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var find = typeof(PageActionShortcutController).GetMethod("Find", flags)!;
            foreach (var (key, field) in new[] {
                (Keys.Alt | Keys.A, "addChargeButton"),
                (Keys.Alt | Keys.N, "nextStudentButton"),
                (Keys.Alt | Keys.U, "fullAmountButton"),
                (Keys.Alt | Keys.H, "paymentHistoryButton"),
                (Keys.Alt | Keys.P, "printReceiptButton"),
                (Keys.Control | Keys.B, "printStudentBillButton"),
                (Keys.Alt | Keys.B, "printBillButton"),
                (Keys.Alt | Keys.V, "studentCreditButton"),
                (Keys.Alt | Keys.F, "studentFeesMenuButton") })
            {
                var shortcut = AppHotKeys.PageActions.Single(s => s.KeyData == key);
                var expected = typeof(FeeCollectionPage).GetField(field, flags)!.GetValue(page);
                var matches = (List<ModernButton>)find.Invoke(page.ShortcutController,
                    new object[] { shortcut.ButtonPrefixes })!;
                Check(matches.Count == 1 && ReferenceEquals(matches[0], expected),
                    $"Real Fee Collection maps {shortcut.DisplayKey} to {field} without ambiguity");
            }
            Check(!page.IsHandleCreated, "Collection shortcut matching never starts the SQL loader");
        }
        finally
        {
            CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName,
                previous.MustChangePassword, previous.Permissions);
        }
    }
}
