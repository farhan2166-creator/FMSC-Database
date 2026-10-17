using System.Runtime.CompilerServices;
namespace FMSC_SMS.Core;
/// <summary>ONE inline-validation mechanism for every screen, instead of a message box per missing field. * every invalid input gets a subtle danger-coloured border (no red fill, no animation).</summary>
public sealed class FormValidator : IDisposable
{
    private readonly Dictionary<Control, Label> _messageLabels = new();
    /// <summary>Uses a reserved message row instead of the caption badge for this input.</summary>
    public void BindMessageLabel(Control control, Label label) => _messageLabels[control] = label;
    private sealed class Entry
    {
        public required Control Control { get; init; }
        public string Message { get; set; } = string.Empty;
        public Func<bool>? StillInvalidCheck { get; set; }
        public Label? Badge { get; set; }
        public Action? Unsubscribe { get; set; }
    }
    /* One badge per caption label, so a second validator (or a second run) reuses it. */
    private static readonly ConditionalWeakTable<Label, Label> Badges = new();
    private readonly Dictionary<Control, Entry> _entries = new();
    private readonly ToolTip _tip = new() { AutoPopDelay = 5000, InitialDelay = 0, ReshowDelay = 0 };
    private Label? _summaryLabel;
    private string _summaryOriginalText = string.Empty;
    private Color _summaryOriginalColor;
    private bool _disposed;
    /// <summary>Receives the one-line summary ("3 fields need attention: ...") or null when everything is valid.</summary>
    public Action<string?>? SummaryChanged { get; set; }
    /// <summary>Called before an invalid input is focused, so a screen that keeps inputs on tabs can show the right tab first.</summary>
    public Action<Control>? EnsureVisible { get; set; }
    /// <summary>Raised whenever an input becomes invalid or valid.</summary>
    public event EventHandler? Changed;
    public bool HasErrors => _entries.Count > 0;
    public int ErrorCount => _entries.Count;
    /// <summary>True when <paramref name="control"/> is currently marked invalid.</summary>
    public bool IsMarked(Control control) => _entries.ContainsKey(control);
    /// <summary>Uses <paramref name="label"/> (for example a dialog's hint line) as the summary line: it shows the summary in the danger colour while something is invalid and gets its own text back afterwards.</summary>
    public void BindSummary(Label label)
    {
        _summaryLabel = label;
        _summaryOriginalText = label.Text;
        _summaryOriginalColor = label.ForeColor;
    }
    // Running a validation pass
    /// <summary>Start of a validation pass: forgets the previous run's marks.</summary>
    public void Begin() => ClearAll();
    /// <summary>End of a validation pass: publishes the summary and moves the focus to the first invalid input. Returns true when nothing is invalid.</summary>
    public bool Finish()
    {
        RaiseSummary();
        if (!HasErrors)
            return true;
        FocusFirstInvalid();
        return false;
    }
    /// <summary>The input must have a value (text, a selection, a chosen student, a ticked box).</summary>
    public bool Required(Control control, string message = "Required") =>
        Check(control, HasValue(control), message, () => HasValue(control));
    /// <summary>Validates every visible, enabled input marked required by <see cref="HumanUi.Field"/>.</summary>
    public bool RequiredFields(Control root, string message = "Required")
    {
        bool valid = true;
        if (root.Visible && root.Enabled && IsRequiredInput(root))
            valid &= Required(root, message);
        foreach (Control control in Descendants(root))
        {
            if (!control.Visible || !control.Enabled || !IsRequiredInput(control))
                continue;
            valid &= Required(control, message);
        }
        RecordTextPolicy.ValidateInputs(this, root);
        return valid && !HasErrors;
    }
    /// <summary>The input must satisfy <paramref name="isValid"/>. The same rule is re-run whenever the input changes, and the mark goes away as soon as it passes.</summary>
    public bool Rule(Control control, Func<bool> isValid, string message)
    {
        bool ok;
        try
        {
            ok = isValid();
        }
        catch (Exception nonFatalException3)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException3, "FormValidator: Rule fallback");
            ok = false;
        }
        return Check(control, ok, message, isValid);
    }
    /// <summary>Marks or clears one input.</summary>
    public bool Check(Control control, bool isValid, string message, Func<bool>? recheck = null)
    {
        if (control is null || _disposed)
            return isValid;
        if (isValid)
        {
            Clear(control);
            return true;
        }
        Mark(control, message, recheck);
        return false;
    }
    /// <summary>Marks an input invalid without a rule; an edit of the input clears it.</summary>
    public void Fail(Control control, string message) => Mark(control, message, null);
    public void Clear(Control control)
    {
        if (control is null || !_entries.Remove(control, out Entry? entry))
            return;
        entry.Unsubscribe?.Invoke();
        entry.Unsubscribe = null;
        if (!control.IsDisposed && control is IValidationTarget target)
            target.IsInvalid = false;
        HideBadge(entry);
        _tip.SetToolTip(control, null);
        Changed?.Invoke(this, EventArgs.Empty);
        if (_entries.Count == 0)
            RaiseSummary();
    }
    public void ClearAll()
    {
        if (_entries.Count == 0)
        {
            RaiseSummary();
            return;
        }
        foreach (Control control in _entries.Keys.ToArray())
        {
            Entry entry = _entries[control];
            entry.Unsubscribe?.Invoke();
            entry.Unsubscribe = null;
            if (!control.IsDisposed && control is IValidationTarget target)
                target.IsInvalid = false;
            HideBadge(entry);
            _tip.SetToolTip(control, null);
        }
        _entries.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
        RaiseSummary();
    }
    /// <summary>Focuses the first invalid input in reading order (top row first, then left to right).</summary>
    public bool FocusFirstInvalid()
    {
        foreach (Control control in InReadingOrder())
        {
            if (control.IsDisposed)
                continue;
            EnsureVisible?.Invoke(control);
            if (!control.Visible || !control.Enabled)
                continue;
            if (!FocusInput(control))
                continue;
            /* The badge already names the problem for an input with a caption; an input without one gets the message as a short tooltip so it is never silent. */
            Entry entry = _entries[control];
            if (entry.Badge is null && control.IsHandleCreated)
            {
                try
                {
                    _tip.Show(entry.Message, control, 0, control.Height + 2, 4000);
                }
                catch (Exception nonFatalException4)
                {
                    FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException4, "FormValidator: FocusFirstInvalid fallback");
                    // Courtesy only.
                }
            }
            return true;
        }
        return false;
    }
    /// <summary>The summary line: one message when one input is invalid, a short list when several are.</summary>
    public string? BuildSummary()
    {
        if (_entries.Count == 0)
            return null;
        Control[] ordered = InReadingOrder().ToArray();
        if (ordered.Length == 1)
        {
            string name = FieldName(ordered[0]);
            string message = _entries[ordered[0]].Message;
            return name.Length == 0 ? message : $"{name}: {message}";
        }
        string[] names = ordered
            .Select(FieldName)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string list = names.Length switch {
            0 => string.Empty,
            <= 4 => ": " + string.Join(", ", names),
            _ => ": " + string.Join(", ", names.Take(4)) + $" and {names.Length - 4} more"
        };
        return $"{ordered.Length} fields need attention{list}.";
    }
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (Entry entry in _entries.Values)
            entry.Unsubscribe?.Invoke();
        _entries.Clear();
        _tip.Dispose();
    }
    // Internals
    private void Mark(Control control, string message, Func<bool>? recheck)
    {
        if (control.IsDisposed)
            return;
        message = string.IsNullOrWhiteSpace(message) ? "Please check this" : message.Trim();
        bool isNew = !_entries.TryGetValue(control, out Entry? entry);
        if (isNew)
        {
            entry = new Entry { Control = control };
            _entries[control] = entry;
            entry.Unsubscribe = Subscribe(control, entry);
        }
        entry!.Message = message;
        entry.StillInvalidCheck = recheck;
        if (control is IValidationTarget target)
            target.IsInvalid = true;
        ShowBadge(entry);
        if (isNew)
            Changed?.Invoke(this, EventArgs.Empty);
    }
    private Action Subscribe(Control control, Entry entry)
    {
        void OnChanged(object? sender, EventArgs e)
        {
            if (_disposed || control.IsDisposed)
                return;
            bool stillInvalid;
            try
            {
                /* A rule-based mark clears only when the rule now passes; a plain mark clears on any edit. */
                stillInvalid = entry.StillInvalidCheck is not null && !entry.StillInvalidCheck();
            }
            catch (Exception nonFatalException5)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException5, "FormValidator: Subscribe fallback");
                stillInvalid = true;
            }
            if (!stillInvalid)
                Clear(control);
        }
        switch (control)
        {
            case StudentPicker picker:
                picker.UserSelectionChanged += OnChanged;
                picker.SelectionCleared += OnChanged;
                return () => {
                    picker.UserSelectionChanged -= OnChanged;
                    picker.SelectionCleared -= OnChanged;
                };
            case ModernComboBox combo:
                combo.SelectedIndexChanged += OnChanged;
                return () => combo.SelectedIndexChanged -= OnChanged;
            case ModernDatePicker date:
                date.ValueChanged += OnChanged;
                return () => date.ValueChanged -= OnChanged;
            case CheckBox check:
                check.CheckedChanged += OnChanged;
                return () => check.CheckedChanged -= OnChanged;
            default:
                control.TextChanged += OnChanged;
                return () => control.TextChanged -= OnChanged;
        }
    }
    private void RaiseSummary()
    {
        string? summary = BuildSummary();
        SummaryChanged?.Invoke(summary);
        if (_summaryLabel is { IsDisposed: false } label)
        {
            if (summary is null)
            {
                label.Text = _summaryOriginalText;
                label.ForeColor = _summaryOriginalColor;
            }
            else
            {
                label.Text = summary;
                label.ForeColor = AppTheme.Danger;
            }
        }
    }
    private IEnumerable<Control> InReadingOrder()
    {
        return _entries.Keys
            .Where(c => !c.IsDisposed)
            .Select(c => (Control: c, Point: SafeScreenPoint(c)))
            .OrderBy(x => x.Point.Y / 12)
            .ThenBy(x => x.Point.X)
            .Select(x => x.Control);
    }
    private static Point SafeScreenPoint(Control control)
    {
        try
        {
            return control.IsHandleCreated
                ? control.PointToScreen(Point.Empty)
                : control.Location;
        }
        catch (Exception nonFatalException6)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException6, "FormValidator: SafeScreenPoint fallback");
            return control.Location;
        }
    }
    /// <summary>Puts the keyboard focus on the input, however it is wrapped.</summary>
    public static bool FocusInput(Control control)
    {
        switch (control)
        {
            case ModernTextBox text:
                bool focused = text.Focus();
                if (focused && !text.Multiline)
                    text.SelectAll();
                return focused;
            case ModernComboBox combo:
                return combo.Focus();
            case ModernDatePicker date:
                return date.Focus();
            case StudentPicker picker:
                return picker.Focus();
            default:
                return control.Focus();
        }
    }
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            if (child.HasChildren)
            {
                foreach (Control descendant in Descendants(child))
                    yield return descendant;
            }
        }
    }
    private static bool IsRequiredInput(Control control)
    {
        if (!string.Equals(control.AccessibleDescription, "Required", StringComparison.OrdinalIgnoreCase))
            return false;
        return control is StudentPicker or ModernComboBox or ModernDatePicker or CheckBox or ModernTextBox;
    }
    /// <summary>True when the input holds something (the meaning of "Required" for each kind of input).</summary>
    public static bool HasValue(Control control) =>
        control switch {
            StudentPicker picker => picker.SelectedId > 0,
            LookupBox lookup => lookup.SelectedId > 0,
            ModernComboBox combo =>
                combo.SelectedIndex >= 0 ||
                combo.DropDownStyle != ComboBoxStyle.DropDownList &&
                !string.IsNullOrWhiteSpace(combo.Text),
            ModernDatePicker date => !date.ShowCheckBox || date.Checked,
            CheckBox check => check.Checked,
            ModernTextBox text => !string.IsNullOrWhiteSpace(text.Text),
            _ => !string.IsNullOrWhiteSpace(control.Text)
        };
    // The message next to the field
    /// <summary>The caption made by <see cref="HumanUi.Field"/> that belongs to this input, if it has one.</summary>
    private static Label? CaptionOf(Control control)
    {
        Control? parent = control.Parent;
        if (parent is null)
            return null;
        foreach (Control sibling in parent.Controls)
        {
            if (sibling is Label label &&
                Equals(label.Tag, UiHelper.FieldCaptionTag))
            {
                return label;
            }
        }
        /* MuteFieldCaption re-tags a caption "ThemeMutedText"; a field panel is exactly caption + input. */
        if (parent.Controls.Count == 2)
        {
            foreach (Control sibling in parent.Controls)
            {
                if (sibling is Label label &&
                    Equals(label.Tag, "ThemeMutedText"))
                {
                    return label;
                }
            }
        }
        return null;
    }
    private static string FieldName(Control control)
    {
        Label? caption = CaptionOf(control);
        if (caption is not null)
        {
            string text = UiAccessibility.CleanCaption(GetCaptionText(caption));
            if (text.Length > 0)
                return text;
        }
        if (!string.IsNullOrWhiteSpace(control.AccessibleName))
            return control.AccessibleName;
        return string.Empty;
    }
    /* The badge is a child of the caption label, so the label's own Text still reads as the caption. */
    private static string GetCaptionText(Label caption) => caption.Text;
    private void ShowBadge(Entry entry)
    {
        if (_messageLabels.TryGetValue(entry.Control, out Label? messageLabel) && !messageLabel.IsDisposed)
        {
            entry.Badge = messageLabel;
            messageLabel.Text = entry.Message;
            messageLabel.Visible = true;
            _tip.SetToolTip(messageLabel, entry.Message);
            _tip.SetToolTip(entry.Control, entry.Message);
            return;
        }
        Label? caption = CaptionOf(entry.Control);
        if (caption is null || caption.IsDisposed)
        {
            entry.Badge = null;
            _tip.SetToolTip(entry.Control, entry.Message);
            return;
        }
        if (!Badges.TryGetValue(caption, out Label? badge) || badge.IsDisposed)
        {
            badge = CreateBadge(caption);
            Badges.Remove(caption);
            Badges.Add(caption, badge);
        }
        entry.Badge = badge;
        badge.Text = entry.Message;
        FitBadge(caption, badge);
        badge.Visible = true;
        badge.BringToFront();
        _tip.SetToolTip(badge, entry.Message);
        _tip.SetToolTip(entry.Control, entry.Message);
    }
    private void HideBadge(Entry entry)
    {
        if (entry.Badge is { IsDisposed: false } badge)
        {
            badge.Visible = false;
            badge.Text = string.Empty;
        }
        entry.Badge = null;
    }
    private static Label CreateBadge(Label caption)
    {
        var badge = new Label {
            Dock = DockStyle.Right,
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent,
            ForeColor = AppTheme.Danger,
            Font = AppTheme.SmallFont,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 2, 0),
            Cursor = Cursors.Default,
            Visible = false,
            AccessibleRole = AccessibleRole.Alert
        };
        caption.Controls.Add(badge);
        caption.Resize += (_, _) => FitBadge(caption, badge);
        return badge;
    }
    private static void FitBadge(Label caption, Label badge)
    {
        if (caption.IsDisposed || badge.IsDisposed)
            return;
        int wanted = TextRenderer.MeasureText(badge.Text, badge.Font).Width + 10;
        // The caption still draws its own text beneath this child label. A fixed
        // percentage can cover a long caption (for example Primary Phone), so
        // reserve its measured text width before allocating space to the badge.
        int captionWidth = TextRenderer.MeasureText(caption.Text, caption.Font).Width;
        int available = Math.Max(0, caption.ClientSize.Width - caption.Padding.Horizontal - captionWidth - AppScale.Scale(8));
        int width = Math.Min(wanted, available);
        if (badge.Width != width)
            badge.Width = width;
    }
}
