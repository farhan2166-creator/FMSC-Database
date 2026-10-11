using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Reports.Finance;
/// <summary>Read-only: dbo.GetBillingCycleStatus reports, per academic year and campus, how many active enrollments already have a charge for the month and how many do not.</summary>
public sealed class BillingStatusReportPage : V2Page
{
    private readonly LookupBox year = new();
    private readonly LookupBox campus = new();
    private readonly ModernDatePicker asOfDate = HumanUi.Date();
    private (int Year, int Campus, DateTime Month)? requestedScope;
    public BillingStatusReportPage(int academicYearId, int campusId, DateTime billingMonth) : this()
    {
        requestedScope = (academicYearId, campusId, billingMonth.Date);
        SetPageHeaderText(string.Empty,
            "Checks the chosen campus and year. Class, section and fee type are not filtered. One posted fee does not mean all fees are posted.");
    }
    public BillingStatusReportPage()
        : base(
            "Billing Status",
            "See how many active students have at least one monthly fee posted.")
    {
        asOfDate.Value = DateTime.Today;
        var viewStatus = AddBusyAction("View Status", ViewStatusAsync, "Primary");
        var actionButtons = new List<Control> { viewStatus };
        RelocateFromActions(actionButtons.ToArray());
        Defer(BindLookupsAsync);
        var filters = ReportUi.Bar(
            ("Academic Year", year),
            ("Campus", campus),
            ("As Of Date", asOfDate));
        UiHelper.SetGridPlaceholderMessage(Grid, UiText.EmptyGrid.RunReport);
        SetContent(ReportUi.Layout(filters, Grid, "Billing Status", actionButtons.ToArray()));
        /* The rows on screen must be attributable to the filters that produced them. */
        year.SelectedIndexChanged += (_, _) => OnFiltersChanged();
        campus.SelectedIndexChanged += (_, _) => OnFiltersChanged();
        asOfDate.ValueChanged += (_, _) => OnFiltersChanged();
    }
    private string? _appliedFilterSignature;
    private string FilterSignature() =>
        $"{year.SelectedId}|{campus.SelectedId}|{asOfDate.Value.Date:yyyy-MM-dd}";
    private void OnFiltersChanged()
    {
        if (string.IsNullOrEmpty(_appliedFilterSignature) ||
            string.Equals(_appliedFilterSignature, FilterSignature(), StringComparison.Ordinal))
        {
            return;
        }
        _appliedFilterSignature = null;
        /* drop the cached rows as well as the grid: the report shell's pager and Export can both reach that cache. */
        InvalidateReport();
        SetStatus("Filters changed since this report was run. Click View Status again.", true);
    }
    private async Task BindLookupsAsync()
    {
        try
        {
            Task<DataTable> yearsTask = DatabaseHelper.QueryUserAsync("GetAcademicYears");
            Task<DataTable> campusesTask = DatabaseHelper.QueryUserAsync("GetCampuses");
            await Task.WhenAll(yearsTask, campusesTask);
            if (IsDisposed) return;
            year.Bind(await yearsTask, "AcademicYearId", "YearName");
            campus.Bind(await campusesTask, "CampusId", "CampusName");
            if (requestedScope is { } scope)
            {
                year.SelectedValue = scope.Year;
                campus.SelectedValue = scope.Campus;
                asOfDate.Value = scope.Month;
                if (year.SelectedIndex < 0 || campus.SelectedIndex < 0)
                {
                    if (year.SelectedIndex < 0) year.SelectedIndex = 0;
                    if (campus.SelectedIndex < 0) campus.SelectedIndex = 0;
                    Warn("A selected campus or year is unavailable. Review the filters, then click View Status.");
                    return;
                }
                await ViewStatusAsync();
            }
        }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
    }
    private async Task ViewStatusAsync()
    {
        if (!CurrentUser.HasPermission("Fees.Reports.View")) return;
        /* The filters this run is for, re-checked once the query returns. */
        string requestedFilters = FilterSignature();
        try
        {
            /* Cross-thread fix: capture the filter control values here on the UI thread. BindReportAsync runs the delegate on a thread-pool thread, and reading a control from there throws. */
            int academicYearId = year.SelectedId;
            int campusId = campus.SelectedId;
            DateTime asOf = asOfDate.Value;
            await BindReportAsync(() => DatabaseHelper.QueryUser(
                "GetBillingCycleStatus",
                DatabaseHelper.Int("@AcademicYearId", academicYearId),
                DatabaseHelper.Int("@CampusId", campusId),
                DatabaseHelper.Date("@AsOfDate", asOf)));
            if (IsDisposed) return;
            /* BindReportAsync has already bound whatever came back, so a run whose filters moved while it was loading would otherwise leave one campus/year's rows on screen under a different selection. */
            if (!string.Equals(requestedFilters, FilterSignature(), StringComparison.Ordinal))
            {
                /* see OnFiltersChanged: the cache goes with the grid, so the rows just superseded cannot be paged or exported. */
                InvalidateReport();
                SetStatus("Filters changed while the report was loading. Click View Status again.", true);
                return;
            }
            _appliedFilterSignature = requestedFilters;
            ConfigureStatusGrid();
        }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
    }
    private void ConfigureStatusGrid()
    {
        if (Grid.Columns.Count == 0)
            return;
        HideColumn("AcademicYearId");
        HideColumn("CampusId");
        SetHeader("BillingMonth", "Billing Month", 140);
        SetHeader("TotalActiveEnrollments", "Total Active Enrolments", 190);
        SetHeader("BilledThisMonth", "Students With Fees Posted", 200);
        SetHeader("RemainingToBill", "Students Without Fees Posted", 220);
        Grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
    }
    private void HideColumn(string columnName)
    {
        if (Grid.Columns.Contains(columnName))
            Grid.Columns[columnName].Visible = false;
    }
    private void SetHeader(string columnName, string header, int width)
    {
        if (!Grid.Columns.Contains(columnName))
            return;
        Grid.Columns[columnName].HeaderText = header;
        Grid.Columns[columnName].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        Grid.Columns[columnName].Width = width;
    }
}
