namespace FMSC_SMS.Core;

/// <summary>Inline completion and a non-activating matching list for shared text inputs.</summary>
internal sealed class LiveTextSuggestions : IDisposable
{
    private readonly ModernTextBox _host;
    private readonly TextBox _input;
    private StudentPickerPopupForm? _popup;
    private ListBox? _list;
    private string[] _matches = Array.Empty<string>();
    private string _prefix = string.Empty;
    private bool _applying, _queued, _editing;
    internal LiveTextSuggestions(ModernTextBox host, TextBox input)
    {
        _host = host; _input = input;
        input.KeyDown += OnKeyDown;
        input.KeyUp += (_, _) => _editing = false;
        input.TextChanged += OnTextChanged;
        input.LostFocus += (_, _) => Hide();
        host.VisibleChanged += (_, _) => { if (!host.Visible) Hide(); };
    }
    internal bool Enabled { get; set; }
    internal void Refresh()
    {
        if (!Enabled || !_input.Focused || _input.ReadOnly) return;
        _editing = true;
        OnTextChanged(_input, EventArgs.Empty);
    }
    internal static string[] Matching(IEnumerable<string> source, string prefix) =>
        string.IsNullOrWhiteSpace(prefix) ? Array.Empty<string>() : source
            .Where(value => !string.IsNullOrWhiteSpace(value) && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToArray();
    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (_applying || !Enabled || !_editing || !_input.Focused || _input.ReadOnly || _queued) return;
        _queued = true;
        _input.BeginInvoke(new Action(() => {
            _queued = false; _editing = false;
            if (_input.IsDisposed || !_input.Focused || !Enabled) return;
            // Completion applies only at the end of the field, never to a middle edit.
            if (_input.SelectionStart != _input.TextLength || _input.SelectionLength != 0) { Hide(); return; }
            _prefix = _input.Text;
            _matches = Matching(_host.AutoCompleteCustomSource.Cast<string>()
                .Where(value => value.Length <= _input.MaxLength), _prefix);
            if (_matches.Length == 0) { Hide(); return; }
            ShowMatches();
            Complete(_matches[0]);
        }));
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!Enabled) return;
        _editing = false;
        if (_popup?.Visible == true && (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up))
        {
            int delta = e.KeyCode == Keys.Down ? 1 : -1;
            _list!.SelectedIndex = Math.Clamp(_list.SelectedIndex + delta, 0, _matches.Length - 1);
            Complete(_matches[_list.SelectedIndex]);
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape && _popup?.Visible == true)
        {
            _applying = true;
            try { _input.Text = _prefix; _input.SelectionStart = _input.TextLength; }
            finally { _applying = false; }
            Hide(); e.Handled = e.SuppressKeyPress = true;
        }
        else if (!e.Alt && ((!e.Control && (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete ||
                     e.KeyCode == Keys.Space ||
                     (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.Z) ||
                     (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.Divide) ||
                     (e.KeyCode >= Keys.OemSemicolon && e.KeyCode <= Keys.OemBackslash))) ||
                     (e.Control && (e.KeyCode == Keys.V || e.KeyCode == Keys.X)))) _editing = true;
    }
    private void Complete(string match)
    {
        if (match.Length > _input.MaxLength) return;
        _applying = true;
        try {
            // Retain typed casing and select only the suggested suffix.
            _input.Text = _prefix + match.Substring(_prefix.Length);
            _input.Select(_prefix.Length, match.Length - _prefix.Length);
        }
        finally { _applying = false; }
    }
    private void ShowMatches()
    {
        if (_popup is null)
        {
            _list = new ListBox { BorderStyle = BorderStyle.None, IntegralHeight = false, TabStop = false };
            _popup = new StudentPickerPopupForm(_list, new Panel { Height = 0 });
            _list.MouseDown += (_, e) => {
                int index = _list.IndexFromPoint(e.Location);
                if (index < 0 || index >= _matches.Length) return;
                Complete(_matches[index]);
                _input.SelectionStart = _input.TextLength; _input.SelectionLength = 0;
                Hide(); _input.Focus();
            };
        }
        _list!.Font = _input.Font;
        _list.BackColor = AppTheme.Surface; _list.ForeColor = AppTheme.Text;
        _popup.BackColor = AppTheme.InputBorder;
        _list.BeginUpdate();
        try { _list.Items.Clear(); _list.Items.AddRange(_matches); _list.SelectedIndex = 0; }
        finally { _list.EndUpdate(); }
        var screen = Screen.FromControl(_host).WorkingArea;
        int width = Math.Min(Math.Max(_host.Width, AppScale.Scale(240)), screen.Width);
        int height = Math.Min(_matches.Length, 6) * _list.ItemHeight + 2;
        Point below = _host.PointToScreen(new Point(0, _host.Height));
        int top = below.Y + height <= screen.Bottom ? below.Y : below.Y - _host.Height - height;
        _popup.Bounds = new Rectangle(Math.Clamp(below.X, screen.Left, screen.Right - width),
            Math.Clamp(top, screen.Top, Math.Max(screen.Top, screen.Bottom - height)), width, height);
        if (!_popup.Visible && _host.FindForm() is { } owner) _popup.Show(owner);
    }
    internal void Hide() => _popup?.Hide();
    public void Dispose() => _popup?.Dispose();
}
