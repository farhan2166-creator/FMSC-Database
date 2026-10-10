using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Reports.Students;

public sealed class ClassSheetsPage : V2Page
{
    private readonly LookupBox campus = new(), year = new(), klass = new(), section = new();
    private readonly ModernComboBox sheet = HumanUi.Combo();
    private readonly ModernTextBox exam = HumanUi.TextBox(false, 100), subject = HumanUi.TextBox(false, 100),
        examDate = HumanUi.TextBox(false, 50), teacher = HumanUi.TextBox(false, 100);
    private DataTable? placements;
    private bool rebinding;

    public ClassSheetsPage() : base("Class Marks & Attendance Sheets",
        "Print class lists for teachers to fill in by hand. Two matching copies on each A4 page.")
    {
        sheet.Items.AddRange(new object[] { "Marks Sheet", "Exam Attendance Sheet" });
        sheet.SelectedIndex = 0;
        var preview = AddBusyAction("Print Preview", PreviewAsync, "Primary");
        RelocateFromActions(preview);
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, RowCount = 3,
            Height = AppMetrics.FieldRowHeight * 3 };
        for (int i = 0; i < 4; i++) fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        for (int i = 0; i < 3; i++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        (string Caption, Control Input)[] inputs = {
            ("Campus", campus), ("Academic Year", year), ("Class", klass), ("Section", section),
            ("Sheet", sheet), ("Exam (optional)", exam), ("Subject / Paper (optional)", subject),
            ("Date (optional)", examDate), ("Teacher (optional)", teacher)
        };
        for (int i = 0; i < inputs.Length; i++)
            fields.Controls.Add(HumanUi.Field(inputs[i].Caption, inputs[i].Input, i < 4), i % 4, i / 4);
        examDate.PlaceholderText = "Leave blank to write by hand";
        SetContent(ReportUi.Layout(fields, Grid, "Class List", preview),
            "Choose a campus, year, class and section. Only active students are listed. Exam details and teacher name may be left blank.");
        UiHelper.SetGridPlaceholderMessage(Grid, "Choose a class and section, then press Print Preview.");
        campus.UserSelectionChanged += (_, _) => BindClasses();
        klass.UserSelectionChanged += (_, _) => BindSections();
        Defer(LoadAsync);
    }

    private async Task LoadAsync()
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
            klass.Bind(selected.DefaultView.ToTable(true, "ClassId", "ClassName"), "ClassId", "ClassName");
        }
        finally { rebinding = false; }
        BindSections();
    }

    private void BindSections()
    {
        if (rebinding || placements is null) return;
        var selected = placements.Clone();
        foreach (DataRow row in placements.Rows)
            if (campus.SelectedId > 0 && klass.SelectedId > 0 &&
                Convert.ToInt32(row["CampusId"]) == campus.SelectedId &&
                Convert.ToInt32(row["ClassId"]) == klass.SelectedId)
                selected.ImportRow(row);
        section.Bind(selected.DefaultView.ToTable(true, "SectionId", "SectionName"), "SectionId", "SectionName");
    }

    private async Task PreviewAsync()
    {
        if (!CurrentUser.HasPermission("Reports.View")) return;
        if (campus.SelectedId <= 0 || year.SelectedId <= 0 || klass.SelectedId <= 0 || section.SelectedId <= 0)
        { Warn("Choose a campus, academic year, class and section first."); return; }
        int campusId = campus.SelectedId, yearId = year.SelectedId, classId = klass.SelectedId, sectionId = section.SelectedId;
        bool attendance = sheet.SelectedIndex == 1;
        string examText = exam.Text, subjectText = subject.Text, dateText = examDate.Text, teacherText = teacher.Text;
        try
        {
            var students = await DatabaseHelper.QueryUserAsync("GetStudentRegister",
                DatabaseHelper.Int("@CampusId", campusId), DatabaseHelper.Int("@AcademicYearId", yearId),
                DatabaseHelper.Int("@ClassId", classId), DatabaseHelper.Int("@SectionId", sectionId),
                DatabaseHelper.P("@StudentStatus", "Active"), DatabaseHelper.P("@EnrollmentStatus", "Active"),
                DatabaseHelper.Bit("@IncludeFormerStudents", false), DatabaseHelper.Bit("@IncludeBalance", false));
            var school = await InstitutionProfile.GetNameAsync();
            if (IsDisposed) return;
            if (string.IsNullOrWhiteSpace(school)) { Warn("Check School Profile before printing class sheets."); return; }
            var data = ClassSheetData.Create(students, school, examText, subjectText, dateText, teacherText);
            if (data.Rows.Count == 0) { Grid.DataSource = null; Warn("No active students were found in this class and section for the selected year."); return; }
            var list = new DataTable(); list.Columns.Add("Sr. No.", typeof(int)); list.Columns.Add("Student Name");
            foreach (DataRow page in data.Rows)
                for (int i = 1; i <= ClassSheetData.StudentsPerPage; i++)
                    if (Convert.ToString(page[$"Number{i:00}"]) is string number && number.Length > 0)
                        list.Rows.Add(int.Parse(number), page[$"Name{i:00}"]);
            Grid.DataSource = list;
            Info($"{list.Rows.Count} students. {data.Rows.Count} A4 page(s), with two copies on each page.");
            RdlcReportPreview.Show(FindForm(), attendance ? "Exam Attendance Sheet" : "Marks Sheet",
                Path.Combine(AppContext.BaseDirectory, "Reports", attendance ? "ClassAttendanceSheet.rdlc" : "ClassMarksSheet.rdlc"),
                "ClassSheetData", data);
        }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
    }
}
