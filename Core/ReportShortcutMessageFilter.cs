namespace FMSC_SMS.Core;

/// <summary>Dispatches preview shortcuts before the report viewer consumes native key messages.</summary>
internal sealed class ReportShortcutMessageFilter : IMessageFilter, IDisposable
{
    private readonly ApplicationWindowForm _window;
    private bool _disposed;
    private static readonly HashSet<Keys> Shortcuts = new()
    {
        Keys.Control | Keys.P, Keys.Control | Keys.E, Keys.Control | Keys.Shift | Keys.P,
        Keys.Control | Keys.Left, Keys.Control | Keys.Right,
        Keys.Control | Keys.D0, Keys.Control | Keys.D1, Keys.Escape
    };

    internal ReportShortcutMessageFilter(ApplicationWindowForm window)
    {
        _window = window;
        Application.AddMessageFilter(this);
    }

    public bool PreFilterMessage(ref Message message) => TryHandleMessage(ref message, Control.ModifierKeys);

    internal bool TryHandleMessage(ref Message message, Keys modifiers)
    {
        if (_disposed || _window.IsDisposed || !_window.Visible || !_window.Enabled ||
            message.Msg is not (0x0100 or 0x0104)) return false; // WM_KEYDOWN / WM_SYSKEYDOWN
        // Only intercept messages addressed to this preview, never an owned print/export dialog.
        var source = Control.FromChildHandle(message.HWnd);
        if (source is null || !ReferenceEquals(source.FindForm(), _window)) return false;
        Keys key = (Keys)message.WParam.ToInt32() | modifiers;
        if (!Shortcuts.Contains(key)) return false;
        return _window.ShortcutController.TryHandle(key);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Application.RemoveMessageFilter(this);
    }
}
