namespace FMSC_SMS.Pages.Reports.Students;
/// <summary>Kept so existing links to "Admissions Report" still open somewhere sensible: it is the single <see cref="StudentReportsPage"/> started on its matching preset.</summary>
public sealed class AdmissionsReportPage : StudentReportsPage
{
    public AdmissionsReportPage()
        : base(ModeAdmissions, "Admissions Report", string.Empty)
    {
    }
}
