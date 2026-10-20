using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Reports.Finance;
/// <summary>Shared behavior for the three canonical finance reports. The same captured table feeds the grid, printer and CSV export.</summary>
public abstract class CanonicalFinanceReportPage : V2Page
{
    protected readonly ModernDatePicker From = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd-MMM-yyyy", Value = DateTime.Today.AddMonths(-1) };
    protected readonly ModernDatePicker To = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd-MMM-yyyy", Value = DateTime.Today };
    protected readonly LookupBox Campus = new();
    protected readonly LookupBox Year = new();
    protected readonly LookupBox Class = new();
    protected readonly LookupBox Section = new();
    protected readonly ModernComboBox View = new();
    protected readonly ModernComboBox GroupBy = new();
    protected readonly ModernComboBox PaymentMethod = new();
    private readonly string _procedure;
    private readonly bool _usesDates;
    private readonly bool _usesYear;
    private readonly ModernButton _print;
    private readonly ModernButton _export;
    private readonly Label _summary = new() {
        Dock = DockStyle.Top,
        Height = Math.Max(38, AppMetrics.TextHeight(AppTheme.ButtonFont) + 10),
        Padding = new Padding(8, 6, 8, 4),
        Font = AppTheme.ButtonFont, ForeColor = AppTheme.HeadingText,
        BackColor = AppTheme.SurfaceSecondary, Text = "Run the report to see totals."
    };
    private FormValidator? _rangeValidator;
    private DataTable? _displayed;
    private string _appliedSignature = string.Empty;
    private string _filterSummary = string.Empty;
    private readonly string _scopePreferenceKey;
    private bool _scopeEdited;
    private bool _applyingScope;
    private bool _runningReport;
    protected CanonicalFinanceReportPage(
        string title,
        string description,
        string procedure,
        string exportName,
        string[] views,
        string[] groupings,
        bool usesDates,
        bool usesYear,
        bool paymentMethod,
        bool grouping,
        bool classOutstandingPrint = false)
        : base(title, description)
    {
        _procedure = procedure;
        _usesDates = usesDates;
        _usesYear = usesYear;
        _scopePreferenceKey = $"finance.{exportName}.last-scope";
        if (usesDates)
        {
            From.ShowCheckBox = To.ShowCheckBox = true;
            From.Checked = To.Checked = false;
        }
        View.Items.AddRange(views);
        View.SelectedIndex = 0;
        GroupBy.Items.AddRange(groupings);
        if (GroupBy.Items.Count > 0) GroupBy.SelectedIndex = 0;
        PaymentMethod.Items.AddRange(new object[] { "All", "Cash", "Bank", "Card", "Online", "Other", "Refund", "Correction" });
        PaymentMethod.SelectedIndex = 0;
        FinanceHelp.Attach(this,PaymentMethod,"Filter the recorded payment method or adjustment type. Correction means a reduction in the recorded payment, rather than a change to the original fee.","A Rs. 1,000 payment reduced to Rs. 800 has a Rs. 200 payment reduction. It is separate from changing the fee itself.");
        ReportUi.RegisterChoiceFilter(PaymentMethod);
        var run = AddBusyAction("Run Report", RunAsync, "Primary");
        _print = AddBusyAction(UiText.Actions.Print, PrintAsync, "Secondary");
        FinanceHelp.Attach(this,_print,"Print the report already generated on screen. It does not receive money or change student fees.","Printing a report with Rs. 1,000 outstanding leaves the student's outstanding balance at Rs. 1,000.");
        _export = AddExportAction(exportName);
        var reportActions = new List<ModernButton> { run, _print };
        if (classOutstandingPrint)
        {
            reportActions.Add(AddBusyAction("Class / Section A4", PreviewClassOutstandingAsync, "Secondary"));
        }
        reportActions.Add(_export);
        RelocateFromActions(reportActions.ToArray());
        Defer(BindLookupsAsync);
        var fields = new List<(string, Control)> { ("View", View) };
        if (usesDates) { fields.Add(("From (optional)", From)); fields.Add(("To (optional)", To)); }
        if (usesYear) fields.Add(("Academic Year", Year));
        fields.Add(("Campus", Campus));
        fields.Add(("Class", Class));
        fields.Add(("Section", Section));
        if (paymentMethod) fields.Add(("Payment Method", PaymentMethod));
        if (grouping) fields.Add(("Group Summary By", GroupBy));
        UiHelper.SetGridPlaceholderMessage(Grid, UiText.EmptyGrid.RunReport);
        SetContent(ReportUi.CompactLayout(ReportUi.Bar(fields.ToArray()), Grid, title, () => {
            _scopeEdited = true;
            ReportLayoutStore.SetPreference(_scopePreferenceKey, "");
            _displayed = null;
            _appliedSignature = _filterSummary = string.Empty;
            _print.Enabled = _export.Enabled = false;
            _summary.Text = "Run the report to see totals.";
            InvalidateReport();
            SetStatus("Filters cleared. Choose filters, then press Run Report.");
        }, reportActions.ToArray()));
        Grid.Parent?.Controls.Add(_summary);
        Grid.BringToFront();
        View.SelectedIndexChanged += (_, _) => { UpdateViewControls(); FiltersChanged(); };
        GroupBy.SelectedIndexChanged += (_, _) => FiltersChanged();
        PaymentMethod.SelectedIndexChanged += (_, _) => FiltersChanged();
        From.ValueChanged += (_, _) => FiltersChanged();
        To.ValueChanged += (_, _) => FiltersChanged();
        foreach (var lookup in new[] { Campus, Year, Class, Section })
            lookup.SelectedIndexChanged += (_, _) => {
                if (!_applyingScope) _scopeEdited = true;
                FiltersChanged();
            };
        UpdateViewControls();
    }
    protected virtual Task PreviewClassOutstandingAsync() => Task.CompletedTask;
    protected override void OnWorkspaceStateRestoring() => _scopeEdited = true;
    protected abstract IEnumerable<SqlParameter> BuildParameters();
    protected virtual bool IsSummaryView => false;
    protected string ViewName => View.Text;
    protected string GroupName => GroupBy.Text;
    protected string MethodName => PaymentMethod.Text == "All" ? string.Empty : PaymentMethod.Text;
    private async Task BindLookupsAsync()
    {
        try
        {
            Task<DataTable> campusesTask = DatabaseHelper.QueryUserAsync("GetCampuses");
            Task<DataTable> yearsTask = DatabaseHelper.QueryUserAsync("GetAcademicYears");
            Task<DataTable> classesTask = DatabaseHelper.QueryUserAsync("GetClasses");
            Task<DataTable> sectionsTask = ReportUi.SectionFilterListAsync();
            await Task.WhenAll(campusesTask, yearsTask, classesTask, sectionsTask);
            if (IsDisposed) return;
            _applyingScope = true;
            try
            {
                Campus.Bind(await campusesTask, "CampusId", "CampusName", "All");
                Year.Bind(await yearsTask, "AcademicYearId", "YearName", "All");
                Class.Bind(await classesTask, "ClassId", "ClassName", "All");
                Section.Bind(await sectionsTask, "SectionId", "SectionName", "All");
                if (!_scopeEdited && _appliedSignature.Length == 0) RestoreLastScope();
            }
            finally { _applyingScope = false; }
        }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
    }
    private ReportScopeSelection CurrentScope() => new(Campus.SelectedId, _usesYear ? Year.SelectedId : 0, Class.SelectedId, Section.SelectedId);
    private void RestoreLastScope()
    {
        var saved = ReportScopeSelection.Read(ReportLayoutStore.GetPreference(_scopePreferenceKey));
        if (saved is null) return;
        void Select(LookupBox box, int id)
        {
            if (id > 0) box.SelectedValue = id;
            if (box.SelectedId <= 0) box.SelectedIndex = 0;
        }
        Select(Campus, saved.Campus);
        if (_usesYear) Select(Year, saved.Year);
        Select(Class, saved.Class);
        Select(Section, saved.Section);
        bool matches = saved == CurrentScope();
        SetStatus(matches ? "Last location filters restored. Review them, then press Run Report."
            : "Some saved filters are unavailable. Review them before running the report.", !matches);
    }
    private void UpdateViewControls()
    {
        GroupBy.Enabled = IsSummaryView;
    }
    private string Signature() => string.Join('|', ViewName, GroupName, MethodName,
        $"{From.Checked}:{From.Value.Date:yyyy-MM-dd}", $"{To.Checked}:{To.Value.Date:yyyy-MM-dd}",
        Campus.SelectedId, Year.SelectedId, Class.SelectedId, Section.SelectedId);
    protected override string ReportFilterSignature() => Signature();
    protected override string ReportExportSubtitle() =>
        _filterSummary.Length == 0 ? base.ReportExportSubtitle() : _filterSummary;
    private void FiltersChanged()
    {
        if (_runningReport)
        {
            CancelPendingReport();
            return;
        }
        if (_appliedSignature.Length == 0) return;
        bool current = string.Equals(_appliedSignature, Signature(), StringComparison.Ordinal);
        _print.Enabled = current;
        _export.Enabled = current;
        if (current) RestoreReportStale(); else MarkReportStale();
        SetStatus(current ? UiText.Reports.MatchesFilters : UiText.Reports.FiltersChanged, !current);
    }
    private string FiltersText()
    {
        string Pick(LookupBox box, string label) => $"{label}: {(box.SelectedId > 0 ? box.Text : "All")}";
        var parts = new List<string> { $"View: {ViewName}" };
        if (_usesDates) parts.Add($"Period: {(From.Checked ? From.Value.ToString("dd-MMM-yyyy") : "Any start")} to {(To.Checked ? To.Value.ToString("dd-MMM-yyyy") : "Any end")}");
        if (_usesYear) parts.Add(Pick(Year, "Year"));
        parts.Add(Pick(Campus, "Campus"));
        parts.Add(Pick(Class, "Class"));
        parts.Add(Pick(Section, "Section"));
        if (PaymentMethod.Enabled && MethodName.Length > 0) parts.Add($"Method: {MethodName}");
        if (IsSummaryView) parts.Add($"Grouped by: {GroupName}");
        return string.Join("   ", parts);
    }
    private async Task RunAsync()
    {
        _scopeEdited = true;
        string requested = Signature();
        ReportScopeSelection scopeToRemember = CurrentScope();
        try
        {
            if (_usesDates && !ReportUi.ValidDateRange(_rangeValidator ??= CreateValidator(), From, To)) return;
            _runningReport = true;
            _print.Enabled = _export.Enabled = false;
            _summary.Text = "Generating report…";
            SqlParameter[] parameters = BuildParameters().ToArray();
            DataTable? captured = null;
            DataTable? totals = null;
            string summary = FiltersText();
            await BindReportAsync(async token => {
                DataSet result = await DatabaseHelper.QueryUserSetAsync(_procedure, token, parameters).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                captured = result.Tables.Count > 0 ? result.Tables[0] : new DataTable();
                totals = result.Tables.Count > 1 ? result.Tables[1] : null;
                // Outstanding totals already appear above the grid. Keep its rows
                // limited to actual records so pagination does not count a total.
                if (_procedure != "GetOutstandingAndAgingReport" && _procedure != "GetCollectionsAndReceiptsReport")
                    AppendAuthoritativeTotalRow(captured, totals);
                return captured;
            });
            if (!ReportLanded || captured is null)
            {
                if (!IsDisposed) _summary.Text = "Run the report again to see current totals.";
                return;
            }
            // Pager rows are detached copies. Keep the private full result for
            // print eligibility without duplicating every financial row.
            _displayed = captured;
            string totalsText = FormatTotals(totals);
            _summary.Text = totalsText;
            _filterSummary = summary + "   " + totalsText;
            _appliedSignature = requested;
            bool current = ReportMatchesCurrentFilters(requested);
            if (current) ReportLayoutStore.SetPreference(_scopePreferenceKey, scopeToRemember.Serialize());
            _print.Enabled = current;
            _export.Enabled = current;
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                _displayed = null;
                _appliedSignature = _filterSummary = string.Empty;
                _print.Enabled = _export.Enabled = false;
                _summary.Text = "Could not generate the report. Run Report to retry.";
                Fail(ex);
            }
        }
        finally { _runningReport = false; }
    }
    private static string FormatTotals(DataTable? totals)
    {
        if (totals is null || totals.Rows.Count == 0) return "No matching financial activity.";
        DataRow row = totals.Rows[0];
        return string.Join("     ", totals.Columns.Cast<DataColumn>().Select(column => {
            object value = row[column];
            string shown = value == DBNull.Value ? "0" : value is decimal money
                ? $"Rs. {money:N0}" : Convert.ToString(value) ?? "0";
            return $"{column.ColumnName}: {shown}";
        }));
    }
    private static void AppendAuthoritativeTotalRow(DataTable data, DataTable? totals)
    {
        if (data.Columns.Count == 0 || totals is null || totals.Rows.Count == 0) return;
        DataRow source = totals.Rows[0];
        DataRow total = data.NewRow();
        DataColumn? label = data.Columns.Cast<DataColumn>().FirstOrDefault(c => c.DataType == typeof(string));
        if (label is not null) total[label] = "TOTAL";
        var aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
            ["Gross Received"] = new[] { "Gross Received" }, ["Refunds"] = new[] { "Refunds" },
            ["Corrections"] = new[] { "Corrections" }, ["Net Collection"] = new[] { "Net Collection", "Net Amount", "Amount" },
            ["Receipts"] = new[] { "Receipts" }, ["Gross Billed"] = new[] { "Gross Charges", "Gross Amount" },
            ["Discounts"] = new[] { "Discounts Applied", "Discount", "Applied Discount" },
            ["Net Billed"] = new[] { "Net Charges", "Net Charge" },
            ["Total Outstanding"] = new[] { "Outstanding" }, ["Students Owing"] = new[] { "Students" }
        };
        bool wroteValue = false;
        foreach (DataColumn metric in totals.Columns)
        {
            if (!aliases.TryGetValue(metric.ColumnName, out string[]? candidates)) continue;
            DataColumn? destination = candidates.Select(name => data.Columns[name]).FirstOrDefault(c => c is not null);
            if (destination is null || source[metric] == DBNull.Value) continue;
            total[destination] = source[metric];
            wroteValue = true;
        }
        if (wroteValue) data.Rows.Add(total);
    }
    private async Task PrintAsync()
    {
        try
        {
            if (_displayed is null) { Warn("Run the report first."); return; }
            if (!string.Equals(_appliedSignature, Signature(), StringComparison.Ordinal)) { Warn("Filters changed. Run the report again before printing."); return; }
            if (_displayed.Rows.Count == 0) { Warn("No records match these filters."); return; }
            await PreviewReportAsync();
        }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
    }
}
