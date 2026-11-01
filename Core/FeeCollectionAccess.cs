namespace FMSC_SMS.Core;

internal static class FeeCollectionAccess
{
    // Student search requires Students.View, balances require Fees.View,
    // and settlement requires Fees.Collect. Do not grant missing rights implicitly.
    internal static bool CanUse(Func<string,bool> hasPermission) =>
        hasPermission("Fees.Collect") && hasPermission("Fees.View") &&
        hasPermission("Students.View");
}
