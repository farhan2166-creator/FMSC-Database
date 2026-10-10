using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Students.Enrollment;
public sealed class BulkPromotionPage : V2Page
{
    private readonly LookupBox sourceCampus = new();
    private readonly LookupBox sourceYear = new();
    private readonly LookupBox sourceClass = new();
    private readonly LookupBox sourceSection = new();
    private readonly LookupBox destinationYear = new();
    private readonly LookupBox destinationClass = new();
    private readonly LookupBox destinationSection = new();
    private readonly ModernDatePicker effectiveDate =
        HumanUi.Date();
    private readonly ModernTextBox remarks =
        HumanUi.TextBox(false, 500);
    private readonly DataGridView candidatesGrid =
        HumanUi.Grid(true);
    private readonly ContextMenuStrip candidateSelectionMenu = new StyledContextMenu();
    private void SetHighlightedSelection(bool selected)
    {
        if (busyCount > 0 || candidateData is null) return;
        candidatesGrid.EndEdit();
        foreach (DataGridViewRow row in candidatesGrid.SelectedRows)
            if (row.DataBoundItem is DataRowView item) item.Row["Selected"] = selected;
        candidatesGrid.Refresh();
        ApplyStateAndHint();
    }
    private readonly TableLayoutPanel promotionSetupTable =
        new() {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
    private readonly TableLayoutPanel sourceTable =
        new() {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
    private readonly TableLayoutPanel destinationTable =
        new() {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
    private readonly Panel sourceCampusField;
    private readonly Panel sourceYearField;
    private readonly Panel sourceClassField;
    private readonly Panel sourceSectionField;
    private readonly Panel destinationYearField;
    private readonly Panel destinationClassField;
    private readonly Panel destinationSectionField;
    private readonly Panel effectiveDateField;
    private readonly Panel remarksField;
    private readonly ModernButton loadButton = new() {
        Text = "Load Candidates",
        Width = AppScale.Scale(150)
    };
    private readonly ModernButton selectAllButton = new() {
        Text = "Select All",
        Width = AppScale.Scale(120)
    };
    private readonly ModernButton clearSelectionButton = new() {
        Text = "Clear Selection",
        Width = AppScale.Scale(145)
    };
    private readonly ModernButton promoteAllButton = new() {
        Text = "Ticked: Promote",
        Width = AppScale.Scale(145)
    };
    private readonly ModernButton holdAllButton = new() {
        Text = "Ticked: Hold",
        Width = AppScale.Scale(130)
    };
    private readonly ModernButton promoteSelectedButton = new() {
        Text = "Process Ticked",
        Width = AppScale.Scale(170)
    };
    private readonly ModernButton clearButton = new() {
        Text = "Clear",
        Width = AppScale.Scale(120)
    };
    private DataTable? candidateData;
    /* Appears after a promotion: the promoted students cannot be billed until their return is confirmed, so it offers Confirm Return Admission (on the student, when just one was promoted). */
    private readonly NextStepBar nextSteps;
    /* Start date of every academic year, read once. */
    private readonly Dictionary<int, DateTime> yearStarts = new();
    private DataTable? yearTable;
    /* All classes, read once. The destination class list is derived from it (same program, later class than the source), see BuildDestinationClasses. */
    private DataTable? classTable;
    /* > 0 while a load or a promotion runs; ApplyState locks the whole page meanwhile. */
    private int busyCount;
    /// <param name="navigate">The shell's navigate delegate, so the step after promoting (Confirm Return Admission) can open the next screen.</param>
    public BulkPromotionPage(Action<Func<Control>, string, string>? navigate = null)
        : base("Bulk Promotion", "Promote a class or selected students in repeatable batches")
    {
        nextSteps = new NextStepBar(navigate, Fail);
        sourceCampusField =
            HumanUi.Field("Source Campus", sourceCampus, true);
        sourceYearField =
            HumanUi.Field("Source Academic Year", sourceYear, true);
        sourceClassField =
            HumanUi.Field("Source Class", sourceClass, true);
        sourceSectionField =
            HumanUi.Field("Source Section", sourceSection);
        destinationYearField =
            HumanUi.Field("Destination Academic Year", destinationYear, true);
        destinationClassField =
            HumanUi.Field("Destination Class", destinationClass, true);
        destinationSectionField =
            HumanUi.Field("Destination Section", destinationSection, true);
        effectiveDate.Value = CurrentYearStart();
        effectiveDateField =
            HumanUi.Field("Effective Date", effectiveDate, true, fillHeight: false);
        remarksField =
            HumanUi.Field("Remarks", remarks, fillHeight: false);
        ConfigureTables();
        ConfigureGrid();
        ConfigureButtons();
        /* CASCADE RULE: what the operator picks in a parent combo resets everything beneath it. source campus -> source section, destination section source year -> destination year… */
        sourceCampus.UserSelectionChanged += async (_, _) => await OnSourceCampusChanged();
        sourceYear.UserSelectionChanged += (_, _) => OnSourceYearChanged();
        sourceClass.UserSelectionChanged += async (_, _) => await OnSourceClassChanged();
        sourceSection.UserSelectionChanged += (_, _) => OnSourceSectionChanged();
        destinationYear.UserSelectionChanged += (_, _) => OnDestinationYearChanged();
        destinationClass.UserSelectionChanged += async (_, _) => await OnDestinationClassChanged();
        destinationSection.UserSelectionChanged += (_, _) => ApplyStateAndHint();
        Defer(async () => {
            await Task.WhenAll(
                BindCampuses(),
                BindYears(),
                LoadClasses());
            ClearForm();
        });
        BuildPage();
    }
    private void ConfigureTables()
    {
        promotionSetupTable.ColumnStyles.Clear();
        promotionSetupTable.RowStyles.Clear();
        promotionSetupTable.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50f));
        promotionSetupTable.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50f));
        promotionSetupTable.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight * 2));
        promotionSetupTable.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        ConfigureInnerTable(sourceTable, 2);
        ConfigureInnerTable(destinationTable, 2);
        sourceTable.Controls.Clear();
        sourceTable.Controls.Add(sourceCampusField, 0, 0);
        sourceTable.Controls.Add(sourceYearField, 1, 0);
        sourceTable.Controls.Add(sourceClassField, 0, 1);
        sourceTable.Controls.Add(sourceSectionField, 1, 1);
        destinationTable.Controls.Clear();
        destinationTable.Controls.Add(destinationYearField, 0, 0);
        destinationTable.Controls.Add(destinationClassField, 1, 0);
        destinationTable.Controls.Add(destinationSectionField, 0, 1);
        destinationTable.Controls.Add(effectiveDateField, 1, 1);
        sourceTable.Margin =
            new Padding(0, 0, 8, 0);
        destinationTable.Margin =
            new Padding(8, 0, 0, 0);
        promotionSetupTable.Controls.Clear();
        promotionSetupTable.Controls.Add(sourceTable, 0, 0);
        promotionSetupTable.Controls.Add(destinationTable, 1, 0);
        promotionSetupTable.Controls.Add(remarksField, 0, 1);
        promotionSetupTable.SetColumnSpan(remarksField, 2);
    }
    private static void ConfigureInnerTable(TableLayoutPanel table, int rowCount)
    {
        table.ColumnStyles.Clear();
        table.RowStyles.Clear();
        table.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50f));
        table.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50f));
        table.RowCount = rowCount;
        for (int i = 0; i < rowCount; i++)
        {
            table.RowStyles.Add(
                new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        }
    }
    private void ConfigureButtons()
    {
        loadButton.Click += async (_, _) =>
            await LoadCandidates();
        selectAllButton.Click += (_, _) =>
            SelectAll(true);
        clearSelectionButton.Click += (_, _) =>
            SelectAll(false);
        promoteAllButton.Click += (_, _) =>
            SetAllDecision("PROMOTE");
        holdAllButton.Click += (_, _) =>
            SetAllDecision("HOLD");
        /* The button is disabled for the duration and re-enabled in a finally. */
        promoteSelectedButton.Click += async (_, _) => {
            if (!promoteSelectedButton.Enabled)
                return;
            /* The promotion owns the screen while it runs, so the whole setup card is held, not just this button. */
            BeginBusy();
            try
            {
                await ProcessSelected();
            }
            finally
            {
                EndBusy();
            }
        };
        clearButton.Click += (_, _) =>
            ClearForm();
        promoteSelectedButton.ThemeRole = "Success";
    }
    private void ConfigureGrid()
    {
        candidatesGrid.MultiSelect = true;
        candidatesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        candidateSelectionMenu.Items.Add("Tick highlighted students", null, (_, _) => SetHighlightedSelection(true));
        candidateSelectionMenu.Items.Add("Untick highlighted students", null, (_, _) => SetHighlightedSelection(false));
        candidateSelectionMenu.Opening += (_, e) => e.Cancel = busyCount > 0 || candidatesGrid.SelectedRows.Count == 0;
        candidatesGrid.ContextMenuStrip = candidateSelectionMenu;
        Disposed += (_, _) => candidateSelectionMenu.Dispose();
        candidatesGrid.AutoGenerateColumns = false;
        candidatesGrid.Columns.Add(
            new DataGridViewCheckBoxColumn {
                Name = "Selected",
                HeaderText = "Select",
                DataPropertyName = "Selected",
                Width = AppScale.Scale(65),
                MinimumWidth = AppScale.Scale(65),
                SortMode =
                    DataGridViewColumnSortMode.NotSortable
            });
        candidatesGrid.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "AdmissionNumber",
                HeaderText = "Admission Number",
                DataPropertyName = "AdmissionNumber",
                Width = AppScale.Scale(90),
                MinimumWidth = AppScale.Scale(80),
                ReadOnly = true
            });
        candidatesGrid.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "RegistrationNumber",
                HeaderText = UiText.Fields.RegistrationNumber,
                DataPropertyName = "RegistrationNumber",
                Width = AppScale.Scale(125),
                MinimumWidth = AppScale.Scale(110),
                ReadOnly = true
            });
        candidatesGrid.Columns.Add(
      new DataGridViewTextBoxColumn {
          Name = "StudentName",
          HeaderText = "Student",
          DataPropertyName = "StudentName",
          AutoSizeMode =
              DataGridViewAutoSizeColumnMode.None,
          FillWeight = 100,
          MinimumWidth = AppScale.Scale(90),
          ReadOnly = true
      });
        candidatesGrid.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "FatherName",
                HeaderText = "Father",
                DataPropertyName = "FatherName",
                AutoSizeMode =
                    DataGridViewAutoSizeColumnMode.None,
                FillWeight = 85,
                MinimumWidth = AppScale.Scale(90),
                ReadOnly = true
            });
        candidatesGrid.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "CurrentClass",
                HeaderText = "Current Class",
                DataPropertyName = "ClassName",
                Width = AppScale.Scale(110),
                MinimumWidth = AppScale.Scale(100),
                ReadOnly = true
            });
        candidatesGrid.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "CurrentSection",
                HeaderText = "Section",
                DataPropertyName = "SectionName",
                Width = AppScale.Scale(90),
                MinimumWidth = AppScale.Scale(80),
                ReadOnly = true
            });
        candidatesGrid.Columns.Add(
            new DataGridViewComboBoxColumn {
                Name = "Decision",
                HeaderText = "Decision",
                DataPropertyName = "Decision",
                Width = AppScale.Scale(110),
                MinimumWidth = AppScale.Scale(105),
                FlatStyle = FlatStyle.Flat,
                Items =
                {
                    "PROMOTE",
                    "HOLD"
                }
            });
        HumanUi.ApplyGridStyle(candidatesGrid, AppTheme.ModuleAccent("Students"));
        candidatesGrid.AutoSizeRowsMode =
            DataGridViewAutoSizeRowsMode.None;
        candidatesGrid.RowTemplate.Height =
            AppMetrics.GridRowHeight;
        candidatesGrid.CellContentClick += (_, e) => {
            if (e.RowIndex >= 0 &&
                e.ColumnIndex == 0)
            {
                candidatesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        /* Every committed change refreshes which buttons are usable and the "N ticked, N to promote" line. */
        candidatesGrid.CurrentCellDirtyStateChanged += (_, _) => {
            if (candidatesGrid.IsCurrentCellDirty)
                candidatesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        candidatesGrid.CellValueChanged += (_, e) => {
            if (e.RowIndex >= 0)
                ApplyStateAndHint();
        };
        candidatesGrid.DataError += (_, e) =>
            e.ThrowException = false;
    }
    private void BuildPage()
    {
        var setupCard =
            HumanUi.Card(
                "Promotion Setup & Destination",
                "Select the source campus, year, class and section, then choose the destination year, class, section and effective date.");
        setupCard.Content.Controls.Add(promotionSetupTable);
        var candidatesCard =
            HumanUi.Card(
                "Students to Process",
                "Select the entire class or individual students. Unselected students remain available for a later promotion batch.");
        candidatesCard.Content.Controls.Add(candidatesGrid);
        candidatesCard.Content.Controls.Add(BuildCandidateActionBar());
        var root =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        int setupHeight =
            AppMetrics.CardHeaderHeight()
            + AppMetrics.CardContentVerticalPadding
            + (AppMetrics.FieldRowHeight * 3)
            + 8;
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, setupHeight));
        /* The next-step strip: no height while hidden. */
        root.RowStyles.Add(
            new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100f));
        root.Controls.Add(setupCard, 0, 0);
        root.Controls.Add(nextSteps, 0, 1);
        root.Controls.Add(candidatesCard, 0, 2);
        SetContent(
            root,
            "Choose the source class to load candidates, then set the destination class and section they will be promoted into. Review the candidate list before promoting the whole class or only the selected students.");
    }
    private Control BuildCandidateActionBar()
    {
        var row =
            new TableLayoutPanel {
                Dock = DockStyle.Bottom,
                Height = AppMetrics.ButtonRowHeight,
                ColumnCount = 7,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        float[] widths =
        {
            17f,
            12f,
            17f,
            16f,
            12f,
            18f,
            8f
        };
        for (int i = 0; i < widths.Length; i++)
        {
            row.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, widths[i]));
        }
        row.Controls.Add(loadButton, 0, 0);
        row.Controls.Add(selectAllButton, 1, 0);
        row.Controls.Add(clearSelectionButton, 2, 0);
        row.Controls.Add(promoteAllButton, 3, 0);
        row.Controls.Add(holdAllButton, 4, 0);
        row.Controls.Add(promoteSelectedButton, 5, 0);
        row.Controls.Add(clearButton, 6, 0);
        foreach (Control control in row.Controls)
        {
            control.Dock = DockStyle.Fill;
            control.Margin =
                new Padding(4, 6, 4, 6);
        }
        return row;
    }
    private async Task BindCampuses()
    {
        try
        {
            var raw = await Task.Run(() => DatabaseHelper.QueryUser("GetCampuses"));
            if (IsDisposed) return;
            sourceCampus.Bind(raw, "CampusId", "CampusName");
        }
        catch (Exception ex) { Fail(ex); }
    }
    private async Task BindYears()
    {
        try
        {
            var raw = await Task.Run(() => DatabaseHelper.QueryUser("GetAcademicYears"));
            if (IsDisposed) return;
            yearTable = raw;
            yearStarts.Clear();
            foreach (DataRow row in raw.Rows)
            {
                if (row["StartDate"] != DBNull.Value)
                    yearStarts[Convert.ToInt32(row["AcademicYearId"])] = Convert.ToDateTime(row["StartDate"]).Date;
            }
            BindSourceYears();
        }
        catch (Exception ex) { Fail(ex); }
    }
    /// <summary>A source year is a year some later year exists for: the server refuses a destination that does not start after the source (52514).</summary>
    private void BindSourceYears()
    {
        var dt = new DataTable();
        dt.Columns.Add("AcademicYearId", typeof(int));
        dt.Columns.Add("YearName", typeof(string));
        if (yearTable is not null && yearStarts.Count > 0)
        {
            DateTime newest = yearStarts.Values.Max();
            foreach (DataRow row in yearTable.Rows)
            {
                int id = Convert.ToInt32(row["AcademicYearId"]);
                if (yearStarts.TryGetValue(id, out DateTime start) && start < newest)
                    dt.Rows.Add(id, Convert.ToString(row["YearName"]));
            }
        }
        sourceYear.Bind(dt, "AcademicYearId", "YearName");
    }
    private async Task LoadClasses()
    {
        try
        {
            var raw = await Task.Run(() => DatabaseHelper.QueryUser("GetClasses"));
            if (IsDisposed) return;
            classTable = raw;
            sourceClass.Bind(raw, "ClassId", "ClassName");
            /* The destination list depends on the source class, so it stays empty until one is chosen. */
            destinationClass.ClearLookup();
        }
        catch (Exception ex) { Fail(ex); }
    }
    /// <summary>Rebuilds the destination year list from the source year: only years that start AFTER it, earliest first, with the next one preselected (the effective date follows).</summary>
    private void BuildDestinationYears()
    {
        destinationYear.ClearLookup();
        effectiveDate.Value = CurrentYearStart();
        int sourceId = sourceYear.SelectedId;
        if (sourceId <= 0 || yearTable is null || !yearStarts.TryGetValue(sourceId, out DateTime sourceStart))
            return;
        var later = new List<(int Id, string Name, DateTime Start)>();
        foreach (DataRow row in yearTable.Rows)
        {
            int id = Convert.ToInt32(row["AcademicYearId"]);
            if (id != sourceId && yearStarts.TryGetValue(id, out DateTime start) && start > sourceStart)
                later.Add((id, Convert.ToString(row["YearName"]) ?? string.Empty, start));
        }
        later.Sort((a, b) => a.Start.CompareTo(b.Start));
        var dt = new DataTable();
        dt.Columns.Add("AcademicYearId", typeof(int));
        dt.Columns.Add("YearName", typeof(string));
        foreach (var year in later)
            dt.Rows.Add(year.Id, year.Name);
        destinationYear.Bind(dt, "AcademicYearId", "YearName");
        if (later.Count > 0)
        {
            destinationYear.SelectedValue = later[0].Id;
            ApplyDefaultEffectiveDate();
        }
    }
    /// <summary>Rebuilds the destination class list from the source class with the rule the Leave Certificate promotion list already uses (GetLeaveCertificatePromotionClasses).</summary>
    private void BuildDestinationClasses()
    {
        destinationClass.ClearLookup();
        destinationSection.ClearLookup();
        int sourceId = sourceClass.SelectedId;
        if (sourceId <= 0 || classTable is null)
            return;
        DataRow? source = classTable.AsEnumerable().FirstOrDefault(r => Convert.ToInt32(r["ClassId"]) == sourceId);
        if (source is null)
            return;
        bool canOrder = classTable.Columns.Contains("DisplayOrder") && classTable.Columns.Contains("AcademicProgramId");
        static int Order(DataRow r) => r["DisplayOrder"] == DBNull.Value ? 0 : Convert.ToInt32(r["DisplayOrder"]);
        var later = new List<DataRow>();
        foreach (DataRow row in classTable.Rows)
        {
            int id = Convert.ToInt32(row["ClassId"]);
            if (id == sourceId)
                continue;
            if (!canOrder)
            {
                later.Add(row);
                continue;
            }
            bool sameProgram = Convert.ToInt32(row["AcademicProgramId"]) == Convert.ToInt32(source["AcademicProgramId"]);
            bool higher = Order(row) > Order(source) || (Order(row) == Order(source) && id > sourceId);
            if (sameProgram && higher)
                later.Add(row);
        }
        if (canOrder)
        {
            later = later
                .OrderBy(r => Order(r))
                .ThenBy(r => Convert.ToString(r["ClassName"]), StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => Convert.ToInt32(r["ClassId"]))
                .ToList();
        }
        var dt = new DataTable();
        dt.Columns.Add("ClassId", typeof(int));
        dt.Columns.Add("ClassName", typeof(string));
        foreach (DataRow row in later)
            dt.Rows.Add(Convert.ToInt32(row["ClassId"]), Convert.ToString(row["ClassName"]));
        destinationClass.Bind(dt, "ClassId", "ClassName");
        if (later.Count == 0)
        {
            SetStatus($"There is no later class than '{sourceClass.Text}' in the same academic program, so it has nowhere to be promoted into. Only Hold Back applies.", true);
            return;
        }
        destinationClass.SelectedValue = Convert.ToInt32(later[0]["ClassId"]);
    }
    private async Task LoadSourceSections()
    {
        sourceSection.ClearLookup();
        if (sourceCampus.SelectedId <= 0 || sourceClass.SelectedId <= 0) return;
        var campusId = sourceCampus.SelectedId;
        var classId = sourceClass.SelectedId;
        try
        {
            var raw = await Task.Run(() => DatabaseHelper.QueryUser("GetClassSections", DatabaseHelper.Int("@CampusId", campusId), DatabaseHelper.Int("@ClassId", classId), DatabaseHelper.Int("@SectionId", 0)));
            if (IsDisposed || campusId != sourceCampus.SelectedId || classId != sourceClass.SelectedId) return;
            var dt = new DataTable();
            dt.Columns.Add("SectionId", typeof(int)); dt.Columns.Add("Display", typeof(string));
            foreach (DataRow row in raw.Rows) dt.Rows.Add(Convert.ToInt32(row["SectionId"]), Convert.ToString(row["SectionName"]));
            /* The empty choice is a real one - every section of the class - so it says so instead of "-- Select --". */
            sourceSection.Bind(dt, "SectionId", "Display", "All sections");
            if (dt.Rows.Count == 0)
                SetStatus("The chosen source class has no section at this campus, so there is nobody to load. Check the campus and class.", true);
        }
        catch (Exception ex) { Fail(ex); }
    }
    private async Task LoadDestinationSections()
    {
        destinationSection.ClearLookup();
        if (sourceCampus.SelectedId <= 0 || destinationClass.SelectedId <= 0) return;
        var campusId = sourceCampus.SelectedId;
        var classId = destinationClass.SelectedId;
        try
        {
            var raw = await Task.Run(() => DatabaseHelper.QueryUser("GetClassSections", DatabaseHelper.Int("@CampusId", campusId), DatabaseHelper.Int("@ClassId", classId), DatabaseHelper.Int("@SectionId", 0)));
            if (IsDisposed || campusId != sourceCampus.SelectedId || classId != destinationClass.SelectedId) return;
            var dt = new DataTable();
            dt.Columns.Add("ClassSectionId", typeof(int)); dt.Columns.Add("Display", typeof(string));
            foreach (DataRow row in raw.Rows) dt.Rows.Add(Convert.ToInt32(row["ClassSectionId"]), $"{row["ClassName"]} — {row["SectionName"]}");
            destinationSection.Bind(dt, "ClassSectionId", "Display");
            /* Say why the list is empty instead of leaving a blank combo: the class is real but has no section placed at this campus yet. */
            if (dt.Rows.Count == 0)
                Warn($"The chosen destination class has no section at this campus. Add one under {PageTitles.CampusAssignments}, or choose another class.");
            else if (dt.Rows.Count == 1)
                destinationSection.SelectedValue = Convert.ToInt32(dt.Rows[0]["ClassSectionId"]);
        }
        catch (Exception ex) { Fail(ex); }
    }
    /// <summary>The latest 1 April that is not in the future: the opening value of the effective date.</summary>
    private static DateTime CurrentYearStart()
    {
        DateTime today = DateTime.Today;
        var april = new DateTime(today.Year, 4, 1);
        return april > today ? april.AddYears(-1) : april;
    }
    /// <summary>The effective date defaults to the first day of the destination year (for example 01-Apr-2026).</summary>
    private void ApplyDefaultEffectiveDate()
    {
        if (!yearStarts.TryGetValue(destinationYear.SelectedId, out DateTime start))
            return;
        effectiveDate.Value = start;
    }
    /* Cascade handlers: what the operator picks resets everything beneath it */
    private async Task OnSourceCampusChanged()
    {
        InvalidateCandidates();
        /* Sections belong to a campus, so both section lists are stale the moment it changes. */
        sourceSection.ClearLookup();
        destinationSection.ClearLookup();
        ApplyState();
        await Task.WhenAll(
            LoadSourceSections(),
            LoadDestinationSections());
        ApplyStateAndHint();
    }
    private void OnSourceYearChanged()
    {
        InvalidateCandidates();
        BuildDestinationYears();
        ApplyStateAndHint();
    }
    private async Task OnSourceClassChanged()
    {
        InvalidateCandidates();
        sourceSection.ClearLookup();
        BuildDestinationClasses();
        ApplyState();
        await Task.WhenAll(
            LoadSourceSections(),
            LoadDestinationSections());
        ApplyStateAndHint();
    }
    private void OnSourceSectionChanged()
    {
        InvalidateCandidates();
        ApplyState();
    }
    private void OnDestinationYearChanged()
    {
        ApplyDefaultEffectiveDate();
        ApplyStateAndHint();
    }
    private async Task OnDestinationClassChanged()
    {
        destinationSection.ClearLookup();
        ApplyState();
        await LoadDestinationSections();
        ApplyStateAndHint();
    }
    /* Page state: one place decides what is enabled */
    private void BeginBusy()
    {
        busyCount++;
        ApplyState();
    }
    private void EndBusy()
    {
        if (busyCount > 0)
            busyCount--;
        if (!IsDisposed)
            ApplyState();
    }
    /// <summary>The single source of truth for what the operator can touch, worked out from the page as it is now and called after every change.</summary>
    private void ApplyState()
    {
        if (IsDisposed)
            return;
        bool idle = busyCount == 0;
        bool campus = sourceCampus.SelectedId > 0;
        bool sourceYearChosen = sourceYear.SelectedId > 0;
        bool sourceClassChosen = sourceClass.SelectedId > 0;
        bool destinationYearChosen = destinationYear.SelectedId > 0;
        bool destinationClassChosen = destinationClass.SelectedId > 0;
        bool hasRows = candidateData is not null && candidateData.Rows.Count > 0;
        sourceCampusField.Enabled = idle;
        sourceYearField.Enabled = idle;
        sourceClassField.Enabled = idle && campus;
        sourceSectionField.Enabled = idle && campus && sourceClassChosen;
        destinationYearField.Enabled = idle && sourceYearChosen;
        destinationClassField.Enabled = idle && sourceClassChosen;
        destinationSectionField.Enabled = idle && campus && destinationClassChosen;
        effectiveDateField.Enabled = idle && destinationYearChosen;
        remarksField.Enabled = idle;
        loadButton.Enabled = idle && campus && sourceYearChosen && sourceClassChosen;
        selectAllButton.Enabled = idle && hasRows;
        clearSelectionButton.Enabled = idle && hasRows;
        promoteAllButton.Enabled = idle && hasRows;
        holdAllButton.Enabled = idle && hasRows;
        promoteSelectedButton.Enabled = idle && PromoteBlockReason() is null;
        clearButton.Enabled = idle;
    }
    /// <summary>ApplyState, and while candidates are loaded, a live "N ticked ... " line saying what is still needed.</summary>
    private void ApplyStateAndHint()
    {
        ApplyState();
        if (IsDisposed || candidateData is null || busyCount > 0)
            return;
        var (ticked, promote, hold) = CountTicked();
        string? reason = PromoteBlockReason();
        SetStatus(
            $"{ticked:N0} of {candidateData.Rows.Count:N0} ticked ({promote:N0} to promote, {hold:N0} to hold back). " +
            (reason ?? "Ready to promote."));
    }
    private (int Ticked, int Promote, int Hold) CountTicked()
    {
        int ticked = 0, promote = 0;
        if (candidateData is not null)
        {
            foreach (DataRow row in candidateData.Rows)
            {
                if (row["Selected"] is bool on && on)
                {
                    ticked++;
                    if (string.Equals(Convert.ToString(row["Decision"]), "PROMOTE", StringComparison.OrdinalIgnoreCase))
                        promote++;
                }
            }
        }
        return (ticked, promote, ticked - promote);
    }
    /// <summary>What still stops "Promote Selected" from running, or null when it can run.</summary>
    private string? PromoteBlockReason()
    {
        if (candidateData is null || candidateData.Rows.Count == 0)
            return "Load the candidates first.";
        var (ticked, promote, _) = CountTicked();
        if (ticked == 0)
            return "Tick at least one student to process.";
        if (destinationYear.SelectedId <= 0)
            return "Choose the destination academic year.";
        if (promote > 0 && destinationClass.SelectedId <= 0)
            return "Choose the destination class for the students being promoted.";
        if (promote > 0 && destinationSection.SelectedId <= 0)
            return "Choose the destination section for the students being promoted.";
        return null;
    }
    /// <summary>The candidate set is defined by the source context, so a change to any part of it must drop that set rather than leave rows the operator can still tick and process.</summary>
    private void InvalidateCandidates()
    {
        if (candidateData is null &&
            candidatesGrid.DataSource is null)
        {
            return;
        }
        candidateData = null;
        candidatesGrid.DataSource = null;
        nextSteps.Dismiss();
        SetStatus("Source context changed. Load the candidates again before promoting.", true);
    }
    private async Task LoadCandidates()
    {
        if (!ValidateRequiredInputs(sourceTable))
            return;
        /* Held for the whole load: the setup card and every button are locked, so the context cannot change under the query (the guards below stay as a backstop). */
        BeginBusy();
        try
        {
            if (sourceCampus.SelectedId <= 0 || sourceYear.SelectedId <= 0 || sourceClass.SelectedId <= 0)
            { Warn("Select source campus, source academic year and source class."); return; }
            var campusId = sourceCampus.SelectedId; var yearId = sourceYear.SelectedId; var classId = sourceClass.SelectedId; var sectionId = sourceSection.SelectedId;
            /* Every control value the background work needs is read HERE, on the UI thread. Nothing inside a Task.Run lambda in this page may read a control. */
            var destClassId = destinationClass.SelectedId;
            SetStatus("Loading promotion candidates...");
            var data = await Task.Run(() => DatabaseHelper.QueryUser("GetBulkPromotionCandidates", DatabaseHelper.Int("@SourceAcademicYearId", yearId), DatabaseHelper.Int("@SourceCampusId", campusId), DatabaseHelper.Int("@SourceClassId", classId), DatabaseHelper.Int("@SourceSectionId", sectionId)));
            if (IsDisposed || campusId != sourceCampus.SelectedId || yearId != sourceYear.SelectedId || classId != sourceClass.SelectedId || sectionId != sourceSection.SelectedId) return;
            candidateData = data;
            if (!candidateData.Columns.Contains("Selected")) candidateData.Columns.Add("Selected", typeof(bool));
            if (!candidateData.Columns.Contains("Decision")) candidateData.Columns.Add("Decision", typeof(string));
            foreach (DataRow row in candidateData.Rows)
            {
                // Every active enrollment defaults to PROMOTE; staff can flip individual students to HOLD as needed.
                row["Selected"] = true; row["Decision"] = "PROMOTE";
            }
            candidatesGrid.DataSource = candidateData;
            var baseStatus = $"{candidateData.Rows.Count:N0} candidate(s) loaded. Select the whole class or process a subset. Unselected students remain available for another batch.";
            if (destClassId > 0)
            {
                try
                {
                    var gaps = await Task.Run(() => QueryFeeGaps(campusId, destClassId));
                    if (IsDisposed || campusId != sourceCampus.SelectedId || yearId != sourceYear.SelectedId || classId != sourceClass.SelectedId || sectionId != sourceSection.SelectedId) return;
                    if (gaps.Rows.Count > 0)
                    {
                        var missing = string.Join(" and ", gaps.AsEnumerable().Select(r => Convert.ToString(r["FeeTypeName"])));
                        SetStatus($"{baseStatus} ⚠ Destination class '{destinationClass.Text}' has no {missing} fee configured for this campus — promoted students won't be billed for it until it's set up.", true);
                        return;
                    }
                }
                catch (Exception nonFatalException72)
                {
                    FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException72, "BulkPromotionPage: LoadCandidates fallback");
                    // Non-critical early warning; the server still hard-blocks promotion if fees are genuinely missing, so a failed check here isn't fatal.
                }
            }
            SetStatus(baseStatus);
        }
        catch (Exception ex) { Fail(ex); }
        finally { EndBusy(); }
    }
    private static DataTable QueryFeeGaps(int campusId, int classId) =>
        DatabaseHelper.QueryUser(
            "CheckClassFeeConfiguration",
            DatabaseHelper.Int("@CampusId", campusId),
            DatabaseHelper.Int("@ClassId", classId));
    private void SelectAll(bool selected)
    {
        if (busyCount > 0) return;
        candidatesGrid.EndEdit();
        if (candidateData is null)
        {
            Warn("Load promotion candidates first.");
            return;
        }
        foreach (DataRow row in candidateData.Rows)
            row["Selected"] = selected;
        candidatesGrid.Refresh();
        /* Reports "N of M ticked ..." and re-checks Promote Selected. */
        ApplyStateAndHint();
    }
    private void SetAllDecision(string decision)
    {
        if (busyCount > 0) return;
        candidatesGrid.EndEdit();
        if (candidateData is null)
        {
            Warn("Load promotion candidates first.");
            return;
        }
        foreach (DataRow row in candidateData.Rows)
            if (row["Selected"] is true) row["Decision"] = decision;
        candidatesGrid.Refresh();
        /* Whether destination class and section are needed depends on the decisions. */
        ApplyStateAndHint();
    }
    private async Task ProcessSelected()
    {
        try
        {
            if (candidateData is null ||
                candidateData.Rows.Count == 0)
            {
                Warn("Load promotion candidates first.");
                return;
            }
            candidatesGrid.EndEdit();
            var selected =
                candidateData.AsEnumerable()
                    .Where(r =>
                        r.Field<bool>("Selected"))
                    .ToList();
            if (selected.Count == 0)
            {
                Warn("Select at least one student to process.");
                return;
            }
            if (destinationYear.SelectedId <= 0)
            {
                Warn("Select the destination academic year.");
                return;
            }
            int promoteCount =
                selected.Count(r =>
                    string.Equals(
                        Convert.ToString(r["Decision"]), "PROMOTE", StringComparison.OrdinalIgnoreCase));
            int holdCount =
                selected.Count -
                promoteCount;
            Control requiredDestination =
                promoteCount > 0
                    ? destinationTable
                    : destinationYearField;
            if (!ValidateRequiredInputs(requiredDestination))
                return;
            /* The effective date is checked here so the refusal is plain. */
            DateTime effective = effectiveDate.Value.Date;
            if (effective > DateTime.Today)
            {
                Warn("The effective date cannot be in the future.");
                return;
            }
            if (yearStarts.TryGetValue(sourceYear.SelectedId, out DateTime sourceStart) &&
                effective < sourceStart)
            {
                Warn($"The effective date cannot be before the source academic year starts ({sourceStart:dd-MMM-yyyy}).");
                return;
            }
            DataRow? destinationDates = yearTable?.AsEnumerable().FirstOrDefault(row => Convert.ToInt32(row["AcademicYearId"]) == destinationYear.SelectedId);
            if (destinationDates is not null &&
                (effective < Convert.ToDateTime(destinationDates["StartDate"]).Date ||
                 (destinationDates["EndDate"] != DBNull.Value && effective > Convert.ToDateTime(destinationDates["EndDate"]).Date)))
            {
                Warn("The effective date must fall within the destination academic year.");
                return;
            }
            string feeWarning = string.Empty;
            if (promoteCount > 0 &&
                destinationClass.SelectedId > 0)
            {
                try
                {
                    /* Read on the UI thread, used on the pool thread - never a control in the lambda. */
                    int feeCampusId = sourceCampus.SelectedId;
                    int feeClassId = destinationClass.SelectedId;
                    var gaps = await Task.Run(() => QueryFeeGaps(feeCampusId, feeClassId));
                    if (gaps.Rows.Count > 0)
                    {
                        var missing = string.Join(" and ", gaps.AsEnumerable().Select(r => Convert.ToString(r["FeeTypeName"])));
                        feeWarning =
                            $"⚠ WARNING: Destination class '{destinationClass.Text}' has no {missing} fee configured for this campus.\n" +
                            "Promoted students will not be billed for it until fee setup is completed.\n\n";
                    }
                }
                catch (Exception nonFatalException73)
                {
                    FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException73, "BulkPromotionPage: ProcessSelected fallback");
                    // Non-critical; the server still hard-blocks promotion if fees are genuinely missing, so a failed check here should not stop the flow.
                }
            }
            /* Say where the students are going (audit D5): counts alone let a wrong destination class or year through unnoticed. */
            string from =
                $"From: {sourceCampus.Text} · {sourceClass.Text} · " +
                (sourceSection.SelectedId > 0 ? sourceSection.Text : "all sections") +
                $" · {sourceYear.Text}\n";
            string to = promoteCount > 0
                ? $"To: {destinationSection.Text} · {destinationYear.Text}\n"
                : $"To: {destinationYear.Text} (held-back students stay in their current class)\n";
            var confirm =
                ModernMessageDialog.Show(
                    feeWarning +
                    $"Process {selected.Count:N0} student(s)?\n\n" +
                    from + to +
                    $"Effective date: {effective:dd-MMM-yyyy}\n\n" +
                    $"Promote: {promoteCount:N0}\n" +
                    $"Hold Back: {holdCount:N0}\n\n" +
                    "A new enrollment will be created in the destination academic year for each processed student.",
                    "Confirm Bulk Promotion",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
            if (confirm !=
                DialogResult.Yes)
            {
                return;
            }
            var decisions =
                new DataTable();
            decisions.Columns.Add("AdmissionId", typeof(int));
            decisions.Columns.Add("Decision", typeof(string));
            foreach (DataRow row in selected)
            {
                decisions.Rows.Add(
                    Convert.ToInt32(
                        row["AdmissionId"]),
                    Convert.ToString(row["Decision"]));
            }
            var parameter =
                new SqlParameter(
                    "@Decisions",
                    SqlDbType.Structured) {
                    TypeName =
                        "dbo.BulkPromotionDecisionType",
                    Value = decisions
                };
            /* dbo.BulkPromoteStudents returns its own single-row result (PromotionBatchId, ProcessedCount, PromotedCount, HeldBackCount). Nothing about which rows are processed changes: the same procedure is called with the same parameters. */
            DataTable result = await DatabaseHelper.QueryUserAsync(
                "BulkPromoteStudents",
                DatabaseHelper.Int(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@SourceAcademicYearId",
                    sourceYear.SelectedId),
                DatabaseHelper.Int(
                    "@DestinationAcademicYearId",
                    destinationYear.SelectedId),
                DatabaseHelper.Int(
                    "@SourceCampusId",
                    sourceCampus.SelectedId),
                DatabaseHelper.Int(
                    "@SourceClassId",
                    sourceClass.SelectedId),
                DatabaseHelper.Int(
                    "@SourceSectionId",
                    sourceSection.SelectedId),
                DatabaseHelper.Int(
                    "@DestinationClassSectionId",
                    destinationSection.SelectedId),
                DatabaseHelper.Date(
                    "@EffectiveDate",
                    effectiveDate.Value),
                parameter,
                DatabaseHelper.P("@Remarks", remarks.Text.Trim()));
            int attempted = selected.Count;
            bool serverCountsRead =
                result.Rows.Count > 0 &&
                result.Columns.Contains("ProcessedCount") &&
                result.Columns.Contains("PromotedCount") &&
                result.Columns.Contains("HeldBackCount");
            if (!serverCountsRead)
            {
                /* The procedure ran, but this build could not read its result row. Say exactly that: the outcome is unknown, so nothing here may be worded as a completed promotion. */
                Warn(
                    $"The bulk promotion request was sent for {attempted:N0} student(s), but the " +
                    "server's own result could not be read, so this screen cannot confirm how many " +
                    "students were actually processed.\n\n" +
                    $"Ticked as Promote: {promoteCount:N0}\n" +
                    $"Ticked as Hold Back: {holdCount:N0}\n\n" +
                    "Reload the candidate list and check the students' enrollments before assuming " +
                    "the promotion was applied - do not simply retry.");
                await LoadCandidates();
                return;
            }
            DataRow serverResult = result.Rows[0];
            int processed = serverResult["ProcessedCount"] == DBNull.Value
                ? 0
                : Convert.ToInt32(serverResult["ProcessedCount"]);
            int promoted = serverResult["PromotedCount"] == DBNull.Value
                ? 0
                : Convert.ToInt32(serverResult["PromotedCount"]);
            int heldBack = serverResult["HeldBackCount"] == DBNull.Value
                ? 0
                : Convert.ToInt32(serverResult["HeldBackCount"]);
            string outcome =
                $"Bulk promotion result from the server:\n\n" +
                $"Selected: {attempted:N0} student(s)\n" +
                $"Processed: {processed:N0}\n" +
                $"Promoted: {promoted:N0}\n" +
                $"Held Back: {heldBack:N0}";
            Action reportOutcome;
            if (processed < attempted)
            {
                /* Partial outcome - never present this as success. */
                outcome +=
                    $"\n\nNOT processed: {attempted - processed:N0} of the {attempted:N0} selected " +
                    "student(s). Reload the list and check those students before retrying.";
                string partial = outcome;
                reportOutcome = () => Warn(partial);
            }
            else if (processed > attempted || promoted + heldBack != processed)
            {
                /* The server's figures do not line up with what was ticked, so report them without claiming anything about the selection. */
                outcome +=
                    $"\n\nThe server's figures do not match the ticked selection exactly. " +
                    "Check the students' enrollments before relying on this result.";
                string mismatch = outcome;
                reportOutcome = () => Warn(mismatch);
            }
            else
            {
                string complete =
                    outcome +
                    $"\n\nAll {processed:N0} selected student(s) were processed.";
                reportOutcome = () => Info(complete);
            }
            reportOutcome();
            /* Read now: the reload below replaces the candidate table. A single promoted student is carried to the next screen; a batch opens it on the pending list. */
            int promotedAdmissionId = promoted == 1
                ? selected
                    .Where(r => string.Equals(Convert.ToString(r["Decision"]), "PROMOTE", StringComparison.OrdinalIgnoreCase))
                    .Select(r => Convert.ToInt32(r["AdmissionId"]))
                    .FirstOrDefault()
                : 0;
            string destinationName = destinationClass.Text;
            await LoadCandidates();
            /* The outcome is stated again so it is what the operator is left looking at. */
            if (!IsDisposed)
                reportOutcome();
            if (!IsDisposed && promoted > 0)
            {
                nextSteps.Offer(
                    promoted == 1
                        ? $"1 student promoted to {destinationName}. Their new-year fees start only after their return is confirmed."
                        : $"{promoted:N0} students promoted to {destinationName}. Their new-year fees start only after each return is confirmed.",
                    promotedAdmissionId,
                    StudentFlow.ConfirmReturn(primary: true));
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    /// <summary>Puts the whole page back to its opening state.</summary>
    private void ClearForm()
    {
        ResetToBlank(sourceCampus);
        ResetToBlank(sourceYear);
        ResetToBlank(sourceClass);
        sourceSection.ClearLookup();
        destinationYear.ClearLookup();
        destinationClass.ClearLookup();
        destinationSection.ClearLookup();
        effectiveDate.Value = CurrentYearStart();
        remarks.Clear();
        candidateData = null;
        candidatesGrid.DataSource = null;
        nextSteps.Dismiss();
        ApplyState();
        SetStatus(
            sourceYear.Items.Count <= 1
                ? "No academic year has a later year to promote into. Add the next academic year first."
                : "Bulk promotion workflow ready.",
            sourceYear.Items.Count <= 1);
    }
    /// <summary>The list's leading "-- Select --" row, or nothing when the list is empty.</summary>
    private static void ResetToBlank(LookupBox box)
    {
        box.SelectedIndex = box.Items.Count > 0 ? 0 : -1;
    }
}
