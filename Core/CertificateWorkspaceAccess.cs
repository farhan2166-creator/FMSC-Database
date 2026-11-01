namespace FMSC_SMS.Core;

internal static class CertificateWorkspaceAccess
{
    // The shared student picker and certificate detail lookups require Students.View.
    // SQL separately authorizes ordinary certificates and leaving certificates.
    internal static bool CanOpenGeneral(Func<string, bool> hasPermission) =>
        hasPermission("Students.View") && hasPermission("Certificates.Create");

    internal static bool CanOpenLeaving(Func<string, bool> hasPermission) =>
        hasPermission("Students.View") && hasPermission("Students.Leave");

    internal static bool CanOpenHub(Func<string, bool> hasPermission) =>
        CanOpenGeneral(hasPermission) || CanOpenLeaving(hasPermission);

    internal static bool CanOpenCard(bool leaving, Func<string, bool> hasPermission) =>
        leaving ? CanOpenLeaving(hasPermission) : CanOpenGeneral(hasPermission);
}
