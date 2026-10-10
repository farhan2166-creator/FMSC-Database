using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Finance.Billing;
/// <summary>The Reverse action on one row of Fee Collection's charges grid. Asking here means the operator gets the field focused instead of a round trip, the same trade DiscontinueFeeAssignmentForm makes.</summary>
public sealed class ReverseChargeDialog : ApplicationWindowForm
{
    private readonly long billingChargeId;
    private readonly ModernTextBox reason = HumanUi.TextBox(true, 500);
    private readonly ModernButton reverseButton = new() {
        Text = "Reverse Charge",
        Width = AppScale.Scale(140),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Danger"
    };
    /* Set only once the reversal has been written, so that path closes without the unsaved-changes prompt. */
    private bool savedSuccessfully;
    /* One confirmation per close gesture. Assigning DialogResult on a modal form raises FormClosing too, so a single Cancel click would otherwise ask twice. */
    private bool discardConfirmed;
    /* True from the moment the write leaves for the server until it has answered. */
    private bool saving;
    /* UX - the missing reason is marked on its field, not shown in a message box. */
    private readonly FormValidator validator = new();
    private readonly ModernButton cancelButton = new() {
        Text = UiText.Actions.Cancel,
        Width = AppScale.Scale(105),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Secondary"
    };
    /// <summary>True only when dbo.ReverseBillingCharge actually committed. The caller reloads the charges grid on this and on nothing else.</summary>
    public bool Reversed { get; private set; }
    public ReverseChargeDialog(
        long billingChargeId,
        string chargeName,
        string billingMonth,
        decimal amount,
        string studentDisplay)
    {
        this.billingChargeId = billingChargeId;
        Text = "Reverse Charge";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(AppScale.Scale(600, AppScale.ControlSizeScale), AppScale.Scale(350, AppScale.ControlSizeScale));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        BuildLayout(chargeName, billingMonth, amount, studentDisplay);
        reverseButton.Click += async (_, _) => await SaveAsync();
        /* The window's own close (X, Alt+F4, Esc) is guarded the same way; programmatic closes are left alone. */
        FormClosing += (_, e) => {
            /* The write is in flight: ignore the gesture entirely rather than ask, so the result flags are always set before the caller gets the window back. */
            if (saving && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }
            if (e.CloseReason == CloseReason.UserClosing)
                e.Cancel = !ConfirmDiscard();
        };
    }
    private void BuildLayout(
        string chargeName,
        string billingMonth,
        decimal amount,
        string studentDisplay)
    {
        var cancel = cancelButton;
        /* The guard runs here rather than from the button's own DialogResult, because a button that assigns DialogResult dismisses the modal form before the confirmation can keep it open. */
        cancel.Click += (_, _) => {
            /* A click that slipped through while the write is in flight. */
            if (saving)
                return;
            if (!ConfirmDiscard())
                return;
            DialogResult = DialogResult.Cancel;
            Close();
        };
        var card = HumanUi.Card(
            $"Reverse: {chargeName}",
            $"Student: {studentDisplay}    Billing month: {billingMonth}    Amount: Rs. {amount:N0}",
            AppTheme.ModuleAccent("Finance"));
        /* The consequence, in the operator's terms, before anything is written: this is a cancellation, not a deletion, and it is bounded by money. */
        var consequence = new Label {
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(AppScale.Scale(660), 0),
            Margin = new Padding(0, 0, 0, AppScale.Scale(6)),
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.MutedText,
            Text =
                "The charge is marked Cancelled. It leaves the bill, the outstanding balance and the "
                + "reports, and stays visible as history - nothing is deleted.\r\n\r\n"
                + "Once a payment has been allocated to this charge the reversal is refused, because the "
                + "money has to be dealt with first: use Correction, Refund or Reversal on Student Fees."
        };
        var fields = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fields.Controls.Add(consequence, 0, 0);
        fields.Controls.Add(HumanUi.Field(UiText.Fields.Reason, reason, true), 0, 1);
        card.Content.Controls.Add(fields);
        var buttons = HumanUi.ButtonRow(reverseButton, cancel);
        buttons.Dock = DockStyle.Fill;
        buttons.Margin = new Padding(0);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(AppScale.Scale(16))
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(card, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = reverseButton;
        CancelButton = cancel;
        Disposed += (_, _) => validator.Dispose();
    }
    private async Task SaveAsync()
    {
        if (!reverseButton.Enabled)
            return;
        validator.Begin();
        validator.Required(reason, "Give a reason - it is recorded");
        if (!validator.Finish())
            return;
        /* The reason is validated above and the write is one await away, so a second click while it is in flight would post a second reversal for the same charge. */
        reverseButton.Enabled = false;
        cancelButton.Enabled = false;
        saving = true;
        try
        {
            await DatabaseHelper.ExecuteUserAsync(
                "ReverseBillingCharge",
                DatabaseHelper.BigInt("@BillingChargeId", billingChargeId),
                DatabaseHelper.P("@Reason", reason.Text.Trim()));
            /* The flags the caller reads are set before anything can close this window. */
            savedSuccessfully = true;
            Reversed = true;
            saving = false;
            /* The window can still have been torn down while the server was answering (the application closing, for instance). */
            if (IsDisposed || Disposing)
                return;
            cancelButton.Enabled = true;
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            saving = false;
            /* The window can still have been torn down (the application closing, for instance) while the server was answering. */
            if (IsDisposed || Disposing)
                return;
            /* The procedure's refusals (53200 - 53204) arrive here already translated by DbMessage, and say what to do next. */
            ModernMessageDialog.Show(
                this,
                DbMessage.UserMessage(ex),
                "Reverse Charge",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            reverseButton.Enabled = true;
            cancelButton.Enabled = true;
        }
    }
    /// <summary>The one value this form edits: the reason. The form opens with it empty, so anything typed is an edit, and a form whose write is already under way is never dirty.</summary>
    private bool IsDirty() =>
        !savedSuccessfully &&
        DialogResult != DialogResult.OK &&
        reason.Text.Trim().Length > 0;
    /// <summary>asks at most once for a close gesture. True when the caller may proceed with the close.</summary>
    private bool ConfirmDiscard()
    {
        return ModernMessageDialog.ConfirmDiscardOnce(this, IsDirty, ref discardConfirmed);
    }
}

