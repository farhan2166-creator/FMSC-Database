namespace FMSC_SMS.Core;

internal static class FeeChargeCancellation
{
    // Cancelling a debt is an adjustment, even when initiated from collection.
    internal static bool CanCancel => CurrentUser.HasPermission("Fees.Collect")
        && CurrentUser.HasPermission("Fees.Adjust");
}
