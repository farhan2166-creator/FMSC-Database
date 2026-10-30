using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Billing;

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
        {
            using var host = new Form();
            var command = new ModernButton { Text = shortcut.ButtonPrefixes[0] };
            host.Controls.Add(command);
            var commands = new PageActionShortcutController(host, tip);
            int clicks = 0;
            command.Click += (_, _) => clicks++;
            host.Show();
            Check(commands.TryHandle(shortcut.KeyData) && clicks == 1,
                $"{shortcut.DisplayKey} dispatches {shortcut.Action} exactly once");
            Check(commands.AvailableHints().Any(h => h.Target == command && h.Key == shortcut.DisplayKey),
                $"{shortcut.Action} hint matches its dispatch key");
            host.Close();
        }
    }
}
