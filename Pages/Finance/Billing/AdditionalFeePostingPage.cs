using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.Billing;
public sealed class AdditionalFeePostingPage : V2Page
{
    private readonly LookupBox year = new(), campus = new(), klass = new(), section = new(), fee = new();
    private readonly ModernDatePicker date = HumanUi.Date();
    private readonly ModernNumericTextBox amount = new() { Minimum = 0, Maximum = 100000000, DecimalPlaces = 0, Dock = DockStyle.Fill };
    private readonly ModernTextBox reason = HumanUi.TextBox(false, 500);
    private readonly DataGridView students = HumanUi.Grid(true);
    private readonly HashSet<int> selected = new();
    private DataTable? candidates;
    private Guid requestId = Guid.NewGuid();
    private bool loading;
    public AdditionalFeePostingPage() : base("Post Additional Fee",
        "Select students and add the same fee charge to each.")
    {
        var load = AddBusyAction("Load Students", LoadAsync, "Secondary");
        var selectAll = AddAction(UiText.Actions.SelectAll, (_, _) => SelectAll(), "Secondary");
        var post = AddBusyAction("Post Selected", PostAsync, "Success");
        RelocateFromActions(load, selectAll, post);
        students.ReadOnly = false;
        students.AutoGenerateColumns = false;
        students.AllowUserToAddRows = false;
        students.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "Post", Width = AppScale.Scale(65) });
        /* Admission Number first - it is what identifies a student; the registration number is a reporting label and follows it. */
        foreach (var name in new[] { "AdmissionNumber", "StudentName", "FatherName", "CampusName", "ClassName", "SectionName" })
            students.Columns.Add(new DataGridViewTextBoxColumn {
                Name = name, HeaderText = name switch {
                    "AdmissionNumber" => "Admission Number",
                    "RegistrationNumber" => "Registration",
                    "StudentName" => "Student",
                    "FatherName" => "Father",
                    "CampusName" => "Campus",
                    "ClassName" => "Class",
                    _ => "Section"
                }, DataPropertyName = name, ReadOnly = true
            });
        students.CurrentCellDirtyStateChanged += (_, _) => {
            if (students.IsCurrentCellDirty) students.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        students.CellValueChanged += (_, e) => {
            if (loading || e.RowIndex < 0 || e.ColumnIndex != 0 || candidates is null) return;
            if (students.Rows[e.RowIndex].DataBoundItem is not DataRowView bound) return;
            int id = Convert.ToInt32(bound["EnrollmentId"]);
            if (Convert.ToBoolean(students.Rows[e.RowIndex].Cells[0].Value)) selected.Add(id);
            else selected.Remove(id);
        };
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
        for (int i = 0; i < 4; i++) fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        for (int i = 0; i < 2; i++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        fields.Controls.Add(HumanUi.Field("Academic year", year, true), 0, 0);
        fields.Controls.Add(HumanUi.Field("Campus", campus), 1, 0);
        fields.Controls.Add(HumanUi.Field("Class", klass), 2, 0);
        fields.Controls.Add(HumanUi.Field("Section", section), 3, 0);
        fields.Controls.Add(HumanUi.Field("Charge date", date, true, fillHeight: false), 0, 1);
        fields.Controls.Add(HumanUi.Field("Amount per student", amount, true, fillHeight: false), 1, 1);
        fields.Controls.Add(HumanUi.Field("Fee type", fee, true), 2, 1);
        fields.Controls.Add(HumanUi.Field("Reason", reason, true, fillHeight: false), 3, 1);
        /* The three actions were removed from the action bar above and never put anywhere else, so the page had no buttons at all. */
        var buttonRow = HumanUi.ButtonRow(load, selectAll, post);
        buttonRow.Dock = DockStyle.Fill;
        buttonRow.Margin = new Padding(0);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight * 2));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(fields, 0, 0);
        root.Controls.Add(buttonRow, 0, 1);
        root.Controls.Add(students, 0, 2);
        SetContent(root);
        FormFlow.OrderTableFields(root);
        FormFlow.EnterMovesToNext(this);
        year.SelectedIndexChanged += (_, _) => ClearCandidates();
        campus.SelectedIndexChanged += (_, _) => ClearCandidates();
        klass.SelectedIndexChanged += (_, _) => ClearCandidates();
        section.SelectedIndexChanged += (_, _) => ClearCandidates();
        year.Enabled = false;
        Defer(BindAsync);
    }
    private async Task BindAsync()
    {
        try
        {
            Task<DataTable> yearsTask = DatabaseHelper.QueryUserAsync("GetAcademicYears");
            Task<DataTable> campusesTask = DatabaseHelper.QueryUserAsync("GetCampuses");
            Task<DataTable> classesTask = DatabaseHelper.QueryUserAsync("GetClasses");
            Task<DataTable> sectionsTask = DatabaseHelper.QueryUserAsync("GetSections");
            Task<DataTable> feeTypesTask = DatabaseHelper.QueryUserAsync(
                "GetFeeTypes");
            await Task.WhenAll(yearsTask, campusesTask, classesTask, sectionsTask, feeTypesTask);
            DataTable years = await yearsTask;
            year.Bind(years, "AcademicYearId", "YearName");
            HumanUi.SelectCurrentAcademicYear(year, years);
            campus.Bind(await campusesTask, "CampusId", "CampusName", "All campuses");
            klass.Bind(await classesTask, "ClassId", "ClassName", "All classes");
            section.Bind(await sectionsTask, "SectionId", "SectionName", "All sections");
            fee.Bind(await feeTypesTask, "FeeTypeId", "FeeTypeName");
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void ClearCandidates()
    {
        candidates = null;
        students.DataSource = null;
        selected.Clear();
    }
    private void SelectAll()
    {
        if (candidates is null) return;
        loading = true;
        foreach (DataGridViewRow row in students.Rows)
        {
            if (row.DataBoundItem is not DataRowView bound) continue;
            row.Cells[0].Value = true;
            selected.Add(Convert.ToInt32(bound["EnrollmentId"]));
        }
        loading = false;
        SetStatus($"{selected.Count} students selected. Untick any exceptions before posting.");
    }
    private async Task LoadAsync()
    {
        ClearCandidates();
        if (year.SelectedId <= 0)
        {
            Warn("Set the current academic year in Academic Year setup first.");
            return;
        }
        try
        {
            candidates = await DatabaseHelper.QueryUserAsync("GetAdditionalFeeCandidates",
                DatabaseHelper.Int("@AcademicYearId", year.SelectedId),
                DatabaseHelper.Int("@CampusId", campus.SelectedId),
                DatabaseHelper.Int("@ClassId", klass.SelectedId),
                DatabaseHelper.Int("@SectionId", section.SelectedId));
            loading = true;
            students.DataSource = candidates;
            foreach (DataGridViewRow row in students.Rows) row.Cells[0].Value = false;
            loading = false;
            SetStatus($"{candidates.Rows.Count} admitted students loaded. Tick those to charge.");
        }
        catch (Exception ex) { loading = false; Fail(ex); }
    }
    private async Task PostAsync()
    {
        if (!ValidateRequiredInputs())
            return;
        students.EndEdit();
        if (candidates is null || selected.Count == 0 || fee.SelectedId <= 0 ||
            amount.Value <= 0 || string.IsNullOrWhiteSpace(reason.Text))
        {
            Warn("Select students and a fee, then enter an amount and reason.");
            return;
        }
        int count = selected.Count;
        if (!Confirm($"Post {fee.Text} to {count} students?\nCharge date: {date.Value:dd-MMM-yyyy}\nAmount each: Rs. {amount.Value:N0}\nTotal: Rs. {amount.Value * count:N0}",
            "Post Additional Fee")) return;
        string postedFeeName = fee.Text;
        decimal postedAmount = amount.Value;
        DateTime postedDate = date.Value;
        try
        {
            await DatabaseHelper.QueryUserAsync("PostAdditionalFee",
                DatabaseHelper.P("@RequestId", requestId),
                DatabaseHelper.Int("@FeeTypeId", fee.SelectedId),
                DatabaseHelper.Date("@ChargeDate", date.Value),
                DatabaseHelper.P("@Amount", amount.Value),
                DatabaseHelper.P("@DiscountAmount", 0m),
                DatabaseHelper.P("@Reason", reason.Text.Trim()),
                new SqlParameter("@Selection", SqlDbType.Structured) { TypeName = "dbo.ManualChargeSelectionType", Value = Tvp.ManualChargeSelection(selected) });
            requestId = Guid.NewGuid();
            Info($"{postedFeeName} posted to {count} student(s): Rs. {postedAmount:0.##} each, dated {postedDate:dd MMM yyyy}.");
            ClearCandidates();
        }
        catch (Exception ex) { Fail(ex); }
    }
}
