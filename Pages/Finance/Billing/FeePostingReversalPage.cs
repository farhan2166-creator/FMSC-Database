using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.Billing;
/// <summary>Reverses a monthly fee posting - but only the part that has NOT been collected. That is the intended repair loop: post -> mistake -> reverse -> repost.</summary>
public sealed class FeePostingReversalPage : V2Page
{
    /// <summary>The months that have a live posting to reverse (dbo.GetReversibleMonths), newest first. A month with nothing posted, or already fully reversed, is not offered. Keyed as yyyymm - see BillingMonthKey.</summary>
    private readonly LookupBox monthBox = new();
    private readonly LookupBox campus = new();
    private readonly LookupBox klass = new();
    private readonly LookupBox section = new();
    private readonly TextBox reason = new();
    private DataTable? previewRows;
    /* The filter set the preview on screen was actually loaded with. */
    private string? appliedFilterSignature;
    /// <summary>The month the impact on screen was previewed for. Reverse reads this, never the live list, so the confirmation and the reversal cannot disagree.</summary>
    private DateTime loadedMonth;
    /* Set while the page itself rebinds a list, so that rebinding does not look like the operator changing a filter. */
    private bool suppressFilterEvents;
    /* Guards RefreshMonthsAsync: only the newest request may land. */
    private int monthsLoadVersion;
    private ModernButton reverseButton = null!;
    public FeePostingReversalPage()
        : base("Reverse a Posting", "Review and cancel uncollected fee charges for a month.")
    {
        // Required by dbo.ReverseMonthlyFees, so it is a labelled input rather than an optional remark.
        reason.Width = AppScale.Scale(320);
        reason.PlaceholderText = "e.g. Wrong month selected when posting.";
        var preview = AddBusyAction("Preview Impact", PreviewAsync, "Primary");
        /* reversal cancels a whole month's charges, so it must not be able to run twice from a double click. */
        reverseButton = AddBusyAction("Reverse Uncollected Fees", ReverseAsync, "Danger");
        RelocateFromActions(preview, reverseButton);
        Defer(BindLookupsAsync);
        // The entries carry a count, so the open list is wider than the field.
        monthBox.DropDownWidth = AppScale.Scale(400);
        var filters = ReportUi.Bar(
            ("Billing Month", monthBox),
            ("Campus", campus),
            ("Class", klass),
            ("Section", section),
            (UiText.Fields.Reason, reason));
        SetContent(ReportUi.Layout(filters, Grid, "Reversal Impact", preview, reverseButton));
        /* any filter change drops the previewed impact, so Reverse can never run against a scope the operator has not seen. */
        monthBox.SelectedIndexChanged += (_, _) => {
            if (!suppressFilterEvents)
                InvalidatePreview();
        };
        /* The filters also change WHICH months have a live posting, so they refresh the month list as well as dropping the preview. */
        campus.SelectedIndexChanged += async (_, _) => await OnScopeChangedAsync();
        klass.SelectedIndexChanged += async (_, _) => await OnScopeChangedAsync();
        section.SelectedIndexChanged += async (_, _) => await OnScopeChangedAsync();
    }
    /// <summary>every filter the preview query is built from, in one comparable string, read from the live controls. The reason box is not part of it: it is not a query filter.</summary>
    private string FilterSignature() =>
        string.Join('|', monthBox.SelectedId, campus.SelectedId, klass.SelectedId, section.SelectedId);
    /// <summary>The previewed impact no longer describes the filters on screen, so it is dropped rather than left to be reversed under them.</summary>
    private void InvalidatePreview(bool announce = true)
    {
        if (previewRows is null)
            return;
        appliedFilterSignature = null;
        previewRows = null;
        /* The preview cache goes with the grid, so the superseded impact cannot be paged or exported under the new filters. */
        InvalidateReport();
        if (announce)
            SetStatus("Filters changed. Preview the impact again before reversing.", true);
    }
    private async Task OnScopeChangedAsync()
    {
        if (suppressFilterEvents)
            return;
        InvalidatePreview();
        await RefreshMonthsAsync();
    }
    /// <summary>Rebuilds the Billing Month list from the months that have a live posting for the selected filters.</summary>
    private async Task RefreshMonthsAsync()
    {
        int version = ++monthsLoadVersion;
        // Read on the UI thread, before the await.
        int previousKey = monthBox.SelectedId;
        int campusId = campus.SelectedId;
        int classId = klass.SelectedId;
        int sectionId = section.SelectedId;
        try
        {
            var feeTypes = new SqlParameter("@FeeTypeIds", SqlDbType.Structured) {
                TypeName = "dbo.FeeTypeIdList",
                Value = Tvp.FeeTypeIds(Array.Empty<int>())
            };
            DataTable rows = await DatabaseHelper.QueryUserAsync(
                "GetReversibleMonths",
                DatabaseHelper.Int("@CampusId", campusId),
                DatabaseHelper.Int("@ClassId", classId),
                DatabaseHelper.Int("@SectionId", sectionId),
                feeTypes);
            if (IsDisposed || version != monthsLoadVersion)
                return;
            // Keep a month the operator picked while this was loading.
            previousKey = monthBox.SelectedId;
            var months = new DataTable();
            months.Columns.Add("MonthKey", typeof(int));
            months.Columns.Add("Display", typeof(string));
            foreach (DataRow row in rows.Rows)
            {
                DateTime m = Convert.ToDateTime(row["BillingMonth"]);
                int charges = Convert.ToInt32(row["ChargesAffected"]);
                int reversible = Convert.ToInt32(row["ReversibleCharges"]);
                months.Rows.Add(
                    BillingMonthKey.From(m),
                    $"{m:MMMM yyyy}  -  {charges:N0} posted, {reversible:N0} reversible");
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
            // The month moved without the operator touching it, so an impact previewed for the old month no longer describes what is selected.
            if (monthBox.SelectedId != previousKey)
                InvalidatePreview(announce: false);
            if (previewRows is null)
            {
                SetStatus(
                    months.Rows.Count == 0
                        ? "There is no live posting for these filters, so there is nothing to reverse."
                        : $"{months.Rows.Count:N0} month(s) have postings. Choose a month, then Preview Impact.",
                    months.Rows.Count == 0);
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
            // Each Bind fires SelectedIndexChanged, which must not read as the operator changing a filter; the month list is built once, at the end.
            suppressFilterEvents = true;
            Task<DataTable> campusesTask = DatabaseHelper.QueryUserAsync("GetCampuses");
            Task<DataTable> classesTask = DatabaseHelper.QueryUserAsync("GetClasses");
            Task<DataTable> sectionsTask = ReportUi.SectionFilterListAsync();
            await Task.WhenAll(campusesTask, classesTask, sectionsTask);
            campus.Bind(await campusesTask, "CampusId", "CampusName");
            klass.Bind(await classesTask, "ClassId", "ClassName");
            section.Bind(await sectionsTask, "SectionId", "SectionName");
            suppressFilterEvents = false;
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
    private async Task PreviewAsync()
    {
        try
        {
            DateTime? chosenMonth = BillingMonthKey.ToMonth(monthBox.SelectedId);
            if (chosenMonth is null)
            {
                Warn("Choose a billing month first. The list shows only the months that have a posting.");
                return;
            }
            DateTime previewMonth = chosenMonth.Value;
            /* F8/F9 - the filters this preview is run for, re-checked after the await. */
            string requestedFilters = FilterSignature();
            previewRows = null;
            var feeTypes = new SqlParameter("@FeeTypeIds", SqlDbType.Structured) {
                TypeName = "dbo.FeeTypeIdList",
                Value = Tvp.FeeTypeIds(Array.Empty<int>())
            };
            DataTable rows = await DatabaseHelper.QueryUserAsync(
                "PreviewPostingReversal",
                DatabaseHelper.Date("@BillingMonth", previewMonth),
                DatabaseHelper.Int("@CampusId", campus.SelectedId),
                DatabaseHelper.Int("@ClassId", klass.SelectedId),
                DatabaseHelper.Int("@SectionId", section.SelectedId),
                feeTypes);
            if (!string.Equals(requestedFilters, FilterSignature(), StringComparison.Ordinal))
            {
                /* InvalidatePreview returns early while previewRows is null (it was cleared before this query), so the grid of the previous preview is still on screen and exportable. Drop it here. */
                InvalidateReport();
                SetStatus("Filters changed while the preview was loading. Preview the impact again before reversing.", true);
                return;
            }
            previewRows = rows;
            loadedMonth = previewMonth;
            appliedFilterSignature = requestedFilters;
            await BindReportAsync(() => Task.FromResult(rows));
            ConfigureGrid();
            if (rows.Rows.Count == 0)
            {
                SetStatus(
                    $"There is no live posting for {previewMonth:MMMM yyyy} " +
                    "matching these filters, so there is nothing to reverse.",
                    true);
                return;
            }
            ReportImpact(rows);
        }
        catch (Exception ex)
        {
            // previewRows was cleared before the query, so drop the old grid too rather than leave the previous preview on screen behind the error.
            InvalidateReport();
            Fail(ex);
        }
    }
    /// <summary>The impact summary is computed from the same detail rows the operator can see, so the numbers and the list can never disagree.</summary>
    private void ReportImpact(DataTable rows)
    {
        int total = rows.Rows.Count;
        int reversible = 0;
        int blocked = 0;
        decimal posted = 0m;
        decimal collected = 0m;
        decimal uncollected = 0m;
        var students = new HashSet<int>();
        foreach (DataRow row in rows.Rows)
        {
            students.Add(Convert.ToInt32(row["AdmissionId"]));
            posted += Convert.ToDecimal(row["NetAmount"]);
            collected += Convert.ToDecimal(row["CollectedAmount"]);
            uncollected += Convert.ToDecimal(row["UncollectedAmount"]);
            if (Convert.ToInt32(row["HasCollectedMoney"]) == 0)
                reversible++;
            else
                blocked++;
        }
        string message =
            $"{loadedMonth:MMMM yyyy}: {total:N0} charge(s) across " +
            $"{students.Count:N0} student(s). Posted Rs. {posted:N0}, collected " +
            $"Rs. {collected:N0}, uncollected Rs. {uncollected:N0}.\n\n" +
            $"Reversible now (nothing collected): {reversible:N0} charge(s).";
        if (blocked > 0)
        {
            message +=
                $"\n\n{blocked:N0} charge(s) have money collected against them and " +
                "CANNOT be reversed here. Use Fee Collection " +
                "Correction/Refund/Reversal for those, so the payment records stay " +
                "correct.";
        }
        SetStatus(message, blocked > 0);
    }
    /* Every Next / Previous rebinds the paged grid and drops this page's column setup. */
    protected override void OnReportPageBound()
    {
        ConfigureGrid();
    }
    private void ConfigureGrid()
    {
        if (Grid.Columns.Count == 0)
            return;
        HideColumn("BillingChargeId");
        HideColumn("HasCollectedMoney");
        HideColumn("GrossAmount");
        HideColumn("DiscountAmount");
        /* Students are identified by Admission Number, so it comes first; the registration number (a reporting label) follows it. */
        ShowColumn("AdmissionNumber", "Admission Number", 115);
        SetHeader("RegistrationNumber", "Reg. No.", 95);
        SetHeader("StudentName", "Student", 170);
        SetHeader("CampusName", "Campus", 110);
        SetHeader("ClassName", "Class", 95);
        SetHeader("SectionName", "Section", 85);
        SetHeader("FeeTypeName", "Fee Type", 140);
        SetHeader("BillingMonth", "Billing Month", 120);
        SetHeader("NetAmount", "Posted", 100);
        SetHeader("CollectedAmount", "Collected", 100);
        SetHeader("UncollectedAmount", "Uncollected", 105);
        SetHeader("ReversalNote", "Reversal", 260);
        Grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
    }
    private async Task ReverseAsync()
    {
        try
        {
            if (previewRows is null || previewRows.Rows.Count == 0)
            {
                Warn("Preview the impact first, then reverse. The preview is what " +
                     "shows you which fees can be reversed and which are protected " +
                     "because money has been collected against them.");
                return;
            }
            /* The preview and the live filters must agree. */
            if (!string.Equals(appliedFilterSignature, FilterSignature(), StringComparison.Ordinal))
            {
                Warn(
                    "The filters have changed since this impact was previewed. " +
                    "Preview the impact again before reversing.");
                return;
            }
            string trimmedReason = reason.Text.Trim();
            if (trimmedReason.Length == 0)
            {
                Warn("Enter a reason. Reversal is recorded on the charge and in the " +
                     "audit trail, and a reversal cannot be told apart from any other " +
                     "cancellation afterwards without it.");
                reason.Focus();
                return;
            }
            // Counted from the same rows the operator saw, so the confirmation cannot promise to reverse something the preview marked as blocked.
            int reversible = previewRows.Rows
                .Cast<DataRow>()
                .Count(row => Convert.ToInt32(row["HasCollectedMoney"]) == 0);
            int blocked = previewRows.Rows.Count - reversible;
            decimal reversibleAmount = previewRows.Rows
                .Cast<DataRow>()
                .Where(row => Convert.ToInt32(row["HasCollectedMoney"]) == 0)
                .Sum(row => Convert.ToDecimal(row["UncollectedAmount"]));
            string month = loadedMonth.ToString("MMMM yyyy");
            if (reversible == 0)
            {
                Warn(
                    $"None of the {previewRows.Rows.Count:N0} charge(s) for {month} can be " +
                    "reversed here, because every one of them has money collected " +
                    "against it.\n\nUse Fee Collection Correction/Refund/Reversal " +
                    "instead - that workflow keeps the payment and receipt records " +
                    "correct, which cancelling the charge would not.");
                return;
            }
            string confirmation =
                $"Reverse {reversible:N0} uncollected charge(s) for {month}?\n\n" +
                $"Amount to be reversed: Rs. {reversibleAmount:N0}\n\n" +
                "These charges will be marked Cancelled. No payment records are " +
                "touched, because none of these charges has any payment against it. " +
                "The students become eligible for this month again, so you can post " +
                "the fees afresh.";
            if (blocked > 0)
            {
                confirmation +=
                    $"\n\n{blocked:N0} charge(s) will NOT be reversed because money has " +
                    "been collected against them. Handle those in Fee Collection.";
            }
            if (!Confirm(confirmation, "Reverse Posting"))
                return;
            var feeTypes = new SqlParameter("@FeeTypeIds", SqlDbType.Structured) {
                TypeName = "dbo.FeeTypeIdList",
                Value = Tvp.FeeTypeIds(Array.Empty<int>())
            };
            // Two result sets: the reversal summary, then the blocked charges that need the financial workflow.
            DataSet result = await DatabaseHelper.QueryUserSetAsync(
                "ReverseMonthlyFees",
                DatabaseHelper.Date("@BillingMonth", loadedMonth),
                DatabaseHelper.P("@Reason", trimmedReason),
                DatabaseHelper.Int("@CampusId", campus.SelectedId),
                DatabaseHelper.Int("@ClassId", klass.SelectedId),
                DatabaseHelper.Int("@SectionId", section.SelectedId),
                feeTypes);
            DataTable summary = result.Tables.Cast<DataTable>().FirstOrDefault(table=>table.Columns.Contains("ChargesReversed")) ?? new DataTable();
            DataTable blockedRows = result.Tables.Cast<DataTable>().FirstOrDefault(table=>table.Columns.Contains("ActionRequired")) ?? new DataTable();
            int reversed = 0;
            decimal reversedAmount = 0m;
            if (summary.Rows.Count > 0)
            {
                DataRow row = summary.Rows[0];
                if (summary.Columns.Contains("ChargesReversed"))
                    reversed = Convert.ToInt32(row["ChargesReversed"]);
                if (summary.Columns.Contains("ReversedAmount"))
                    reversedAmount = Convert.ToDecimal(row["ReversedAmount"]);
            }
            /* attempted / succeeded / not completed, from the server's own figures, so a partial reversal cannot read as a full one. */
            int requested = reversible;
            int notReversed = Math.Max(0, requested - reversed);
            string outcome =
                $"Reversal result for {month}:\n\n" +
                $"Requested (uncollected charges): {requested:N0}\n" +
                $"Reversed: {reversed:N0}, totalling Rs. {reversedAmount:N0}\n" +
                $"NOT reversed - attempted but not completed: {notReversed:N0}";
            if (blockedRows.Rows.Count > 0)
            {
                outcome +=
                    $"\n\n{blockedRows.Rows.Count:N0} charge(s) were left alone because " +
                    "they have money collected against them. Use Fee Collection " +
                    "Correction/Refund/Reversal for those.";
            }
            if (notReversed > 0)
            {
                outcome +=
                    $"\n\n{notReversed:N0} of the {requested:N0} charge(s) this screen asked to " +
                    "reverse were NOT reversed. This page cannot name them - the server does not " +
                    "itemise the shortfall - but the preview below has been reloaded, so any " +
                    "charge still listed is still posted. Retry once the cause is cleared.";
                Warn(outcome);
            }
            else
            {
                outcome +=
                    $"\n\nEvery charge this screen asked to reverse was reversed. Those students " +
                    $"are eligible for {month} again.";
                Info(outcome);
            }
            // Clear the reason so it cannot be reused by accident on a second reversal, then re-preview to show the true remaining state.
            reason.Clear();
            // Refresh the month list first: a month with nothing live left leaves it.
            int reversedKey = BillingMonthKey.From(loadedMonth);
            await RefreshMonthsAsync();
            if (monthBox.SelectedId == reversedKey)
            {
                await PreviewAsync();
            }
            else
            {
                SetStatus(
                    $"No live posting is left for {month} with these filters, so it has been " +
                    "removed from the Billing Month list.");
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
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
