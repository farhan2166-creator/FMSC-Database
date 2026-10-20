using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.Billing;
/// <summary>One deliberate extra charge, without changing monthly or annual defaults.</summary>
public sealed class AddAdditionalFeeDialog : ApplicationWindowForm
{
    private readonly AdditionalFeeEntry entry;
    public bool Posted { get; private set; }
    public AddAdditionalFeeDialog(int enrollmentId, string studentName)
    {
        Text = "Add Charge - " + studentName;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(AppScale.Scale(660), AdditionalFeeEntry.PreferredHeight);
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        var close = new ModernButton {
            Text = UiText.Actions.Cancel, Width = AppScale.Scale(110), Height = AppMetrics.ButtonHeight,
            ThemeRole = "Secondary", DialogResult = DialogResult.Cancel
        };
        entry = new AdditionalFeeEntry(close);
        Controls.Add(entry);
        AcceptButton = entry.PostButton;
        CancelButton = close;
        entry.ChargePosted += (_, _) => {
            Posted = true;
            DialogResult = DialogResult.OK;
            Close();
        };
        Shown += async (_, _) => await entry.LoadStudentAsync(enrollmentId);
        FormClosing += (_, e) => { if (entry.IsPosting) e.Cancel = true; };
    }
}

/// <summary>Shared one-off charge entry for collection and the student purchases page.</summary>
public sealed class AdditionalFeeEntry : UserControl
{
    private int enrollmentId;
    private Guid requestId = Guid.NewGuid();
    private readonly LookupBox feeType = new();
    private readonly ModernNumericTextBox amount = new() {
        Minimum = 0, Maximum = 100000000, WholeRupeesOnly = true, DecimalPlaces = 2,
        ThousandsSeparator = true, TrimTrailingZeros = true, Dock = DockStyle.Fill
    };
    private readonly ModernNumericTextBox discount = new() {
        Minimum = 0, Maximum = 100000000, WholeRupeesOnly = true, DecimalPlaces = 2,
        ThousandsSeparator = true, TrimTrailingZeros = true, Dock = DockStyle.Fill
    };
    private readonly ModernDatePicker chargeDate = HumanUi.Date();
    private readonly ModernTextBox reason = HumanUi.TextBox(false, 500);
    private readonly ModernButton post = new() {
        Text = "Add Charge", Width = AppScale.Scale(130), Height = AppMetrics.ButtonHeight,
        ThemeRole = "Success", Enabled = false
    };
    private DataTable? options;
    private bool busy;
    private int loadVersion;
    private TableLayoutPanel inputs = null!;
    private DateTime? earliestChargeDate;
    private readonly Label dateNote = new() {
        AutoSize = true, Dock = DockStyle.Fill,
        Font = AppTheme.SmallFont, ForeColor = AppTheme.MutedText
    };
    /* UX - what is wrong is marked on the fields, all at once, instead of one message box. */
    private readonly FormValidator validator = new();
    public static int PreferredHeight =>
        3 * AppMetrics.FieldRowHeight + AppMetrics.ButtonRowHeight + AppScale.Scale(108);
    public bool IsPosting => busy;
    public bool HasDraft => feeType.SelectedId > 0 || amount.Value > 0 || discount.Value > 0
        || !string.IsNullOrWhiteSpace(reason.Text);
    public ModernButton PostButton => post;
    public event EventHandler? ChargePosted;
    public event EventHandler? PostingChanged;
    public AdditionalFeeEntry(ModernButton? secondaryAction = null)
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        var fields = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3,
            Padding = Padding.Empty, Margin = Padding.Empty
        };
        inputs = fields;
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 3; i++)
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        /* Added in reading order, and given explicit tab stops in that same order. */
        var feeTypeField = HumanUi.Field("Fee Type", feeType, true);
        var dateField = HumanUi.Field("Date", chargeDate, true, fillHeight: false);
        var amountField = HumanUi.Field("Amount (Rs.)", amount, true, fillHeight: false);
        var discountField = HumanUi.Field("Discount (Rs.)", discount, fillHeight: false);
        FinanceHelp.Attach(this,amount,FinanceHelp.Topic.FeeRate);
        FinanceHelp.Attach(this,discount,FinanceHelp.Topic.Discount);
        FinanceHelp.Attach(this,post,FinanceHelp.Topic.PostFees);
        fields.Controls.Add(feeTypeField, 0, 0);
        fields.Controls.Add(dateField, 1, 0);
        fields.Controls.Add(amountField, 0, 1);
        fields.Controls.Add(discountField, 1, 1);
        var reasonField = HumanUi.Field("Remarks", reason, false, fillHeight: false);
        fields.Controls.Add(reasonField, 0, 2);
        fields.SetColumnSpan(reasonField, 2);
        feeTypeField.TabIndex = 0;
        dateField.TabIndex = 1;
        amountField.TabIndex = 2;
        discountField.TabIndex = 3;
        reasonField.TabIndex = 4;
        var guidance = new Label {
            Text = "Choose a one-off purchase or event. Admission, tuition and recurring transport fees use their own fee workflows.",
            AutoSize = true, ForeColor = AppTheme.MutedText,
            Font = AppTheme.NormalFont, Dock = DockStyle.Fill
        };
        post.Click += async (_, _) => await PostAsync();
        post.TabIndex = 0;
        validator.BindSummary(guidance);
        Disposed += (_, _) => validator.Dispose();
        var buttons = secondaryAction is null
            ? HumanUi.ButtonRow(post)
            : HumanUi.ButtonRow(post, secondaryAction);
        buttons.Dock = DockStyle.Fill;
        buttons.Margin = Padding.Empty;
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(AppMetrics.SpacingLg), Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 3 * AppMetrics.FieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
        root.Controls.Add(fields, 0, 0);
        root.Controls.Add(dateNote, 0, 1);
        root.Controls.Add(guidance, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);
        /* Normal dialog keys: Enter presses Add Charge (after moving through the fields the way Tab does, like the other data-entry dialogs) and Escape cancels. */
        FormFlow.EnterMovesToNext(this);
        feeType.SelectedIndexChanged += (_, _) => {
            if (feeType.SelectedId <= 0 || options is null) return;
            DataRow? option = options.AsEnumerable().FirstOrDefault(
                r => Convert.ToInt32(r["FeeTypeId"]) == feeType.SelectedId);
            if (option is not null)
                amount.Value = Convert.ToDecimal(option["DefaultAmount"]);
        };
        SetEntryEnabled(false);
    }
    public void FocusFee() => feeType.Focus();
    public void ClearStudent()
    {
        if (busy) return;
        ++loadVersion;
        enrollmentId = 0;
        options = null;
        earliestChargeDate = null;
        feeType.ClearLookup();
        amount.Value = 0;
        discount.Value = 0;
        reason.Clear();
        dateNote.Text = string.Empty;
        validator.Begin();
        post.Enabled = false;
        SetEntryEnabled(false);
        requestId = Guid.NewGuid();
    }
    public async Task LoadStudentAsync(int selectedEnrollmentId)
    {
        if (busy) return;
        ClearStudent();
        int version = loadVersion;
        enrollmentId = selectedEnrollmentId;
        try
        {
            post.Enabled = false;
            int userId = FMSC_SMS.Pages.Security.CurrentUser.UserId;
            var dates = await Task.Run(() => DatabaseHelper.QuerySql("""
                EXEC dbo.RequirePermission @UserId, N'Fees.View';
                SELECT COALESCE(CONVERT(date, E.ReturnConfirmedAt), E.EnrollmentDate) AS ChargeStartDate
                FROM dbo.Enrollments E WHERE E.EnrollmentId = @EnrollmentId;
                """, DatabaseHelper.Int("@UserId", userId),
                DatabaseHelper.Int("@EnrollmentId", selectedEnrollmentId)));
            if (IsDisposed || version != loadVersion) return;
            if (dates.Rows.Count == 0)
                throw new InvalidOperationException("The selected placement is no longer available.");
            ConfigureChargeDates(Convert.ToDateTime(dates.Rows[0]["ChargeStartDate"]));
            var loadedOptions = await DatabaseHelper.QueryUserAsync(
                "GetAdditionalFeeOptions", DatabaseHelper.Int("@EnrollmentId", selectedEnrollmentId));
            if (IsDisposed || version != loadVersion) return;
            options = loadedOptions;
            feeType.Bind(options, "FeeTypeId", "Display");
            post.Enabled = earliestChargeDate <= DateTime.Today && options.Rows.Count > 0;
            SetEntryEnabled(post.Enabled);
            if (options.Rows.Count == 0)
                dateNote.Text = "This placement is not ready for additional charges.";
        }
        catch (Exception ex)
        {
            if (IsDisposed || version != loadVersion) return;
            post.Enabled = false;
            ModernMessageDialog.Show(this, DbMessage.UserMessage(ex),
                "Add Charge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private void ConfigureChargeDates(DateTime startDate)
    {
        earliestChargeDate = startDate.Date;
        chargeDate.MinDate = DateTimePicker.MinimumDateTime;
        chargeDate.Value = DateTime.Today < startDate.Date ? startDate.Date : DateTime.Today;
        chargeDate.MinDate = startDate.Date;
        dateNote.Text = $"Charge date must be on or after {startDate:dd-MMM-yyyy} (placement start date), and not in the future.";
    }
    private void SetEntryEnabled(bool enabled)
    {
        inputs.Enabled = enabled;
        post.Enabled = enabled;
    }
    private async Task PostAsync()
    {
        if (busy || !post.Enabled || enrollmentId <= 0) return;
        validator.Begin();
        validator.Required(feeType, "Choose a fee");
        validator.Rule(amount, () => amount.Value > 0 && WholeRupees.IsWhole(amount.Value), "Enter a positive whole-rupee amount");
        if (amount.Value > 0)
            validator.Rule(discount, () => discount.Value >= 0 && discount.Value <= amount.Value && WholeRupees.IsWhole(discount.Value), "Enter a whole-rupee discount no greater than the amount");
        validator.Rule(chargeDate,
            () => earliestChargeDate.HasValue && chargeDate.Value.Date >= earliestChargeDate.Value
                && chargeDate.Value.Date <= DateTime.Today,
            earliestChargeDate.HasValue
                ? $"Choose a date from {earliestChargeDate.Value:dd-MMM-yyyy} through today"
                : "Wait for the placement dates to load");
        if (!validator.Finish())
            return;
        busy = true;
        bool posted = false;
        SetEntryEnabled(false);
        PostingChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await DatabaseHelper.QueryUserAsync(
                "PostAdditionalFee",
                DatabaseHelper.P("@RequestId", requestId),
                DatabaseHelper.Int("@FeeTypeId", feeType.SelectedId),
                DatabaseHelper.Date("@ChargeDate", chargeDate.Value),
                DatabaseHelper.P("@Amount", amount.Value),
                DatabaseHelper.P("@DiscountAmount", discount.Value),
                DatabaseHelper.P("@Reason", string.IsNullOrWhiteSpace(reason.Text) ? $"Additional fee: {feeType.Text}" : reason.Text.Trim()),
                new SqlParameter("@Selection", SqlDbType.Structured) {
                    TypeName = "dbo.ManualChargeSelectionType",
                    Value = Tvp.ManualChargeSelection(new[] { enrollmentId })
                });
            posted = true;
            requestId = Guid.NewGuid();
            amount.Value = 0;
            discount.Value = 0;
            reason.Clear();
            feeType.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            ModernMessageDialog.Show(this, DbMessage.UserMessage(ex),
                "Add Charge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            busy = false;
            if (!IsDisposed) SetEntryEnabled(true);
            PostingChanged?.Invoke(this, EventArgs.Empty);
        }
        if (!IsDisposed && posted)
        {
            ChargePosted?.Invoke(this, EventArgs.Empty);
            if (!IsDisposed) feeType.Focus();
        }
    }
}

