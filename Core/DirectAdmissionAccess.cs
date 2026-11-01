namespace FMSC_SMS.Core;

internal static class DirectAdmissionAccess
{
    // SQL admits with Students.Admit, but safe direct-entry also needs the
    // Students.View reader for existing-student search and duplicate review.
    internal static bool CanCreate(Func<string,bool> hasPermission) =>
        hasPermission("Students.Admit") && hasPermission("Students.View");
}
