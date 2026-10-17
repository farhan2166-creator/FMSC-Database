using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.Billing;
/// <summary>Manual Monthly Fee Posting - the operator decides what fee to post, for which month, and for whom. Re-clicking Post is therefore harmless - the second click posts nothing.</summary>
public sealed class FeePostingPage : V2Page, IBusyPage
{
    private bool posting;
    public bool IsBusy => posting;
    public string BusyMessage => "Monthly fees are being posted. Wait for the result before leaving.";
    private readonly LookupBox academicYear = new() { Enabled = false, TabStop = false };
    /// <summary>The months that can still be posted. LookupBox keys are integers, so each month is keyed as yyyymm (see BillingMonthKey) and shown as text.</summary>
    private readonly LookupBox monthBox = new();
    private readonly LookupBox campus = new();
    private readonly LookupBox klass = new();
    private readonly LookupBox section = new();
    /// <summary>LookupBox.Bind always inserts a blanket row with id 0 ("-- Select --") first.</summary>
    private readonly LookupBox feeType = new();
    /// <summary>The full preview result. Held here rather than read back off the grid, because the grid only ever contains the current page of it.</summary>
    private DataTable? eligibleRows;
    /// <summary>The procedure's own summary row for this month.</summary>
    private DataRow? eligibleSummary;
    /* The filter set the preview on screen was actually loaded with. */
    private string? appliedFilterSignature;
    /// <summary>The month the preview on screen was loaded for.</summary>
    private DateTime loadedMonth;
    /* Set while the page itself rebinds a list, so that rebinding does not look like the operator changing a filter. */
    private bool suppressFilterEvents;
    private bool lookupsReady;
    /* Guards RefreshMonthsAsync: only the newest request may land. */
    private int monthsLoadVersion;
    private ModernButton postButton = null!;
    private readonly Label postingOutcome = new() {
        Dock = DockStyle.Top, Visible = false,
        Height = AppMetrics.TextHeight(AppTheme.NormalFont) * 2 + 12,
        Padding = new Padding(8, 6, 8, 6), Font = AppTheme.NormalFont,
        BackColor = AppTheme.SurfaceSecondary
    };
    public FeePostingPage() : this(true) { }
    internal FeePostingPage(bool initialize)
        : base(
            "Post Monthly Fees",
            "Review and post all eligible monthly fees for the chosen month and filters.")
    {
        Grid.ReadOnly = true;
#if FMSC_LEVEL2
        Grid.AccessibleName = "Monthly posting preview";
#endif
        var loadPreview = AddBusyAction(
            "Load Eligible Fees",
            LoadPreviewAsync,
            "Primary");
        var refreshMonths = AddBusyAction("Refresh Months", ReloadMonthsAsync, "Secondary");
        /* posting creates financial obligations for every eligible row, so a double click must not post twice. */
        postButton = AddBusyAction(
            "Post Eligible Fees",
            PostEligibleAsync,
            "Success");
        RelocateFromActions(refreshMonths, loadPreview, postButton);
        if (initialize) Defer(BindLookupsAsync);
        // The entries carry a count, so the open list is wider than the field.
        monthBox.DropDownWidth = AppScale.Scale(320);
        var filters = ReportUi.Bar(
            ("Academic Year", academicYear),
            ("Billing Month", monthBox),
            ("Campus", campus),
            ("Class", klass),
            ("Section", section),
            ("Fee Type", feeType));
        /* any filter change drops the loaded preview, so Post can never send one filter's rows under another filter's label. */
        monthBox.SelectedIndexChanged += (_, _) => {
            if (!suppressFilterEvents)
                InvalidatePreview();
        };
        /* The year and every filter also change WHICH months still have something to post, so they refresh the month list as well as dropping the preview. */
        academicYear.SelectedIndexChanged += async (_, _) => await OnScopeChangedAsync();
        campus.SelectedIndexChanged += async (_, _) => await OnScopeChangedAsync();
        klass.SelectedIndexChanged += async (_, _) => await OnScopeChangedAsync();
        section.SelectedIndexChanged += async (_, _) => await OnScopeChangedAsync();
        feeType.SelectedIndexChanged += async (_, _) => await OnScopeChangedAsync();
        SetContent(ReportUi.Layout(filters, Grid, "Post Monthly Fees", refreshMonths, loadPreview, postButton));
        Grid.Parent?.Controls.Add(postingOutcome);
        Grid.BringToFront();
        VisibleChanged += async (_, _) => {
            if (Visible && lookupsReady && !posting) await ReloadMonthsAsync();
        };
    }
    private async Task ReloadMonthsAsync()
    {
        if (posting) return;
        InvalidatePreview(announce: false);
        await RefreshMonthsAsync();
    }
    /// <summary>every filter the preview query is built from, in one comparable string, read from the live controls.</summary>
    private string FilterSignature() =>
        string.Join(
            '|',
            academicYear.SelectedId,
            monthBox.SelectedId,
            campus.SelectedId,
            klass.SelectedId,
            section.SelectedId,
            feeType.SelectedId);
    /// <summary>The loaded preview no longer describes the filters on screen, so it is dropped rather than left to be posted under them.</summary>
    private void InvalidatePreview(bool announce = true)
    {
        if (eligibleRows is null)
        {
            return;
        }
        appliedFilterSignature = null;
        eligibleRows = null;
        eligibleSummary = null;
        /* The preview cache goes with the grid, so the superseded rows cannot be paged or exported under the new filters. */
        InvalidateReport();
        if (announce)
        {
            SetStatus(
                "Filters changed. Load the eligible fees again before posting.",
                true);
        }
    }
    private async Task OnScopeChangedAsync()
    {
        if (suppressFilterEvents)
            return;
        InvalidatePreview();
        await RefreshMonthsAsync();
    }
    /// <summary>Rebuilds the Billing Month list for the chosen Academic Year and filters: only months that still have at least one eligible, unposted charge.</summary>
    private async Task RefreshMonthsAsync()
    {
        // User decision (2026-10-11, scenario audit SC-02): academic-year month
        // eligibility is handled by this C# workflow. The DropDownList is bound only
        // to GetPostableMonths' bounded year list; filter changes invalidate the preview,
        // and PostEligibleAsync rechecks that scope before sending its captured month.
        // Do not add SQL changes solely for a direct call bypassing this workflow.
        int version = ++monthsLoadVersion;
        // Read on the UI thread, before the await.
        int previousKey = monthBox.SelectedId;
        int yearId = academicYear.SelectedId;
        int campusId = campus.SelectedId;
        int classId = klass.SelectedId;
        int sectionId = section.SelectedId;
        int feeTypeId = feeType.SelectedId;
        try
        {
            DataTable rows;
            if (yearId <= 0)
            {
                rows = new DataTable();
            }
            else
            {
                var feeTypes = new SqlParameter("@FeeTypeIds", SqlDbType.Structured) {
                    TypeName = "dbo.FeeTypeIdList",
                    Value = Tvp.FeeTypeIds(
                        feeTypeId > 0
                            ? new[] { feeTypeId }
                            : Array.Empty<int>())
                };
                rows = await DatabaseHelper.QueryUserAsync(
                    "GetPostableMonths",
                    DatabaseHelper.Int("@AcademicYearId", yearId),
                    DatabaseHelper.Int("@CampusId", campusId),
                    DatabaseHelper.Int("@ClassId", classId),
                    DatabaseHelper.Int("@SectionId", sectionId),
                    feeTypes);
            }
            if (IsDisposed || version != monthsLoadVersion)
                return;
            // The operator may have picked a month from the old list while this was loading; that pick, not the one read before the await, is what to keep.
            previousKey = monthBox.SelectedId;
            var months = new DataTable();
            months.Columns.Add("MonthKey", typeof(int));
            months.Columns.Add("Display", typeof(string));
            foreach (DataRow row in rows.Rows)
            {
                DateTime m = Convert.ToDateTime(row["BillingMonth"]);
                int charges = Convert.ToInt32(row["ChargesToPost"]);
                months.Rows.Add(
                    BillingMonthKey.From(m),
                    $"{m:MMMM yyyy}  -  {charges:N0} to post");
            }
            int currentKey = BillingMonthKey.From(DateTime.Today);
            int wanted =
                BillingMonthKey.IsListed(months, previousKey) ? previousKey
                : BillingMonthKey.IsListed(months, currentKey) ? currentKey
                : months.Rows.Count > 0 ? Convert.ToInt32(months.Rows[0]["MonthKey"])
                : 0;
            suppressFilterEvents = true;
            try
            {
                monthBox.Bind(months, "MonthKey", "Display");
                monthBox.SelectedValue = wanted;
            }
            finally
            {
                suppressFilterEvents = false;
            }
            // The month moved without the operator touching it (it was fully posted, or the filters no longer include it).
            if (monthBox.SelectedId != previousKey)
                InvalidatePreview(announce: false);
            if (eligibleRows is null)
            {
                if (yearId <= 0)
                {
                    SetStatus("Choose an Academic Year to see the months that can be posted.", true);
                }
                else if (months.Rows.Count == 0)
                {
                    SetStatus(
                        "Nothing is left to post in this academic year for the selected filters. " +
                        "Every fee that is due is already posted, or no fee is assigned yet.",
                        true);
                }
                else
                {
                    SetStatus(
                        $"{months.Rows.Count:N0} month(s) still have fees to post. " +
                        "Choose a month, then Load Eligible Fees.");
                }
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed && version == monthsLoadVersion)
                Fail(ex);
        }
    }
    private async Task BindLookupsAsync()
    {
        try
        {
            // Rebinding the lists must not read as the operator changing a filter (each Bind fires SelectedIndexChanged).
            suppressFilterEvents = true;
            // These reference lists are independent, so one slow lookup no longer serializes the rest of the page startup.
            Task<DataTable> yearsTask = DatabaseHelper.QueryUserAsync("GetAcademicYears");
            Task<DataTable> campusesTask = DatabaseHelper.QueryUserAsync("GetCampuses");
            Task<DataTable> classesTask = DatabaseHelper.QueryUserAsync("GetClasses");
            Task<DataTable> sectionsTask = ReportUi.SectionFilterListAsync();
            Task<DataTable> feeTypesTask = DatabaseHelper.QueryUserAsync(
                "GetFeeTypes");
            await Task.WhenAll(yearsTask, campusesTask, classesTask, sectionsTask, feeTypesTask);
            DataTable years = await yearsTask;
            HumanUi.BindCurrentAcademicYear(academicYear, years);
            // Classes and Sections are global lists (they are not per-campus), so they are bound unscoped - the same as the Class/Section filters on the other fee pages.
            campus.Bind(await campusesTask, "CampusId", "CampusName");
            klass.Bind(await classesTask, "ClassId", "ClassName");
            section.Bind(await sectionsTask, "SectionId", "SectionName");
            // GetFeeTypes returns active types ordered by DisplayOrder.
            feeType.Bind(await feeTypesTask, "FeeTypeId", "FeeTypeName");
            suppressFilterEvents = false;
            lookupsReady = true;
            await RefreshMonthsAsync();
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            suppressFilterEvents = false;
        }
    }
    private async Task LoadPreviewAsync()
    {
        try
        {
            DateTime? chosenMonth = BillingMonthKey.ToMonth(monthBox.SelectedId);
            if (chosenMonth is null)
            {
                Warn(
                    "Choose a billing month first. The list shows only the months " +
                    "that still have fees to post.");
                return;
            }
            DateTime previewMonth = chosenMonth.Value;
            /* F6/F7 - the filters this query is run for, re-checked after the awaits. */
            string requestedFilters = FilterSignature();
            // Empty TVP = every assigned fee type is eligible, which is what the procedures read as "all".
            var feeTypes = new SqlParameter("@FeeTypeIds", SqlDbType.Structured) {
                TypeName = "dbo.FeeTypeIdList",
                Value = Tvp.FeeTypeIds(
                    feeType.SelectedId > 0
                        ? new[] { feeType.SelectedId }
                        : Array.Empty<int>())
            };
            // @ExcludeAlreadyPosted = 1: fees already posted for this month are not eligible and are not listed.
            DataSet result = await DatabaseHelper.QueryUserSetAsync(
                "PreviewMonthlyFees",
                DatabaseHelper.Int("@AcademicYearId", academicYear.SelectedId),
                DatabaseHelper.Date("@BillingMonth", previewMonth),
                DatabaseHelper.Int("@CampusId", campus.SelectedId),
                DatabaseHelper.Int("@ClassId", klass.SelectedId),
                DatabaseHelper.Int("@SectionId", section.SelectedId),
                feeTypes,
                DatabaseHelper.Bit("@ExcludeAlreadyPosted", true));
            DataTable rows = result.Tables.Count > 0 ? result.Tables[0] : new DataTable();
            DataTable summary = result.Tables.Count > 1 ? result.Tables[1] : new DataTable();
            if (!string.Equals(requestedFilters, FilterSignature(), StringComparison.Ordinal))
                return;
            // Keep our own reference to the FULL, unpaged result before binding, because BindReportAsync pages the grid and Post must act on every eligible row, not merely the page…
            eligibleRows = rows;
            eligibleSummary = summary.Rows.Count > 0 ? summary.Rows[0] : null;
            loadedMonth = previewMonth;
            appliedFilterSignature = requestedFilters;
            await BindReportAsync(() => Task.FromResult(rows));
            // The filters can move while the grid is being bound; the change handler has already dropped the preview, so do not announce it.
            if (!string.Equals(requestedFilters, FilterSignature(), StringComparison.Ordinal))
                return;
            ConfigureGrid();
            if (rows.Rows.Count == 0)
            {
                SetStatus(
                    $"Nothing is eligible for {previewMonth:MMMM yyyy}. Every fee " +
                    "for this month is either already posted, not yet effective for the " +
                    "student, or the filters match nobody.",
                    true);
                return;
            }
            UpdatePreviewSummary();
            // Repaint the read-only preview.
            Grid.Invalidate();
        }
        catch (Exception ex)
        {
            // A failed load must not leave the previous rows looking current (after a Post they are the rows that were just posted).
            InvalidatePreview(announce: false);
            Fail(ex);
        }
    }
    /* The grid is paged (500 rows): every Next / Previous rebinds it and drops the hidden id columns, headers and read-only flags this page sets. */
    protected override void OnReportPageBound()
    {
        ConfigureGrid();
        if (eligibleRows is not null)
            UpdatePreviewSummary();
    }
    private void ConfigureGrid()
    {
        if (Grid.Columns.Count == 0)
            return;
        HideColumn("StudentFeeAssignmentId");
        HideColumn("EnrollmentId");
        HideColumn("AcademicYearId");
        HideColumn("FeeTypeId");
        HideColumn("ChargeDate");
        HideColumn("DueDate");
        HideColumn("DiscountAmount");
        Grid.ReadOnly = true;
        foreach (DataGridViewColumn column in Grid.Columns) column.ReadOnly = true;
        /* Students are identified by Admission Number, so it comes first; the registration number (a reporting label) follows it. */
        ShowColumn("AdmissionNumber", "Admission Number", 115);
        SetHeader("RegistrationNumber", "Reg. No.", 95);
        SetHeader("StudentName", "Student", 180);
        SetHeader("CampusName", "Campus", 115);
        SetHeader("ClassName", "Class", 100);
        SetHeader("SectionName", "Section", 90);
        SetHeader("FeeTypeName", UiText.Fees.FeeType, 150);
        SetHeader("BillingMonth", "Billing Month", 120);
        SetHeader("GrossAmount", UiText.Fees.Amount, 100);
        SetHeader("NetAmount", UiText.Fees.NetAmount, 110);
        SetHeader("EligibilityNote", "Status", 170);
        Grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
    }
    private void UpdatePreviewSummary()
    {
        if (eligibleRows is null) return;
        int students = eligibleRows.AsEnumerable().Select(row => Convert.ToInt32(row["AdmissionId"])).Distinct().Count();
        decimal total = eligibleRows.AsEnumerable().Sum(row => Convert.ToDecimal(row["NetAmount"]));
        int reversed = eligibleRows.AsEnumerable().Count(row => Convert.ToString(row["EligibilityNote"])!.StartsWith("Previously reversed", StringComparison.Ordinal));
        SetStatus($"{loadedMonth:MMMM yyyy}: {eligibleRows.Rows.Count:N0} eligible charge(s) for {students:N0} student(s), net Rs. {total:N0}. All listed fees across all pages will be posted after confirmation." +
            (reversed > 0 ? $" Includes {reversed:N0} previously reversed fee(s) to post again." : ""));
    }
    private async Task PostEligibleAsync()
    {
        if (posting) return;
        posting = true;
        try
        {
            using var busy = BeginBusy(BusyMessage, this);
            if (eligibleRows is null || eligibleRows.Rows.Count == 0)
            {
                Warn("Load the eligible fees first, then review the list before posting.");
                return;
            }
            // The captured preview month and rows must still match the bounded month
            // selection; a changed year/month requires a new preview before posting.
            if (!string.Equals(
                    appliedFilterSignature,
                    FilterSignature(),
                    StringComparison.Ordinal))
            {
                Warn(
                    "The filters have changed since these fees were loaded. " +
                    "Load the eligible fees again before posting.");
                return;
            }
            // The list this confirmation is about.
            DataTable previewSnapshot = eligibleRows;
            var chosen = previewSnapshot.Rows
                .Cast<DataRow>()

                .ToList();
            if (chosen.Count == 0)
            {
                Warn("No eligible fees remain in this preview.");
                return;
            }
            decimal net = chosen.Sum(row => Convert.ToDecimal(row["NetAmount"]));
            int students = chosen
                .Select(row => Convert.ToInt32(row["AdmissionId"]))
                .Distinct()
                .Count();
            string month = loadedMonth.ToString("MMMM yyyy");
            int postingYearId = academicYear.SelectedId;
            string scopeText = $"Academic year: {academicYear.Text}\nCampus: {campus.Text}; Class: {klass.Text}; Section: {section.Text}";
            int reversedCount=chosen.Count(row=>Convert.ToString(row["EligibilityNote"])!.StartsWith("Previously reversed",StringComparison.Ordinal));
            if (!Confirm(
                    $"Post {chosen.Count:N0} fee(s) for {students:N0} student(s) for {month}?" +
                    $"\n\n{scopeText}" +
                    $"\n\nTotal net payable: Rs. {net:N0}\n\n" +
                    "All eligible rows in this loaded preview are posted. Fees already posted for this month " +
                    $"are untouched, and nothing is posted for any month other than {month}."+
                    (reversedCount>0 ? $"\n\n{reversedCount} previously reversed fee(s) will receive new charges because they are included in this preview." : string.Empty),
                    "Post Monthly Fees",
                    defaultYes: false))
                return;
            if (!ReferenceEquals(previewSnapshot, eligibleRows) ||
                !string.Equals(appliedFilterSignature, FilterSignature(), StringComparison.Ordinal))
            {
                Warn(
                    "The list changed while you were confirming, so nothing was posted. " +
                    "Load the eligible fees again and post from the fresh list.");
                return;
            }
            // This is what makes the posting explicit rather than filter-driven: the database writes these rows and nothing else.
            var selection = Tvp.PostingSelectionsFromPreview(
                eligibleRows,
                _ => true);
            var selectionParameter = new SqlParameter("@Selection", SqlDbType.Structured) {
                TypeName = "dbo.PostingSelectionType",
                Value = selection
            };
            DataTable result = await DatabaseHelper.QueryUserAsync(
                "PostSelectedMonthlyFees",
                DatabaseHelper.Int("@AcademicYearId", postingYearId),
                DatabaseHelper.Date("@BillingMonth", loadedMonth),
                selectionParameter,
                DatabaseHelper.P("@Reason", $"Posted from Post Monthly Fees for {month}."));
            int created = 0;
            int notPosted = 0;
            bool countsAvailable = result.Rows.Count > 0 && result.Columns.Contains("ChargesCreated")
                && result.Columns.Contains("RowsNotPosted") && !result.Rows[0].IsNull("ChargesCreated")
                && !result.Rows[0].IsNull("RowsNotPosted");
            if (countsAvailable)
            {
                DataRow summary = result.Rows[0];
                if (result.Columns.Contains("ChargesCreated"))
                    created = Convert.ToInt32(summary["ChargesCreated"]);
                if (result.Columns.Contains("RowsNotPosted"))
                    notPosted = Convert.ToInt32(summary["RowsNotPosted"]);
            }
            /* report the attempt alongside the server's own counts, so a partial posting cannot read as a complete one. */
            string resultText = countsAvailable ?
                $"Post result for {month}: selected {chosen.Count:N0} fee row(s); " +
                $"posted {created:N0} charge(s)" +
                (notPosted > 0
                    ? $"; NOT posted {notPosted:N0} row(s) - already posted, or no longer eligible. " +
                      "The server reports those two reasons as one figure; re-check the eligible " +
                      "list below for detail."
                    : "; nothing selected was left unposted.")
                : $"Posting returned for {month}, but the server did not return counts. Review the refreshed eligible fees.";
            postingOutcome.Text = countsAvailable
                ? $"Last posting — {month}: {created:N0} charge(s) posted; {notPosted:N0} not posted.\nSelected: {chosen.Count:N0} fee(s) for {students:N0} student(s), net Rs. {net:N0}."
                : resultText;
            postingOutcome.ForeColor = AppTheme.StatusText(notPosted > 0 || !countsAvailable ? AppTheme.Warning : AppTheme.Success);
            postingOutcome.Visible = true;
            SetStatus(resultText, notPosted > 0 || !countsAvailable);
            // Refresh the month list first: a month that is now fully posted leaves it, which is what stops the operator picking it again.
            int postedKey = BillingMonthKey.From(loadedMonth);
            await RefreshMonthsAsync();
            if (monthBox.SelectedId == postedKey)
            {
                // Reload so the posted rows leave the eligible list - which is what makes a second Post click a no-op rather than a double charge.
                await LoadPreviewAsync();
            }
            else
            {
                SetStatus(
                    resultText +
                    $" {month} has nothing left to post for these filters, so it has " +
                    "been removed from the Billing Month list.",
                    notPosted > 0 || !countsAvailable);
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally { posting = false; }
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
