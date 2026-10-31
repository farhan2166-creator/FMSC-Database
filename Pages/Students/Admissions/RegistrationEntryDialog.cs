using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Students.Admissions;

internal sealed class RegistrationEntryDialog : ApplicationWindowForm
{
    internal int SavedAdmissionId { get; private set; }
    internal RegistrationEntryDialog(Action<Func<Control>, string, string> navigate, int anchor, string parent, string phone, string address, string birth = "")
    {
        Text = "Admit Sibling";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1050, 760); MinimumSize = new Size(850, 600);
        var page = new QuickRegistrationFormPage(navigate, 0, initialize: false) { Dock = DockStyle.Fill };
        page.PrepareForEmbedding(keepActions: false);
        var save = new ModernButton { Text = "Save Registration", ThemeRole = "Success", AutoSize = true };
        SetSaveShortcut(save);
        var close = new ModernButton { Text = "Close", AutoSize = true };
        var actions = HumanUi.ButtonRow(save, close); actions.Dock = DockStyle.Fill;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
        root.Controls.Add(page, 0, 0); root.Controls.Add(actions, 0, 1); Controls.Add(root);
        page.WorkspaceRecoveryRequested = () => { CheckPreviousSave(this, navigate); page.RefreshWorkspaceRecovery(); return Task.CompletedTask; };
        bool saving = false;
        FormClosing += (_, e) => { if (saving || page.IsBusy) e.Cancel = true; };
        save.Click += async (_, _) => {
            if (saving) return;
            saving = true; save.Enabled = close.Enabled = false;
            try { if (await page.SaveWorkspaceAsync()) SavedAdmissionId = page.SavedRegistrationId; }
            finally { saving = false; if (!IsDisposed) save.Enabled = close.Enabled = true; }
            if (SavedAdmissionId > 0) { DialogResult = DialogResult.OK; Close(); }
        };
        close.Click += (_, _) => Close();
        Shown += async (_, _) => {
            save.Enabled = false;
            try { await page.InitializeWorkspaceAsync(); page.ConfigureSiblingEntry(anchor, parent, phone, address, birth); page.ShowAdmissionRegistrationGuidance("Shared details are carried forward. Save creates a pending sibling; complete admission details before confirming."); }
            catch (Exception ex) { page.ShowRegistrationContextWarning(DbMessage.UserMessage(ex)); }
            finally { if (!IsDisposed) { save.Enabled = true; page.FocusSiblingName(); } }
        };
    }
    internal static void CheckPreviousSave(IWin32Window? owner, Action<Func<Control>, string, string> navigate)
    {
        using var page = new QuickRegistrationFormPage(navigate, 0, initialize: false) { Dock = DockStyle.Fill };
        using var dialog = new ApplicationWindowForm { Text = "Check Previous Registration Save", StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(1050, 740) };
        dialog.Controls.Add(page); dialog.FormClosing += (_, e) => { if (page.IsBusy) e.Cancel = true; };
        dialog.Shown += async (_, _) => { try { await page.InitializeRecoveryAsync(); } catch (Exception ex) { page.ShowRegistrationContextWarning(DbMessage.UserMessage(ex)); } };
        dialog.ShowDialog(owner);
    }
}
