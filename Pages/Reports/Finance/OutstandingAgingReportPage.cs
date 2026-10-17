using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Reports.Finance;
public sealed class OutstandingAgingReportPage : CanonicalFinanceReportPage
{
    public OutstandingAgingReportPage() : base(
        "Outstanding & Aging", "Review unpaid charges, outstanding balances and how long they have been due.",
        "GetOutstandingAndAgingReport", "FMSC_Outstanding_Aging",
        new[] { "Students", "Charges", "Aging", "Families" },
        Array.Empty<string>(), false, true, false, false, classOutstandingPrint: true)
    { }
    protected override IEnumerable<SqlParameter> BuildParameters()
    {
        yield return DatabaseHelper.P("@View", ViewName);
        yield return DatabaseHelper.Int("@CampusId", Campus.SelectedId);
        yield return DatabaseHelper.Int("@AcademicYearId", Year.SelectedId);
        yield return DatabaseHelper.Int("@ClassId", Class.SelectedId);
        yield return DatabaseHelper.Int("@SectionId", Section.SelectedId);
        yield return DatabaseHelper.Date("@AsOfDate", DateTime.Today);
    }
    protected override async Task PreviewFeeRemindersAsync()
    {
        if (!CurrentUser.HasPermission("Reports.View")) return;
        var scope = (Campus: Campus.SelectedId, Year: Year.SelectedId, Class: Class.SelectedId, Section: Section.SelectedId);
        string Pick(LookupBox box) => box.SelectedId > 0 ? box.Text : "All";
        string covers = $"Campus: {Pick(Campus)}; Academic year: {Pick(Year)}; Class: {Pick(Class)}; Section: {Pick(Section)}";
        bool ScopeMatches() => scope == (Campus.SelectedId, Year.SelectedId, Class.SelectedId, Section.SelectedId);
        Task<DataTable> ReadBalances() => Task.Run(() => DatabaseHelper.QuerySql(ClassOutstandingReportData.Query,
            DatabaseHelper.Int("@UserId", CurrentUser.UserId), DatabaseHelper.Int("@CampusId", scope.Campus),
            DatabaseHelper.Int("@AcademicYearId", scope.Year), DatabaseHelper.Int("@ClassId", scope.Class),
            DatabaseHelper.Int("@SectionId", scope.Section)));
        try
        {
            var balances = await ReadBalances();
            if (IsDisposed) return;
            if (!ScopeMatches()) { Warn("Filters changed. Open Fee Reminders again for the selected filters."); return; }
            if (balances.Rows.Count == 0) { Warn("No outstanding charges were found for the selected filters."); return; }
            using var selection = new FeeReminderSelectionDialog(balances, covers);
            if (selection.ShowDialog(FindForm()) != DialogResult.OK) return;
            var selected = selection.SelectedStudents;
            var requestedDate = selection.RequestedDate;
            string message = selection.Message;
            bool printOnA4 = selection.PrintOnA4;
            var currentBalances = await ReadBalances();
            var school = await InstitutionProfile.GetPrintIdentityAsync();
            if (IsDisposed) return;
            if (!ScopeMatches()) { Warn("Filters changed. Open Fee Reminders again for the selected filters."); return; }
            if (string.IsNullOrWhiteSpace(school.Name)) { Warn("Check School Profile before printing fee reminders."); return; }
            var data = FeeReminderReportData.Create(currentBalances, selected, school.Name, school.ContactNumber,
                school.Address, covers, DateTime.Now, requestedDate, message);
            if (data.Rows.Count == 0) { Warn("The selected students no longer have outstanding charges for these filters."); return; }
            Info(selected.Count == data.Rows.Count ? $"{data.Rows.Count} fee reminders ready with refreshed balances."
                : $"{data.Rows.Count} fee reminders ready. Students with no remaining balance were excluded.");
            string path = Path.Combine(AppContext.BaseDirectory, "Reports", "FeeReminder.rdlc");
            RdlcReportPreview.Show(FindForm(), printOnA4 ? "Fee Reminders — A4" : "Fee Reminders — Thermal", path, "FeeReceiptData", data,
                fitPageWidth: printOnA4, reportDefinition: printOnA4 ? FeeReminderPrintLayout.A4Definition(path)
                    : ReceiptReportLayout.Definition(path, data, splitFeePeriod: false));
        }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
    }

    protected override async Task PreviewClassOutstandingAsync()
    {
        if (!CurrentUser.HasPermission("Reports.View")) return;
        var scope = (Campus: Campus.SelectedId, Year: Year.SelectedId, Class: Class.SelectedId, Section: Section.SelectedId);
        string yearSelection = scope.Year == 0 ? "All academic years" : Year.Text;
        DateTime generated = DateTime.Now;
        try
        {
            var students = await Task.Run(() => DatabaseHelper.QuerySql(ClassOutstandingReportData.Query,
                DatabaseHelper.Int("@UserId", CurrentUser.UserId), DatabaseHelper.Int("@CampusId", scope.Campus),
                DatabaseHelper.Int("@AcademicYearId", scope.Year), DatabaseHelper.Int("@ClassId", scope.Class),
                DatabaseHelper.Int("@SectionId", scope.Section)));
            var school = await InstitutionProfile.GetNameAsync();
            if (IsDisposed) return;
            if (scope != (Campus.SelectedId, Year.SelectedId, Class.SelectedId, Section.SelectedId))
            { Warn("Filters changed. Open Class / Section A4 again for the selected filters."); return; }
            if (students.Rows.Count == 0) { Warn("No outstanding charges were found for the selected filters."); return; }
            if (string.IsNullOrWhiteSpace(school)) { Warn("Check School Profile before printing outstanding charges."); return; }
            var data = ClassOutstandingReportData.Create(students, school, yearSelection, generated);
            RdlcReportPreview.Show(FindForm(), "Outstanding Charges by Class and Section",
                Path.Combine(AppContext.BaseDirectory, "Reports", "ClassOutstandingCharges.rdlc"), "ClassOutstandingData", data);
        }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
    }

}
