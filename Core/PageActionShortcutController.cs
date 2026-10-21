namespace FMSC_SMS.Core;

/// <summary>Discovers page actions and owns shortcut dispatch and discoverability.</summary>
internal sealed class PageActionShortcutController
{
    private readonly Control _root;
    private readonly ToolTip _toolTip;
    private ModernButton? explicitSaveButton;
    private sealed record LocalShortcut(Control Target, Keys Keys, string Hint, Action Execute);
    private readonly List<LocalShortcut> _localShortcuts = new();
    private readonly HashSet<Control> hintTargets = new();
    internal void ClearLocalShortcuts(){_localShortcuts.Clear();RefreshHints();}
    internal void RegisterShortcut(Control target, Keys keys, string hint, Action execute)
    {
        if (!AppHotKeys.IsValidShortcut(keys))
            throw new ArgumentException("Shortcuts may use at most one modifier and one key.", nameof(keys));
        if (_localShortcuts.Any(shortcut => shortcut.Keys == keys))
            throw new ArgumentException("A shortcut is already registered in this scope.", nameof(keys));
        _localShortcuts.Add(new(target, keys, hint, execute));
        RefreshHints();
    }
    internal void SetSaveButton(ModernButton button) => explicitSaveButton = button;

    internal PageActionShortcutController(Control root, ToolTip toolTip)
    {
        _root = root;
        _toolTip = toolTip;
    }

    internal static (Control Root, PageActionShortcutController Controller) ActiveContent(ApplicationWindowForm window)
    {
        var content = VisibleContent(window).ToList();
        Control? root = content.FirstOrDefault(control => control.ContainsFocus) ?? content.FirstOrDefault();
        return root switch {
            V2Page page => (page, page.ShortcutController),
            ApplicationWindowForm embedded => (embedded, embedded.ShortcutController),
            _ => (window, window.ShortcutController)
        };
    }

    private static IEnumerable<Control> VisibleContent(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child.IsDisposed || !child.Visible) continue;
            // Prefer the inner page over any surrounding workspace or dialog host.
            foreach (var nested in VisibleContent(child)) yield return nested;
            if (child is V2Page or ApplicationWindowForm) yield return child;
        }
    }

    internal bool TryHandle(Keys keyData)
    {
        var local = _localShortcuts.FirstOrDefault(shortcut => shortcut.Keys == keyData);
        if (local is not null)
        {
            // Consume registered keys while unavailable so native viewers cannot bypass readiness.
            if (local.Target.IsDisposed || !local.Target.Visible || !local.Target.Enabled) return true;
            local.Execute();
            return true;
        }
        if (keyData == (Keys.Control | Keys.F) && SearchTarget() is { } search)
        {
            switch (search)
            {
                case StudentPicker picker: picker.Focus(); picker.SelectAll(); break;
                case ModernTextBox text: text.Focus(); text.SelectAll(); break;
                case TextBoxBase text: text.Focus(); text.SelectAll(); break;
            }
            return true;
        }
        foreach (AppHotKeys.ActionShortcut shortcut in AppHotKeys.PageActions)
        {
            if (keyData != shortcut.KeyData)
                continue;
            List<ModernButton> matches = Find(shortcut.ButtonPrefixes);
            if (shortcut.KeyData == (Keys.Control | Keys.P) && matches.Count == 0 && _root is V2Page { IsGridReport: true } report)
            {
                report.BeginReportPreview();
                return true;
            }
            if (matches.Count != 1 || !matches[0].Enabled || !matches[0].Visible)
                return false;
            Control? previous = FocusedInput(_root);
            if (shortcut.KeyData == Keys.F5)
            {
                LookupDataCache.Invalidate();
                StudentSearchCache.Invalidate();
            }
            matches[0].PerformClick();
            // Let the action's own validation or editor reset choose focus first.
            // Recover only when focus was left on a button or lost during reload.
            if (!_root.IsDisposed && _root.IsHandleCreated)
                _root.BeginInvoke((Action)(() => RestoreInputFocus(previous)));
            return true;
        }
        return false;
    }

    internal IReadOnlyList<(Control Target, string Key)> AvailableHints()
    {
        var hints = new List<(Control, string)>();
        hints.AddRange(_localShortcuts.Where(shortcut => !shortcut.Target.IsDisposed && shortcut.Target.Visible && shortcut.Target.Enabled)
            .Select(shortcut => (shortcut.Target, shortcut.Hint)));
        Control? search = SearchTarget();
        if (search is not null) hints.Add((search, "Ctrl+F"));
        foreach (var shortcut in AppHotKeys.PageActions)
        {
            if (search is not null && shortcut.KeyData == (Keys.Control | Keys.F)) continue;
            var matches = Find(shortcut.ButtonPrefixes);
            if (shortcut.KeyData == (Keys.Control | Keys.P) && matches.Count == 0 && _root is V2Page { IsGridReport: true, CanPreviewReport: true } report)
                hints.Add((report.ReportShortcutTarget, "Ctrl+P"));
            if (matches.Count == 1 && matches[0].Enabled)
                hints.Add((matches[0], shortcut.DisplayKey));
        }
        return hints;
    }
    internal IEnumerable<(Control Target,string Key)> LocalHints()=>_localShortcuts
        .Where(s=>!s.Target.IsDisposed && s.Target.Visible && s.Target.Enabled).Select(s=>(s.Target,s.Hint));

    private Control? SearchTarget()
    {
        var candidates = SearchInputs(_root).ToList();
        // Split setup pages can each have a record search. Prefer the editor being used.
        Control focused = _root;
        while (focused.Controls.Cast<Control>().FirstOrDefault(child => child.ContainsFocus) is { } child)
            focused = child;
        for (Control? region = focused.Parent; region is not null && region != _root; region = region.Parent)
        {
            var scoped = candidates.Where(input => IsWithin(input, region)).ToList();
            if (scoped.Count == 1) return scoped[0];
        }
        return candidates.FirstOrDefault();
    }

    private static IEnumerable<Control> SearchInputs(Control root)
    {
        foreach (Control child in root.Controls.Cast<Control>().OrderBy(control => control.TabIndex))
        {
            if (child.IsDisposed || !child.Visible || !child.Enabled) continue;
            if (child is StudentPicker) { yield return child; continue; }
            if (child is ModernTextBox or TextBoxBase)
            {
                string placeholder = child switch {
                    ModernTextBox text => text.PlaceholderText,
                    TextBox text => text.PlaceholderText,
                    _ => string.Empty
                };
                bool IsSearchCaption(string caption) =>
                    caption.Trim().StartsWith("Search", StringComparison.OrdinalIgnoreCase) ||
                    caption.Trim().StartsWith("Find", StringComparison.OrdinalIgnoreCase) ||
                    caption.Contains("Student Name or Admission Number", StringComparison.OrdinalIgnoreCase);
                bool labelled = child.Parent?.Controls.OfType<Label>().Any(label => IsSearchCaption(label.Text)) == true;
                if (labelled || IsSearchCaption(child.AccessibleName ?? string.Empty) || child.Name.Contains("search", StringComparison.OrdinalIgnoreCase) ||
                    placeholder.Contains("search", StringComparison.OrdinalIgnoreCase) ||
                    placeholder.Contains("Admission Number /", StringComparison.OrdinalIgnoreCase))
                    yield return child;
                continue; // Do not count a ModernTextBox's inner editor twice.
            }
            foreach (var input in SearchInputs(child)) yield return input;
        }
    }

    private void RestoreInputFocus(Control? previous)
    {
        if (_root.IsDisposed || !_root.Visible || FocusedInput(_root) is not null) return;
        if (previous is { IsDisposed: false, Visible: true, Enabled: true } && previous.CanFocus)
        {
            previous.Focus();
            return;
        }
        if (FormFlow.FocusFirstInput(_root)) return;
        foreach (Control input in InputsInTabOrder(_root))
            if (input.CanFocus && input.Focus()) return;
    }

    private static Control? FocusedInput(Control root) =>
        InputsInTabOrder(root).FirstOrDefault(input => input.ContainsFocus);

    private static IEnumerable<Control> InputsInTabOrder(Control root)
    {
        foreach (Control child in root.Controls.Cast<Control>().OrderBy(control => control.TabIndex))
        {
            if (child.IsDisposed || !child.Visible || !child.Enabled) continue;
            if (child.TabStop && child is TextBoxBase or ComboBox or DateTimePicker or
                DataGridView or ListBox or TreeView or NumericUpDown or CheckBox or RadioButton)
                yield return child;
            else
                foreach (Control input in InputsInTabOrder(child)) yield return input;
        }
    }

    internal void RefreshHints()
    {
        if (_root.IsDisposed) return;
        var letters=new Dictionary<ModernButton,char>();
        var hints=new Dictionary<Control,List<string>>();
        void AddHint(Control target,string key)
        {
            if (!hints.TryGetValue(target,out var keys)) hints[target]=keys=new();
            keys.Add(key);
        }
        foreach (AppHotKeys.ActionShortcut shortcut in AppHotKeys.PageActions)
        {
            List<ModernButton> matches = Find(shortcut.ButtonPrefixes);
            foreach (ModernButton button in matches)
            {
                if(matches.Count==1)AddHint(button,shortcut.DisplayKey);
                if(matches.Count==1 && button.Enabled && !letters.ContainsKey(button) &&
                    !(shortcut.KeyData==(Keys.Control|Keys.F) && SearchTarget() is not null)) {
                    var code=shortcut.KeyData & Keys.KeyCode;
                    if(code>=Keys.A && code<=Keys.Z)letters[button]=(char)code;
                }
            }
        }
        foreach(var local in _localShortcuts)
            if(!local.Target.IsDisposed)AddHint(local.Target,local.Hint);
        foreach(var local in _localShortcuts)if(local.Target is ModernButton button && button.Visible && button.Enabled) {
            var code=local.Keys & Keys.KeyCode;
            if(code>=Keys.A && code<=Keys.Z)letters[button]=(char)code;
        }
        foreach(var button in AllButtons(_root))button.ShortcutLetter=letters.TryGetValue(button,out char letter)?letter:null;
        foreach(var target in hintTargets.Union(hints.Keys).ToArray())
            ControlToolTips.SetShortcuts(_toolTip,target,this,hints.TryGetValue(target,out var keys)?keys:Array.Empty<string>());
        hintTargets.Clear();
        hintTargets.UnionWith(hints.Keys);
    }
    private static IEnumerable<ModernButton> AllButtons(Control root) {
        foreach(Control child in root.Controls) {
            if(child is ModernButton button)yield return button;
            foreach(var nested in AllButtons(child))yield return nested;
        }
    }

    private List<ModernButton> Find(IEnumerable<string> leadingWords)
    {
        if (leadingWords.SequenceEqual(new[] { "Save" }) && explicitSaveButton is { IsDisposed: false, Visible: true })
            return new List<ModernButton> { explicitSaveButton };
        var matches = new List<ModernButton>();
        foreach (string leadingWord in leadingWords)
            CollectButtons(_root, leadingWord, matches);
        matches = matches.Distinct().ToList();
        if (leadingWords.SequenceEqual(new[] { "Save" }))
        {
            // Save & New is a separate workflow; Ctrl+S uses the ordinary Save.
            var primary = matches.Where(button => button.Text.Trim().TrimStart('✓', ' ').Equals("Save", StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count > 1)
            {
                Control focused = _root;
                while (focused.Controls.Cast<Control>().FirstOrDefault(child => child.ContainsFocus) is { } child)
                    focused = child;
                for (Control? region = focused.Parent; region is not null && region != _root; region = region.Parent)
                {
                    var scoped = matches.Where(button => IsWithin(button, region)).ToList();
                    if (scoped.Count == 1) return scoped;
                    var scopedPrimary = primary.Where(button => IsWithin(button, region)).ToList();
                    if (scopedPrimary.Count == 1) return scopedPrimary;
                    if (scoped.Count > 1) return scoped;
                }
            }
            if (primary.Count == 1) return primary;
        }
        // Prefer actions in the focused editor for every command, not just Save.
        if (matches.Count > 1)
        {
            Control focused = _root;
            while (focused.Controls.Cast<Control>().FirstOrDefault(child => child.ContainsFocus) is { } child)
                focused = child;
            for (Control? region = focused.Parent; region is not null && region != _root; region = region.Parent)
            {
                var scoped = matches.Where(button => IsWithin(button, region)).ToList();
                if (scoped.Count == 1) return scoped;
                if (scoped.Count > 1) return scoped; // Never fall through to another editor.
            }
        }
        return matches;
    }
    private static bool IsWithin(Control control, Control root)
    {
        for (Control? current = control; current is not null; current = current.Parent)
            if (ReferenceEquals(current, root)) return true;
        return false;
    }

    private static bool MatchesCaption(string caption, string prefix) =>
        caption.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        (caption.Length == prefix.Length || !char.IsLetterOrDigit(caption[prefix.Length]));

    private static void CollectButtons(Control root, string leadingWord, List<ModernButton> matches)
    {
        foreach (Control child in root.Controls)
        {
            if (child is ModernButton button && button.Visible &&
                button.ThemeRole is not ("Navigation" or "TabActive" or "TabInactive"))
            {
                string label = (button.Text ?? string.Empty).TrimStart();
                int firstLetter = 0;
                while (firstLetter < label.Length && !char.IsLetter(label[firstLetter]))
                    firstLetter++;
                if (MatchesCaption(label[firstLetter..], leadingWord) ||
                    leadingWord == "Print" && label.Contains(" / Print", StringComparison.OrdinalIgnoreCase))
                    matches.Add(button);
            }
            if (child.HasChildren)
                CollectButtons(child, leadingWord, matches);
        }
    }
}
