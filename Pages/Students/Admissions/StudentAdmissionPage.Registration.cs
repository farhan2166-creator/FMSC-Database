using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Maintenance;

namespace FMSC_SMS.Pages.Students.Admissions;

public sealed partial class StudentAdmissionPage
{
    protected override bool StatusBelowActions => true;
    private ModernButton? quickRegistrationButton, registrationRecoveryButton;
    private readonly Label pendingSiblingCount = new() { AutoSize = false, Height = AppMetrics.ButtonHeight, Font = AppTheme.NormalFont, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ModernButton addNewSiblingButton = new() { Text = "Add New Sibling", ThemeRole = "Secondary" };
    private bool IsCancelledRegistration => _workspaceStudentStatus == "Cancelled";
    private Action<Func<Control>, string, string> RegistrationNavigation => _navigate ?? ((_, _, _) => { });

    private void ConfigureRegistrationActions()
    {
        quickRegistrationButton = AddAction("Admit Sibling", async (_, _) => await RegisterChildAsync(true), "Secondary");
        Actions.Controls.Add(pendingSiblingCount);
        registrationRecoveryButton = AddAction("Check Registration Save", async (_, _) => {
            if (!IsBusy) RegistrationEntryDialog.CheckPreviousSave(FindForm(), RegistrationNavigation);
            await Task.CompletedTask;
        }, "Secondary");
        addNewSiblingButton.Click += async (_, _) => await RegisterChildAsync(true);
        var draft = new ToolStripMenuItem("Save Registration Draft");
        draft.Click += async (_, _) => { if (IsPendingRegistration && !IsBusy) await SaveDraftOrDetailsAsync(); };
        enrollmentActionsMenu.Items.Add(draft);
        enrollmentActionsMenu.Opening += (_, _) => { draft.Visible = IsPendingRegistration; draft.Enabled = !IsBusy && CurrentUser.HasPermission("Students.Admit"); };
        siblingsGrid.CellContentClick += async (_, e) => await RunSiblingRegistrationActionAsync(e.RowIndex, e.ColumnIndex);
        siblingsGrid.CellPainting += (_, e) => {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || siblingsGrid.Columns[e.ColumnIndex] is not DataGridViewButtonColumn ||
                !string.IsNullOrWhiteSpace(Convert.ToString(e.Value))) return;
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border | DataGridViewPaintParts.SelectionBackground);
            e.Handled = true;
        };
        foreach (var button in new[] { quickRegistrationButton }) GateAction(button, "Students.Admit");
        UpdateRegistrationActions();
    }
    private void UpdateRegistrationActions()
    {
        bool ready = !IsBusy && unconfirmedAdmission is null;
        if (quickRegistrationButton is not null) quickRegistrationButton.Enabled = ready && currentAdmissionId > 0 && !IsCancelledRegistration && CurrentUser.HasPermission("Students.Admit") && CurrentUser.HasPermission("Students.View");
        if (addNewSiblingButton is not null) addNewSiblingButton.Enabled = ready && currentAdmissionId > 0 && !IsCancelledRegistration && CurrentUser.HasPermission("Students.Admit") && CurrentUser.HasPermission("Students.Administer");
        if (registrationRecoveryButton is not null)
        {
            try { registrationRecoveryButton.Visible = QuickRegistrationRecoveryJournal.ReadPending() is not null; }
            catch { registrationRecoveryButton.Visible = true; }
            registrationRecoveryButton.Enabled = ready && QuickRegistrationAccess.CanUse(CurrentUser.HasPermission);
        }
        admissionIdCaption.Text = IsPendingRegistration || IsCancelledRegistration ? "Temp Reference" : "Admission No.";
        if (saveButton is not null && !registrationReview) saveButton.Text = IsPendingRegistration ? "Confirm & Save" : UiText.Actions.Save;
        RefreshSiblingRegistrationRows();
    }
    private async Task RunSiblingRegistrationActionAsync(int rowIndex, int columnIndex)
    {
            if (rowIndex < 0 || columnIndex < 0 || IsBusy || openingSibling) return;
            string action = siblingsGrid.Columns[columnIndex].Name;
            if (action is not ("RegistrationConfirm" or "RegistrationCancel" or "RegistrationReopen") ||
                string.IsNullOrWhiteSpace(Convert.ToString(siblingsGrid.Rows[rowIndex].Cells[columnIndex].Value))) return;
            if (action == "RegistrationConfirm" && !CurrentUser.HasPermission("Students.Admit") ||
                action == "RegistrationCancel" && !CurrentUser.HasPermission("Students.Administer") ||
                action == "RegistrationReopen" && (!CurrentUser.HasPermission("Students.Administer") || !CurrentUser.HasPermission("Students.Admit") || !CurrentUser.HasPermission("Fees.View"))) return;
            int id = Convert.ToInt32(siblingsGrid.Rows[rowIndex].Cells["AdmissionId"].Value);
            if (id != currentAdmissionId)
            {
                if (!ConfirmDiscardAdmission() || !await LoadStudentForEditAsync(id) || currentAdmissionId != id) return;
            }
            if (action == "RegistrationConfirm") { SwitchTab(0); await CompleteRegistrationAsync(); }
            else if (action == "RegistrationCancel") await CloseAdmissionAsync(true);
            else await ReopenRegistrationAsync();
    }
    private void ConfigureSiblingRegistrationRows()
    {
        foreach (var item in new[] { ("RegistrationConfirm", "Confirm"), ("RegistrationCancel", "Cancel"), ("RegistrationReopen", "Reopen") })
            siblingsGrid.Columns.Add(new DataGridViewButtonColumn {
                Name = item.Item1, HeaderText = item.Item2, ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width = AppScale.Scale(item.Item1 == "RegistrationConfirm" ? 145 : 100),
                MinimumWidth = AppScale.Scale(item.Item1 == "RegistrationConfirm" ? 145 : 100)
            });
        RefreshSiblingRegistrationRows();
    }
    private void RefreshSiblingRegistrationRows()
    {
        int waiting = siblingsGrid.Columns.Contains("Status") ? siblingsGrid.Rows.Cast<DataGridViewRow>().Count(row => !row.IsNewRow &&
            Convert.ToString(row.Cells["Status"].Value) == "Pending" && Convert.ToInt32(row.Cells["AdmissionId"].Value) != currentAdmissionId) : 0;
        pendingSiblingCount.Text = currentAdmissionId > 0 ? $"{waiting} sibling(s) awaiting confirmation" : "Load a student to admit a sibling";
        pendingSiblingCount.Width = TextRenderer.MeasureText(pendingSiblingCount.Text, pendingSiblingCount.Font).Width + 16;
        if (!siblingsGrid.Columns.Contains("RegistrationConfirm")) return;
        foreach (DataGridViewRow row in siblingsGrid.Rows)
        {
            if (row.IsNewRow) continue;
            string state = Convert.ToString(row.Cells["Status"].Value) ?? "";
            bool ready = !IsBusy;
            row.Cells["RegistrationConfirm"].Value = ready && state == "Pending" && CurrentUser.HasPermission("Students.Admit") ? "Confirm & Save" : "";
            row.Cells["RegistrationCancel"].Value = ready && state == "Pending" && CurrentUser.HasPermission("Students.Administer") ? "Cancel" : "";
            row.Cells["RegistrationReopen"].Value = ready && state == "Cancelled" && CurrentUser.HasPermission("Students.Administer") && CurrentUser.HasPermission("Students.Admit") && CurrentUser.HasPermission("Fees.View") ? "Reopen" : "";
        }
        foreach (string column in new[] { "RegistrationConfirm", "RegistrationCancel", "RegistrationReopen" })
            siblingsGrid.Columns[column].Visible = siblingsGrid.Rows.Cast<DataGridViewRow>().Any(row => !row.IsNewRow &&
                (column == "RegistrationReopen" ? Convert.ToString(row.Cells["Status"].Value) == "Cancelled" : Convert.ToString(row.Cells["Status"].Value) == "Pending"));
    }
    private async Task RegisterChildAsync(bool sibling)
    {
        if (IsBusy || !QuickRegistrationAccess.CanUse(CurrentUser.HasPermission) ||
            sibling && (currentAdmissionId <= 0 || IsCancelledRegistration || !CurrentUser.HasPermission("Students.Administer"))) return;
        if (HasUnsavedChanges) { Warn("Save or discard the loaded student's edits before admitting a sibling."); return; }
        int anchor = currentAdmissionId;
        using var dialog = new RegistrationEntryDialog(RegistrationNavigation, anchor, fatherName.Text, contact.Text, address.Text, placeOfBirthInput.Text);
        dialog.ShowDialog(FindForm());
        if (dialog.SavedAdmissionId > 0)
        {
            StudentSearchCache.Invalidate();
            if (sibling) { await LoadCurrentSiblingsAsync(); SetStatus("Sibling saved as Pending. Select and open the sibling to complete details, then Confirm & Save.", PageStatusKind.Success); }
            else await LoadStudentForEditAsync(dialog.SavedAdmissionId);
        }
    }
    private async Task OpenPendingRegistrationListAsync()
    {
        if (IsBusy || !QuickRegistrationAccess.CanUse(CurrentUser.HasPermission) || !ConfirmDiscardAdmission()) return;
        int selected = 0;
        using var dialog = new ApplicationWindowForm { Text = "Pending Registrations", StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(1150, 720) };
        using var page = new AdmissionReviewListPage(RegistrationNavigation, true, id => {
            selected = id; dialog.DialogResult = DialogResult.OK; dialog.Close(); return Task.CompletedTask;
        }) { Dock = DockStyle.Fill };
        dialog.Controls.Add(page); dialog.ShowDialog(FindForm());
        if (selected > 0) await LoadStudentForEditAsync(selected);
    }
    private async Task ReopenRegistrationAsync()
    {
        if (IsBusy || !IsCancelledRegistration || !CurrentUser.HasPermission("Students.Administer") || !CurrentUser.HasPermission("Students.Admit")) return;
        if (HasUnsavedChanges && !ConfirmDiscardAdmission()) return;
        int id = currentAdmissionId;
        using var dialog = new RegistrationReopenDialog(id, name.Text);
        dialog.ShowDialog(FindForm());
        StudentSearchCache.Invalidate();
        await LoadStudentForEditAsync(id);
    }
}
