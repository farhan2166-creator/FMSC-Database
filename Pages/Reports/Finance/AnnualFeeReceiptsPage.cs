using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Reports.Finance;
public sealed class AnnualFeeReceiptsPage : V2Page, IBusyPage
{
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
        int buttonRowHeight = AppMetrics.ButtonRowHeight;
        var fieldsTable = new TableLayoutPanel {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            Height = fieldRowHeight * 2 + buttonRowHeight,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        fieldsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldRowHeight));
        fieldsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldRowHeight));
        fieldsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, buttonRowHeight));
        fieldsTable.Controls.Add(HumanUi.Field("Student", student), 0, 0);
        fieldsTable.Controls.Add(HumanUi.Field("Academic Year", year), 0, 1);
        var buttonRow = HumanUi.ButtonRow(view);
        buttonRow.Dock = DockStyle.Fill;
        fieldsTable.Controls.Add(buttonRow, 0, 2);
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(2) + buttonRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(selector, 0, 0);
        root.Controls.Add(tip, 0, 1);
        SetContent(root);
    }
    // Mirrors FeeCollectionPage's existing preloadAdmissionId constructor (same pattern, not a new one) - Defer + a dedicated load method that reuses the same GetStudents/FilterStudentsByAdmissionIdOrName/ BindStudentTable path SearchStudentsAsync already uses.
    public AnnualFeeReceiptsPage(int preloadAdmissionId)
        : this()
    {
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
                HumanUi.SelectCurrentAcademicYear(year, raw);
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
            DataTable data = await DatabaseHelper.QueryUserAsync(
                "GetStudentAnnualFeeReceipts",
                DatabaseHelper.Int("@AdmissionId", admissionId),
                DatabaseHelper.Int("@AcademicYearId", academicYearId));
            if(!Current())return;
            if (data.Rows.Count == 0)
            {
                Warn("No paid fee receipts were found for this student in the selected academic year.");
                return;
            }
            var institution = await InstitutionProfile.GetPrintIdentityAsync();
            if(!Current())return;
            if (string.IsNullOrWhiteSpace(institution.Name))
            {
                Warn("Check School Profile before printing receipts.");
                return;
            }
            var receipts = ReceiptReportData.Create();
            int pageKey = 0;
            foreach (var receipt in data.AsEnumerable().GroupBy(row => Convert.ToString(row["ReceiptNumber"]) ?? "")
                .OrderBy(group => Convert.ToDateTime(group.First()["PaymentDate"]))
                .ThenBy(group => Convert.ToInt32(group.First()["FeePaymentId"])))
            {
                if (string.IsNullOrWhiteSpace(receipt.Key)) continue;
                var header = receipt.First();
                var lines = await FeeReceiptPlacement.QueryLinesAsync(receipt.Key,"Fees.Statement.View");
                if(!Current())return;
                foreach (var enrollment in lines.AsEnumerable().Where(row => row.Field<int>("AdmissionId") == admissionId)
                    .GroupBy(row => row.Field<int>("EnrollmentId")))
                {
                    pageKey++;
                    var first = enrollment.First();
                    decimal total = enrollment.Sum(row => Convert.ToDecimal(row["Amount"]));
                    foreach (var line in enrollment)
                    {
                        var row = receipts.Rows.Add(pageKey,institution.Name,"CASH RECEIPT","STUDENT COPY",
                            receipt.Key,first["StudentName"],AdmissionNumbers.ForId(admissionId),
                            $"{first["ClassName"]} - {first["SectionName"]}",
                            Convert.ToDateTime(header["PaymentDate"]).ToString("dd-MMM-yyyy hh:mm tt"),
                            "",line["FeeTypeName"],line["Amount"],0m,total,0m,header["PaymentMethod"],
                            "",false,false,institution.ContactNumber,institution.Address,
                            first["CampusName"],first["FatherName"],
                            line["BillingMonth"] is DBNull ? "" : Convert.ToDateTime(line["BillingMonth"]).ToString("MMM-yyyy",System.Globalization.CultureInfo.InvariantCulture));
                        row["PaymentEnrollmentId"] = enrollment.Key;
                    }
                }
            }
            if (receipts.Rows.Count == 0)
            {
                Warn("No printable receipts were found for this academic year.");
                return;
            }
            await FeeReceiptPlacement.ApplyCurrentBalanceAsync(receipts,"Fees.Statement.View");
            if(!Current())return;
            ReceiptReportData.GroupPeriods(receipts);
            string reportPath = Path.Combine(
                AppContext.BaseDirectory,
                "Reports",
                "FeePaymentReceipt.rdlc");
            if (!File.Exists(reportPath))
            {
                throw new FileNotFoundException(
                    "The annual fee receipts report template was not found.",
                    reportPath);
            }
            RdlcReportPreview.Show(
                FindForm(),
                "Student Annual Fee Receipts",
                reportPath,
                "FeeReceiptData",
                receipts,
                fitPageWidth: true,
                reportDefinition: ReceiptReportLayout.Definition(reportPath,receipts,annualSheet: true));
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
