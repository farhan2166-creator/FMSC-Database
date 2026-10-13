namespace FMSC_SMS.Core;

internal static class QuickRegistrationAccess
{
    // SQL's detail/list queries require View; admission/completion requires Admit.
    // Reflect both requirements without granting either permission implicitly.
    internal static bool CanUse(Func<string,bool> hasPermission) =>
        hasPermission("Students.Admit") && hasPermission("Students.View");
}
