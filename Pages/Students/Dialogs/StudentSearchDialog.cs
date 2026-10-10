using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Students.Dialogs;
public sealed class StudentSearchDialog : ApplicationWindowForm
{
    private readonly ModernTextBox searchBox = HumanUi.TextBox(false, 320);
    private readonly LookupBox searchCampus = new();
    private readonly LookupBox searchYear = new();
    private readonly LookupBox searchClass = new();
    private readonly ModernComboBox searchStatus = HumanUi.Combo();
    private readonly DataGridView resultsGrid = HumanUi.Grid(false);
    private readonly ModernButton searchButton = new() {
        Text = "Search",
        Width = AppScale.Scale(110),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = UiDesignSystem.Actions.Primary
    };
    private readonly ModernButton resetButton = new() {
        Text = UiText.Actions.Reset,
        Width = AppScale.Scale(110),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = UiDesignSystem.Actions.Secondary
    };
    private readonly ModernButton openButton = new() {
        Text = "Open Student",
        Width = AppScale.Scale(140),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = UiDesignSystem.Actions.Success
    };
    private readonly ModernButton closeButton = new() {
        Text = UiText.Actions.Close,
        Width = AppScale.Scale(110),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = UiDesignSystem.Actions.Secondary
    };
    private readonly Label status = new() {
        Dock = DockStyle.Fill,
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        Margin = new Padding(0)
    };
    private int searchVersion;
    public int SelectedAdmissionId { get; private set; }
    public string? NewAdmissionNumber { get; private set; }
    private string? missingAdmissionNumber;
    public StudentSearchDialog(IWin32Window? owner = null)
    {
        Text = "Find Student";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        /* Opens large - most of the working area of the screen it is on. */
        Rectangle workArea =
            (owner is Control ownerControl
                ? Screen.FromControl(ownerControl)
                : Screen.PrimaryScreen)?.WorkingArea
            ?? new Rectangle(0, 0, 1366, 768);
        Width = Math.Min(workArea.Width - 24, Math.Max(AppScale.Scale(1200, AppScale.ControlSizeScale), (int)(workArea.Width * 0.92)));
        Height = Math.Min(workArea.Height - 24, (int)(workArea.Height * 0.94));
        MinimumSize = new Size(
            Math.Min(AppScale.Scale(950, AppScale.ControlSizeScale), Width),
            Math.Min(AppScale.Scale(560, AppScale.ControlSizeScale), Height));
        BackColor = AppTheme.PageBackground;
        ForeColor = AppTheme.Text;
        Font = AppTheme.NormalFont;
        searchBox.PlaceholderText =
            "Admission Number (number) or Student Name / Father's Name";
        searchStatus.Items.Clear();
        searchStatus.Items.AddRange(new object[] {
            /* this read "Active, Left & Withdrawn". The label named a state the system cannot represent. */
            "All Statuses (Active & Left)",
            "Active Only"
        });
        searchStatus.SelectedIndex = 0;
        ConfigureGrid();
        _ = LoadFiltersAsync();
        WireEvents();
        Controls.Add(BuildRoot());
        ClearSelectedDetails();
        status.Text =
            "Enter a search value or use the filters, then select a student to open.";
        Shown += (_, _) => searchBox.Focus();
        KeyDown += (_, e) => {
            /* UX - a drop-down that is open owns Esc/Enter (close the list / pick the item). The dialog used to close, or run a search, underneath it. */
            bool listOpen = ActiveControl is ComboBox { DroppedDown: true };
            if (listOpen)
                return;
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            else if (e.KeyCode == Keys.Enter && resultsGrid.Focused)
            {
                e.SuppressKeyPress = true;
                OpenSelectedStudent();
            }
            else if (e.KeyCode == Keys.Enter && ActiveControl is ButtonBase)
            {
                /* UX - Enter on a focused button presses THAT button (Reset, Open Student, Close). It used to run a search instead, whatever had focus. */
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (searchButton.Enabled)
                    Search();
            }
            else if (e.KeyCode == Keys.Down && ActiveControl is TextBox && resultsGrid.Rows.Count > 0)
            {
                /* UX - from the search box, Down goes straight to the results so the whole type -> Enter -> Down -> Enter path works without the mouse or Tab. */
                e.SuppressKeyPress = true;
                resultsGrid.Focus();
            }
        };
    }
    /* UX - Enter on the results grid must open the student. ProcessCmdKey runs first and is deterministic. */
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter && resultsGrid.ContainsFocus && !resultsGrid.IsCurrentCellInEditMode && GetSelectedAdmissionId() > 0)
        {
            OpenSelectedStudent();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private Control BuildRoot()
    {
        /* Everything the dialog needs sits in three bands: one row of search criteria, the results (all the remaining height), and a slim action row that also carries the status text. */
        int captionHeight =
            Math.Max(16, AppMetrics.TextHeight(UiDesignSystem.Typography.Caption));
        /* The buttons line up with the inputs: same height, and pushed down by the caption row. */
        searchButton.Height = AppMetrics.InputControlHeight;
        resetButton.Height = AppMetrics.InputControlHeight;
        searchButton.Margin = new Padding(0, captionHeight, 8, 0);
        resetButton.Margin = new Padding(0, captionHeight, 0, 0);
        var buttons = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        buttons.Controls.Add(searchButton);
        buttons.Controls.Add(resetButton);
        var searchGrid = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        searchGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        searchGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17F));
        searchGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17F));
        searchGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17F));
        searchGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19F));
        searchGrid.ColumnStyles.Add(new ColumnStyle(
            SizeType.Absolute,
            searchButton.Width + resetButton.Width + 8 + 8));
        searchGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        searchGrid.Controls.Add(HumanUi.Field("Search", searchBox), 0, 0);
        searchGrid.Controls.Add(HumanUi.Field("Campus (optional)", searchCampus), 1, 0);
        searchGrid.Controls.Add(HumanUi.Field("Academic Year (optional)", searchYear), 2, 0);
        searchGrid.Controls.Add(HumanUi.Field("Class (optional)", searchClass), 3, 0);
        searchGrid.Controls.Add(HumanUi.Field("Status", searchStatus), 4, 0);
        searchGrid.Controls.Add(buttons, 5, 0);
        var searchPanel = new RoundedPanel {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            ShowBorder = true,
            CornerRadius = AppTheme.CardRadius,
            Padding = new Padding(10, 6, 10, 2),
            Margin = new Padding(0, 0, 0, 8)
        };
        searchPanel.Controls.Add(searchGrid);
        var resultsPanel = new RoundedPanel {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            ShowBorder = true,
            CornerRadius = AppTheme.CardRadius,
            Padding = new Padding(2),
            Margin = new Padding(0)
        };
        resultsPanel.Controls.Add(resultsGrid);
        /* ButtonRow makes the buttons equally wide and puts a margin round each, so the column is measured AFTER it has run. */
        var actions = HumanUi.ButtonRow(openButton, closeButton);
        var actionRow = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,
            openButton.Width + closeButton.Width +
            openButton.Margin.Horizontal + closeButton.Margin.Horizontal + 24));
        actions.Dock = DockStyle.Fill;
        actionRow.Controls.Add(status, 0, 0);
        actionRow.Controls.Add(actions, 1, 0);
        openButton.Enabled = false;
        int pad = Math.Min(AppMetrics.PagePadding, 12);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(pad),
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            AppMetrics.FieldRowHeight + 6 + 2 + 8 + 4));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
        root.Controls.Add(searchPanel, 0, 0);
        root.Controls.Add(resultsPanel, 0, 1);
        root.Controls.Add(actionRow, 0, 2);
        return root;
    }
    private void WireEvents()
    {
        searchButton.Click += (_, _) => Search();
        resetButton.Click += (_, _) => ResetFilters();
        /* changing any criterion invalidates a search that is already running, so its result can never land under filters it was not run for. */
        searchBox.TextChanged += (_, _) => searchVersion++;
        searchCampus.SelectedIndexChanged += (_, _) => searchVersion++;
        searchYear.SelectedIndexChanged += (_, _) => searchVersion++;
        searchClass.SelectedIndexChanged += (_, _) => searchVersion++;
        searchStatus.SelectedIndexChanged += (_, _) => searchVersion++;
        openButton.Click += (_, _) => OpenSelectedStudent();
        closeButton.Click += (_, _) => {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        resultsGrid.SelectionChanged += (_, _) => ShowSelectedDetails();
        resultsGrid.CellDoubleClick += (_, e) => {
            if (e.RowIndex >= 0)
                OpenSelectedStudent();
        };
    }
    private async Task LoadFiltersAsync()
    {
        try
        {
            var results = await Task.WhenAll(
                DatabaseHelper.QueryUserAsync("GetCampuses"),
                DatabaseHelper.QueryUserAsync("GetAcademicYears"),
                DatabaseHelper.QueryUserAsync("GetClasses"));
            if (IsDisposed) return;
            searchCampus.Bind(results[0], "CampusId", "CampusName");
            searchYear.Bind(results[1], "AcademicYearId", "YearName");
            searchClass.Bind(results[2], "ClassId", "ClassName");
            if (searchCampus.Items.Count > 0) searchCampus.SelectedIndex = 0;
            if (searchYear.Items.Count > 0) searchYear.SelectedIndex = 0;
            if (searchClass.Items.Count > 0) searchClass.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                ModernMessageDialog.Show(this, DbMessage.UserMessage(ex), "Student Search", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private void ResetFilters()
    {
        searchBox.Clear();
        if (searchCampus.Items.Count > 0)
            searchCampus.SelectedIndex = 0;
        if (searchYear.Items.Count > 0)
            searchYear.SelectedIndex = 0;
        if (searchClass.Items.Count > 0)
            searchClass.SelectedIndex = 0;
        searchStatus.SelectedIndex = 0;
        resultsGrid.DataSource = null;
        ClearSelectedDetails();
        status.Text =
            "Filters reset. Enter a search value or choose filters, then Search.";
        searchBox.Focus();
    }
    /// <summary>every criterion the search request is built from, in one comparable string, read from the live controls.</summary>
    private string FilterSignature()
    {
        /* An Admission Number search does not use Campus / Year / Class / Status (they are an independent set of filters). */
        string text = searchBox.Text.Trim();
        bool byAdmissionId = AdmissionNumbers.TryNormalize(text, out _);
        return string.Join(
            '|',
            byAdmissionId ? 0 : searchCampus.SelectedId,
            byAdmissionId ? 0 : searchYear.SelectedId,
            byAdmissionId ? 0 : searchClass.SelectedId,
            byAdmissionId ? 0 : searchStatus.SelectedIndex,
            text);
    }
    private void Search() => _ = SearchAsync();
    /* Item 7 - how many searches are actually running. The buttons follow this count instead: they come back as soon as nothing is running, whichever search that was. */
    private int searchesRunning;
    private async Task SearchAsync()
    {
        int version = ++searchVersion;
        ClearSelectedDetails();
        bool counted = false;
        try
        {
            /* The criteria this query is actually run for are captured here and compared again after the await. */
            string filterSignature = FilterSignature();
            string searchText = searchBox.Text.Trim();
            bool isAdmissionIdSearch = AdmissionNumbers.TryNormalize(searchText, out string admissionNumber);
            /* The filters the operator has set on screen (before an Admission Number search sets them aside), only so the status line can say they were not applied. */
            bool filtersSetOnScreen =
                searchCampus.SelectedId > 0 || searchYear.SelectedId > 0 ||
                searchClass.SelectedId > 0 || searchStatus.SelectedIndex != 0;
            /* Admission Number is an independent filter (): a number in the Search box finds that student whatever Campus / Academic Year / Class / Status say. */
            int campusId = isAdmissionIdSearch ? 0 : searchCampus.SelectedId;
            int academicYearId = isAdmissionIdSearch ? 0 : searchYear.SelectedId;
            int classId = isAdmissionIdSearch ? 0 : searchClass.SelectedId;
            int statusIndex = isAdmissionIdSearch ? 0 : searchStatus.SelectedIndex;
            bool hasFilter = campusId > 0 || academicYearId > 0 || classId > 0 || !string.IsNullOrWhiteSpace(searchText);
            if (!hasFilter)
            {
                resultsGrid.DataSource = null;
                ClearSelectedDetails();
                status.Text = "Enter an Admission Number, student name, or father's name, or choose a filter, then Search.";
                return;
            }
            status.Text = UiText.Prompts.SearchingStudents;
            counted = true;
            searchesRunning++;
            searchButton.Enabled = false;
            resetButton.Enabled = false;
            /* made a numeric search obey the filters on screen, so a campus + an admission number from another campus returned nothing. */
            DataTable raw = isAdmissionIdSearch
                ? await DatabaseHelper.QueryUserAsync(
                    "GetStudents",
                    DatabaseHelper.Int("@CampusId", campusId),
                    DatabaseHelper.Int("@AcademicYearId", academicYearId),
                    DatabaseHelper.Int("@ClassId", classId),
                    DatabaseHelper.Int("@ClassSectionId", 0),
                    DatabaseHelper.P("@Search", admissionNumber),
                    DatabaseHelper.Bit("@IncludeFormerStudents", statusIndex == 0))
                : await DatabaseHelper.QueryUserAsync(
                    "GetStudents",
                    DatabaseHelper.Int("@CampusId", campusId),
                    DatabaseHelper.Int("@AcademicYearId", academicYearId),
                    DatabaseHelper.Int("@ClassId", classId),
                    DatabaseHelper.Int("@ClassSectionId", 0),
                    DatabaseHelper.P("@Search", string.IsNullOrWhiteSpace(searchText) ? null : searchText),
                    DatabaseHelper.Bit("@IncludeFormerStudents", statusIndex == 0));
            if (IsDisposed || version != searchVersion || !string.Equals(filterSignature, FilterSignature(), StringComparison.Ordinal)) return;
            IEnumerable<DataRow> candidates = isAdmissionIdSearch
                ? raw.Rows.Cast<DataRow>()
                : HumanUi.FindStudentMatches(raw, searchText);
            var filtered = new List<DataRow>();
            foreach (DataRow row in candidates)
            {
                if (isAdmissionIdSearch && !string.Equals(AdmissionNumbers.Read(row), admissionNumber, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (statusIndex != 0 &&
                    !string.Equals(GetTextColumn(row, "Status"), "Active", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!isAdmissionIdSearch)
                {
                    int rowCampusId = GetIntColumn(row, "CurrentCampusId");
                    if (rowCampusId <= 0) rowCampusId = GetIntColumn(row, "AdmissionCampusId");
                    if (campusId > 0 && rowCampusId != campusId) continue;
                    if (academicYearId > 0 && GetIntColumn(row, "AcademicYearId") != academicYearId) continue;
                    if (classId > 0 && GetIntColumn(row, "ClassId") != classId) continue;
                }
                filtered.Add(row);
            }
            if (IsDisposed || version != searchVersion || !string.Equals(filterSignature, FilterSignature(), StringComparison.Ordinal)) return;
            resultsGrid.DataSource = BuildDisplayTable(filtered);
            ConfigureGrid();
            ClearSelectedDetails();
            if (filtered.Count == 0)
            {
                if (isAdmissionIdSearch && CurrentUser.HasPermission("Students.Administer"))
                {
                    missingAdmissionNumber = admissionNumber;
                    openButton.Text = "New Admission";
                    openButton.Enabled = true;
                }
                status.Text = isAdmissionIdSearch
                    ? $"No student found for Admission Number {admissionNumber}."
                    : "No students match the selected criteria.";
                return;
            }
            /* Item 8 - binding highlights the first row on its own, WITHOUT raising SelectionChanged. */
            SelectFirstResultRow();
            ShowSelectedDetails();
            status.Text = filtered.Count == 1
                ? (isAdmissionIdSearch
                    ? $"Admission Number {admissionNumber} found. Press Enter to open it."
                        + (filtersSetOnScreen ? " Campus, Year, Class and Status are not applied to an Admission Number search." : string.Empty)
                    : "1 student found. Press Enter to open the record.")
                : $"{filtered.Count:N0} student(s) found. The first one is selected - "
                  + "press Down to choose another, then Enter to open.";
            /* UX - one match: hand focus to the result so Enter opens it. */
            if (filtered.Count == 1 && !resultsGrid.Focused && ActiveControl is TextBox)
                resultsGrid.Focus();
        }
        catch (Exception ex)
        {
            if (!IsDisposed && version == searchVersion)
                ModernMessageDialog.Show(this, DbMessage.UserMessage(ex), "Student Search", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (counted && searchesRunning > 0)
                searchesRunning--;
            if (!IsDisposed && searchesRunning == 0)
            {
                searchButton.Enabled = true;
                resetButton.Enabled = true;
            }
        }
    }
    private void ConfigureGrid()
    {
        /* Fill: the columns share the whole width (weighted by their old fixed widths), so a wide dialog has no empty strip beside the list and no horizontal scrollbar. */
        resultsGrid.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.None;
        resultsGrid.AutoSizeRowsMode =
            DataGridViewAutoSizeRowsMode.None;
        resultsGrid.ScrollBars = ScrollBars.Vertical;
        resultsGrid.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;
        resultsGrid.MultiSelect = false;
        resultsGrid.ReadOnly = true;
        resultsGrid.AllowUserToAddRows = false;
        resultsGrid.AllowUserToDeleteRows = false;
        resultsGrid.RowHeadersVisible = false;
        if (resultsGrid.Columns.Contains("AdmissionId"))
            resultsGrid.Columns["AdmissionId"].Visible = false;
        SetColumn("AdmissionNumber", "Admission Number", 110);
        SetColumn("StudentName", "Student Name", 205);
        SetColumn("FatherName", "Father Name", 205);
        SetColumn("Campus", "Campus", 130);
        SetColumn("AcademicYear", "Academic Year", 115);
        SetColumn("Class", "Class", 95);
        SetColumn("Section", "Section", 95);
        SetColumn("Status", "Status", 115);
    }
    private void SetColumn(string name, string header, int width)
    {
        if (!resultsGrid.Columns.Contains(name))
            return;
        resultsGrid.Columns[name].HeaderText = header;
        resultsGrid.Columns[name].MinimumWidth = AppScale.Scale(width * 6 / 10, AppScale.ControlSizeScale);
        resultsGrid.Columns[name].FillWeight = width;
        resultsGrid.Columns[name].SortMode =
            DataGridViewColumnSortMode.Automatic;
    }
    /// <summary>Item 8 - puts the selection on the first row and, just as importantly, puts the CURRENT cell there.</summary>
    private void SelectFirstResultRow()
    {
        if (resultsGrid.Rows.Count == 0)
            return;
        resultsGrid.ClearSelection();
        resultsGrid.Rows[0].Selected = true;
        DataGridViewColumn? firstVisibleColumn =
            resultsGrid.Columns
                .Cast<DataGridViewColumn>()
                .FirstOrDefault(c => c.Visible);
        if (firstVisibleColumn != null)
            resultsGrid.CurrentCell =
                resultsGrid.Rows[0].Cells[firstVisibleColumn.Index];
    }
    private void ShowSelectedDetails()
    {
        int id = GetSelectedAdmissionId();
        openButton.Enabled = id > 0;
        if (id <= 0)
        {
            ClearSelectedDetails();
            return;
        }
        DataGridViewRow row = resultsGrid.CurrentRow!;
        string studentName = Convert.ToString(row.Cells["StudentName"].Value) ?? "-";
        status.Text =
            $"Selected: {studentName} (Admission Number {AdmissionNumbers.ForId(id)}). Double-click or choose Open Student.";
    }
    private int GetSelectedAdmissionId()
    {
        if (resultsGrid.CurrentRow is null ||
            !resultsGrid.Columns.Contains("AdmissionId"))
            return 0;
        object? value =
            resultsGrid.CurrentRow.Cells["AdmissionId"].Value;
        return value is null || value == DBNull.Value
            ? 0
            : int.TryParse(Convert.ToString(value), out int id)
                ? id
                : 0;
    }
    private void OpenSelectedStudent()
    {
        if (!string.IsNullOrWhiteSpace(missingAdmissionNumber))
        {
            NewAdmissionNumber = missingAdmissionNumber;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }
        int id = GetSelectedAdmissionId();
        if (id <= 0)
            return;
        SelectedAdmissionId = id;
        DialogResult = DialogResult.OK;
        Close();
    }
    private void ClearSelectedDetails()
    {
        missingAdmissionNumber = null;
        openButton.Text = "Open Student";
        openButton.Enabled = false;
    }
    private static DataTable BuildDisplayTable(IEnumerable<DataRow> source)
    {
        var display = new DataTable();
        display.Columns.Add("AdmissionId", typeof(int));
        display.Columns.Add("AdmissionNumber", typeof(string));
        display.Columns.Add("StudentName", typeof(string));
        display.Columns.Add("FatherName", typeof(string));
        display.Columns.Add("Campus", typeof(string));
        display.Columns.Add("AcademicYear", typeof(string));
        display.Columns.Add("Class", typeof(string));
        display.Columns.Add("Section", typeof(string));
        display.Columns.Add("Status", typeof(string));
        foreach (DataRow row in source)
        {
            string campus = GetTextColumn(row, "CurrentCampusName");
            if (string.IsNullOrWhiteSpace(campus))
                campus = GetTextColumn(row, "AdmissionCampusName");
            display.Rows.Add(
                GetIntColumn(row, "AdmissionId"),
                AdmissionNumbers.Read(row),
                GetTextColumn(row, "StudentName"),
                GetTextColumn(row, "FatherName"),
                campus,
                GetTextColumn(row, "YearName"),
                GetTextColumn(row, "ClassName"),
                GetTextColumn(row, "SectionName"),
                GetTextColumn(row, "Status"));
        }
        return display;
    }
    private static int GetIntColumn(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName) ||
            row[columnName] == DBNull.Value ||
            row[columnName] is null)
            return 0;
        return int.TryParse(Convert.ToString(row[columnName]), out int value)
            ? value
            : 0;
    }
    private static string GetTextColumn(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName) ||
            row[columnName] == DBNull.Value ||
            row[columnName] is null)
            return string.Empty;
        return Convert.ToString(row[columnName]) ?? string.Empty;
    }
}

