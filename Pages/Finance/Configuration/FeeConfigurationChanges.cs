using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Finance.Configuration;
/// <summary>Same contract as <see cref="FeeTypeCatalog"/>: raised on the UI thread after the write has committed, one failing subscriber does not stop the rest.</summary>
public static class FeeConfigurationChanges
{
    public static event Action? Changed;
    public static void NotifyChanged()
    {
        Action? handlers = Changed;
        if (handlers is null)
            return;
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
                    "Refreshing a fee screen after fee configurations changed");
            }
        }
    }
}
