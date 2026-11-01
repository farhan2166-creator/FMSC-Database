namespace FMSC_SMS.Core;

internal static class FeeCollectionAccess
{
    // SQL requires Fees.View for collection balances and Fees.Collect for settlement.
    // Check both for entry points; never grant a missing permission implicitly.
    internal static bool CanUse(Func<string,bool> hasPermission) =>
        hasPermission("Fees.Collect") && hasPermission("Fees.View");
}
