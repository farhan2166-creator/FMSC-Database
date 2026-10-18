namespace FMSC_SMS.Pages.Students.Admissions;

/// <summary>Direct admission entry point; registration and data review have separate routes.</summary>
public static class AdmissionsWorkspace
{
    public static Control Create(Action<Func<Control>, string, string> navigate)
        => new StudentAdmissionPage(navigate);
}
