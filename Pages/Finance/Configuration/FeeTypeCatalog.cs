using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Finance.Configuration;
/// <summary>Announces that the list of fee types has changed, so screens that loaded it once (the Configuration tab's fee-type drop-down, Apply To Existing Students) can read it again.</summary>
public static class FeeTypeCatalog
{
    public static event Action? Changed;
    public static void NotifyChanged()
    {
        Action? handlers = Changed;
        if (handlers is null)
            return;
        /* One screen failing to refresh must not stop the others, and must not turn a save that already committed into an error message. */
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception ex)
            {
                AppExceptionHandler.LogNonFatal(
                    ex,
                    "Refreshing a fee screen after the fee types changed");
            }
        }
    }
}
