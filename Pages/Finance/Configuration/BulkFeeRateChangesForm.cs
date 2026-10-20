using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.Configuration;
/// <summary>Adjust Fees tab: raise or lower many fee configurations at once, by a percentage or a flat amount, with optional rounding.</summary>
public sealed class BulkFeeRateChangesForm : ApplicationWindowForm, IUnsavedChanges
{
    public bool HasUnsavedChanges => IsDirty();
    public string UnsavedChangesMessage => "Discard the unapplied bulk fee-rate changes?";
    private sealed class PlanRow
    {
        public int FeeConfigurationId;
        public int CampusId;
        public string CampusName = "";
        public int ClassId;
        public string ClassName = "";
        public int FeeTypeId;
        public string FeeTypeName = "";
        public string Frequency = "";
        public string Remarks = "";
        public decimal OldAmount;
        public decimal NewAmount;
    }
    private const int AllId = 0;
    private readonly ModernComboBox campusScope = HumanUi.Combo();
    private readonly ModernComboBox classScope = HumanUi.Combo();
    private readonly ModernComboBox feeTypeScope = HumanUi.Combo();
    private readonly ModernComboBox frequencyScope = HumanUi.Combo();
    private readonly RadioButton typePercentage = new() {
        Text = "Percentage",
        AutoSize = true,
        Checked = true
    };
    private readonly RadioButton typeFlatAmount = new() {
        Text = "Flat Amount (Rs.)",
        AutoSize = true
    };
    private readonly RadioButton directionIncrease = new() {
        Text = "Increase",
        AutoSize = true,
        Checked = true
    };
    private readonly RadioButton directionDecrease = new() {
        Text = "Decrease",
        AutoSize = true
    };
    private readonly ModernNumericTextBox adjustmentValue = new() {
        Minimum = 0,
        Maximum = 1000000,
        DecimalPlaces = 2,
        Dock = DockStyle.Fill,
        Height = AppMetrics.InputControlHeight,
        Font = AppTheme.NormalFont
    };
    private readonly ModernComboBox roundingScope = HumanUi.Combo();
    private readonly DataGridView preview = HumanUi.Grid(false);
    private readonly Label summaryLabel = new() {
        AutoSize = true,
        ForeColor = AppTheme.MutedText,
        Font = AppTheme.NormalFont
    };
    private readonly ModernButton previewButton = new() {
        Text = UiText.Actions.Preview,
        Width = AppScale.Scale(110),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Secondary"
    };
    private readonly ModernButton applyButton = new() {
        Text = "Apply Changes",
        Width = AppScale.Scale(145),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Warning",
        Enabled = false
    };
    private readonly ModernButton selectAllButton = new() { Text = UiText.Actions.SelectAll, ThemeRole = "Secondary" };
    private readonly ModernButton selectNoneButton = new() { Text = UiText.Actions.SelectNone, ThemeRole = "Secondary" };
    private List<PlanRow> SelectedPlan() => preview.Rows.Cast<DataGridViewRow>()
        .Where(row => row.Tag is PlanRow item && item.OldAmount != item.NewAmount && row.Cells["Include"].Value is true)
        .Select(row => (PlanRow)row.Tag!).ToList();
    private void SelectPreviewRows(bool include)
    {
        foreach (DataGridViewRow row in preview.Rows)
            row.Cells["Include"].Value = include && row.Tag is PlanRow item && item.OldAmount != item.NewAmount;
        UpdateSelectedSummary();
    }
    private void UpdateSelectedSummary()
    {
        var selected = SelectedPlan();
        int unchanged = plan.Count(row => row.OldAmount == row.NewAmount);
        int zeroRates = selected.Count(row => row.NewAmount == 0m);
        summaryLabel.Text = $"{selected.Count} of {plan.Count} fee rates selected. {unchanged} unchanged."
            + (previewUnreadableClasses > 0 ? $" {previewUnreadableClasses} class(es) could not be read." : "")
            + (zeroRates > 0 ? $" {zeroRates} selected rate(s) become Rs. 0." : "")
            + (selected.Count > 0 ? $" Total rate change: Rs. {selected.Sum(row => row.NewAmount - row.OldAmount):N2}." : "");
        applyButton.Enabled = selected.Count > 0 && appliedPreviewSignature is not null;
        selectAllButton.Enabled = selectNoneButton.Enabled = plan.Count > 0;
    }
    private DataTable campusesTable = new();
    private List<PlanRow> plan = new();
    private int? pendingClassSelection;
    private string previewAdjustmentText = "";
    /* The settings the plan on screen was previewed under. */
    private string? appliedPreviewSignature;
    /* how many classes the preview could not read (GetFeeConfigurations threw for them). */
    private int previewUnreadableClasses;
    /// <summary>every control that decides what a preview would contain, in one comparable string.</summary>
    private string PreviewSignature() =>
        string.Join(
            '|',
            campusScope.SelectedIndex,
            classScope.SelectedIndex,
            feeTypeScope.SelectedIndex,
            frequencyScope.SelectedIndex,
            roundingScope.SelectedIndex,
            typePercentage.Checked,
            typeFlatAmount.Checked,
            directionIncrease.Checked,
            directionDecrease.Checked,
            adjustmentValue.Text.Trim());
    private string previewScopeText = "";
    private string previewAmountRangeText = "";
    private string previewNewAmountRangeText = "";
    /* The scope and adjustment choices this dialog was opened with, so Close on an untouched dialog asks nothing. */
    private string loadedSnapshot = "";
    /* Set only once the changes have been applied, so that path closes without the unsaved-changes prompt. */
    private bool savedSuccessfully;
    /* Confirm once: setting DialogResult also raises FormClosing. */
    private bool discardConfirmed;
    public bool Applied { get; private set; }
    /// <summary>Atomic application succeeds for the entire plan or none of it.</summary>
    public int AppliedCount { get; private set; }
    public BulkFeeRateChangesForm(int currentCampusId = 0, int currentClassId = 0)
        : this(currentCampusId, currentClassId, true) { }
    internal BulkFeeRateChangesForm(int currentCampusId, int currentClassId, bool initialize)
    {
        Text = PageTitles.BulkFeeRateChanges;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(AppScale.Scale(900), AppScale.Scale(640));
        MinimumSize = new Size(AppScale.Scale(720), AppScale.Scale(420));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        BuildLayout();
        FinanceHelp.Attach(this,adjustmentValue,"Enter the rate change, choosing percentage or rupees and increase or decrease. Preview the new rates before saving.","A 10% increase on Rs. 1,000 gives Rs. 1,100; a flat Rs. 100 decrease gives Rs. 900 before any rounding choice.");
        FinanceHelp.Attach(this,applyButton,FinanceHelp.Topic.FeeRate);
        FinanceHelp.Attach(this,previewButton,FinanceHelp.Topic.Review);
        SetSaveShortcut(applyButton);
        if (initialize) LoadScopeSources(currentCampusId, currentClassId);
        campusScope.SelectedIndexChanged += (_, _) => ReloadClassScope();
        previewButton.Click += (_, _) => RunPreview();
        applyButton.Click += (_, _) => Apply();
        /* Invalidate the preview when any scope or adjustment input changes. */
        campusScope.SelectedIndexChanged += (_, _) => InvalidateBulkPreview();
        classScope.SelectedIndexChanged += (_, _) => InvalidateBulkPreview();
        feeTypeScope.SelectedIndexChanged += (_, _) => InvalidateBulkPreview();
        frequencyScope.SelectedIndexChanged += (_, _) => InvalidateBulkPreview();
        roundingScope.SelectedIndexChanged += (_, _) => InvalidateBulkPreview();
        adjustmentValue.TextChanged += (_, _) => InvalidateBulkPreview();
        typePercentage.CheckedChanged += (_, _) => InvalidateBulkPreview();
        typeFlatAmount.CheckedChanged += (_, _) => InvalidateBulkPreview();
        directionIncrease.CheckedChanged += (_, _) => InvalidateBulkPreview();
        directionDecrease.CheckedChanged += (_, _) => InvalidateBulkPreview();
        /* The choices the dialog opened with, taken once the initial scope has finished loading. */
        CaptureAppliedBaseline();
        /* The window's own close (X, Alt+F4, Esc) is guarded the same way; programmatic closes are left alone. */
        FormClosing += (_, e) => {
            if (e.CloseReason == CloseReason.UserClosing)
                e.Cancel = !ConfirmDiscard();
        };
    }
    private Label? titleLabel;
    private TableLayoutPanel? rootPanel;
    private readonly ToolTip tip = new();
    private bool embedded;
    private readonly ModernButton closeButton = new() {
        Text = UiText.Actions.Close,
        Width = AppScale.Scale(105),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Secondary"
    };
    public static BulkFeeRateChangesForm CreateEmbedded()
    {
        var form = new BulkFeeRateChangesForm();
        form.PrepareForEmbedding();
        return form;
    }
    /// <summary>Hide Close and bypass modal close handling when embedded in a workspace.</summary>
    public void PrepareForEmbedding()
    {
        embedded = true;
        TopLevel = false;
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        closeButton.Visible = false;
        CancelButton = null;
        if (titleLabel is not null)
            titleLabel.Visible = false;
        if (rootPanel is not null)
            rootPanel.Padding = new Padding(4, 4, 4, 6);
    }
    private void BuildLayout()
    {
        titleLabel = new Label {
            Text = PageTitles.BulkFeeRateChanges,
            Dock = DockStyle.Top,
            Height = Math.Max(30, AppMetrics.TextHeight(AppTheme.PageTitleFont) + 8),
            Font = AppTheme.PageTitleFont,
            ForeColor = AppTheme.Text
        };
        var help = FeeToolLayout.Help(
            "Choose the fees and adjustment amount, then preview the changes before applying them.",
            tip);
        /* Present direction, amount and unit as one adjustment. */
        typePercentage.Text = "Percent (%)";
        typeFlatAmount.Text = "Flat (Rs.)";
        var directionPanel = ChoicePanel(directionIncrease, directionDecrease);
        var typePanel = ChoicePanel(typePercentage, typeFlatAmount);
        adjustmentValue.Dock = DockStyle.Fill;
        var scopeRow = FeeToolLayout.FieldRow(
            ("Campus", campusScope, false),
            ("Class", classScope, false),
            ("Fee type", feeTypeScope, false),
            ("Frequency", frequencyScope, false));
        var changeRow = FeeToolLayout.FieldRow(
            ("Change", directionPanel, false),
            ("Amount", adjustmentValue, false),
            ("Type", typePanel, false),
            ("Rounding", roundingScope, false));
        preview.Dock = DockStyle.Fill;
        preview.AutoGenerateColumns = false;
        preview.ReadOnly = false;
        preview.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", HeaderText = "Apply", Width = AppScale.Scale(55) });
        preview.Columns.Add("Campus", "Campus");
        preview.Columns.Add("Class", "Class");
        preview.Columns.Add("FeeType", UiText.Fees.FeeType);
        preview.Columns.Add("Frequency", UiText.Fees.Frequency);
        preview.Columns.Add("OldAmount", UiText.Fees.CurrentAmount);
        preview.Columns.Add("NewAmount", UiText.Fees.NewAmount);
        preview.Columns.Add("Change", "Change (Rs.)");
        foreach (DataGridViewColumn column in preview.Columns)
            column.ReadOnly = column.Name != "Include";
        preview.CurrentCellDirtyStateChanged += (_, _) => {
            if (preview.IsCurrentCellDirty) preview.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        preview.CellValueChanged += (_, e) => {
            if (e.RowIndex >= 0 && e.ColumnIndex == preview.Columns["Include"].Index)
                UpdateSelectedSummary();
        };
        selectAllButton.Click += (_, _) => SelectPreviewRows(true);
        selectNoneButton.Click += (_, _) => SelectPreviewRows(false);
        selectAllButton.Enabled = selectNoneButton.Enabled = false;
        HumanUi.ApplyGridStyle(preview, AppTheme.Warning);
        /* Confirm before assigning DialogResult so Cancel can keep the dialog open. */
        closeButton.Click += (_, _) => {
            if (embedded)
                return;
            if (!ConfirmDiscard())
                return;
            DialogResult = DialogResult.Cancel;
            Close();
        };
        var toolbar = FeeToolLayout.Toolbar(summaryLabel, tip, previewButton, selectAllButton, selectNoneButton, applyButton, closeButton);
        // Give the selection summary its own line so bulk actions do not squeeze it.
        Control toolbarActions = toolbar.GetControlFromPosition(1, 0)!;
        toolbar.SetCellPosition(summaryLabel, new TableLayoutPanelCellPosition(0, 1));
        toolbar.SetCellPosition(toolbarActions, new TableLayoutPanelCellPosition(0, 0));
        toolbar.ColumnCount = 1;
        toolbar.ColumnStyles.Clear();
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.RowCount = 2;
        toolbar.RowStyles.Clear();
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight));
        int summaryHeight = AppMetrics.TextHeight(summaryLabel.Font) * 2 + 8;
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, summaryHeight));
        toolbar.Height = AppMetrics.ButtonHeight + summaryHeight + toolbar.Padding.Vertical;
        rootPanel = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(14, 10, 14, 10)
        };
        for (int i = 0; i < 5; i++)
            rootPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootPanel.Controls.Add(titleLabel, 0, 0);
        rootPanel.Controls.Add(help, 0, 1);
        rootPanel.Controls.Add(scopeRow, 0, 2);
        rootPanel.Controls.Add(changeRow, 0, 3);
        rootPanel.Controls.Add(toolbar, 0, 4);
        rootPanel.Controls.Add(preview, 0, 5);
        Controls.Add(rootPanel);
        CancelButton = closeButton;
        Disposed += (_, _) => tip.Dispose();
    }
    /// <summary>Two radio buttons on one line, vertically centred on the input row.</summary>
    private static Control ChoicePanel(RadioButton first, RadioButton second)
    {
        first.Margin = new Padding(4, 0, 18, 0);
        second.Margin = new Padding(0, 0, 0, 0);
        var flow = new FlowLayoutPanel {
            Dock = DockStyle.Top,
            Height = AppMetrics.InputControlHeight,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0),
            Margin = new Padding(0)
        };
        flow.Controls.Add(first);
        flow.Controls.Add(second);
        return flow;
    }
    private void LoadScopeSources(int currentCampusId, int currentClassId)
    {
        campusesTable = DatabaseHelper.QueryUser("GetCampuses");
        var campusChoices = campusesTable.Clone();
        var allCampusRow = campusChoices.NewRow();
        allCampusRow["CampusId"] = AllId;
        allCampusRow["CampusName"] = "All Campuses";
        campusChoices.Rows.Add(allCampusRow);
        foreach (DataRow r in campusesTable.Rows)
            campusChoices.ImportRow(r);
        campusScope.DisplayMember = "CampusName";
        campusScope.ValueMember = "CampusId";
        campusScope.DataSource = campusChoices;
        campusScope.SelectedValue = currentCampusId > 0 ? currentCampusId : AllId;
        var feeTypesTable = DatabaseHelper.QueryUser("GetFeeTypes");
        var feeChoices = feeTypesTable.Clone();
        var allFeeRow = feeChoices.NewRow();
        allFeeRow["FeeTypeId"] = AllId;
        allFeeRow["FeeTypeName"] = "All Fee Types";
        feeChoices.Rows.Add(allFeeRow);
        foreach (DataRow r in feeTypesTable.Rows)
            feeChoices.ImportRow(r);
        feeTypeScope.DisplayMember = "FeeTypeName";
        feeTypeScope.ValueMember = "FeeTypeId";
        feeTypeScope.DataSource = feeChoices;
        feeTypeScope.SelectedValue = AllId;
        frequencyScope.Items.Clear();
        frequencyScope.Items.AddRange(new object[] {
            "All Frequencies", "Monthly", "Yearly", "As Needed"
        });
        frequencyScope.SelectedIndex = 0;
        roundingScope.Items.Clear();
        roundingScope.Items.AddRange(new object[] {
            "No rounding", "Nearest 10", "Nearest 50", "Nearest 100"
        });
        roundingScope.SelectedIndex = 0;
        pendingClassSelection = currentClassId > 0 ? currentClassId : (int?)null;
        ReloadClassScope();
    }
    private void ReloadClassScope()
    {
        int campusId = campusScope.SelectedValue == null
            ? AllId
            : Convert.ToInt32(campusScope.SelectedValue);
        DataTable classesSource;
        if (campusId == AllId)
        {
            classesSource = DatabaseHelper.QueryUser("GetClasses");
        }
        else
        {
            var assignments = DatabaseHelper.QueryUser(
                "GetClassSections",
                DatabaseHelper.Int("@CampusId", campusId),
                DatabaseHelper.Int("@ClassId", 0),
                DatabaseHelper.Int("@SectionId", 0));
            var distinctIds = assignments.AsEnumerable()
                .Select(r => Convert.ToInt32(r["ClassId"]))
                .ToHashSet();
            var allClasses = DatabaseHelper.QueryUser("GetClasses");
            classesSource = allClasses.Clone();
            foreach (DataRow row in LookupDisplayOrder.ForBinding(allClasses, "ClassId").Rows)
            {
                if (distinctIds.Contains(Convert.ToInt32(row["ClassId"])))
                    classesSource.ImportRow(row);
            }
        }
        var classChoices = classesSource.Clone();
        var allClassRow = classChoices.NewRow();
        allClassRow["ClassId"] = AllId;
        allClassRow["ClassName"] = "All Classes";
        classChoices.Rows.Add(allClassRow);
        foreach (DataRow r in classesSource.Rows)
            classChoices.ImportRow(r);
        int? desired = pendingClassSelection ??
            (classScope.SelectedValue == null ? null : Convert.ToInt32(classScope.SelectedValue));
        pendingClassSelection = null;
        bool desiredExists = desired.HasValue &&
            classChoices.AsEnumerable().Any(r => Convert.ToInt32(r["ClassId"]) == desired.Value);
        classScope.DataSource = null;
        classScope.DisplayMember = "ClassName";
        classScope.ValueMember = "ClassId";
        classScope.DataSource = classChoices;
        classScope.SelectedValue = desiredExists ? desired!.Value : AllId;
    }
    private void RunPreview()
    {
        appliedPreviewSignature = null;
        applyButton.Enabled = selectAllButton.Enabled = selectNoneButton.Enabled = false;
        plan = new List<PlanRow>();
        preview.Rows.Clear();
        /* Reset the preview count before any early return. */
        previewUnreadableClasses = 0;
        try
        {
            int campusScopeId = campusScope.SelectedValue == null
                ? AllId
                : Convert.ToInt32(campusScope.SelectedValue);
            int classScopeId = classScope.SelectedValue == null
                ? AllId
                : Convert.ToInt32(classScope.SelectedValue);
            int feeTypeScopeId = feeTypeScope.SelectedValue == null
                ? AllId
                : Convert.ToInt32(feeTypeScope.SelectedValue);
            string frequencyFilter = frequencyScope.SelectedItem?.ToString() ?? "All Frequencies";
            decimal magnitude = adjustmentValue.Value;
            if (magnitude <= 0)
            {
                summaryLabel.Text = "Enter an adjustment value greater than zero.";
                applyButton.Enabled = false;
                return;
            }
            int sign = directionIncrease.Checked ? 1 : -1;
            bool percentage = typePercentage.Checked;
            if(!percentage)WholeRupees.Require(magnitude);
            int roundTo = roundingScope.SelectedIndex switch {
                1 => 10,
                2 => 50,
                3 => 100,
                _ => 0
            };
            var campuses = campusesTable.AsEnumerable()
                .Where(r => campusScopeId == AllId || Convert.ToInt32(r["CampusId"]) == campusScopeId)
                .Select(r => (Id: Convert.ToInt32(r["CampusId"]), Name: Convert.ToString(r["CampusName"]) ?? ""))
                .ToList();
            int negativeClampCount = 0;
            foreach (var c in campuses)
            {
                var assignments = DatabaseHelper.QueryUser(
                    "GetClassSections",
                    DatabaseHelper.Int("@CampusId", c.Id),
                    DatabaseHelper.Int("@ClassId", classScopeId),
                    DatabaseHelper.Int("@SectionId", 0));
                var classesForCampus = assignments.AsEnumerable()
                    .Select(r => (Id: Convert.ToInt32(r["ClassId"]), Name: Convert.ToString(r["ClassName"]) ?? ""))
                    .Distinct()
                    .ToList();
                var feeSnapshot = new Lazy<DataTable>(() => DatabaseHelper.QueryUser(
                    "GetFeeConfigurations",
                    DatabaseHelper.Int("@CampusId", c.Id),
                    DatabaseHelper.Int("@ClassId", classesForCampus.Count == 1 ? classesForCampus[0].Id : 0),
                    DatabaseHelper.Bit("@AllClasses", classesForCampus.Count > 1)));
                foreach (var cls in classesForCampus)
                {
                    DataTable fees;
                    try
                    {
                        fees = new DataView(feeSnapshot.Value) { RowFilter = $"ClassId = {cls.Id}" }.ToTable();
                    }
                    catch (Exception nonFatalException31)
                    {
                        FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException31, "BulkFeeRateChangesForm: RunPreview fallback");
                        /* Count classes omitted from the plan because they have no configurations. */
                        previewUnreadableClasses++;
                        continue;
                    }
                    foreach (DataRow row in fees.Rows)
                    {
                        int feeTypeId = Convert.ToInt32(row["FeeTypeId"]);
                        if (feeTypeScopeId != AllId && feeTypeId != feeTypeScopeId)
                            continue;
                        string rowFrequency = Convert.ToString(row["Frequency"]) ?? "";
                        if (!string.Equals(frequencyFilter, "All Frequencies", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(rowFrequency, frequencyFilter, StringComparison.OrdinalIgnoreCase))
                            continue;
                        decimal oldAmount = Convert.ToDecimal(row["Amount"]);
                        decimal newAmount = percentage
                            ? oldAmount * (1 + sign * (magnitude / 100m))
                            : oldAmount + sign * magnitude;
                        if (newAmount < 0)
                        {
                            newAmount = 0;
                            negativeClampCount++;
                        }
                        if (roundTo > 0)
                            newAmount = Math.Round(newAmount / roundTo, MidpointRounding.AwayFromZero) * roundTo;
                        if(!WholeRupees.IsWhole(newAmount))
                            throw new ArgumentException("This adjustment produces fractional fees. Select an explicit rounding option or change the adjustment; rates are not rounded automatically.");
                        plan.Add(new PlanRow {
                            FeeConfigurationId = Convert.ToInt32(row["FeeConfigurationId"]),
                            CampusId = c.Id,
                            CampusName = c.Name,
                            ClassId = cls.Id,
                            ClassName = cls.Name,
                            FeeTypeId = feeTypeId,
                            FeeTypeName = Convert.ToString(row["FeeTypeName"]) ?? "",
                            Frequency = rowFrequency,
                            Remarks = row["Remarks"] == DBNull.Value ? "" : Convert.ToString(row["Remarks"]) ?? "",
                            OldAmount = oldAmount,
                            NewAmount = newAmount
                        });
                    }
                }
            }
            foreach (var row in plan
                         .OrderBy(p => p.CampusName)
                         .ThenBy(p => p.ClassName)
                         .ThenBy(p => p.FeeTypeName))
            {
                int index = preview.Rows.Add(
                    row.OldAmount != row.NewAmount,
                    row.CampusName,
                    row.ClassName,
                    row.FeeTypeName,
                    row.Frequency,
                    /* Show decimals in preview so fractional candidates are not displayed as rounded amounts. */
                    $"Rs. {row.OldAmount:N2}",
                    $"Rs. {row.NewAmount:N2}",
                    $"{row.NewAmount - row.OldAmount:+0.00;-0.00;0.00}");
                preview.Rows[index].Tag = row;
                if (row.OldAmount == row.NewAmount) preview.Rows[index].Cells["Include"].ReadOnly = true;
            }
            string unreadableText = previewUnreadableClasses > 0
                ? $" {previewUnreadableClasses} class(es) could not be read and are not included."
                : "";
            summaryLabel.Text = plan.Count == 0
                ? "No fee configurations match this scope." + unreadableText
                : $"{plan.Count} fee(s) will change." +
                  (negativeClampCount > 0
                      ? $" {negativeClampCount} clamped to Rs. 0 (would have gone negative)."
                      : "") +
                  unreadableText;
            /* recorded with the plan it describes; Apply re-checks it before sending the TVP. */
            appliedPreviewSignature = PreviewSignature();
            UpdateSelectedSummary();
            if (plan.Count == 0)
            {
                previewScopeText = "";
                previewAdjustmentText = "";
                previewAmountRangeText = "";
                previewNewAmountRangeText = "";
                return;
            }
            string campusText = SelectedComboText(campusScope, "All Campuses");
            string classText = SelectedComboText(classScope, "All Classes");
            string feeTypeText = SelectedComboText(feeTypeScope, "All Fee Types");
            string frequencyText = frequencyScope.SelectedItem?.ToString() ?? "All Frequencies";
            string roundingText = roundingScope.SelectedItem?.ToString() ?? "No rounding";
            var scopeParts = new List<string> {
                $"Campus: {campusText}",
                $"Class: {classText}",
                $"Fee type: {feeTypeText}"
            };
            if (!string.Equals(frequencyText, "All Frequencies", StringComparison.OrdinalIgnoreCase))
                scopeParts.Add($"Frequency: {frequencyText}");
            previewScopeText = string.Join("  |  ", scopeParts);
            string magnitudeText = percentage
                ? $"{magnitude:0.##}%"
                : $"Rs. {magnitude:N2}";
            previewAdjustmentText =
                $"{(sign > 0 ? "Increase" : "Decrease")} {magnitudeText} ({roundingText.ToLowerInvariant()})";
            decimal minOld = plan.Min(p => p.OldAmount);
            decimal maxOld = plan.Max(p => p.OldAmount);
            decimal minNew = plan.Min(p => p.NewAmount);
            decimal maxNew = plan.Max(p => p.NewAmount);
            previewAmountRangeText = minOld == maxOld
                ? $"Rs. {minOld:N2}"
                : $"Rs. {minOld:N2} to Rs. {maxOld:N2}";
            previewNewAmountRangeText = minNew == maxNew
                ? $"Rs. {minNew:N2}"
                : $"Rs. {minNew:N2} to Rs. {maxNew:N2}";
        }
        catch (Exception ex)
        {
            summaryLabel.Text = DbMessage.UserMessage(ex);
            applyButton.Enabled = false;
        }
    }
    /* Accept Control because ModernComboBox wraps its native input. */
    private static string SelectedComboText(Control combo, string fallback) =>
        string.IsNullOrWhiteSpace(combo.Text) ? fallback : combo.Text.Trim();
    /// <summary>Snapshot scope and adjustment inputs, excluding the derived preview grid.</summary>
    private string EditorState() =>
        string.Join("\u001f",
            campusScope.SelectedValue?.ToString() ?? "",
            classScope.SelectedValue?.ToString() ?? "",
            feeTypeScope.SelectedValue?.ToString() ?? "",
            frequencyScope.SelectedIndex.ToString(),
            roundingScope.SelectedIndex.ToString(),
            typePercentage.Checked ? "1" : "0",
            directionIncrease.Checked ? "1" : "0",
            adjustmentValue.Value.ToString("0.####"));
    private void CaptureAppliedBaseline() { loadedSnapshot = EditorState(); discardConfirmed = false; }
    /// <summary>True when the dialog holds choices the operator has not applied.</summary>
    private bool IsDirty() =>
        !savedSuccessfully &&
        DialogResult != DialogResult.OK &&
        !string.Equals(EditorState(), loadedSnapshot, StringComparison.Ordinal);
    /// <summary>asks at most once for a close gesture. True when the caller may proceed with the close.</summary>
    private bool ConfirmDiscard()
    {
        return ModernMessageDialog.ConfirmDiscardOnce(this, IsDirty, ref discardConfirmed);
    }
    /// <summary>Discard a preview when its inputs change.</summary>
    private void InvalidateBulkPreview()
    {
        if (plan.Count == 0 &&
            preview.Rows.Count == 0)
        {
            return;
        }
        appliedPreviewSignature = null;
        plan = new List<PlanRow>();
        preview.Rows.Clear();
        previewUnreadableClasses = 0;
        previewScopeText = "";
        previewAdjustmentText = "";
        previewAmountRangeText = "";
        previewNewAmountRangeText = "";
        applyButton.Enabled = false;
        selectAllButton.Enabled = selectNoneButton.Enabled = false;
        summaryLabel.Text = "Settings changed. Preview the changes again before applying them.";
    }
    private void Apply()
    {
        preview.EndEdit();
        var selected = SelectedPlan();
        if (selected.Count == 0 || Applied)
            return;
        /* The plan and the controls must still agree. */
        if (!string.Equals(appliedPreviewSignature, PreviewSignature(), StringComparison.Ordinal))
        {
            ModernMessageDialog.Show(
                this,
                "The settings have changed since this preview was built. " +
                "Preview the changes again before applying them.",
                PageTitles.BulkFeeRateChanges,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        /* some classes' fee configurations could not be read, so the plan covers less than the scope on screen says it does. */
        if (previewUnreadableClasses > 0 &&
            !ModernMessageDialog.Confirm(
                this,
                $"{previewUnreadableClasses} class(es) could not be read, so their fees are NOT in this " +
                "plan and will not change.\r\n\r\n" +
                $"Apply the adjustment to the {selected.Count} selected fee(s) that could be read?",
                PageTitles.BulkFeeRateChanges))
        {
            return;
        }
        var bulkLines = new List<string> {
            "Apply bulk fee changes?",
            "",
            $"Affected configurations: {selected.Count} selected out of {plan.Count} previewed",
            $"Adjustment: {previewAdjustmentText}",
            $"Scope: {previewScopeText}",
            $"Selected old amounts: Rs. {selected.Min(row => row.OldAmount):N2} to Rs. {selected.Max(row => row.OldAmount):N2}",
            $"Selected new amounts: Rs. {selected.Min(row => row.NewAmount):N2} to Rs. {selected.Max(row => row.NewAmount):N2}",
            "",
            "Review the preview carefully. This will update the fee configuration records shown in the preview."
        };
        if (previewUnreadableClasses > 0)
            bulkLines.Insert(3, $"Not included: {previewUnreadableClasses} class(es) could not be read.");
        if (!ModernMessageDialog.Confirm(this, string.Join(Environment.NewLine, bulkLines), PageTitles.BulkFeeRateChanges))
            return;
        try
        {
            /* ONE atomic call instead of a row-by-row loop. */
            DatabaseHelper.ExecuteUser(
                "ApplyFeeConfigurationPlan",
                DatabaseHelper.Int("@UserId", CurrentUser.UserId),
                new SqlParameter("@Plan", SqlDbType.Structured) {
                    TypeName = "dbo.FeeConfigurationPlanType",
                    Value = Tvp.FeeConfigurationPlan(
                        selected.Select(row => (
                            row.FeeConfigurationId,
                            row.CampusId,
                            row.ClassId,
                            row.FeeTypeId,
                            row.NewAmount,
                            (string?)row.Frequency,
                            (string?)row.Remarks)))
                });
            AppliedCount = selected.Count;
            FeeConfigurationChanges.NotifyChanged();
            if (embedded)
            {
                CaptureAppliedBaseline();
                /* The tab stays open. The plan is spent - the amounts it was built from have just changed - so it is cleared and a new preview is required. */
                string done = $"Adjusted {AppliedCount} fee configuration(s). Preview again to see the current amounts.";
                appliedPreviewSignature = null;
                plan = new List<PlanRow>();
                preview.Rows.Clear();
                previewUnreadableClasses = 0;
                applyButton.Enabled = false;
                selectAllButton.Enabled = selectNoneButton.Enabled = false;
                summaryLabel.Text = done;
                return;
            }
            Applied = true;
            /* The changes committed, so this close is not a discard. */
            savedSuccessfully = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            ModernMessageDialog.Show(this, DbMessage.UserMessage(ex), PageTitles.BulkFeeRateChanges, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            applyButton.Focus();
        }
    }
}

