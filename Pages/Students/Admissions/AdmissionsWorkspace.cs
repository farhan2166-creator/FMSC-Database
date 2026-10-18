using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Maintenance;

namespace FMSC_SMS.Pages.Students.Admissions;

/// <summary>One admission entry point, organized by the child's operational state.</summary>
public static class AdmissionsWorkspace
{
    public static Control Create(Action<Func<Control>, string, string> navigate)
    {
        var pages = new List<(string Caption, Func<Control> Factory)> {
            ("Admit / Edit Student", () => new StudentAdmissionPage(navigate))
        };
        if (QuickRegistrationAccess.CanUse(CurrentUser.HasPermission))
            pages.Add(("Registration & Pending", () => new QuickRegistrationPage(navigate)));
        if (CurrentUser.HasPermission("Students.Administer") && CurrentUser.HasPermission("Students.View"))
            pages.Add(("Existing Student Setup", () => new AdmissionReviewListPage(navigate, false)));
        return new WorkspacePage(PageTitles.Admissions,
            "Admit a new child, finish a pending registration, or explicitly start billing for an existing record.", pages.ToArray());
    }
}
