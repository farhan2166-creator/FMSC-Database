using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Finance.StudentAccounts;
/// <summary>Contextual fee-assignment dialog for the selected enrollment. Hosts <see cref="StudentFeeAssignmentsPanel"/> and exposes whether changes were applied.</summary>
public sealed class FeeAssignmentsDialog : ApplicationWindowForm
{
    private readonly StudentFeeAssignmentsPanel panel = new(allowManualPosting: false);
    /// <summary>True if any change (add / discontinue / amount change / discount / remove) was actually applied.</summary>
    public bool ChangesMade => panel.ChangesMade;
    public bool ReviewCompleted => panel.ReviewCompleted;
    public FeeAssignmentsDialog(int enrollmentId, int admissionId, string studentDisplay)
    {
        Text = $"Fee Discounts & Transport — {studentDisplay}";
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
/// <summary>Add a recurring non-transport fee. Transport uses the shared transport dialog.</summary>
public sealed class AddFeeAssignmentForm : ApplicationWindowForm
{
    private readonly int enrollmentId;
    private readonly ModernComboBox feeType = HumanUi.Combo();
    /* Mark all invalid fields together before submission. */
    private readonly FormValidator validator = new();
    /* Show an empty choice set inline. */
    private readonly Label availabilityNote = new() {
        AutoSize = true,
        Dock = DockStyle.Top,
        ForeColor = AppTheme.MutedText,
        Font = AppTheme.NormalFont,
        Padding = new Padding(UiDesignSystem.Spacing.Xs, UiDesignSystem.Spacing.Xs, 0, 0)
    };
    private readonly ModernButton addButton = new() {
        SavesChanges = true, Text = "Add Fee",
        Width = AppScale.Scale(110),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Success"
    };
    public AddFeeAssignmentForm(int enrollmentId)
    {
        this.enrollmentId = enrollmentId;
        Text = "Add Fee";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = AppMetrics.CompactEditorDialogClientSize;
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
            "Add Fee",
            "Recurring class fees. Use Start Transport for transport and Fee Collection for purchases.",
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
                    if(isTransport) continue;
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
                /* When no fee can be added, disable Add Fee and explain inline. */
                availabilityNote.Text =
                    configured != null
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
