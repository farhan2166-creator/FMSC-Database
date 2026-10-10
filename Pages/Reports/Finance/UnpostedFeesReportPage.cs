using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Reports.Finance;
/// <summary>Read-only reconciliation (dbo.GetMissingChargeCandidates) - which (student, fee, month) combinations the projection expects by now that nevertheless have no charge.</summary>
public sealed class UnpostedFeesReportPage : V2Page
{
    private readonly LookupBox year = new();
    private readonly LookupBox campus = new();
    private readonly ModernDatePicker asOfDate = HumanUi.Date();
    private readonly ModernButton exportButton;
    /* The filter values that produced the rows currently in the grid. Empty until a report has actually been run for this page. */
    private string _appliedFilterSignature = string.Empty;
    /* True while a Find Unposted Fees query is out. */
    private bool _loadInFlight;
    public UnpostedFeesReportPage()
        : base(
            PageTitles.UnpostedFees,
            "Fees the projection expects by now that have no charge yet - a read-only view, not an error list.")
    {
        Grid.MultiSelect = true;
        asOfDate.Value = DateTime.Today;
        var findUnposted = AddBusyAction("Find Unposted Fees", RunAsync, "Primary");
        var export = AddExportAction("FMSC_Fee_Unposted");
        RelocateFromActions(findUnposted, export);
        Defer(BindLookupsAsync);
        var filters = ReportUi.Bar(
            ("Academic Year", year),
            ("Campus", campus),
            ("As Of Date", asOfDate));
        UiHelper.SetGridPlaceholderMessage(Grid, UiText.EmptyGrid.RunReport);
        SetContent(ReportUi.Layout(filters, Grid, "Unposted Fees", findUnposted, export));
        exportButton = export;
        /* The grid keeps showing the rows an earlier query produced, so a filter change has to be recorded here. */
        foreach (var filter in new[] { year, campus })
            filter.SelectedIndexChanged += (_, _) => OnFiltersChanged();
        /* The as-of date is a filter too: moving it makes the displayed rows stale until the report is run again. */
        asOfDate.ValueChanged += (_, _) => OnFiltersChanged();
    }
    private string FilterSignature() =>
        $"{year.SelectedId}|{campus.SelectedId}|{asOfDate.Value.Date:yyyy-MM-dd}";
    private void SetReportActionsEnabled(bool enabled)
    {
        exportButton.Enabled = enabled;
    }
    /* report the displayed report as stale the moment the filters no longer match the values that generated it. */
    private void OnFiltersChanged()
    {
        if (string.IsNullOrEmpty(_appliedFilterSignature))
            return;
        bool current = string.Equals(
            _appliedFilterSignature,
            FilterSignature(),
            StringComparison.Ordinal);
        /* see _loadInFlight: while a run is out, the rows on screen are on their way to being replaced, so they are not exportable even when the filters match them again. */
        SetReportActionsEnabled(current && !_loadInFlight);
        if (!current)
            SetStatus(UiText.Reports.FiltersChanged, true);
        else if (_loadInFlight)
            SetStatus("Still loading. Export is available again once the report has finished.", true);
        else
            SetStatus(UiText.Reports.MatchesFilters);
    }
    private async Task BindLookupsAsync()
    {
        try
        {
            Task<DataTable> yearsTask = DatabaseHelper.QueryUserAsync("GetAcademicYears");
            Task<DataTable> campusesTask = DatabaseHelper.QueryUserAsync("GetCampuses");
            await Task.WhenAll(yearsTask, campusesTask);
            year.Bind(await yearsTask, "AcademicYearId", "YearName");
            campus.Bind(await campusesTask, "CampusId", "CampusName");
        }
        catch (Exception ex) { Fail(ex); }
    }
    private async Task RunAsync()
    {
        string requestedFilters = FilterSignature();
        try
        {
            int academicYearId = year.SelectedId;
            int campusId = campus.SelectedId;
            DateTime asOf = asOfDate.Value;
            _loadInFlight = true;
            try
            {
                await BindReportAsync(() => DatabaseHelper.QueryUser(
                    "GetMissingChargeCandidates",
                    DatabaseHelper.Int("@AcademicYearId", academicYearId),
                    DatabaseHelper.Int("@CampusId", campusId),
                    DatabaseHelper.Date("@AsOfDate", asOf)));
            }
            finally
            {
                _loadInFlight = false;
            }
            ConfigureGrid();
            /* only claim the grid matches the filters when they did not move while the query was running. */
            if (!string.Equals(requestedFilters, FilterSignature(), StringComparison.Ordinal))
            {
                /* BindReportAsync has already bound this run's rows, so leaving quietly left another scope's figures on screen, paged and exportable under the filters now selected. */
                _appliedFilterSignature = string.Empty;
                SetReportActionsEnabled(false);
                InvalidateReport();
                SetStatus(UiText.Reports.FiltersChanged, true);
                return;
            }
            _appliedFilterSignature = requestedFilters;
            SetReportActionsEnabled(true);
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void ConfigureGrid()
    {
        if (Grid.Columns.Count == 0)
            return;
        HideColumn("EnrollmentId");
        HideColumn("FeeTypeId");
        HideColumn("StudentFeeAssignmentId");
        /* Students are identified by Admission Number, so it comes first; the registration number (a reporting label) follows it. */
        ShowColumn("AdmissionNumber", "Admission Number", 115);
        SetHeader("RegistrationNumber", "Reg. No.", 95);
        SetHeader("StudentName", "Student", 180);
        SetHeader("CampusName", "Campus", 130);
        SetHeader("ClassName", "Class", 100);
        SetHeader("SectionName", "Section", 90);
        SetHeader("FeeTypeName", "Fee Type", 140);
        SetHeader("ExpectedBillingMonth", "Missing Month", 130);
        SetHeader("ExpectedAmount", "Amount", 110);
        Grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
    }
    private void HideColumn(string columnName)
    {
        if (Grid.Columns.Contains(columnName))
            Grid.Columns[columnName].Visible = false;
    }
    private void ShowColumn(string columnName, string header, int width)
    {
        if (!Grid.Columns.Contains(columnName))
            return;
        Grid.Columns[columnName].Visible = true;
        SetHeader(columnName, header, width);
        if (Grid.Columns.Contains("RegistrationNumber") &&
            Grid.Columns[columnName].DisplayIndex > Grid.Columns["RegistrationNumber"].DisplayIndex)
        {
            Grid.Columns[columnName].DisplayIndex = Grid.Columns["RegistrationNumber"].DisplayIndex;
        }
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
