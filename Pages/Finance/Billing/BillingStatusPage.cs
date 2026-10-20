using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;
using FMSC_SMS.Pages.Students.Admissions;
using FMSC_SMS.Pages.Reports;

namespace FMSC_SMS.Pages.Finance.Billing;

public sealed class BillingStatusPage : V2Page, IBusyPage
{
    private readonly Label summary = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
    private readonly ModernComboBox view = HumanUi.Combo();
    private readonly ModernButton openStudent, collect, run, refresh;
    private readonly Action<Func<Control>, string, string>? navigate;
    private readonly Func<Task<DataSet>> read;
    private DataSet? review;
    private bool busy;
    private int generation;
    public bool IsBusy => busy;
    public string BusyMessage => "Wait for billing review or posting to finish.";

    public BillingStatusPage(Action<Func<Control>, string, string>? navigate = null)
        : this(navigate, AutomaticMonthlyBilling.ReviewAsync, true) { }

    internal BillingStatusPage(Action<Func<Control>, string, string>? navigate, Func<Task<DataSet>> read, bool initialize)
        : base("Billing Status", "Review pending billing and students needing attention.")
    {
        this.navigate = navigate;
        this.read = read;
        view.Items.AddRange(new object[] { "Months awaiting billing", "Students needing attention", "Legacy references (excluded)" });
        view.SelectedIndex = 0;
        view.SelectedIndexChanged += (_, _) => Display();
        refresh = AddAction("Refresh", async (_, _) => await RefreshAsync(), "Secondary");
        run = AddAction("Run Billing / Review Missed Months", async (_, _) => {
            if (busy) return;
            SetBusy(true);
            try { await AutomaticMonthlyBilling.CheckAsync(FindForm()!, force: true); }
            finally { if (!IsDisposed) SetBusy(false); }
            if (!IsDisposed) await RefreshAsync();
        }, "Primary");
        openStudent = AddAction("Open Student Setup", (_, _) => OpenStudent(false), "Secondary");
        collect = AddAction("Open Fee Collection", (_, _) => OpenStudent(true), "Secondary");
        FinanceHelp.Attach(this,run,FinanceHelp.Topic.PostFees);
        FinanceHelp.Attach(this,collect,FinanceHelp.Topic.ReceiveMoney);
        Grid.SelectionChanged += (_, _) => UpdateActions();
        Grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenStudent(false); };
        RelocateFromActions(refresh, run, openStudent, collect);
        var filter = ReportUi.Bar(("Show", view));
        var scope = new Panel { Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8) };
        scope.Controls.Add(filter); scope.Controls.Add(summary);
        // Docked AutoSize labels include padding in their laid-out height.
        // PreferredHeight omits that space and clips the following filter.
        void FitScope() => scope.Height = summary.Height + filter.Height + scope.Padding.Vertical;
        summary.SizeChanged += (_, _) => FitScope();
        filter.SizeChanged += (_, _) => FitScope();
        FitScope();
        SetContent(ReportUi.WorkflowLayout(scope, Grid, "Billing Review",
            "Choose a view and refresh. Run billing to review pending or missed months.",
            "Billing Results", "Select a student to open setup or fee collection.", refresh, run, openStudent, collect));
        Disposed += (_, _) => { generation++; review?.Dispose(); };
        UpdateActions();
        if (initialize) Defer(RefreshAsync);
    }

    private int SelectedStudent => Grid.CurrentRow?.DataBoundItem is DataRowView row && row.Row.Table.Columns.Contains("AdmissionId")
        ? Convert.ToInt32(row["AdmissionId"]) : 0;

    private void OpenStudent(bool collection)
    {
        if (busy || navigate is null || SelectedStudent <= 0) return;
        int id = SelectedStudent;
        if (collection && CurrentUser.HasPermission("Fees.Collect"))
            navigate(() => new FeeCollectionPage(id), "Finance", "Fee Collection");
        else if (!collection && CurrentUser.HasPermission("Students.View") && CurrentUser.HasPermission("Students.Administer"))
            navigate(() => new StudentAdmissionPage(navigate, id), "Students", PageTitles.Admissions);
    }

    private void SetBusy(bool value)
    {
        busy = value;
        refresh.Enabled = run.Enabled = !value;
        view.Enabled = Grid.Enabled = !value;
        UpdateActions();
    }

    private void UpdateActions()
    {
        bool selected = !busy && navigate is not null && SelectedStudent > 0;
        openStudent.Enabled = selected && CurrentUser.HasPermission("Students.View") && CurrentUser.HasPermission("Students.Administer");
        collect.Enabled = selected && CurrentUser.HasPermission("Fees.Collect");
    }

    private async Task RefreshAsync()
    {
        if (busy) return;
        int request = ++generation;
        SetBusy(true);
        try
        {
            DataSet current = await read();
            if (current.Tables[2].Rows.Count > 0)
                AddStudentDetails(current.Tables[2], await StudentSearchCache.GetAllAsync());
            if (IsDisposed || request != generation) { current.Dispose(); return; }
            DataSet? previous = review;
            Grid.DataSource = null;
            review = current;
            previous?.Dispose();
            DataRow row = current.Tables[0].Rows[0];
            summary.Text = $"{Convert.ToDateTime(row["BillingMonth"]):MMMM yyyy}: " +
                $"{row["CurrentCharges"]} charges pending · Rs. {Convert.ToDecimal(row["CurrentAmount"]):N0}\n" +
                $"{current.Tables[2].AsEnumerable().Count(item => !Convert.ToBoolean(item["IsLegacyReference"])):N0} students need attention. " +
                "Select a student to open setup or collection. Legacy references are excluded from automatic billing.";
            Display();
        }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
        finally { if (!IsDisposed && request == generation) SetBusy(false); }
    }

    private void Display()
    {
        if (review is null) return;
        if (view.SelectedIndex == 0) Grid.DataSource = review.Tables[1];
        else
        {
            var students = review.Tables[2];
            AdmissionNumbers.AddSortColumns(students);
            Grid.DataSource = new DataView(students) {
                RowFilter = view.SelectedIndex == 2 ? "IsLegacyReference = true" : "IsLegacyReference = false",
                Sort = students.Columns.Contains("AdmissionNumber") ? "__AdmissionNumberBase ASC, __AdmissionNumberSuffix ASC" : "AdmissionId ASC"
            };
            int index = 0;
            foreach (string name in new[] { "AdmissionNumber", "StudentName", "FatherName", "ClassSection", "IsLegacyReference", "Reason" })
                if (Grid.Columns.Contains(name)) Grid.Columns[name].DisplayIndex = index++;
            if (Grid.Columns.Contains("ClassSection")) Grid.Columns["ClassSection"].HeaderText = "Class - Section";
        }
        if (Grid.Columns.Contains("BillingMonth")) Grid.Columns["BillingMonth"].DefaultCellStyle.Format = "MMMM yyyy";
        if (Grid.Columns.Contains("Amount")) Grid.Columns["Amount"].DefaultCellStyle.Format = "N0";
        UpdateActions();
    }

    internal static void AddStudentDetails(DataTable exceptions, DataTable students)
    {
        foreach (string column in new[] { "FatherName", "ClassSection" })
            if (!exceptions.Columns.Contains(column)) exceptions.Columns.Add(column, typeof(string));
        var lookup = students.AsEnumerable().ToLookup(row => Convert.ToInt32(row["AdmissionId"]));
        foreach (DataRow row in exceptions.Rows)
        {
            var student = lookup[Convert.ToInt32(row["AdmissionId"])].FirstOrDefault();
            string Read(string column) => student is not null && students.Columns.Contains(column) && !student.IsNull(column)
                ? Convert.ToString(student[column])?.Trim() ?? "" : "";
            row["FatherName"] = Read("FatherName");
            string className = Read("ClassName"), section = Read("SectionName");
            row["ClassSection"] = className.Length == 0 ? "" : section.Length == 0 ? className : $"{className} - {section}";
        }
    }
}
