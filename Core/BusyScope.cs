namespace FMSC_SMS.Core;
/// <summary>UX - one shared, non-modal "work in progress" state. Scopes nest: each one restores what it captured, in reverse order.</summary>
public sealed class BusyScope : IDisposable
{
    private sealed class Captured
    {
        public Control Control = null!;
        public bool WasEnabled;
        public bool PageChanged;
        public EventHandler Watcher = null!;
    }
    private readonly List<Captured> _controls = new();
    private readonly Func<string> _getStatus;
    private readonly Action<string, PageStatusKind> _setStatus;
    private readonly string _message;
    private readonly string _previousStatus;
    private readonly PageStatusKind _previousStatusKind;
    private readonly bool _previousUseWaitCursor;
    private readonly Control? _focusOwner;
    private readonly Control? _focusBefore;
    private readonly Control? _focusAfterDisable;
    private bool _disposed;
    public BusyScope(
        string message,
        IEnumerable<Control?> controlsToDisable,
        Func<string> getStatus,
        Func<PageStatusKind> getStatusKind,
        Action<string, PageStatusKind> setStatus,
        Control? focusOwner = null)
    {
        _message = message ?? string.Empty;
        _getStatus = getStatus;
        _setStatus = setStatus;
        _previousStatus = getStatus() ?? string.Empty;
        _previousStatusKind = getStatusKind();
        _focusOwner = focusOwner;
        _focusBefore = (focusOwner as Form)?.ActiveControl;
        _previousUseWaitCursor = focusOwner?.UseWaitCursor ?? false;
        foreach (Control? control in controlsToDisable)
        {
            if (control is null || control.IsDisposed)
                continue;
            var captured = new Captured {
                Control = control,
                WasEnabled = control.Enabled
            };
            captured.Watcher = (_, _) => captured.PageChanged = true;
            _controls.Add(captured);
        }
        /* Disable after everything is captured, and only then watch for the page's own changes, so this scope's own Enabled = false is not mistaken for one. */
        foreach (Captured captured in _controls)
        {
            if (captured.WasEnabled)
                captured.Control.Enabled = false;
            captured.Control.EnabledChanged += captured.Watcher;
        }
        _focusAfterDisable = (focusOwner as Form)?.ActiveControl;
        if (_focusOwner is { IsDisposed: false })
            _focusOwner.UseWaitCursor = true;
        if (_message.Length > 0)
            _setStatus(_message, PageStatusKind.Busy);
    }
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        for (int i = _controls.Count - 1; i >= 0; i--)
        {
            Captured captured = _controls[i];
            captured.Control.EnabledChanged -= captured.Watcher;
            if (!captured.Control.IsDisposed &&
                captured.WasEnabled &&
                !captured.PageChanged)
            {
                captured.Control.Enabled = true;
            }
        }
        if (_focusOwner is { IsDisposed: false })
            _focusOwner.UseWaitCursor = _previousUseWaitCursor;
        if (_message.Length > 0 &&
            string.Equals(_getStatus(), _message, StringComparison.Ordinal))
        {
            _setStatus(_previousStatus, _previousStatusKind);
        }
        /* Disabling the control that had focus makes Windows move focus on. Put it back on the control that had it, but only if nothing moved focus on purpose meanwhile. */
        if (_focusOwner is Form { IsDisposed: false } form &&
            _focusBefore is { IsDisposed: false, Enabled: true, Visible: true } before &&
            !ReferenceEquals(form.ActiveControl, before) &&
            ReferenceEquals(form.ActiveControl, _focusAfterDisable))
        {
            FormValidator.FocusInput(before);
        }
    }
}
