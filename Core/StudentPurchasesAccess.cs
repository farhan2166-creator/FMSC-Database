namespace FMSC_SMS.Core;

internal static class StudentPurchasesAccess
{
    // Student picker and enrollment context require Students.View;
    // charge history/options require Fees.View; posting requires Fees.Collect.
    // SQL remains authoritative and missing grants are never added automatically.
    internal static bool CanUse(Func<string, bool> hasPermission) =>
        hasPermission("Fees.Collect") &&
        hasPermission("Fees.View") &&
        hasPermission("Students.View");
}
