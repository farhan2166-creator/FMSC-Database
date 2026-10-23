using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Reports.Finance;
public sealed class AnnualFeeReceiptsPage : V2Page, IBusyPage
{
    private readonly int initialAcademicYearId;
    private readonly StudentPicker student = new();
    private readonly LookupBox year = new();
    private bool suppressStudentEvents;
    /* View / Print Ledger reads the selected student and the selected academic year, so its availability is derived from those two selections instead of being left permanently enabled. */
    private ModernButton? ledgerAction;
    public bool IsBusy=>BusyOperationRunning;
    public string BusyMessage=>"Annual receipts are being prepared. Wait for the preview before changing the student or year.";
    public AnnualFeeReceiptsPage() : this(true) { }
    internal AnnualFeeReceiptsPage(bool initialize)
        : base(
            PageTitles.AnnualFeeReceipts,
            "Print a student’s receipts for an academic year, oldest first.")
    {
        var view = AddBusyAction("View / Print Receipts", ViewLedgerAsync, "Primary");
        RelocateFromActions(view);
        ledgerAction = view;
        // The page opens with neither a student nor a year chosen, so the one action it has starts disabled.
        ApplyActionAvailability();
        if(initialize)Defer(BindYearsAsync);
        /* The shared student finder (StudentPicker): type in the box and the list narrows on every keystroke. */
        if(initialize)StudentSearchCache.Warm();
        /* The box no longer names the student the page was pointed at (edited or cleared), so the action is re-derived. */
        student.SelectionCleared += (_, _) => {
            if (suppressStudentEvents)
                return;
            ApplyActionAvailability();
        };
        TrackStudentSearchStatus(student);
        /* A student has just been chosen, so the action derived from that selection is re-derived. */
        student.UserSelectionChanged += (_, _) => ApplyActionAvailability();
        year.UserSelectionChanged += (_, _) => ApplyActionAvailability();
        var selector = HumanUi.Card(
            "Student and Year",
            "Choose a student and year to print compact receipts in two columns per page.");
        int fieldRowHeight = AppMetrics.FieldRowHeight;
        AppMetrics.FitButtonWidth(view,minWidth:170);
        var fieldsTable = new TableLayoutPanel {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 1,
            Height = fieldRowHeight,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        fieldsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,75));
        fieldsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
        fieldsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,view.Width+AppMetrics.SpacingSm));
        fieldsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldRowHeight));
        fieldsTable.Controls.Add(HumanUi.Field("Student", student), 0, 0);
        fieldsTable.Controls.Add(HumanUi.Field("Academic Year", year), 1, 0);
        foreach(var field in fieldsTable.Controls.OfType<TableLayoutPanel>())
            field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var buttonCell=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty,
            Padding=new Padding(0,Math.Max(16,AppMetrics.TextHeight(AppTheme.FieldCaptionFont)),AppMetrics.SpacingSm,AppMetrics.SpacingSm) };
        view.Dock=DockStyle.Top;view.Margin=Padding.Empty;
        buttonCell.Controls.Add(view);
        fieldsTable.Controls.Add(buttonCell, 2, 0);
        selector.Content.Controls.Add(fieldsTable);
        var tip = HumanUi.Card(
            "Tip",
            "Receipts are ordered oldest to newest and kept together wherever the page has room.");
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(1)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(selector, 0, 0);
        root.Controls.Add(tip, 0, 1);
        SetContent(root);
    }
    // Mirrors FeeCollectionPage's existing preloadAdmissionId constructor (same pattern, not a new one) - Defer + a dedicated load method that reuses the same GetStudents/FilterStudentsByAdmissionIdOrName/ BindStudentTable path SearchStudentsAsync already uses.
    public AnnualFeeReceiptsPage(int preloadAdmissionId, int preloadAcademicYearId = 0)
        : this()
    {
        initialAcademicYearId = preloadAcademicYearId;
        if (preloadAdmissionId > 0)
        {
            Defer(() => LoadStudentByAdmissionIdAsync(preloadAdmissionId));
        }
    }
    private async Task LoadStudentByAdmissionIdAsync(int admissionId)
    {
        try
        {
            DataRow? found = FindCachedStudent(await StudentSearchCache.GetAllAsync(), admissionId);
            if (found is null)
            {
                /* Admitted since the list was read. */
                StudentSearchCache.Invalidate();
                found = FindCachedStudent(await StudentSearchCache.GetAllAsync(), admissionId);
            }
            if (IsDisposed || found is null)
                return;
            suppressStudentEvents = true;
            try
            {
                student.SetStudent(found);
            }
            finally
            {
                suppressStudentEvents = false;
            }
            /* The preloaded student was chosen programmatically, which raises no UserSelectionChanged, so the action's state is derived after the selection is in place. */
            ApplyActionAvailability();
        }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
    }
    private static DataRow? FindCachedStudent(DataTable all, int admissionId)
    {
        foreach (DataRow row in all.Rows)
        {
            if (all.Columns.Contains("AdmissionId") &&
                row["AdmissionId"] != DBNull.Value &&
                Convert.ToInt32(row["AdmissionId"]) == admissionId)
            {
                return row;
            }
        }
        return null;
    }
    private async Task BindYearsAsync()
    {
        try
        {
            var raw = await DatabaseHelper.QueryUserAsync("GetAcademicYears");
            if (!IsDisposed)
            {
                year.Bind(raw, "AcademicYearId", "YearName");
                /* The current year is what is asked for nearly every time; leaving it on "-- Select --" cost a click on every use. */
                if (initialAcademicYearId > 0)
                {
                    if (raw.AsEnumerable().Any(row => Convert.ToInt32(row["AcademicYearId"]) == initialAcademicYearId))
                        year.SelectedValue = initialAcademicYearId;
                    else
                        SetStatus("The statement's academic year is no longer available. Choose a year for receipts.", PageStatusKind.Warning);
                }
                else HumanUi.SelectCurrentAcademicYear(year, raw);
                /* Bind selects the blank "-- Select --" row, so the year half of the action's rule is re-derived against the list that was just put in place. */
                ApplyActionAvailability();
            }
        }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
    }
    private Task ViewLedgerAsync()=>RunBusyAsync(BusyMessage,ViewLedgerCoreAsync,this);
    private async Task ViewLedgerCoreAsync()
    {
        if (!CurrentUser.HasPermission("Fees.Statement.View")) return;
        try
        {
            if (student.SelectedId <= 0)
            {
                Warn(UiText.Prompts.SelectStudent);
                return;
            }
            if (year.SelectedId <= 0)
            {
                Warn("Select an academic year first.");
                return;
            }
            int admissionId = student.SelectedId;
            int academicYearId = year.SelectedId;
            bool Current()=>!IsDisposed&&student.SelectedId==admissionId&&year.SelectedId==academicYearId;
            await AnnualReceiptPreview.ShowAsync(FindForm(),admissionId,academicYearId,Current,Warn);
        }
        catch (Exception ex) { if(!IsDisposed)Fail(ex); }
    }
    /// <summary>derives the enabled state of View / Print Ledger from the two selections ViewLedgerAsync itself requires at the top of its handler: a student (student.SelectedId) and an academic year (year.SelectedId).</summary>
    private void ApplyActionAvailability()
    {
        if (ledgerAction is null)
            return;
        // Zero is what "not chosen" means for both pickers: LookupBox.Bind leaves its blank "-- Select --" row selected, and ClearLookup leaves nothing.
        bool hasStudent = student.SelectedId > 0;
        bool hasYear = year.SelectedId > 0;
        ledgerAction.Visible = CurrentUser.HasPermission("Fees.Statement.View");
        ledgerAction.Enabled = hasStudent && hasYear && ledgerAction.Visible;
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}
