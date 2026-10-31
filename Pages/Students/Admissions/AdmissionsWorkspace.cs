namespace FMSC_SMS.Pages.Students.Admissions;

/// <summary>Admission entry point for direct admission, pending registration and sibling review.</summary>
public static class AdmissionsWorkspace
{
    public static Control Create(Action<Func<Control>, string, string> navigate)
        => new StudentAdmissionPage(navigate);
}
