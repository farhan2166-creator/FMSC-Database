using System.Text.Json;
using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Students.Certificates;
/// <summary>Optional clearance checklist helper; currently not invoked by the Leave Certificate flow.</summary>
// Business rule (V20 audit F-02): clearance is optional, confirmed by the owner.
// A missing checklist is not a reason to block leaving or issuing a Leave Certificate.
// Do not wire this helper as a mandatory UI/DB gate without a new policy decision.
// See Docs/AUDIT_DECISIONS.md; this helper's balance/checklist rules apply only when used.
internal static class ClearanceUi
{
    public static async Task<bool> ShowClearanceChecklistAsync(IWin32Window owner, int admissionId, string studentDisplayName)
    {
        decimal outstandingBalance;
        DataTable items;
        try
        {
            object? balanceResult = await DatabaseHelper.ScalarUserAsync(
                "GetAdmissionOutstandingBalance",
                DatabaseHelper.Int("@AdmissionId", admissionId));
            outstandingBalance = balanceResult is null or DBNull
                ? 0m
                : Convert.ToDecimal(balanceResult);
            items = await DatabaseHelper.QueryUserAsync("GetActiveClearanceChecklistItems");
        }
        catch (Exception ex)
        {
            ModernMessageDialog.Show(
                owner,
                DbMessage.UserMessage(ex, "Could not load the clearance checklist."),
                "Withdrawal Clearance",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
        using var dialog = new ApplicationWindowForm {
            Text = "Withdrawal Clearance Checklist",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(AppScale.Scale(520), AppScale.Scale(480)),
            BackColor = AppTheme.Surface
        };
        var heading = new Label {
            Dock = DockStyle.Top,
            Height = AppScale.Scale(44),
            Font = AppTheme.HeadingFont,
            ForeColor = AppTheme.HeadingText,
            Text = $"Clearing: {studentDisplayName}"
        };
        var balanceLabel = new Label {
            Dock = DockStyle.Top,
            Height = AppScale.Scale(32),
            Font = AppTheme.NormalFont,
            ForeColor = outstandingBalance > 0 ? AppTheme.Danger : AppTheme.MutedText,
            Text = outstandingBalance > 0
                ? $"Outstanding balance: Rs. {outstandingBalance:N0} (not yet cleared)"
                : "Outstanding balance: Rs. 0.00 (fully cleared)"
        };
        var itemsPanel = new FlowLayoutPanel {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Height = Math.Max(1, items.Rows.Count) * AppScale.Scale(30),
            Padding = new Padding(0, 8, 0, 8)
        };
        var checkBoxes = new List<(int ItemId, string ItemText, CheckBox Box)>();
        foreach (DataRow row in items.Rows)
        {
            int itemId = Convert.ToInt32(row["ItemId"]);
            string itemText = Convert.ToString(row["ItemText"]) ?? "";
            var box = new CheckBox {
                Text = itemText,
                AutoSize = true,
                Font = AppTheme.NormalFont,
                ForeColor = AppTheme.Text
            };
            checkBoxes.Add((itemId, itemText, box));
            itemsPanel.Controls.Add(box);
        }
        var remarksLabel = new Label {
            Dock = DockStyle.Top,
            Height = AppScale.Scale(22),
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText,
            Text = "Remarks (required if the balance isn't cleared or an item is left unchecked):"
        };
        var remarks = new ModernTextBox {
            Dock = DockStyle.Top,
            Multiline = true,
            Height = AppScale.Scale(70),
            MaxLength = 500
        };
        /* UX - a missing remark is marked on the remarks box, with the reason in the label above it, instead of a message box. */
        var validator = new FormValidator();
        validator.BindSummary(remarksLabel);
        dialog.Disposed += (_, _) => validator.Dispose();
        var confirmButton = new ModernButton { Text = "Confirm Clearance & Continue", ThemeRole = "Success", Width = AppScale.Scale(220) };
        /* no DialogResult on the button: a button that assigns it dismisses the modal form before the discard confirmation can keep it open. */
        var cancelButton = new ModernButton { Text = "Cancel", ThemeRole = "Secondary", Width = AppScale.Scale(100) };
        var buttonRow = HumanUi.ButtonRow(confirmButton, cancelButton);
        buttonRow.Dock = DockStyle.Bottom;
        bool confirmed = false;
        /* One confirmation per close gesture. Assigning DialogResult on a modal form raises FormClosing too, so a single Cancel click would otherwise ask twice. */
        bool discardConfirmed = false;
        /* (9) - the clearance write below is awaited, so the dialog stays live while dbo.RecordStudentClearance runs. */
        bool writeInFlight = false;
        /* The only values this dialog takes from the operator are the checklist boxes and the remarks box. */
        bool IsDirty() =>
            !confirmed &&
            (checkBoxes.Any(c => c.Box.Checked) || remarks.Text.Length > 0);
        /* asks at most once for a close gesture. True when the caller may proceed with the close. */
        bool ConfirmDiscard()
        {
            if (writeInFlight)
                return false;
            return ModernMessageDialog.ConfirmDiscardOnce(dialog, IsDirty, ref discardConfirmed);
        }
        cancelButton.Click += (_, _) => {
            if (!ConfirmDiscard())
                return;
            dialog.DialogResult = DialogResult.Cancel;
        };
        /* The window's own close (X, Alt+F4, Esc) is guarded the same way; programmatic closes are left alone. */
        dialog.FormClosing += (_, e) => {
            if (e.CloseReason == CloseReason.UserClosing)
                e.Cancel = !ConfirmDiscard();
        };
        /* Confirm Clearance records the checklist through dbo.RecordStudentClearance, so a double click could record it twice. */
        confirmButton.Click += async (_, _) => {
            /* A click already queued when the button was disabled must not start a second write either, and a clearance already recorded is never written twice. */
            if (!confirmButton.Enabled || confirmed)
                return;
            confirmButton.Enabled = false;
            try
            {
                bool allChecked = checkBoxes.All(c => c.Box.Checked);
                validator.Begin();
                if (outstandingBalance > 0 || !allChecked)
                {
                    validator.Required(
                        remarks,
                        "Add a remark explaining the balance and/or unchecked item(s).");
                }
                if (!validator.Finish())
                    return;
                try
                {
                    var checklistResult = checkBoxes
                        .Select(c => new ClearanceItemResult(c.ItemText, c.Box.Checked))
                        .ToList();
                    string checklistJson = JsonSerializer.Serialize(checklistResult);
                    writeInFlight = true;
                    cancelButton.Enabled = false;
                    try
                    {
                        await DatabaseHelper.ExecuteUserAsync(
                            "RecordStudentClearance",
                            DatabaseHelper.Int("@AdmissionId", admissionId),
                            DatabaseHelper.P("@OutstandingBalance", outstandingBalance),
                            DatabaseHelper.P("@ChecklistJson", checklistJson),
                            DatabaseHelper.P("@Remarks", DatabaseHelper.OptionalText(remarks.Text)));
                    }
                    finally
                    {
                        writeInFlight = false;
                        if (!cancelButton.IsDisposed)
                            cancelButton.Enabled = true;
                    }
                    /* The clearance IS recorded from here on, so the result is set before anything else can close the dialog. */
                    confirmed = true;
                    if (!dialog.IsDisposed)
                        dialog.DialogResult = DialogResult.OK;
                }
                catch (Exception ex)
                {
                    ModernMessageDialog.Show(
                        dialog,
                        DbMessage.UserMessage(ex, "Could not record the clearance."),
                        "Withdrawal Clearance",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally
            {
                /* Nothing to re-enable once the clearance is recorded and the dialog is closing, and the form may already be gone. */
                if (!confirmButton.IsDisposed && !confirmed)
                    confirmButton.Enabled = true;
            }
        };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16) };
        scroll.Controls.Add(remarks);
        scroll.Controls.Add(remarksLabel);
        scroll.Controls.Add(itemsPanel);
        scroll.Controls.Add(balanceLabel);
        scroll.Controls.Add(heading);
        dialog.Controls.Add(scroll);
        dialog.Controls.Add(buttonRow);
        dialog.CancelButton = cancelButton;
        dialog.ShowDialog(owner);
        return confirmed;
    }
    private sealed record ClearanceItemResult(string ItemText, bool Checked);
}

