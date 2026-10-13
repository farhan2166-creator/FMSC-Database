using System.Globalization;
using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.Configuration;
public sealed class FeeRateMatrixForm : ApplicationWindowForm, IUnsavedChanges
{
    private sealed class ExistingCell
    {
        public int FeeConfigurationId;
        public decimal Amount;
        public string Frequency = "";
        public string Remarks = "";
    }
    private sealed class ClassColumn
    {
        public int ClassId;
        public string ClassName = "";
    }
    private sealed class FeeTypeRow
    {
        public int FeeTypeId;
        public string FeeTypeName = "";
        public string DefaultFrequency = "";
    }
    private static readonly string[] Frequencies =
    {
        "Monthly",
        "Yearly",
        "As Needed"
    };
    private readonly ModernComboBox campus = HumanUi.Combo();
    private readonly DataGridView grid = HumanUi.Grid(true);
    private readonly Label summaryLabel = new() {
        AutoSize = true,
        ForeColor = AppTheme.MutedText,
        Font = AppTheme.NormalFont
    };
    private readonly ModernButton saveButton = new() {
        Text = "Save Grid",
        Width = AppScale.Scale(120),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Success",
        Enabled = false
    };
    private readonly ModernButton closeButton = new() {
        Text = UiText.Actions.Close,
        Width = AppScale.Scale(100),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Secondary"
    };
    private readonly ModernButton applyStudentsButton = new() {
        Text = PageTitles.UpdateStudentFeeRates + "…",
        Width = AppScale.Scale(235),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Warning"
    };
    private readonly ModernButton editFeeButton = new() {
        Text = "Edit Selected Fee", Width = AppScale.Scale(150), Height = AppMetrics.ButtonHeight, ThemeRole = "Secondary"
    };
    private sealed record CellDetails(string Frequency, string Remarks);
    private readonly Dictionary<(int ClassId, int FeeTypeId), CellDetails> editedDetails = new();
    private List<ClassColumn> classes = new();
    private List<FeeTypeRow> feeTypeRows = new();
    private Dictionary<(int ClassId, int FeeTypeId), ExistingCell> existing = new();
    /* The Frequency column is ONE value per fee-type row, but each class cell has its own stored frequency. */
    private readonly HashSet<int> frequencyEditedFeeTypeIds = new();
    private bool buildingGridRows;
    /* A campus change during an in-flight save runs LoadGridAsync, which re-enables saveButton, so the disabled button alone cannot refuse a click in that window. */
    private bool savingGrid;
    /* selecting campus A and then quickly B left two loads running with no owner. */
    private int gridLoadVersion;
    /* The editable cell values the grid was last loaded with, so Close on an untouched grid asks nothing. Re-captured by every load, including the reload that follows a successful save. */
    private string loadedSnapshot = "";
    private readonly Dictionary<DataGridViewCell, object?> loadedCellValues = new();
    private readonly ContextMenuStrip selectionMenu = new StyledContextMenu();
    private IEnumerable<DataGridViewCell> SelectedAmountCells() => grid.SelectedCells.Cast<DataGridViewCell>()
        .Where(cell => !cell.ReadOnly && cell.OwningColumn.Name.StartsWith("CL_", StringComparison.Ordinal));
    private void FillSelectedAmounts()
    {
        if (savingGrid || grid.CurrentCell is not { } source || !source.OwningColumn.Name.StartsWith("CL_", StringComparison.Ordinal)) return;
        if (!grid.EndEdit()) return;
        string text = Convert.ToString(source.Value)?.Trim() ?? "";
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount) || amount < 0 || !WholeRupees.IsWhole(amount))
        {
            source.ErrorText = "Enter a whole-rupee amount of zero or more before filling selected cells.";
            return;
        }
        foreach (var cell in SelectedAmountCells().ToArray()) cell.Value = FormatAmount(amount);
        RefreshEditSummary();
    }
    private void RestoreSelectedCells()
    {
        if (savingGrid) return;
        grid.CancelEdit();
        foreach (DataGridViewCell cell in grid.SelectedCells)
            if (!cell.ReadOnly && loadedCellValues.TryGetValue(cell, out var value))
            {
                cell.Value = value; cell.ErrorText = "";
                if (cell.OwningColumn.Name.StartsWith("CL_", StringComparison.Ordinal) && cell.RowIndex < feeTypeRows.Count)
                    editedDetails.Remove((int.Parse(cell.OwningColumn.Name[3..]), feeTypeRows[cell.RowIndex].FeeTypeId));
            }
        // Restoring a frequency must also remove its row-wide update intent.
        for (int index = 0; index < feeTypeRows.Count && index < grid.Rows.Count; index++)
        {
            var cell = grid.Rows[index].Cells[FrequencyColumnName];
            if (loadedCellValues.TryGetValue(cell, out var value) && Equals(cell.Value, value))
            {
                frequencyEditedFeeTypeIds.Remove(feeTypeRows[index].FeeTypeId);
                if (grid.SelectedCells.Contains(cell))
                {
                    int feeId = feeTypeRows[index].FeeTypeId;
                    foreach (var key in editedDetails.Keys.Where(k => k.FeeTypeId == feeId).ToArray())
                    {
                        existing.TryGetValue(key, out var saved);
                        var restored = editedDetails[key] with { Frequency = saved?.Frequency ?? Convert.ToString(value) ?? "Monthly" };
                        if (saved is not null && saved.Frequency == restored.Frequency && saved.Remarks == restored.Remarks)
                            editedDetails.Remove(key);
                        else editedDetails[key] = restored;
                    }
                }
            }
        }
        RefreshEditSummary();
    }
    private void RefreshEditSummary()
    {
        if (buildingGridRows || savingGrid || loadedCellValues.Count == 0) return;
        int changed = loadedCellValues.Count(pair => !string.Equals(Convert.ToString(pair.Key.Value) ?? "", Convert.ToString(pair.Value) ?? "", StringComparison.Ordinal));
        foreach (var pair in loadedCellValues)
        {
            bool detailsChanged = pair.Key.OwningColumn.Name.StartsWith("CL_", StringComparison.Ordinal) &&
                pair.Key.RowIndex < feeTypeRows.Count && editedDetails.ContainsKey((int.Parse(pair.Key.OwningColumn.Name[3..]), feeTypeRows[pair.Key.RowIndex].FeeTypeId));
            pair.Key.Style.BackColor = Equals(pair.Key.Value, pair.Value) && !detailsChanged ? Color.Empty : AppTheme.Tint(AppTheme.Warning, 0.12f);
            if (pair.Key.OwningColumn.Name.StartsWith("CL_", StringComparison.Ordinal) && pair.Key.RowIndex < feeTypeRows.Count)
            {
                var fee = feeTypeRows[pair.Key.RowIndex];
                var details = DetailsForCell(int.Parse(pair.Key.OwningColumn.Name[3..]), fee,
                    Convert.ToString(grid.Rows[pair.Key.RowIndex].Cells[FrequencyColumnName].Value) ?? DefaultFrequencyFor(fee));
                pair.Key.ToolTipText = $"{details.Frequency}\n{details.Remarks}";
            }
        }
        summaryLabel.Text = $"{changed} edited value(s), {editedDetails.Count} fee detail change(s). Save Grid to store class rates.";
    }
    /* One confirmation per close gesture. */
    private bool discardConfirmed;
    /* Set only by PrepareForEmbedding: this instance is being shown as a tab rather than as a modal dialog. */
    private bool embedded;
    public bool Applied { get; private set; }
    /// <summary>Hosts this dialog inside a tab instead of showing it as a modal.</summary>
    public void PrepareForEmbedding()
    {
        embedded = true;
        TopLevel = false;
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        closeButton.Visible = false;
        CancelButton = null;
        // The tab is the frame: no title of our own, and the tab host already pads the page.
        if (titleLabel is not null)
            titleLabel.Visible = false;
        if (rootPanel is not null)
            rootPanel.Padding = new Padding(4, 4, 4, 6);
    }
    /// <summary>Created and prepared in one step, for a WorkspacePage tab factory.</summary>
    public static FeeRateMatrixForm CreateEmbedded(int? defaultCampusId = null)
    {
        var form = new FeeRateMatrixForm(defaultCampusId);
        form.PrepareForEmbedding();
        return form;
    }
    /// <summary>'s contract, implemented here because an embedded instance has no FormClosing to guard it.</summary>
    public bool HasUnsavedChanges => IsDirty();
    public string UnsavedChangesMessage =>
        "The fee matrix has unsaved changes. Leaving this screen will discard them.";
    public FeeRateMatrixForm(int? defaultCampusId = null)
    {
        Text = PageTitles.FeeRateMatrix;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(AppScale.Scale(980), AppScale.Scale(620));
        MinimumSize = new Size(AppScale.Scale(720), AppScale.Scale(420));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        BuildLayout();
        editFeeButton.Click += (_, _) => EditSelectedFee();
        applyStudentsButton.Click += (_, _) => ApplyToStudents();
        /* D-H10 - the campus selector is wired BEFORE the list is loaded, and the grid is loaded AFTER it. */
        campus.SelectedIndexChanged += async (_, _) => await LoadGridAsync();
        /* Save Grid is a bulk financial write: one insert/update per class x fee-type cell, so a double click could submit the whole batch twice. */
        saveButton.Click += async (_, _) => {
            if (savingGrid || !saveButton.Enabled)
                return;
            /* UX - commit a cell that is still being typed in, so the value on screen is the value that is saved. */
            if (!grid.EndEdit()) return;
            savingGrid = true;
            saveButton.Enabled = false;
            /* The save writes the model the grid currently shows, so the campus selector and the grid are held while it runs. */
            campus.Enabled = false;
            grid.Enabled = false;
            try
            {
                await SaveGridAsync();
            }
            finally
            {
                savingGrid = false;
                campus.Enabled = true;
                grid.Enabled = true;
                saveButton.Enabled =
                    campus.SelectedValue != null &&
                    classes.Count > 0 &&
                    feeTypeRows.Count > 0;
                /* UX - disabling the grid during the write dropped keyboard focus; hand it back so the person can carry on typing (or land on the first cell that needs fixing)… */
                if (!IsDisposed && grid.CanFocus)
                    grid.Focus();
            }
        };
        /* Embedded, Close would DISPOSE this form (see PrepareForEmbedding) and leave an empty tab; the guard is belt-and-braces, because the button is hidden there and nobody can click it. */
        closeButton.Click += (_, _) => {
            if (!embedded)
                Close();
        };
        /* Close and Esc (the CancelButton) both call Close, and the window's own X raises the same event, so one guard covers every way this form can be abandoned. */
        FormClosing += (_, e) => {
            /* A save owns the form until it settles: closing mid-write would dispose the model the save is still reading and then run its completion against a disposed form. */
            if (savingGrid)
            {
                e.Cancel = true;
                return;
            }
            if (e.CloseReason == CloseReason.UserClosing)
                e.Cancel = !ConfirmDiscard();
        };
        /* Copy Fees / Adjust Fees can change the amounts this grid shows. Reload - but never over edits the operator has not saved. */
        void OnConfigurationsChanged()
        {
            if (IsDisposed || savingGrid || IsDirty())
                return;
            _ = LoadGridAsync();
        }
        FeeConfigurationChanges.Changed += OnConfigurationsChanged;
        FeeTypeCatalog.Changed += OnConfigurationsChanged;
        Disposed += (_, _) => FeeConfigurationChanges.Changed -= OnConfigurationsChanged;
        Disposed += (_, _) => FeeTypeCatalog.Changed -= OnConfigurationsChanged;
        _ = InitializeAsync(defaultCampusId);
    }
    /// <summary>D-H10 - one ordered start instead of two racing fire-and-forget loads: the campus list first, then the grid. LoadGridAsync is version-guarded, so the overlap is harmless.</summary>
    private async Task InitializeAsync(int? defaultCampusId)
    {
        try
        {
            await LoadCampusesAsync(defaultCampusId);
            await LoadGridAsync();
        }
        catch (Exception ex)
        {
            summaryLabel.Text = DbMessage.UserMessage(ex);
        }
    }
    private Label? titleLabel;
    private TableLayoutPanel? rootPanel;
    private readonly ToolTip summaryTip = new();
    private void BuildLayout()
    {
        /* Vertical space is the grid's. */
        titleLabel = new Label {
            Text = PageTitles.FeeRateMatrix,
            Dock = DockStyle.Top,
            Height = Math.Max(30, AppMetrics.TextHeight(AppTheme.PageTitleFont) + 8),
            Font = AppTheme.PageTitleFont,
            ForeColor = AppTheme.Text
        };
        var subtitle = new Label {
            Text = UiText.Fees.MatrixScope,
            Dock = DockStyle.Top,
            AutoSize = false,
            AutoEllipsis = true,
            Height = AppMetrics.TextHeight(AppTheme.NormalFont) * 2 + 8,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText
        };
        summaryTip.SetToolTip(subtitle, subtitle.Text);
        summaryTip.SetToolTip(saveButton, "Save class rates. Existing student fees and bills stay unchanged.");
        summaryTip.SetToolTip(editFeeButton, "Change the frequency and remarks for one class. The Frequency column changes the whole row.");
        summaryTip.SetToolTip(applyStudentsButton, "Review and update eligible existing student fee rates. This does not create bills.");
        var campusLabel = new Label {
            Text = "Campus",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.Text,
            Margin = new Padding(0, 0, 8, 0)
        };
        campus.Dock = DockStyle.None;
        campus.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        campus.Margin = new Padding(0);
        summaryLabel.AutoSize = false;
        summaryLabel.AutoEllipsis = true;
        summaryLabel.Dock = DockStyle.Fill;
        summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        summaryLabel.Margin = new Padding(14, 0, 8, 0);
        // Long messages (errors, invalid-cell counts) are cut to one line here; hovering shows all of it.
        summaryLabel.TextChanged += (_, _) => summaryTip.SetToolTip(summaryLabel, summaryLabel.Text);
        var actions = new FlowLayoutPanel {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0)
        };
        saveButton.Margin = new Padding(0, 0, 8, 0);
        closeButton.Margin = new Padding(0);
        actions.Controls.Add(saveButton);
        editFeeButton.Margin = new Padding(0, 0, 8, 0);
        actions.Controls.Add(editFeeButton);
        applyStudentsButton.Margin = new Padding(0, 0, 8, 0);
        actions.Controls.Add(applyStudentsButton);
        actions.Controls.Add(closeButton);
        ActionButtonStyle.Arrange(actions);
        var toolbar = new TableLayoutPanel {
            Dock = DockStyle.Top,
            Height = AppMetrics.ButtonHeight + AppMetrics.InputControlHeight + 16,
            ColumnCount = 4,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0, 4, 0, 4)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + 8));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toolbar.Controls.Add(actions, 0, 0);
        toolbar.SetColumnSpan(actions, 4);
        toolbar.Controls.Add(campusLabel, 0, 1);
        toolbar.Controls.Add(campus, 1, 1);
        toolbar.Controls.Add(summaryLabel, 2, 1);
        toolbar.SetColumnSpan(summaryLabel, 2);
        grid.Dock = DockStyle.Fill;
        grid.AutoGenerateColumns = false;
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.MultiSelect = true;
        selectionMenu.Items.Add("Fill selected amounts from current cell (Ctrl+D)", null, (_, _) => FillSelectedAmounts());
        selectionMenu.Items.Add("Restore selected cells", null, (_, _) => RestoreSelectedCells());
        selectionMenu.Items.Add("Edit selected fee details…", null, (_, _) => EditSelectedFee());
        selectionMenu.Opening += (_, _) => {
            selectionMenu.Items[0].Enabled = !savingGrid && SelectedAmountCells().Any() && grid.CurrentCell?.OwningColumn.Name.StartsWith("CL_", StringComparison.Ordinal) == true;
            selectionMenu.Items[1].Enabled = !savingGrid && grid.SelectedCells.Cast<DataGridViewCell>().Any(cell => !cell.ReadOnly && loadedCellValues.ContainsKey(cell));
            selectionMenu.Items[2].Enabled = !savingGrid && grid.CurrentCell?.OwningColumn.Name.StartsWith("CL_", StringComparison.Ordinal) == true;
        };
        grid.ContextMenuStrip = selectionMenu;
        grid.KeyDown += (_, e) => {
            if (e.Control && e.KeyCode == Keys.D) { FillSelectedAmounts(); e.Handled = true; e.SuppressKeyPress = true; }
        };
        grid.CellValueChanged += (_, _) => RefreshEditSummary();
        Disposed += (_, _) => selectionMenu.Dispose();
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        grid.CellEndEdit += (_, _) => ClearCellError();
        grid.CellValueChanged += (_, e) => {
            if (buildingGridRows ||
                e.RowIndex < 0 ||
                e.RowIndex >= feeTypeRows.Count ||
                e.ColumnIndex < 0 ||
                grid.Columns[e.ColumnIndex].Name != FrequencyColumnName)
            {
                return;
            }
            frequencyEditedFeeTypeIds.Add(feeTypeRows[e.RowIndex].FeeTypeId);
            int feeId = feeTypeRows[e.RowIndex].FeeTypeId;
            foreach (var key in editedDetails.Keys.Where(k => k.FeeTypeId == feeId).ToArray())
                editedDetails[key] = editedDetails[key] with { Frequency = Convert.ToString(grid.Rows[e.RowIndex].Cells[FrequencyColumnName].Value) ?? "Monthly" };
        };
        HumanUi.ApplyGridStyle(grid, AppTheme.Primary);
        rootPanel = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14, 10, 14, 10)
        };
        rootPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootPanel.Controls.Add(titleLabel, 0, 0);
        rootPanel.Controls.Add(subtitle, 0, 1);
        rootPanel.Controls.Add(toolbar, 0, 2);
        rootPanel.Controls.Add(grid, 0, 3);
        Controls.Add(rootPanel);
        Disposed += (_, _) => summaryTip.Dispose();
        AcceptButton = saveButton;
        CancelButton = closeButton;
    }
    private async Task LoadCampusesAsync(int? defaultCampusId)
    {
        try
        {
            var dt = await DatabaseHelper.QueryUserAsync("GetCampuses");
            campus.DisplayMember = "CampusName";
            campus.ValueMember = "CampusId";
            campus.DataSource = dt;
            if (defaultCampusId is > 0 &&
                dt.AsEnumerable().Any(r => Convert.ToInt32(r["CampusId"]) == defaultCampusId))
            {
                campus.SelectedValue = defaultCampusId.Value;
            }
            else
            {
                campus.SelectedIndex = dt.Rows.Count > 0 ? 0 : -1;
                /* D-H10 - an empty campus list is the one case where this form has nothing to show and no campus to name. */
                if (dt.Rows.Count == 0)
                    summaryLabel.Text = "No campuses are set up yet - add one in Setup before using the fee matrix.";
            }
        }
        catch (Exception ex)
        {
            summaryLabel.Text = DbMessage.UserMessage(ex);
        }
    }
    private async Task LoadGridAsync(string? savedMessage = null, int? savedCampusId = null)
    {
        int version = ++gridLoadVersion;
        grid.Columns.Clear();
        grid.Rows.Clear();
        classes = new List<ClassColumn>();
        feeTypeRows = new List<FeeTypeRow>();
        existing = new Dictionary<(int, int), ExistingCell>();
        saveButton.Enabled = false;
        /* The grid is empty from here until it is rebuilt, so there is nothing unsaved to protect until the snapshot is retaken below. */
        loadedSnapshot = "";
        loadedCellValues.Clear();
        try
        {
            if (campus.SelectedValue == null)
            {
                summaryLabel.Text = "Select a campus.";
                return;
            }
            int campusId = Convert.ToInt32(campus.SelectedValue);
            var assignmentsTask = DatabaseHelper.QueryUserAsync(
                "GetClassSections",
                DatabaseHelper.Int("@CampusId", campusId),
                DatabaseHelper.Int("@ClassId", 0),
                DatabaseHelper.Int("@SectionId", 0));
            var classCatalogTask = DatabaseHelper.QueryUserAsync("GetClasses");
            await Task.WhenAll(assignmentsTask, classCatalogTask);
            if (IsDisposed || version != gridLoadVersion)
                return;
            var campusClassIds = assignmentsTask.Result.AsEnumerable()
                .Select(r => Convert.ToInt32(r["ClassId"]))
                .ToHashSet();
            classes = LookupDisplayOrder.ForBinding(classCatalogTask.Result, "ClassId").AsEnumerable()
                .Where(r => campusClassIds.Contains(Convert.ToInt32(r["ClassId"])))
                .Select(r => (
                    ClassId: Convert.ToInt32(r["ClassId"]),
                    ClassName: Convert.ToString(r["ClassName"]) ?? "",
                    DisplayOrder: r.Table.Columns.Contains("DisplayOrder") && !r.IsNull("DisplayOrder")
                        ? Convert.ToInt32(r["DisplayOrder"])
                        : int.MaxValue))
                .Distinct()
                .Select(c => new ClassColumn { ClassId = c.ClassId, ClassName = c.ClassName })
                .ToList();
            if (classes.Count == 0)
            {
                summaryLabel.Text = "This campus has no classes assigned yet (Setup > Campus/Class/Section Assignment).";
                return;
            }
            var feeTypes = await DatabaseHelper.QueryUserAsync("GetFeeTypes");
            if (IsDisposed || version != gridLoadVersion)
                return;
            if (feeTypes.Rows.Count == 0)
            {
                summaryLabel.Text = "No fee types are defined yet.";
                return;
            }
            var defaultFrequencies = feeTypes.AsEnumerable()
                .ToDictionary(r => Convert.ToInt32(r["FeeTypeId"]),
                    r => Convert.ToString(r["DefaultFrequency"]) ?? "Monthly");
            var configs = await DatabaseHelper.QueryUserAsync(
                "GetFeeConfigurations",
                DatabaseHelper.Int("@CampusId", campusId),
                DatabaseHelper.Bit("@AllClasses", true));
            if (IsDisposed || version != gridLoadVersion)
                return;
            var assignedClassIds = classes.Select(c => c.ClassId).ToHashSet();
            foreach (DataRow row in configs.Rows)
            {
                int classId = Convert.ToInt32(row["ClassId"]);
                if (!assignedClassIds.Contains(classId)) continue;
                int feeTypeId = Convert.ToInt32(row["FeeTypeId"]);
                existing[(classId, feeTypeId)] = new ExistingCell {
                    FeeConfigurationId = Convert.ToInt32(row["FeeConfigurationId"]),
                    Amount = Convert.ToDecimal(row["Amount"]),
                    Frequency = Convert.ToString(row["Frequency"]) ?? "",
                    Remarks = row["Remarks"] == DBNull.Value ? "" : Convert.ToString(row["Remarks"]) ?? ""
                };
            }
            feeTypeRows = feeTypes.AsEnumerable()
                .Select(r => {
                    int feeTypeId = Convert.ToInt32(r["FeeTypeId"]);
                    return new FeeTypeRow {
                        FeeTypeId = feeTypeId,
                        FeeTypeName = Convert.ToString(r["FeeTypeName"]) ?? "",
                        DefaultFrequency = defaultFrequencies.TryGetValue(feeTypeId, out var f) ? f : ""
                    };
                })
                .ToList();
            BuildGridColumns();
            BuildGridRows();
            /* The values just shown are the ones a close would have to throw away, so this is the baseline the guard compares against. */
            CaptureGridSnapshot();
            // Show the save outcome only after a successful reload of the same campus.
            // A newer load or a load error keeps its own status instead.
            summaryLabel.Text = savedMessage is not null && savedCampusId == campusId
                ? savedMessage
                : $"{feeTypeRows.Count} fee type(s) × {classes.Count} class(es). " +
                    "Save Grid stores class rates. Blank cells are left unconfigured.";
            saveButton.Enabled = true;
        }
        catch (Exception ex)
        {
            summaryLabel.Text = DbMessage.UserMessage(ex);
        }
    }
    private const string FeeTypeColumnName = "FeeType";
    private const string FrequencyColumnName = "Frequency";
    private void BuildGridColumns()
    {
        grid.Columns.Clear();
        grid.Columns.Add(new DataGridViewTextBoxColumn {
            Name = FeeTypeColumnName,
            HeaderText = "Fee Type",
            ReadOnly = true,
            Frozen = true,
            Width = AppScale.Scale(190),
            DefaultCellStyle = { BackColor = AppTheme.SurfaceSecondary, Font = AppTheme.ButtonFont }
        });
        var frequencyColumn = new DataGridViewComboBoxColumn {
            Name = FrequencyColumnName,
            HeaderText = "Frequency",
            Frozen = true,
            Width = AppScale.Scale(120),
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            DefaultCellStyle = { BackColor = AppTheme.SurfaceSecondary }
        };
        frequencyColumn.Items.AddRange(Frequencies);
        grid.Columns.Add(frequencyColumn);
        foreach (var classColumn in classes)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn {
                Name = "CL_" + classColumn.ClassId,
                HeaderText = classColumn.ClassName,
                Width = AppScale.Scale(120),
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
            });
        }
    }
    /* "Monthly" here is no longer a failure fallback. dbo.GetFeeTypes returns the configured default for each fee type and FeeTypes.DefaultFrequency is NOT NULL, so a missing entry should be impossible. */
    private static string DefaultFrequencyFor(FeeTypeRow feeType) =>
        string.IsNullOrEmpty(feeType.DefaultFrequency)
            ? "Monthly"
            : feeType.DefaultFrequency;
    /// <summary>What the row's Frequency shows on load: the frequency most of this fee type's stored configurations use…</summary>
    private string RowFrequencyFor(FeeTypeRow feeType)
    {
        string fallback = DefaultFrequencyFor(feeType);
        var groups = existing
            .Where(pair => pair.Key.FeeTypeId == feeType.FeeTypeId)
            .Select(pair => Frequencies.FirstOrDefault(
                f => string.Equals(f, pair.Value.Frequency, StringComparison.OrdinalIgnoreCase)))
            .Where(f => f != null)
            .GroupBy(f => f!)
            .Select(g => (Frequency: g.Key, Count: g.Count()))
            .ToList();
        if (groups.Count == 0)
            return fallback;
        int top = groups.Max(g => g.Count);
        var leaders = groups.Where(g => g.Count == top).Select(g => g.Frequency).ToList();
        return leaders.Any(f => string.Equals(f, fallback, StringComparison.OrdinalIgnoreCase))
            ? fallback
            : leaders[0];
    }
    private void BuildGridRows()
    {
        buildingGridRows = true;
        frequencyEditedFeeTypeIds.Clear();
        editedDetails.Clear();
        try
        {
            grid.Rows.Clear();
            foreach (var feeType in feeTypeRows)
            {
                int rowIndex = grid.Rows.Add();
                var row = grid.Rows[rowIndex];
                row.Cells[FeeTypeColumnName].Value = feeType.FeeTypeName;
                row.Cells[FrequencyColumnName].Value = RowFrequencyFor(feeType);
                foreach (var classColumn in classes)
                {
                    string cellName = "CL_" + classColumn.ClassId;
                    if (existing.TryGetValue((classColumn.ClassId, feeType.FeeTypeId), out var cell))
                    {
                        row.Cells[cellName].Value = FormatAmount(cell.Amount);
                        row.Cells[cellName].ToolTipText = $"{cell.Frequency}\n{cell.Remarks}";
                    }
                }
            }
        }
        finally
        {
            buildingGridRows = false;
        }
    }
    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.##", CultureInfo.InvariantCulture);
    /// <summary>every editable cell value as one comparable snapshot, so the guard asks about a grid that really differs from the data it was loaded with rather than about a cell that was…</summary>
    private string GridState()
    {
        var values = new List<string>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column.ReadOnly)
                    continue;
                values.Add(Convert.ToString(row.Cells[column.Name].Value) ?? "");
            }
        }
        foreach (var pair in editedDetails.OrderBy(p => p.Key.ClassId).ThenBy(p => p.Key.FeeTypeId))
            values.Add($"{pair.Key.ClassId}:{pair.Key.FeeTypeId}:{pair.Value.Frequency}:{pair.Value.Remarks}");
        return string.Join("\u001f", values);
    }
    private void CaptureGridSnapshot()
    {
        loadedSnapshot = GridState();
        loadedCellValues.Clear();
        foreach (DataGridViewRow row in grid.Rows)
            foreach (DataGridViewCell cell in row.Cells)
                if (!cell.ReadOnly) { loadedCellValues[cell] = cell.Value; cell.Style.BackColor = Color.Empty; }
    }
    /// <summary>True when the grid holds an amount the operator has not saved.</summary>
    private bool IsDirty() =>
        grid.IsCurrentCellDirty ||
        !string.Equals(GridState(), loadedSnapshot, StringComparison.Ordinal);
    private CellDetails DetailsForCell(int classId, FeeTypeRow fee, string rowFrequency)
    {
        var key = (classId, fee.FeeTypeId);
        if (editedDetails.TryGetValue(key, out var edited)) return edited;
        existing.TryGetValue(key, out var saved);
        return new(saved is not null && !frequencyEditedFeeTypeIds.Contains(fee.FeeTypeId)
            ? saved.Frequency : rowFrequency, saved?.Remarks ?? "");
    }
    private void EditSelectedFee()
    {
        if (savingGrid || !grid.EndEdit()) return;
        if (grid.CurrentCell is not { } cell || !cell.OwningColumn.Name.StartsWith("CL_", StringComparison.Ordinal) || cell.RowIndex >= feeTypeRows.Count)
        {
            summaryLabel.Text = "Select a class amount cell, then choose Edit Selected Fee.";
            grid.Focus();
            return;
        }
        int classId = int.Parse(cell.OwningColumn.Name[3..]);
        var fee = feeTypeRows[cell.RowIndex];
        var key = (classId, fee.FeeTypeId);
        existing.TryGetValue(key, out var saved);
        editedDetails.TryGetValue(key, out var details);
        string text = Convert.ToString(cell.Value)?.Trim() ?? "";
        decimal value = 0;
        if (text.Length > 0 && (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) || value < 0 || value > 1000000000 || !WholeRupees.IsWhole(value)))
        {
            cell.ErrorText = "Enter a whole-rupee amount between 0 and 1,000,000,000.";
            grid.Focus();
            return;
        }
        string rowFrequency = Convert.ToString(grid.Rows[cell.RowIndex].Cells[FrequencyColumnName].Value) ?? DefaultFrequencyFor(fee);
        var currentDetails = DetailsForCell(classId, fee, rowFrequency);
        using var dialog = new FeeRateDetailsDialog(classes.First(c => c.ClassId == classId).ClassName, fee.FeeTypeName,
            value, currentDetails.Frequency, currentDetails.Remarks);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) { grid.Focus(); return; }
        cell.Value = FormatAmount(dialog.Amount);
        var updated = new CellDetails(dialog.Frequency, dialog.Remarks);
        if (saved is not null && saved.Frequency == updated.Frequency && saved.Remarks == updated.Remarks && !frequencyEditedFeeTypeIds.Contains(fee.FeeTypeId))
            editedDetails.Remove(key);
        else editedDetails[key] = updated;
        cell.ToolTipText = $"{updated.Frequency}\n{updated.Remarks}";
        RefreshEditSummary();
        grid.Focus();
    }
    private void ApplyToStudents()
    {
        if (savingGrid || !grid.EndEdit()) return;
        if (IsDirty())
        {
            ModernMessageDialog.Show(FindForm(), "Save the grid first, then apply the saved rates to existing students.",
                PageTitles.UpdateStudentFeeRates, MessageBoxButtons.OK, MessageBoxIcon.Information);
            grid.Focus();
            return;
        }
        if (campus.SelectedValue is null || existing.Count == 0)
        {
            ModernMessageDialog.Show(FindForm(), "Choose a campus with saved fee rates first.",
                PageTitles.UpdateStudentFeeRates, MessageBoxButtons.OK, MessageBoxIcon.Information);
            campus.Focus();
            return;
        }
        var scopes = classes.SelectMany(c => feeTypeRows
            .Where(f => existing.TryGetValue((c.ClassId, f.FeeTypeId), out var rate) && rate.Frequency is "Monthly" or "Yearly")
            .Select(f => new SavedFeeScope(c.ClassId, c.ClassName, f.FeeTypeId, f.FeeTypeName)))
            .ToArray();
        if (scopes.Length == 0)
        {
            ModernMessageDialog.Show(FindForm(), "Only monthly and yearly fee rates can be applied to existing assignments.",
                PageTitles.UpdateStudentFeeRates, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new ApplicationWindowForm {
            Text = PageTitles.UpdateStudentFeeRates,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(AppScale.Scale(1250), AppScale.Scale(650)),
            MinimumSize = new Size(AppScale.Scale(720), AppScale.Scale(440)),
            ShowInTaskbar = false, MinimizeBox = false,
            BackColor = AppTheme.PageBackground
        };
        var page = new ApplyFeeConfigurationPage(Convert.ToInt32(campus.SelectedValue), scopes) { Dock = DockStyle.Fill };
        dialog.Controls.Add(page);
        dialog.FormClosing += (_, e) => { if (page.IsApplying) e.Cancel = true; };
        dialog.ShowDialog(FindForm());
        grid.Focus();
    }
    /// <summary>asks at most once for a close gesture. True when the caller may proceed with the close.</summary>
    private bool ConfirmDiscard()
    {
        return ModernMessageDialog.ConfirmDiscardOnce(this, IsDirty, ref discardConfirmed);
    }
    private void ClearCellError()
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            foreach (DataGridViewCell cell in row.Cells)
            {
                if (cell.ColumnIndex <= 1 ||
                    string.IsNullOrEmpty(cell.ErrorText))
                {
                    continue;
                }
                cell.Style.BackColor = cell.RowIndex % 2 == 1
                    ? AppTheme.SurfaceSecondary
                    : AppTheme.Surface;
                cell.ErrorText = "";
            }
        }
    }
    private sealed class ChangeRow
    {
        public int ClassId;
        public string ClassName = "";
        public int FeeTypeId;
        public string FeeTypeName = "";
        public int FeeConfigurationId;
        public decimal Amount;
        public string Frequency = "";
        public string Remarks = "";
        public bool IsNew;
    }
    private async Task SaveGridAsync()
    {
        int campusId = Convert.ToInt32(campus.SelectedValue);
        var changes = new List<ChangeRow>();
        int invalidCount = 0;
        DataGridViewCell? firstInvalidCell = null;
        int unchangedCleared = 0;
        for (int r = 0; r < feeTypeRows.Count; r++)
        {
            var feeType = feeTypeRows[r];
            var gridRow = grid.Rows[r];
            string rowFrequency =
                Convert.ToString(gridRow.Cells[FrequencyColumnName].Value ?? "")?.Trim() ?? "";
            if (rowFrequency.Length == 0)
                rowFrequency = DefaultFrequencyFor(feeType);
            foreach (var classColumn in classes)
            {
                string cellName = "CL_" + classColumn.ClassId;
                var cell = gridRow.Cells[cellName];
                string text = Convert.ToString(cell.Value ?? "")?.Trim() ?? "";
                existing.TryGetValue((classColumn.ClassId, feeType.FeeTypeId), out var existingCell);
                if (text.Length == 0)
                {
                    if (existingCell != null)
                        unchangedCleared++;
                    continue;
                }
                if (!decimal.TryParse(
                        text,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out decimal amount) ||
                    amount < 0 || !WholeRupees.IsWhole(amount))
                {
                    cell.Style.BackColor = AppTheme.Tint(AppTheme.Danger, 0.85f);
                    cell.ErrorText = "Enter a non-negative whole-rupee amount or leave blank.";
                    firstInvalidCell ??= cell;
                    invalidCount++;
                    continue;
                }
                bool amountUnchanged =
                    existingCell != null && existingCell.Amount == amount;
                /* A stored frequency changes only when the operator changed this fee type's Frequency on this screen; otherwise the cell keeps its own. */
                var details = DetailsForCell(classColumn.ClassId, feeType, rowFrequency);
                string cellFrequency = details.Frequency;
                string cellRemarks = details.Remarks;
                bool frequencyUnchanged =
                    existingCell != null &&
                    string.Equals(existingCell.Frequency, cellFrequency, StringComparison.OrdinalIgnoreCase);
                if (amountUnchanged && frequencyUnchanged && cellRemarks == existingCell?.Remarks)
                    continue;
                changes.Add(new ChangeRow {
                    ClassId = classColumn.ClassId,
                    ClassName = classColumn.ClassName,
                    FeeTypeId = feeType.FeeTypeId,
                    FeeTypeName = feeType.FeeTypeName,
                    FeeConfigurationId = existingCell?.FeeConfigurationId ?? 0,
                    Amount = amount,
                    Frequency = cellFrequency,
                    Remarks = cellRemarks,
                    IsNew = existingCell == null
                });
            }
        }
        if (invalidCount > 0)
        {
            summaryLabel.Text = $"{invalidCount} cell(s) have an invalid amount - fix the highlighted cells and try again.";
            /* UX - take the person to the first cell that needs fixing. */
            if (firstInvalidCell != null)
                grid.CurrentCell = firstInvalidCell;
            return;
        }
        if (changes.Count == 0)
        {
            summaryLabel.Text = unchangedCleared > 0
                ? $"Nothing to save ({unchangedCleared} cleared cell(s) were left unchanged, not deleted)."
                : "Nothing to save.";
            return;
        }
        int newCount = changes.Count(c => c.IsNew);
        int updateCount = changes.Count - newCount;
        var confirmParts = new List<string>();
        if (newCount > 0)
            confirmParts.Add($"{newCount} new");
        if (updateCount > 0)
            confirmParts.Add($"{updateCount} updated");
        try
        {
            /* ONE atomic call instead of one SaveFeeConfiguration per cell. dbo.ApplyFeeConfigurationPlan validates the whole plan, then applies every row inside a single transaction and commits only if all of them succeed. */
            await DatabaseHelper.ExecuteUserAsync(
                "ApplyFeeConfigurationPlan",
                DatabaseHelper.Int("@UserId", CurrentUser.UserId),
                new SqlParameter("@Plan", SqlDbType.Structured) {
                    TypeName = "dbo.FeeConfigurationPlanType",
                    Value = Tvp.FeeConfigurationPlan(
                        changes.Select(change => (
                            change.FeeConfigurationId,
                            campusId,
                            change.ClassId,
                            change.FeeTypeId,
                            change.Amount,
                            (string?)change.Frequency,
                            (string?)change.Remarks)))
                });
            Applied = true;
            string savedMessage = UiText.Fees.MatrixSaved +
                $" {string.Join(" and ", confirmParts)}." +
                (unchangedCleared > 0 ? $" {unchangedCleared} cleared cell(s) were left unchanged." : "");
            await LoadGridAsync(savedMessage, campusId);
        }
        catch (Exception ex)
        {
            ModernMessageDialog.Show(this, DbMessage.UserMessage(ex), "Save Grid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}


