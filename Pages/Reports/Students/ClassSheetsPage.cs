using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Reports.Students;

public sealed class ClassSheetsPage : V2Page
{
    private readonly LookupBox campus = new(), year = new(), klass = new(), section = new();
    private readonly ModernComboBox sheet = HumanUi.Combo();
    private readonly ModernDatePicker attendanceMonth = new() {
        Format = DateTimePickerFormat.Custom, CustomFormat = "MMMM yyyy", ShowUpDown = true,
        Value = DateTime.Today
    };
    private readonly ModernTextBox exam = HumanUi.TextBox(false, 100), subject = HumanUi.TextBox(false, 100),
        examDate = HumanUi.TextBox(false, 50), teacher = HumanUi.TextBox(false, 100),
        search = HumanUi.TextBox(false, 100);
    private readonly ModernButton previewButton;
    private readonly ModernButton selectAllButton, clearSelectionButton;
    private DataTable? placements, roster;
    private bool rebinding, loading, changingSelection;
    private int rosterRevision;
    private Scope? loadedScope;
    private sealed record Scope(int Campus, int Year, int Class, int Section);
    private Scope CurrentScope() => new(campus.SelectedId, year.SelectedId, klass.SelectedId, section.SelectedId);

    public ClassSheetsPage() : base("Class Marks & Attendance Sheets",
        "Choose students and print marks or attendance sheets.")
    {
        sheet.Items.AddRange(new object[] { "Marks Sheet", "Exam Attendance Sheet", "Monthly Attendance Register" });
        sheet.SelectedIndex = 0;
        var load = AddBusyAction("Load Students", LoadStudentsAsync, "Primary");
        previewButton = AddBusyAction("Print Preview", PreviewAsync, "Secondary");
        selectAllButton = AddAction("Select All", (_, _) => SelectStudents(true), "Secondary");
        clearSelectionButton = AddAction("Clear Selection", (_, _) => SelectStudents(false), "Secondary");
        RelocateFromActions(load, selectAllButton, clearSelectionButton, previewButton);
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, RowCount = 3,
            Height = AppMetrics.FieldRowHeight * 3 };
        for (int i = 0; i < 4; i++) fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        for (int i = 0; i < 3; i++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        (string Caption, Control Input)[] inputs = {
            ("Campus", campus), ("Academic Year", year), ("Class", klass), ("Section", section),
            ("Sheet", sheet), ("Exam (optional)", exam), ("Subject / Paper (optional)", subject),
            ("Date (optional)", examDate), ("Teacher (optional)", teacher), ("Find Student", search)
        };
        var fieldOf = new Dictionary<Control, Control>();
        var monthField = HumanUi.Field("Month", attendanceMonth, true);
        monthField.Visible = false;
        for (int i = 0; i < inputs.Length; i++)
        {
            var field = HumanUi.Field(inputs[i].Caption, inputs[i].Input, i < 2);
            fieldOf[inputs[i].Input] = field;
            Control cell = field;
            if (inputs[i].Input == exam)
            {
                var host = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
                host.Controls.Add(field); host.Controls.Add(monthField);
                cell = host;
            }
            fields.Controls.Add(cell, i % 4, i / 4);
        }
        void UpdateSheetFields()
        {
            bool monthly = sheet.SelectedIndex == 2;
            monthField.Visible = monthly;
            fieldOf[exam].Visible = fieldOf[subject].Visible = fieldOf[examDate].Visible = !monthly;
            if (monthly) monthField.BringToFront();
            SetPageHeaderText(string.Empty, monthly
                ? "Print a monthly class register with blank daily attendance cells on landscape A4."
                : "Print two matching marks or exam attendance sheets on each A4 page.");
        }
        sheet.SelectedIndexChanged += (_, _) => UpdateSheetFields();
        examDate.PlaceholderText = "Leave blank to write by hand";
        search.PlaceholderText = "Name / admission number";
        Grid.AutoGenerateColumns = false; Grid.ReadOnly = false;
        Grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", DataPropertyName = "Include",
            HeaderText = "Print", Width = AppScale.Scale(65) });
        Grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "AdmissionNumber", DataPropertyName = "AdmissionNumber",
            HeaderText = "Admission Number", ReadOnly = true, Width = AppScale.Scale(140) });
        Grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "StudentName", DataPropertyName = "StudentName",
            HeaderText = "Student Name", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        Grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "FatherName", DataPropertyName = "FatherName",
            HeaderText = "Father Name", ReadOnly = true, Width = AppScale.Scale(220) });
        foreach (string name in new[] { "ClassName", "SectionName" })
            Grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, DataPropertyName = name,
                HeaderText = name == "ClassName" ? "Class" : "Section", ReadOnly = true,
                Width = AppScale.Scale(100), Visible = false });
        Grid.CurrentCellDirtyStateChanged += (_, _) => {
            if (Grid.IsCurrentCellDirty) Grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        Grid.CellValueChanged += (_, _) => { if (!changingSelection) UpdateSelection(); };
        // A shared busy action restores its button after completion. Recheck readiness
        // even if its temporary disabled state hid an invalidation during the await.
        previewButton.EnabledChanged += (_, _) => {
            if (previewButton.Enabled && !CanPreviewSelection()) previewButton.Enabled = false;
        };
        search.TextChanged += (_, _) => FilterStudents();
        var details = HumanUi.Card("Class and Sheet Details",
            "Choose a campus and year. Use All classes or All sections to print several together.");
        details.Content.Controls.Add(fields);
        var studentsCard = HumanUi.Card("Students to Print",
            "Uncheck students to exclude. Search narrows the list; checked hidden students remain included.");
        studentsCard.Content.Controls.Add(Grid);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(3)));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(details, 0, 0);
        var buttons = HumanUi.ButtonRow(load, selectAllButton, clearSelectionButton, previewButton);
        buttons.Dock = DockStyle.Top;
        root.Controls.Add(buttons, 0, 1);
        root.Controls.Add(studentsCard, 0, 2);
        SetContent(root, "Load students, choose who to print, then open Print Preview. Attendance is marked by hand on the printed sheet.");
        UpdateSheetFields();
        UiHelper.SetGridPlaceholderMessage(Grid, "Choose a campus and year, then press Load Students.");
        foreach (var filter in new[] { campus, year, klass, section })
            filter.SelectedIndexChanged += (_, _) => InvalidateRoster();
        campus.UserSelectionChanged += (_, _) => BindClasses();
        klass.UserSelectionChanged += (_, _) => BindSections();
        UpdateSelection();
        Defer(LoadLookupsAsync);
    }

    private async Task LoadLookupsAsync()
    {
        try
        {
            var campusTask = DatabaseHelper.QueryUserAsync("GetCampuses");
            var yearTask = DatabaseHelper.QueryUserAsync("GetAcademicYears");
            var placementTask = ReportClassSectionLookup.QueryAsync();
            await Task.WhenAll(campusTask, yearTask, placementTask);
            if (IsDisposed) return;
            placements = await placementTask;
            campus.Bind(await campusTask, "CampusId", "CampusName");
            var years = await yearTask;
            year.Bind(years, "AcademicYearId", "YearName");
            HumanUi.SelectCurrentAcademicYear(year, years);
            BindClasses();
        }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
    }

    private void BindClasses()
    {
        if (rebinding || placements is null) return;
        rebinding = true;
        try
        {
            var selected = placements.Clone();
            foreach (DataRow row in placements.Rows)
                if (campus.SelectedId > 0 && Convert.ToInt32(row["CampusId"]) == campus.SelectedId)
                    selected.ImportRow(row);
            klass.Bind(selected.DefaultView.ToTable(true, "ClassId", "ClassName"), "ClassId", "ClassName", "All classes");
        }
        finally { rebinding = false; }
        BindSections();
    }

    private void BindSections()
    {
        if (rebinding || placements is null) return;
        var selected = placements.Clone();
        foreach (DataRow row in placements.Rows)
            if (campus.SelectedId > 0 &&
                Convert.ToInt32(row["CampusId"]) == campus.SelectedId &&
                (klass.SelectedId == 0 || Convert.ToInt32(row["ClassId"]) == klass.SelectedId))
                selected.ImportRow(row);
        section.Bind(selected.DefaultView.ToTable(true, "SectionId", "SectionName"), "SectionId", "SectionName", "All sections");
    }

    private void InvalidateRoster()
    {
        bool hadRoster = roster is not null;
        rosterRevision++; loadedScope = null; roster = null; Grid.DataSource = null;
        if (hadRoster) Info("Load students again for the selected filters.");
        UpdateSelection();
    }

    private void CommitSelection()
    {
        Grid.EndEdit();
        if (Grid.DataSource is not null && BindingContext is not null)
            (BindingContext[Grid.DataSource] as CurrencyManager)?.EndCurrentEdit();
    }

    private void FilterStudents()
    {
        CommitSelection();
        if (roster is null) return;
        string text = search.Text.Trim().Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]").Replace("*", "[*]");
        roster.DefaultView.RowFilter = text.Length == 0 ? ""
            : $"StudentName LIKE '%{text}%' OR AdmissionNumber LIKE '%{text}%'";
        UpdateSelection();
    }

    private void SelectStudents(bool include)
    {
        CommitSelection();
        if (roster is null || loading) return;
        changingSelection = true;
        try { foreach (DataRow row in roster.Rows) row["Include"] = include; }
        finally { changingSelection = false; }
        UpdateSelection();
    }

    private bool CanPreviewSelection() => !loading && roster is not null && loadedScope == CurrentScope()
        && roster.AsEnumerable().Any(row => row.Field<bool>("Include"));

    private void UpdateSelection(bool showStatus = true)
    {
        int count = roster?.AsEnumerable().Count(row => row.Field<bool>("Include")) ?? 0;
        bool current = roster is not null && loadedScope == CurrentScope();
        previewButton.Enabled = CanPreviewSelection();
        selectAllButton.Enabled = clearSelectionButton.Enabled = current && roster!.Rows.Count > 0 && !loading;
        if (current && showStatus)
            Info($"{count} of {roster!.Rows.Count} students selected. Select All and Clear Selection apply to the whole loaded list.");
    }

    private async Task LoadStudentsAsync()
    {
        if (loading || !CurrentUser.HasPermission("Reports.View")) return;
        Scope scope = CurrentScope();
        if (scope.Campus <= 0 || scope.Year <= 0)
        { Warn("Choose a campus and academic year first."); return; }
        InvalidateRoster(); int revision = rosterRevision;
        loading = true; UpdateSelection();
        try
        {
            var students = await DatabaseHelper.QueryUserAsync("GetStudentRegister",
                DatabaseHelper.Int("@CampusId", scope.Campus), DatabaseHelper.Int("@AcademicYearId", scope.Year),
                DatabaseHelper.Int("@ClassId", scope.Class), DatabaseHelper.Int("@SectionId", scope.Section),
                DatabaseHelper.P("@StudentStatus", "Active"), DatabaseHelper.P("@EnrollmentStatus", "Active"),
                DatabaseHelper.Bit("@IncludeFormerStudents", false), DatabaseHelper.Bit("@IncludeBalance", false));
            if (IsDisposed || revision != rosterRevision || scope != CurrentScope()) return;
            roster = ClassSheetData.CreateRoster(students); loadedScope = scope;
            Grid.Columns["ClassName"].Visible = scope.Class == 0;
            Grid.Columns["SectionName"].Visible = scope.Class == 0 || scope.Section == 0;
            Grid.DataSource = roster.DefaultView; FilterStudents();
            if (roster.Rows.Count == 0) Warn("No active students were found for the selected filters.");
        }
        catch (Exception ex) { if (!IsDisposed && revision == rosterRevision) Fail(ex); }
        finally { loading = false; if (!IsDisposed) UpdateSelection(showStatus: false); }
    }

    private async Task PreviewAsync()
    {
        if (loading || !CurrentUser.HasPermission("Reports.View")) return;
        CommitSelection();
        if (roster is null || loadedScope != CurrentScope())
        { Warn("Load students for the selected filters first."); return; }
        // Snapshot includes every checked student, even when search hides a row.
        int revision = rosterRevision;
        var selected = roster.Copy();
        bool attendance = sheet.SelectedIndex == 1;
        bool monthly = sheet.SelectedIndex == 2;
        DateTime month = attendanceMonth.Value;
        string examText = exam.Text, subjectText = subject.Text, dateText = examDate.Text, teacherText = teacher.Text;
        if (!selected.AsEnumerable().Any(row => row.Field<bool>("Include")))
        { Warn("Select at least one student to print."); return; }
        loading = true; UpdateSelection();
        try
        {
            var school = await InstitutionProfile.GetNameAsync();
            if (IsDisposed || revision != rosterRevision) return;
            if (string.IsNullOrWhiteSpace(school)) { Warn("Check School Profile before printing class sheets."); return; }
            var data = ClassSheetBatchData.Create(selected, group => monthly
                ? MonthlyAttendanceSheetData.Create(group, school, month, teacherText)
                : ClassSheetData.Create(group, school, examText, subjectText, dateText, teacherText,
                    showFatherForMatchingNames: !attendance));
            string title = monthly ? "Monthly Attendance Register" : attendance ? "Exam Attendance Sheet" : "Marks Sheet";
            string template = monthly ? "ClassMonthlyAttendanceSheet.rdlc" : attendance ? "ClassAttendanceSheet.rdlc" : "ClassMarksSheet.rdlc";
            RdlcReportPreview.Show(FindForm(), title,
                Path.Combine(AppContext.BaseDirectory, "Reports", template),
                "ClassSheetData", data);
        }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
        finally { loading = false; if (!IsDisposed) UpdateSelection(showStatus: false); }
    }
}
