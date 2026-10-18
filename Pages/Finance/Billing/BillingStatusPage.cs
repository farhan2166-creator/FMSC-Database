using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;
using FMSC_SMS.Pages.Students.Admissions;

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
        : base("Billing Status", "Resolve setup exceptions, review missed months and open the selected student's account.")
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
        Grid.SelectionChanged += (_, _) => UpdateActions();
        Grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenStudent(false); };
        var root = new Panel { Dock = DockStyle.Fill };
        var filter = HumanUi.Field("Show", view);
        filter.Dock = DockStyle.Top;
        root.Controls.Add(Grid); root.Controls.Add(filter); root.Controls.Add(summary);
        SetContent(root);
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
            if (IsDisposed || request != generation) { current.Dispose(); return; }
            DataSet? previous = review;
            Grid.DataSource = null;
            review = current;
            previous?.Dispose();
            DataRow row = current.Tables[0].Rows[0];
            summary.Text = $"{Convert.ToDateTime(row["BillingMonth"]):MMMM yyyy}: " +
                $"{row["CurrentCharges"]} charges pending · Rs. {Convert.ToDecimal(row["CurrentAmount"]):N0}\n" +
                $"{current.Tables[2].AsEnumerable().Count(item => !Convert.ToBoolean(item["IsLegacyReference"])):N0} students need attention. " +
                "Select a student below to resolve setup or open collection. Legacy records are never activated by this review.";
            Display();
        }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
        finally { if (!IsDisposed && request == generation) SetBusy(false); }
    }

    private void Display()
    {
        if (review is null) return;
        Grid.DataSource = view.SelectedIndex == 0 ? review.Tables[1] :
            new DataView(review.Tables[2]) { RowFilter = view.SelectedIndex == 2 ? "IsLegacyReference = true" : "IsLegacyReference = false" };
        if (Grid.Columns.Contains("BillingMonth")) Grid.Columns["BillingMonth"].DefaultCellStyle.Format = "MMMM yyyy";
        if (Grid.Columns.Contains("Amount")) Grid.Columns["Amount"].DefaultCellStyle.Format = "N0";
        UpdateActions();
    }
}
