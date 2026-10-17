namespace FMSC_SMS.Pages.Reports.Students;
/// <summary>Kept so existing links to "Student Strength Report" still open somewhere sensible: it is the single <see cref="StudentReportsPage"/> started on its matching preset.</summary>
public sealed class StudentStrengthReportPage : StudentReportsPage
{
    public StudentStrengthReportPage()
        : base(ModeStrength, "Student Strength Report", string.Empty)
    {
    }
}
