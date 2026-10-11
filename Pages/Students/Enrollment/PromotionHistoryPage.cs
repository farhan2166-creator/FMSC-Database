using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports;
namespace FMSC_SMS.Pages.Students.Enrollment;
public sealed class PromotionHistoryPage : V2Page
{
    private readonly LookupBox year = new();
    private readonly LookupBox cls = new();
    private readonly LookupBox section = new();
    private readonly ModernButton exportButton;
    /* The filter values that produced the rows currently in the grid. Empty until a load has actually produced a batch history. */
    private string _appliedFilterSignature = string.Empty;
    private readonly Label summary = new() {
        Dock = DockStyle.Top,
        AutoSize = false,
        Height = Math.Max(24, AppMetrics.TextHeight(AppTheme.NormalFont)),
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText,
        TextAlign = ContentAlignment.MiddleLeft
    };
    public PromotionHistoryPage()
        : base("Promotion History", "Review completed promotions and the students included.")
    {
        var load = AddAction("Load", (_, _) => LoadData(), "Secondary");
        var export = AddExportAction("Promotion_Batch_History");
        var clear = AddAction(UiText.Actions.Clear, (_, _) => ClearForm(), "Secondary");
        RelocateFromActions(load, export, clear);
        var filters = ReportUi.Bar(("Source Academic Year", year), ("Source Class", cls), ("Source Section", section));
        var filterCard = HumanUi.Card("Filter", "Leave filters blank to view all completed promotions.");
        filterCard.Content.Controls.Add(filters);
        var historyCard = HumanUi.Card(
            "Batch History",
            "See the previous class, next class, promotion date and student totals.");
        historyCard.Content.Controls.Add(Grid);
        historyCard.Content.Controls.Add(summary);
        /* Load, Export and Clear were removed from the action bar and never placed, so the page had no buttons. */
        var buttonRow = HumanUi.ButtonRow(load, export, clear);
        buttonRow.Dock = DockStyle.Fill;
        buttonRow.Margin = new Padding(0);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(1)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(filterCard, 0, 0);
        root.Controls.Add(buttonRow, 0, 1);
        root.Controls.Add(historyCard, 0, 2);
        SetContent(root);
        exportButton = export;
        /* The grid keeps showing the batches an earlier load produced, so a filter change has to be recorded here. */
        foreach (var filter in new[] { year, cls, section })
            filter.SelectedIndexChanged += (_, _) => OnFiltersChanged();
        Defer(() => {
            BindLookups();
            LoadData();
        });
    }
    private string FilterSignature() =>
        $"{year.SelectedId}|{cls.SelectedId}|{section.SelectedId}";
    private void SetReportActionsEnabled(bool enabled)
    {
        exportButton.Enabled = enabled;
    }
    /* report the displayed batch history as stale the moment the filters no longer match the values that produced it. */
    private void OnFiltersChanged()
    {
        if (string.IsNullOrEmpty(_appliedFilterSignature))
            return;
        bool current = string.Equals(_appliedFilterSignature, FilterSignature(), StringComparison.Ordinal);
        SetReportActionsEnabled(current);
        if (current)
            SetStatus("Batch history matches the current filters.");
        else
            SetStatus("Filters changed. Use Load again before exporting.", true);
    }
    private void BindLookups()
    {
        try
        {
            year.Bind(DatabaseHelper.QueryUser("GetAcademicYears"), "AcademicYearId", "YearName");
            cls.Bind(DatabaseHelper.QueryUser("GetClasses"), "ClassId", "ClassName");
            section.Bind(ReportUi.SectionFilterList(), "SectionId", "SectionName");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    private void LoadData()
    {
        try
        {
            var data = DatabaseHelper.QueryUser(
                "GetPromotionBatchesFiltered",
                DatabaseHelper.Int("@SourceAcademicYearId", year.SelectedId),
                DatabaseHelper.Int("@SourceClassId", cls.SelectedId),
                DatabaseHelper.Int("@SourceSectionId", section.SelectedId));
            Bind(data);
            UpdateSummary(data);
            /* The load is synchronous, so the filters cannot move while the query runs: a successful load belongs to the filters now selected. */
            _appliedFilterSignature = FilterSignature();
            SetReportActionsEnabled(true);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    // Summarizes count columns across the displayed promotion batches.
    private void UpdateSummary(DataTable data)
    {
        var countColumns = data.Columns.Cast<DataColumn>()
            .Where(c =>
                c.ColumnName.EndsWith("Count", StringComparison.OrdinalIgnoreCase) &&
                (c.DataType == typeof(int) || c.DataType == typeof(long) || c.DataType == typeof(short)))
            .ToList();
        if (data.Rows.Count == 0)
        {
            summary.Text = "No batches match the current filter.";
            return;
        }
        string countsText = string.Join(
            "   ",
            countColumns.Select(c =>
                $"{c.ColumnName}: {data.AsEnumerable().Sum(r => r[c] == DBNull.Value ? 0 : Convert.ToInt64(r[c])):N0}"));
        summary.Text = $"{data.Rows.Count:N0} batch(es).   {countsText}";
    }
    private void ClearForm()
    {
        year.SelectedIndex = -1;
        cls.SelectedIndex = -1;
        section.SelectedIndex = -1;
        Grid.DataSource = null;
        summary.Text = "";
        /* The displayed history is gone, so Export must not be able to re-export the cleared batch list as if it were the current one. */
        _appliedFilterSignature = string.Empty;
        SetReportActionsEnabled(false);
        SetStatus("Promotion history filter cleared.");
    }
}
