using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.Configuration;
/// <summary>Copy Fees tab: copy the fee configurations of one class, or of a whole campus, to another campus.</summary>
public sealed class CopyFeeRatesForm : ApplicationWindowForm
{
    private sealed class PlanRow
    {
        public int FeeTypeId;
        public string FeeTypeName = "";
        public decimal Amount;
        public string Frequency = "";
        public string Remarks = "";
        public int TargetClassId;
        public string TargetClassName = "";
        public int ExistingFeeConfigurationId;
        public string Action = "";
    }
    private const int AllClassesId = 0;
    private readonly ModernComboBox sourceCampus = HumanUi.Combo();
    private readonly ModernComboBox sourceClass = HumanUi.Combo();
    private readonly ModernComboBox targetCampus = HumanUi.Combo();
    private readonly ModernComboBox conflictMode = HumanUi.Combo();
    private readonly DataGridView preview = HumanUi.Grid(false);
    private readonly ModernButton selectAllButton = new() { Text = UiText.Actions.SelectAll, ThemeRole = "Secondary" };
    private readonly ModernButton selectNoneButton = new() { Text = UiText.Actions.SelectNone, ThemeRole = "Secondary" };
    private string previewWarnings = "";
    private static bool CanCopy(PlanRow row) => row.Action is "New" or "Overwrite";
    private List<PlanRow> SelectedPlan() => preview.Rows.Cast<DataGridViewRow>()
        .Where(row => row.Tag is PlanRow item && CanCopy(item) && row.Cells["Include"].Value is true)
        .Select(row => (PlanRow)row.Tag!).ToList();
    private void SelectPreviewRows(bool include)
    {
        foreach (DataGridViewRow row in preview.Rows)
            row.Cells["Include"].Value = include && row.Tag is PlanRow item && CanCopy(item);
        UpdateSelectedSummary();
    }
    private void UpdateSelectedSummary()
    {
        var selected = SelectedPlan();
        summaryLabel.Text = $"{selected.Count} selected: {selected.Count(row => row.Action == "New")} new, "
            + $"{selected.Count(row => row.Action == "Overwrite")} overwrite. "
            + $"{plan.Count(row => !CanCopy(row))} skipped." + previewWarnings;
        applyButton.Enabled = selected.Count > 0;
        selectAllButton.Enabled = selectNoneButton.Enabled = plan.Any(CanCopy);
    }
    private readonly ToolTip tip = new();
    private readonly Label summaryLabel = new() {
        ForeColor = AppTheme.MutedText,
        Font = AppTheme.NormalFont
    };
    private readonly ModernButton applyButton = new() {
        Text = PageTitles.CopyFeeRates,
        Width = AppScale.Scale(130),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Success",
        Enabled = false
    };
    private readonly ModernButton closeButton = new() {
        Text = UiText.Actions.Cancel,
        Width = AppScale.Scale(105),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Secondary",
        DialogResult = DialogResult.Cancel
    };
    private Label? titleLabel;
    private TableLayoutPanel? rootPanel;
    private DataTable campusesTable = new();
    private List<PlanRow> plan = new();
    private int previewNewCount;
    private int previewOverwriteCount;
    private int previewSkipCount;
    private bool loading;
    private bool embedded;
    public bool Applied { get; private set; }
    /// <summary>how many configurations the atomic call applied.</summary>
    public int AppliedCount { get; private set; }
    public static CopyFeeRatesForm CreateEmbedded()
    {
        var form = new CopyFeeRatesForm();
        form.PrepareForEmbedding();
        return form;
    }
    /// <summary>See FeeRateMatrixForm.PrepareForEmbedding: Cancel would call Form.Close, which disposes a form that is not running under ShowDialog, so it is hidden.</summary>
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
    public CopyFeeRatesForm(int sourceCampusId = 0, int sourceClassId = 0)
    {
        Text = PageTitles.CopyFeeRates;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(AppScale.Scale(900), AppScale.Scale(620));
        MinimumSize = new Size(AppScale.Scale(720), AppScale.Scale(420));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        BuildLayout();
        SetSaveShortcut(applyButton);
        conflictMode.Items.AddRange(new object[] {
            "Keep existing fees",
            "Replace existing amounts"
        });
        conflictMode.SelectedIndex = 0;
        LoadSourceCampuses(sourceCampusId, sourceClassId);
        sourceCampus.SelectedIndexChanged += (_, _) => {
            if (loading)
                return;
            LoadSourceClasses(0);
            LoadTargetCampuses();
            RefreshPreview();
        };
        sourceClass.SelectedIndexChanged += (_, _) => RefreshPreview();
        targetCampus.SelectedIndexChanged += (_, _) => RefreshPreview();
        conflictMode.SelectedIndexChanged += (_, _) => RefreshPreview();
        applyButton.Click += (_, _) => Apply();
        Disposed += (_, _) => tip.Dispose();
        RefreshPreview();
    }
    private void BuildLayout()
    {
        titleLabel = new Label {
            Text = PageTitles.CopyFeeRates,
            Dock = DockStyle.Top,
            Height = Math.Max(30, AppMetrics.TextHeight(AppTheme.PageTitleFont) + 8),
            Font = AppTheme.PageTitleFont,
            ForeColor = AppTheme.Text
        };
        var help = FeeToolLayout.Help(
            "Copy fee rates between campuses. Classes are matched by name; review the selected rows before copying.",
            tip);
        var fields = FeeToolLayout.FieldRow(
            ("From Campus", sourceCampus, true),
            ("Class", sourceClass, true),
            ("To Campus", targetCampus, true),
            ("Existing Fees", conflictMode, true));
        preview.Dock = DockStyle.Fill;
        preview.AutoGenerateColumns = false;
        preview.ReadOnly = false;
        preview.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", HeaderText = "Copy", Width = AppScale.Scale(55) });
        preview.Columns.Add("Class", "Class");
        preview.Columns.Add("FeeType", "Fee Type");
        preview.Columns.Add("Amount", "Amount");
        preview.Columns.Add("Frequency", "Frequency");
        preview.Columns.Add("Action", "Result");
        foreach (DataGridViewColumn column in preview.Columns) column.ReadOnly = column.Name != "Include";
        preview.CurrentCellDirtyStateChanged += (_, _) => {
            if (preview.IsCurrentCellDirty) preview.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        preview.CellValueChanged += (_, e) => {
            if (e.RowIndex >= 0 && e.ColumnIndex == preview.Columns["Include"].Index) UpdateSelectedSummary();
        };
        selectAllButton.Click += (_, _) => SelectPreviewRows(true);
        selectNoneButton.Click += (_, _) => SelectPreviewRows(false);
        selectAllButton.Enabled = selectNoneButton.Enabled = false;
        HumanUi.ApplyGridStyle(preview, AppTheme.Primary);
        var toolbar = FeeToolLayout.Toolbar(summaryLabel, tip, selectAllButton, selectNoneButton, applyButton, closeButton);
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
            RowCount = 5,
            Padding = new Padding(14, 10, 14, 10)
        };
        rootPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootPanel.Controls.Add(titleLabel, 0, 0);
        rootPanel.Controls.Add(help, 0, 1);
        rootPanel.Controls.Add(fields, 0, 2);
        rootPanel.Controls.Add(toolbar, 0, 3);
        rootPanel.Controls.Add(preview, 0, 4);
        Controls.Add(rootPanel);
        AcceptButton = applyButton;
        CancelButton = closeButton;
    }
    private static int SelectedId(ModernComboBox combo) =>
        combo.SelectedValue == null ? 0 : Convert.ToInt32(combo.SelectedValue);
    private void LoadSourceCampuses(int sourceCampusId, int sourceClassId)
    {
        loading = true;
        try
        {
            campusesTable = DatabaseHelper.QueryUser("GetCampuses");
            sourceCampus.DisplayMember = "CampusName";
            sourceCampus.ValueMember = "CampusId";
            sourceCampus.DataSource = campusesTable.Copy();
            bool preset = sourceCampusId > 0 &&
                campusesTable.AsEnumerable().Any(r => Convert.ToInt32(r["CampusId"]) == sourceCampusId);
            if (preset)
                sourceCampus.SelectedValue = sourceCampusId;
            else
                sourceCampus.SelectedIndex = -1;
            LoadSourceClasses(sourceClassId);
            LoadTargetCampuses();
        }
        catch (Exception ex)
        {
            summaryLabel.Text = DbMessage.UserMessage(ex);
        }
        finally
        {
            loading = false;
        }
    }
    /// <summary>The distinct classes assigned to a campus, in configured order.</summary>
    private static List<(int ClassId, string ClassName)> ClassesOfCampus(int campusId)
    {
        var assignments = DatabaseHelper.QueryUser(
            "GetClassSections",
            DatabaseHelper.Int("@CampusId", campusId),
            DatabaseHelper.Int("@ClassId", 0),
            DatabaseHelper.Int("@SectionId", 0));
        return assignments.AsEnumerable()
            .Select(r => (ClassId: Convert.ToInt32(r["ClassId"]), ClassName: Convert.ToString(r["ClassName"]) ?? ""))
            .Distinct()
            .ToList();
    }
    private void LoadSourceClasses(int preferredClassId)
    {
        bool wasLoading = loading;
        loading = true;
        try
        {
            int campusId = SelectedId(sourceCampus);
            var table = new DataTable();
            table.Columns.Add("ClassId", typeof(int));
            table.Columns.Add("ClassName", typeof(string));
            if (campusId > 0)
            {
                table.Rows.Add(AllClassesId, "All classes");
                foreach (var c in ClassesOfCampus(campusId))
                    table.Rows.Add(c.ClassId, c.ClassName);
            }
            sourceClass.DataSource = null;
            sourceClass.DisplayMember = "ClassName";
            sourceClass.ValueMember = "ClassId";
            sourceClass.DataSource = table;
            sourceClass.Enabled = table.Rows.Count > 0;
            bool preferredExists = preferredClassId > 0 &&
                table.AsEnumerable().Any(r => Convert.ToInt32(r["ClassId"]) == preferredClassId);
            if (table.Rows.Count == 0)
                sourceClass.SelectedIndex = -1;
            else
                sourceClass.SelectedValue = preferredExists ? preferredClassId : AllClassesId;
        }
        catch (Exception ex)
        {
            summaryLabel.Text = DbMessage.UserMessage(ex);
        }
        finally
        {
            loading = wasLoading;
        }
    }
    private void LoadTargetCampuses()
    {
        bool wasLoading = loading;
        loading = true;
        try
        {
            /* Copying a campus onto itself does nothing, so the source campus is not offered. */
            int sourceId = SelectedId(sourceCampus);
            int previousTarget = SelectedId(targetCampus);
            var dt = campusesTable.Clone();
            foreach (DataRow campusRow in campusesTable.Rows)
            {
                if (Convert.ToInt32(campusRow["CampusId"]) != sourceId)
                    dt.ImportRow(campusRow);
            }
            targetCampus.DataSource = null;
            targetCampus.DisplayMember = "CampusName";
            targetCampus.ValueMember = "CampusId";
            targetCampus.DataSource = dt;
            bool keep = previousTarget > 0 &&
                dt.AsEnumerable().Any(r => Convert.ToInt32(r["CampusId"]) == previousTarget);
            if (dt.Rows.Count == 0)
                targetCampus.SelectedIndex = -1;
            else if (keep)
                targetCampus.SelectedValue = previousTarget;
            else
                targetCampus.SelectedIndex = 0;
        }
        finally
        {
            loading = wasLoading;
        }
    }
    private void RefreshPreview()
    {
        if (loading)
            return;
        plan = new List<PlanRow>();
        previewWarnings = "";
        selectAllButton.Enabled = selectNoneButton.Enabled = false;
        preview.Rows.Clear();
        summaryLabel.ForeColor = AppTheme.MutedText;
        applyButton.Enabled = false;
        try
        {
            int sourceCampusId = SelectedId(sourceCampus);
            if (sourceCampusId <= 0)
            {
                summaryLabel.Text = "Choose the campus to copy from.";
                return;
            }
            if (targetCampus.SelectedValue == null)
            {
                summaryLabel.Text = "Choose the campus to copy to.";
                return;
            }
            int targetCampusId = Convert.ToInt32(targetCampus.SelectedValue);
            int sourceClassId = SelectedId(sourceClass);
            var sourceClasses = sourceClassId == AllClassesId
                ? ClassesOfCampus(sourceCampusId)
                : new List<(int ClassId, string ClassName)> { (sourceClassId, sourceClass.Text.Trim()) };
            var targetClassesByName = ClassesOfCampus(targetCampusId)
                .GroupBy(c => c.ClassName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().ClassId, StringComparer.OrdinalIgnoreCase);
            bool overwrite = conflictMode.SelectedIndex == 1;
            int newCount = 0, overwriteCount = 0, skipCount = 0;
            var unmatched = new List<string>();
            var campusFeeSnapshots = new Dictionary<int, DataTable>();
            DataTable ReadClassFees(int campusId, int classId)
            {
                if (sourceClasses.Count <= 1)
                    return DatabaseHelper.QueryUser("GetFeeConfigurations",
                        DatabaseHelper.Int("@CampusId", campusId), DatabaseHelper.Int("@ClassId", classId));
                if (!campusFeeSnapshots.TryGetValue(campusId, out var allFees))
                {
                    allFees = DatabaseHelper.QueryUser("GetFeeConfigurations",
                        DatabaseHelper.Int("@CampusId", campusId), DatabaseHelper.Bit("@AllClasses", true));
                    campusFeeSnapshots[campusId] = allFees;
                }
                return new DataView(allFees) { RowFilter = $"ClassId = {classId}" }.ToTable();
            }
            foreach (var sourceClassRow in sourceClasses)
            {
                if (!targetClassesByName.TryGetValue(sourceClassRow.ClassName, out int targetClassId))
                {
                    unmatched.Add(sourceClassRow.ClassName);
                    continue;
                }
                if (targetClassId == sourceClassRow.ClassId && targetCampusId == sourceCampusId)
                    continue;
                var sourceFees = ReadClassFees(sourceCampusId, sourceClassRow.ClassId);
                if (sourceFees.Rows.Count == 0)
                    continue;
                var targetFees = ReadClassFees(targetCampusId, targetClassId);
                var targetByFeeType = targetFees.AsEnumerable()
                    .ToDictionary(r => Convert.ToInt32(r["FeeTypeId"]), r => Convert.ToInt32(r["FeeConfigurationId"]));
                foreach (DataRow row in sourceFees.Rows)
                {
                    int feeTypeId = Convert.ToInt32(row["FeeTypeId"]);
                    bool exists = targetByFeeType.TryGetValue(feeTypeId, out int existingId);
                    string action;
                    if (!exists)
                    {
                        action = "New";
                        newCount++;
                    }
                    else if (overwrite)
                    {
                        action = "Overwrite";
                        overwriteCount++;
                    }
                    else
                    {
                        action = "Skip (already exists)";
                        skipCount++;
                    }
                    plan.Add(new PlanRow {
                        FeeTypeId = feeTypeId,
                        FeeTypeName = Convert.ToString(row["FeeTypeName"]) ?? "",
                        Amount = Convert.ToDecimal(row["Amount"]),
                        Frequency = Convert.ToString(row["Frequency"]) ?? "",
                        Remarks = row["Remarks"] == DBNull.Value ? "" : Convert.ToString(row["Remarks"]) ?? "",
                        TargetClassId = targetClassId,
                        TargetClassName = sourceClassRow.ClassName,
                        ExistingFeeConfigurationId = exists ? existingId : 0,
                        Action = action
                    });
                }
            }
            foreach (var row in plan)
            {
                int rowIndex = preview.Rows.Add(CanCopy(row), row.TargetClassName, row.FeeTypeName, $"Rs. {row.Amount:N2}", row.Frequency, row.Action);
                preview.Rows[rowIndex].Tag = row;
                preview.Rows[rowIndex].Cells["Include"].ReadOnly = !CanCopy(row);
                if (row.Action.StartsWith("Skip"))
                    preview.Rows[rowIndex].DefaultCellStyle.ForeColor = AppTheme.MutedText;
            }
            var parts = new List<string>();
            if (newCount > 0) parts.Add($"{newCount} new");
            if (overwriteCount > 0) parts.Add($"{overwriteCount} to overwrite");
            if (skipCount > 0) parts.Add($"{skipCount} already exist (skipped)");
            string text = parts.Count == 0
                ? "Nothing to copy."
                : string.Join(", ", parts) + ".";
            if (unmatched.Count > 0)
            {
                string names = string.Join(", ", unmatched.Take(6)) + (unmatched.Count > 6 ? ", ..." : "");
                previewWarnings = $" Not on {targetCampus.Text.Trim()}: {names}.";
                text += previewWarnings;
            }
            summaryLabel.Text = text;
            applyButton.Enabled = newCount + overwriteCount > 0;
            previewNewCount = newCount;
            previewOverwriteCount = overwriteCount;
            previewSkipCount = skipCount;
            UpdateSelectedSummary();
        }
        catch (Exception ex)
        {
            plan.Clear();
            preview.Rows.Clear();
            selectAllButton.Enabled = selectNoneButton.Enabled = false;
            summaryLabel.Text = DbMessage.UserMessage(ex);
            applyButton.Enabled = false;
        }
    }
    private void Apply()
    {
        /* UX - a repeated click/Enter after the copy committed must not ask again. */
        if (Applied)
            return;
        preview.EndEdit();
        var actionable = SelectedPlan();
        if (actionable.Count == 0)
            return;
        var copyLines = new List<string> {
            "Copy these fee configurations?",
            "",
            $"Source: {sourceCampus.Text.Trim()} - {sourceClass.Text.Trim()}",
            $"Destination campus: {targetCampus.Text}",
            $"New records: {actionable.Count(row => row.Action == "New")}",
            $"Existing records to overwrite: {actionable.Count(row => row.Action == "Overwrite")}"
        };
        if (previewSkipCount > 0)
            copyLines.Add($"Skipped (destination already has the fee type): {previewSkipCount}");
        copyLines.Add("");
        copyLines.Add("Existing destination fees marked for overwrite will be replaced.");
        copyLines.Add("Only the selected rows in the preview will be copied.");
        if (!ModernMessageDialog.Confirm(this, string.Join(Environment.NewLine, copyLines), PageTitles.CopyFeeRates))
            return;
        try
        {
            int targetCampusId = Convert.ToInt32(targetCampus.SelectedValue);
            /* ONE atomic call instead of a row-by-row loop. dbo.ApplyFeeConfigurationPlan validates the whole plan first, then applies every row inside a single transaction and commits only if all of them succeed. */
            DatabaseHelper.ExecuteUser(
                "ApplyFeeConfigurationPlan",
                DatabaseHelper.Int("@UserId", CurrentUser.UserId),
                new SqlParameter("@Plan", SqlDbType.Structured) {
                    TypeName = "dbo.FeeConfigurationPlanType",
                    Value = Tvp.FeeConfigurationPlan(
                        actionable.Select(row => (
                            row.ExistingFeeConfigurationId,
                            targetCampusId,
                            row.TargetClassId,
                            row.FeeTypeId,
                            row.Amount,
                            (string?)row.Frequency,
                            (string?)row.Remarks)))
                });
            AppliedCount = actionable.Count;
            FeeConfigurationChanges.NotifyChanged();
            if (embedded)
            {
                /* The tab stays open: re-preview against the destination as it is now (the copied fees read "already exists") and say what was done. */
                string done = $"Copied {AppliedCount} fee configuration(s) to {targetCampus.Text.Trim()}.";
                RefreshPreview();
                summaryLabel.Text = done + " " + summaryLabel.Text;
                summaryLabel.ForeColor = AppTheme.Success;
                return;
            }
            Applied = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            ModernMessageDialog.Show(this, DbMessage.UserMessage(ex), PageTitles.CopyFeeRates, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            applyButton.Focus();
        }
    }
}

