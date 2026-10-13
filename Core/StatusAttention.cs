namespace FMSC_SMS.Core;

internal static class StatusAttention
{
    internal static void Show(Control owner, Label status, ToolTip tip, string message, Action restore)
    {
        if (owner.IsDisposed || status.IsDisposed || !status.IsHandleCreated || !status.Visible) return;
        try { tip.Show(message, status, 0, status.Height + 2, 5000); }
        catch (Exception nonFatalException19)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException19, "StatusAttention: Show fallback");
        }
        if (AppTheme.UseSimpleRendering) { restore(); return; }
        int ticks = 0;
        var timer = new System.Windows.Forms.Timer { Interval = 170 };
        timer.Tick += (_, _) => {
            ticks++;
            if (status.IsDisposed || ticks > 4)
            {
                timer.Stop(); timer.Dispose();
                if (!status.IsDisposed) restore();
                return;
            }
            status.BackColor = AppTheme.Tint(AppTheme.Warning, ticks % 2 == 1 ? 0.55f : 0.90f);
        };
        timer.Start();
    }
}
