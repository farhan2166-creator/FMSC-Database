namespace FMSC_SMS.Pages.Reports.Students;
/// <summary>Kept so existing links to "Current Students Report" still open somewhere sensible: it is the single <see cref="StudentReportsPage"/> started on its matching preset.</summary>
public sealed class CurrentStudentsReportPage : StudentReportsPage
{
    public CurrentStudentsReportPage()
        : base(ModeList, "Current Students Report", string.Empty)
    {
    }
}
