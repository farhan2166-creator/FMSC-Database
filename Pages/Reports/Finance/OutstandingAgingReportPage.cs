using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Reports.Finance;
public sealed class OutstandingAgingReportPage : CanonicalFinanceReportPage
{
    public OutstandingAgingReportPage() : base(
        "Outstanding & Aging", "Who owes money, which charges make up the balance, and how old the debt is.",
        "GetOutstandingAndAgingReport", "FMSC_Outstanding_Aging",
        new[] { "Students", "Charges", "Aging", "Families" },
        Array.Empty<string>(), false, true, false, false)
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
}
