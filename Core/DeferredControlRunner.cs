namespace FMSC_SMS.Core;

internal static class DeferredControlRunner
{
    internal static void Run(Control owner, Action action, Action<Exception> fail) =>
        Schedule(owner, () => {
            try { action(); }
            catch (Exception ex) { if (!owner.IsDisposed) fail(ex); }
            return Task.CompletedTask;
        });

    internal static void Run(Control owner, Func<Task> action, Action<Exception> fail) =>
        Schedule(owner, async () => {
            try { await action(); }
            catch (Exception ex) { if (!owner.IsDisposed) fail(ex); }
        });

    private static void Schedule(Control owner, Func<Task> action)
    {
        if (owner.IsDisposed)
            return;
        void Start()
        {
            if (!owner.IsDisposed)
                _ = action();
        }
        if (owner.IsHandleCreated)
        {
            owner.BeginInvoke((MethodInvoker)Start);
            return;
        }
        EventHandler? handler = null;
        handler = (_, _) => {
            owner.HandleCreated -= handler;
            if (!owner.IsDisposed)
                owner.BeginInvoke((MethodInvoker)Start);
        };
        owner.HandleCreated += handler;
    }
}
