using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Finance.StudentAccounts;
/// <summary>Contextual fee-assignment dialog for the selected enrollment. Hosts <see cref="StudentFeeAssignmentsPanel"/> and exposes whether changes were applied.</summary>
public sealed class FeeAssignmentsDialog : ApplicationWindowForm
{
    private readonly StudentFeeAssignmentsPanel panel = new();
    /// <summary>True if any change (add / discontinue / amount change / discount / remove) was actually applied.</summary>
    public bool ChangesMade => panel.ChangesMade;
    public bool ReviewCompleted => panel.ReviewCompleted;
    public FeeAssignmentsDialog(int enrollmentId, int admissionId, string studentDisplay)
    {
        Text = "Fee Discounts & Transport";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Rectangle workArea = Screen.FromControl(this).WorkingArea;
        ClientSize = new Size(
            Math.Min(AppScale.Scale(1320, AppScale.ControlSizeScale), workArea.Width - AppScale.Scale(48)),
            Math.Min(AppScale.Scale(650, AppScale.ControlSizeScale), workArea.Height - AppScale.Scale(80)));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        BuildLayout();
        panel.SetStudent(enrollmentId, admissionId);
        FormClosing += (_, e) => {
            if (panel.IsBusy) e.Cancel = true;
        };
    }
    private void BuildLayout()
    {
        var closeButton = new ModernButton {
            Text = UiText.Actions.Close,
            Width = AppScale.Scale(105),
            Height = AppMetrics.ButtonHeight,
            ThemeRole = "Secondary",
            DialogResult = DialogResult.OK
        };
        var assignedCard = HumanUi.Card(
            "Student Fees",
            string.Empty,
            AppTheme.ModuleAccent("Finance"));
        assignedCard.Content.Controls.Add(panel);
        var actions = HumanUi.ButtonRow(closeButton);
        actions.Dock = DockStyle.Fill;
        actions.Margin = new Padding(0);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(AppScale.Scale(16)),
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(assignedCard, 0, 0);
        root.Controls.Add(actions, 0, 1);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = closeButton;
    }
}
/// <summary>Add recurring fees or start transport using the protected restart workflow.</summary>
public sealed class AddFeeAssignmentForm : ApplicationWindowForm
{
    private readonly int enrollmentId;
    private readonly bool transportOnly;
    private readonly ModernComboBox feeType = HumanUi.Combo();
    /* UX - field problems are marked on the fields, all at once, not shown in message boxes. */
    private readonly FormValidator validator = new();
    /* UX - "nothing left to add" is said inline, under the field, instead of in a message box. */
    private readonly Label availabilityNote = new() {
        AutoSize = true,
        Dock = DockStyle.Top,
        ForeColor = AppTheme.MutedText,
        Font = AppTheme.NormalFont,
        Padding = new Padding(UiDesignSystem.Spacing.Xs, UiDesignSystem.Spacing.Xs, 0, 0)
    };
    private readonly ModernButton addButton = new() {
        Text = "Add Fee",
        Width = AppScale.Scale(110),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Success"
    };
    public AddFeeAssignmentForm(int enrollmentId, bool transportOnly = false)
    {
        this.enrollmentId = enrollmentId;
        this.transportOnly=transportOnly;
        Text = transportOnly ? "Start / Restart Transport" : "Add Fee";
        if(transportOnly) addButton.Text="Start Transport";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(AppScale.Scale(460, AppScale.ControlSizeScale), AppScale.Scale(265, AppScale.ControlSizeScale));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        BuildLayout();
        LoadAvailableFeeTypes();
        Disposed += (_, _) => validator.Dispose();
        addButton.Click += (_, _) => Add();
    }
    private void BuildLayout()
    {
        var cancel = new ModernButton {
            Text = UiText.Actions.Cancel,
            Width = AppScale.Scale(105),
            Height = AppMetrics.ButtonHeight,
            ThemeRole = "Secondary",
            DialogResult = DialogResult.Cancel
        };
        var card = HumanUi.Card(
            transportOnly ? "Start / Restart Transport" : "Add Fee",
            transportOnly ? "Starts after paid months and previous transport periods. Existing bills stay unchanged."
                : "Recurring class fees. Use Start / Restart Transport for transport and Fee Collection for purchases.",
            AppTheme.ModuleAccent("Finance"));
        var fields = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var feeTypeField = HumanUi.Field("Fee Type", feeType, true);
        feeTypeField.Dock = DockStyle.Top;
        feeTypeField.Height = AppMetrics.FieldRowHeight;
        fields.Controls.Add(feeTypeField, 0, 0);
        fields.Controls.Add(availabilityNote, 0, 1);
        card.Content.Controls.Add(fields);
        var buttons = HumanUi.ButtonRow(addButton, cancel);
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
        AcceptButton = addButton;
        CancelButton = cancel;
    }
    private void LoadAvailableFeeTypes()
    {
        try
        {
            var feeData = DatabaseHelper.QueryUserSet(
                "GetStudentFeeAssignments",
                DatabaseHelper.Int("@EnrollmentId", enrollmentId),
                DatabaseHelper.Bit("@IncludeAvailableFees", true));
            if (feeData.Tables.Count != 2)
                throw new InvalidOperationException("The placement fee options could not be loaded completely.");
            var currentlyOpen = feeData.Tables[0];
            var openFeeTypeIds = currentlyOpen.AsEnumerable()
                .Where(r => r["EffectiveTo"] == DBNull.Value ||
                    !(r.Table.Columns.Contains("IsTransportLinked") && Convert.ToBoolean(r["IsTransportLinked"])) &&
                    Convert.ToDateTime(r["EffectiveTo"]) >= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1))
                .Select(r => Convert.ToInt32(r["FeeTypeId"]))
                .ToHashSet();
            DataTable configured = feeData.Tables[1];
            var available = new DataTable();
            available.Columns.Add("FeeTypeId", typeof(int));
            available.Columns.Add("FeeTypeName", typeof(string));
            if (configured != null)
            {
                var seen = new HashSet<int>();
                foreach (DataRow row in configured.Rows)
                {
                    bool isTransport=row.Table.Columns.Contains("IsTransportLinked") && Convert.ToBoolean(row["IsTransportLinked"]);
                    if(isTransport!=transportOnly) continue;
                    if (configured.Columns.Contains("Frequency") &&
                        string.Equals(Convert.ToString(row["Frequency"]), "As Needed",
                            StringComparison.OrdinalIgnoreCase))
                        continue; // Purchases are posted as charges in Fee Collection.
                    int id = Convert.ToInt32(row["FeeTypeId"]);
                    if (openFeeTypeIds.Contains(id) || !seen.Add(id))
                        continue;
                    string name = Convert.ToString(row["FeeTypeName"]) ?? "";
                    string amount = !row.Table.Columns.Contains("Amount") || row["Amount"] == DBNull.Value
                        ? ""
                        : Convert.ToDecimal(row["Amount"]).ToString("N0");
                    string frequency = configured.Columns.Contains("Frequency") && row["Frequency"] != DBNull.Value
                        ? Convert.ToString(row["Frequency"]) ?? ""
                        : "";
                    string detail = string.Join(" / ", new[] { amount, frequency }.Where(x => x.Length > 0));
                    available.Rows.Add(id, detail.Length > 0 ? $"{name}  -  {detail}" : name);
                }
            }
            feeType.DisplayMember = "FeeTypeName";
            feeType.ValueMember = "FeeTypeId";
            feeType.DataSource = available;
            addButton.Enabled = available.Rows.Count > 0;
            feeType.Enabled = available.Rows.Count > 0;
            if (available.Rows.Count == 0)
            {
                /* UX - nothing to add is a state, not an event: said under the field, with Add Fee already disabled above, and no message box to dismiss. */
                availabilityNote.Text =
                    transportOnly ? "Transport is already assigned, or no monthly transport rate is configured for this class."
                    : configured != null
                        ? "Every recurring class fee is already assigned. Use Add Charge in Fee Collection for books, examinations, and other purchases."
                        : "Every fee type is already assigned to this placement.";
            }
        }
        catch (Exception ex)
        {
            ModernMessageDialog.Show(
                this,
                DbMessage.UserMessage(ex),
                "Add Fee",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            addButton.Enabled = false;
        }
    }
    /* Drain queued gestures while the synchronous write is held to prevent duplicate assignment. */
    private bool submitting;
    private void Add()
    {
        if (submitting || DialogResult == DialogResult.OK)
            return;
        int feeTypeId = 0;
        validator.Begin();
        validator.Check(
            feeType,
            feeType.SelectedValue != null &&
                int.TryParse(Convert.ToString(feeType.SelectedValue), out feeTypeId) &&
                feeTypeId > 0,
            UiText.Validation.SelectOne,
            () => feeType.SelectedValue != null);
        if (!validator.Finish())
            return;
        submitting = true;
        try
        {
            StudentFeeAssignmentAdd.Save(enrollmentId,feeTypeId);
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            ModernMessageDialog.Show(
                this,
                DbMessage.UserMessage(ex),
                "Add Fee",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            feeType.Focus();
        }
        finally
        {
            submitting = false;
        }
    }
}
/// <summary>Stops transport through the existing writer. An optional note supplements the recorded action summary.</summary>
public sealed class DiscontinueFeeAssignmentForm : ApplicationWindowForm
{
    /* UX - field problems are marked on the fields, all at once, not shown in message boxes. */
    private readonly FormValidator validator = new();
    private readonly int studentFeeAssignmentId;
    private sealed record StopMonth(DateTime Month) {
        public override string ToString() => Month.ToString("MMMM yyyy");
    }
    private readonly ModernComboBox effectiveTo = HumanUi.Combo();
    private readonly DataTable stopContext;
    private DateTime SelectedStopMonth => ((StopMonth)effectiveTo.SelectedItem!).Month;
    private readonly ModernTextBox reason = HumanUi.TextBox(true, 500);
    /* The chosen month is the assignment's LAST active month. */
    private readonly Label effectiveConsequence = ConsequenceLabel();
    private readonly ModernButton discontinueButton = new() {
        Text = "Stop Transport",
        Width = AppScale.Scale(120),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Warning"
    };
    /* The month and reason this form was loaded with, so Cancel on an untouched form closes without asking about changes that are not there. */
    private readonly DateTime initialEffectiveTo;
    private readonly string initialReason;
    /* Set only once the discontinue has been written, so that path closes without the unsaved-changes prompt. */
    private bool savedSuccessfully;
    /* Confirm once: setting DialogResult also raises FormClosing. */
    private bool discardConfirmed;
    public DiscontinueFeeAssignmentForm(int studentFeeAssignmentId, string feeTypeName, DateTime? effectiveFrom)
    {
        this.studentFeeAssignmentId = studentFeeAssignmentId;
        stopContext = TransportStop.Context(studentFeeAssignmentId);
        if (stopContext.Rows.Count == 0) throw new InvalidOperationException("Transport has changed. Reload the student fees.");
        DataRow context = stopContext.Rows[0];
        DateTime first = FirstOfMonth(Convert.ToDateTime(context["BillingStartDate"] == DBNull.Value
            ? context["EnrollmentDate"] : context["BillingStartDate"]));
        if (context["LastProtectedMonth"] != DBNull.Value)
            first = new[] { first, FirstOfMonth(Convert.ToDateTime(context["LastProtectedMonth"])).AddMonths(1) }.Max();
        DateTime last = FirstOfMonth(Convert.ToDateTime(context["EndDate"])).AddMonths(1);
        for (DateTime month = first; month <= last; month = month.AddMonths(1))
            effectiveTo.Items.Add(new StopMonth(month));
        if (effectiveTo.Items.Count > 0) effectiveTo.SelectedIndex = 0;
        discontinueButton.Enabled = effectiveTo.Items.Count > 0;
        Text = "Stop Transport";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(AppScale.Scale(500, AppScale.ControlSizeScale), AppScale.Scale(420, AppScale.ControlSizeScale));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        BuildLayout(feeTypeName);
        initialEffectiveTo = discontinueButton.Enabled ? SelectedStopMonth : DateTime.MinValue;
        initialReason = reason.Text;
        UpdateEffectiveConsequence();
        effectiveTo.SelectedIndexChanged += (_, _) => UpdateEffectiveConsequence();
        discontinueButton.Click += (_, _) => Save();
        /* The window's own close (X, Alt+F4, Esc) is guarded the same way; programmatic closes are left alone. */
        FormClosing += (_, e) => {
            if (e.CloseReason == CloseReason.UserClosing)
                e.Cancel = !ConfirmDiscard();
        };
    }
    private void UpdateEffectiveConsequence()
    {
        if (effectiveTo.SelectedItem is not StopMonth selected)
        {
            effectiveConsequence.Text = "No stopping month is available. Paid or partially paid months require a financial correction first.";
            return;
        }
        var charges = stopContext.AsEnumerable().Where(r => r["BillingMonth"] != DBNull.Value &&
            Convert.ToDateTime(r["BillingMonth"]) >= selected.Month && Convert.ToString(r["Status"]) != "Cancelled").ToArray();
        effectiveConsequence.Text = $"No transport charges from {selected.Month:MMMM yyyy}.\r\n"
            + $"{charges.Length} unpaid charge(s), Rs. {charges.Sum(r => Convert.ToDecimal(r["NetAmount"])):N0}, will be cancelled.\r\n"
            + "Paid and partially paid months are excluded. Already cancelled charges stay cancelled.";
    }
    private static DateTime FirstOfMonth(DateTime value) =>
        new(value.Year, value.Month, 1);
    /* AutoSize so a wrapped consequence sentence is never clipped. */
    private static Label ConsequenceLabel() => new() {
        Dock = DockStyle.Fill,
        AutoSize = true,
        MaximumSize = new Size(AppScale.Scale(640), 0),
        Margin = new Padding(0, 0, 0, AppScale.Scale(6)),
        Font = AppTheme.SmallFont,
        ForeColor = AppTheme.MutedText
    };
    private void BuildLayout(string feeTypeName)
    {
        var cancel = new ModernButton {
            Text = UiText.Actions.Cancel,
            Width = AppScale.Scale(105),
            Height = AppMetrics.ButtonHeight,
            ThemeRole = "Secondary"
        };
        /* Confirm before assigning DialogResult so Cancel can keep the dialog open. */
        cancel.Click += (_, _) => {
            if (!ConfirmDiscard())
                return;
            DialogResult = DialogResult.Cancel;
            Close();
        };
        var card = HumanUi.Card(
            $"Stop: {feeTypeName}",
            "Stop service and cancel unpaid transport charges from the selected month onward.",
            AppTheme.ModuleAccent("Finance"));
        var fields = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        /* caption and consequence both use the field's real meaning - the selected month is the last month that stays active. */
        fields.Controls.Add(HumanUi.Field("Stop Transport From", effectiveTo, true), 0, 0);
        fields.Controls.Add(effectiveConsequence, 0, 1);
        fields.Controls.Add(HumanUi.Field("Reason (optional)", reason), 0, 2);
        card.Content.Controls.Add(fields);
        var buttons = HumanUi.ButtonRow(discontinueButton, cancel);
        buttons.Dock = DockStyle.Fill;
        buttons.Margin = new Padding(0);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(AppScale.Scale(16))
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(card, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = discontinueButton;
        CancelButton = cancel;
        FormFlow.EnterMovesToNext(this);
        Disposed += (_, _) => validator.Dispose();
    }
    private bool submitting;
    private void Save()
    {
        if (submitting || savedSuccessfully)
            return;
        validator.Begin();
        validator.Rule(effectiveTo, () => effectiveTo.SelectedItem is StopMonth, "Choose a stopping month");
        if (!validator.Finish())
            return;
        submitting = true;
        try
        {
            string recordedReason = reason.Text.Trim();
            if (recordedReason.Length == 0)
                recordedReason = $"Stop transport from {SelectedStopMonth:MMMM yyyy}.";
            TransportStop.Save(studentFeeAssignmentId, SelectedStopMonth, recordedReason);
            /* The write committed, so this close is not a discard. */
            savedSuccessfully = true;
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            ModernMessageDialog.Show(
                this,
                DbMessage.UserMessage(ex),
                "Discontinue Fee",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            reason.Focus();
        }
        finally
        {
            submitting = false;
        }
    }
    /// <summary>Compare last active month and reason with the loaded values; saving is not dirty.</summary>
    private bool IsDirty() =>
        !savedSuccessfully &&
        DialogResult != DialogResult.OK &&
        ((effectiveTo.SelectedItem is StopMonth selected ? selected.Month : DateTime.MinValue) != initialEffectiveTo ||
         reason.Text != initialReason);
    /// <summary>asks at most once for a close gesture. True when the caller may proceed with the close.</summary>
    private bool ConfirmDiscard()
    {
        return ModernMessageDialog.ConfirmDiscardOnce(this, IsDirty, ref discardConfirmed);
    }
}
