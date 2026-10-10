using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Reports.Finance;
public sealed class OutstandingAgingReportPage : CanonicalFinanceReportPage
{
    public OutstandingAgingReportPage() : base(
        "Outstanding & Aging", "Review unpaid charges, outstanding balances and how long they have been due.",
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
