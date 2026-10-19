using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Reports.Finance;
public sealed class OutstandingAgingReportPage : CanonicalFinanceReportPage
{
    public OutstandingAgingReportPage() : base(
        "Outstanding & Aging", "Open charges only. Settled amounts include cash and credit; fully settled charges are excluded.",
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
