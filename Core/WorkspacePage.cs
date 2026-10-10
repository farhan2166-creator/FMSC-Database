namespace FMSC_SMS.Core;
public sealed class WorkspacePage : UserControl, IUnsavedChanges, IWorkspaceStateHost, IBusyPage
{
    private IBusyPage? BusyChild => _tabs.TabPages.Cast<TabPage>()
        .SelectMany(Descendants).OfType<IBusyPage>().FirstOrDefault(page => page.IsBusy);
    public bool IsBusy => BusyChild is not null;
    public string BusyMessage => BusyChild?.BusyMessage ?? "Please wait for the current operation to finish.";
    private readonly TabControl _tabs = new();
    private readonly List<(string Caption, Func<Control> Factory)> _pages = new();
    private readonly HashSet<int> _loadedTabs = new();
    private readonly Dictionary<int, IReadOnlyDictionary<string, string>> _pendingTabStates = new();
    private readonly Color _accent;
    private readonly RoundedPanel _header;
    private readonly string _title;
    private TableLayoutPanel? _rootLayout;
    private Label? _titleLabel;
    private bool _embedded;
    public string NavigationTitle { get; }
    public WorkspacePage(string title, string subtitle, params (string Caption, Func<Control> Factory)[] pages)
    {
        // Shared metrics and text measurements size these controls; avoid a second WinForms autoscaling pass.
        AutoScaleMode = AutoScaleMode.None;
        NavigationTitle = title;
        _title = title;
        _accent = AppTheme.ModuleAccent(title);
        Dock = DockStyle.Fill;
        BackColor = AppTheme.PageBackground;
        Padding = new Padding(
            UiDesignSystem.Spacing.Xl,
            AppMetrics.PageTopPadding,
            UiDesignSystem.Spacing.Xl,
            UiDesignSystem.Spacing.Xl);
        Font = UiDesignSystem.Typography.Body;
        _pages.AddRange(pages);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        int titleLabelHeight =
            Math.Max(20, AppMetrics.TextHeight(AppTheme.PageTitleFont));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                titleLabelHeight +
                AppMetrics.PageHeaderContentHeightAllowance));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _rootLayout = root;
        _header = BuildHeader(title, subtitle, titleLabelHeight);
        _tabs.Dock = DockStyle.Fill;
        /* The strip itself (fonts, tab size, owner-drawn selected/inactive state) is shared with the Student Fees page's Assignments/Payments tabs - see TabStrip, which is where this drawing code now lives. */
        TabStrip.Apply(_tabs, _accent);
        for (int i = 0; i < _pages.Count; i++)
        {
            var tab = new TabPage(_pages[i].Caption) {
                BackColor = AppTheme.Surface,
                Padding = new Padding(UiDesignSystem.Spacing.Md),
                Tag = i
            };
            _tabs.TabPages.Add(tab);
        }
        _tabs.SelectedIndexChanged += (_, _) => {
            EnsureSelectedTab();
            if (IsHandleCreated) BeginInvoke(new Action(() => {
                if (!IsDisposed && _tabs.SelectedTab is TabPage selected)
                    FormFlow.ScheduleInitialFocus(this, selected, () => selected == _tabs.SelectedTab);
            }));
        };
        _tabs.Selecting += (_, e) => {
            if (IsBusy) e.Cancel = true;
        };
        root.Controls.Add(_header, 0, 0);
        root.Controls.Add(_tabs, 0, 1);
        Controls.Add(root);
        AppTheme.ThemeChanged += OnThemeChanged;
        Disposed += (_, _) =>
            AppTheme.ThemeChanged -= OnThemeChanged;
        if (IsHandleCreated)
        {
            BeginInvoke(new MethodInvoker(ApplyScale));
        }
        else
        {
            void ApplyInitialScale(object? sender, EventArgs e)
            {
                HandleCreated -= ApplyInitialScale;
                BeginInvoke(new MethodInvoker(ApplyScale));
            }
            HandleCreated += ApplyInitialScale;
        }
        if (_tabs.TabPages.Count > 0)
        {
            if (IsHandleCreated)
            {
                BeginInvoke(new MethodInvoker(EnsureSelectedTab));
            }
            else
            {
                void LoadInitialTab(object? sender, EventArgs e)
                {
                    HandleCreated -= LoadInitialTab;
                    BeginInvoke(new MethodInvoker(EnsureSelectedTab));
                }
                HandleCreated += LoadInitialTab;
            }
        }
    }
    private RoundedPanel BuildHeader(string title, string subtitle, int titleLabelHeight)
    {
        var header = new RoundedPanel {
            Tag = "ThemePageHeader",
            Dock = DockStyle.Fill,
            BackColor = AppTheme.PageBackground,
            BorderColor = AppTheme.PageBackground,
            ShowBorder = false,
            CornerRadius = 0,
            Padding = new Padding(2, 0, 2, 0),
            Margin = new Padding(0, 0, 0, UiDesignSystem.Spacing.Sm)
        };
        var accentBar = new Panel {
            Dock = DockStyle.Left,
            Width = Math.Max(3, AppScale.Scale(4)),
            BackColor = _accent,
            Margin = new Padding(0, 4, UiDesignSystem.Spacing.Md, 4)
        };
        var titlePanel = new TableLayoutPanel {
            Tag = "ThemeHeaderContainer",
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        titlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        titlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var titleLabel = new Label {
            Tag = "ThemeHeaderTitle",
            Text = title,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Font = AppTheme.PageTitleFont,
            ForeColor = AppTheme.HeadingText,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        var subtitleLabel = new Label {
            Tag = "ThemeHeaderGuidance",
            Text = string.IsNullOrWhiteSpace(subtitle) ? string.Empty : $" ({subtitle})",
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        titlePanel.Controls.Add(titleLabel, 0, 0);
        titlePanel.Controls.Add(subtitleLabel, 1, 0);
        header.Controls.Add(accentBar);
        header.Controls.Add(titlePanel);
        _titleLabel = titleLabel;
        return header;
    }
    private void EnsureSelectedTab()
    {
        int index = _tabs.SelectedIndex;
        if (index < 0 ||
            index >= _pages.Count ||
            _loadedTabs.Contains(index))
        {
            return;
        }
        _loadedTabs.Add(index);
        var tab = _tabs.TabPages[index];
        tab.SuspendLayout();
        var oldControls = tab.Controls.Cast<Control>().ToArray();
        tab.Controls.Clear();
        foreach (var oldControl in oldControls)
            oldControl.Dispose();
        try
        {
            Control control = _pages[index].Factory();
            if (control is V2Page v2)
            {
                v2.PrepareForEmbedding(keepActions: true);
            }
            else if (control is WorkspacePage workspace)
                workspace.PrepareForEmbedding();
            control.Dock = DockStyle.Fill;
            tab.Controls.Add(control);
            if (control is IWorkspaceStateHost stateHost &&
                _pendingTabStates.TryGetValue(index, out IReadOnlyDictionary<string, string>? pendingState))
            {
                stateHost.RestoreWorkspaceState(pendingState);
                _pendingTabStates.Remove(index);
            }
            /* An embedded FORM is the one control that does not become visible just by being parented - a form is shown, not merely added. */
            if (control is Form embeddedForm && !embeddedForm.TopLevel)
            {
                embeddedForm.Show();
            }
        }
        catch (Exception ex)
        {
            _loadedTabs.Remove(index);
            tab.Controls.Add(new HumanCard("Unable to open this section", DbMessage.UserMessage(ex), AppTheme.Danger));
        }
        finally
        {
            tab.ResumeLayout(true);
        }
    }
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        BackColor = AppTheme.PageBackground;
        TabStrip.Repaint(_tabs);
        Invalidate(true);
    }
    public void ApplyScale()
    {
        SuspendLayout();
        if (_rootLayout is not null && _rootLayout.RowStyles.Count > 0 && _titleLabel is not null)
        {
            int titleLabelHeight =
                Math.Max(20, AppMetrics.TextHeight(AppTheme.PageTitleFont));
            _rootLayout.RowStyles[0].Height = _embedded ? 0 :
                titleLabelHeight +
                AppMetrics.PageHeaderContentHeightAllowance;
            _titleLabel.Font = AppTheme.PageTitleFont;
        }
        _tabs.Font = AppTheme.ButtonFont;
        ResumeLayout(true);
    }
    public void PrepareForEmbedding(bool keepActions = false)
    {
        _embedded = true;
        _header.Hide();
        if (_rootLayout is not null && _rootLayout.RowStyles.Count > 0)
            _rootLayout.RowStyles[0].Height = 0;
        Padding = new Padding(0);
        BackColor = AppTheme.Surface;
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (AppHotKeys.TrySwitchTab(this, keyData))
            return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }
    Dictionary<string, string> IWorkspaceStateHost.CaptureWorkspaceState()
    {
        var state = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["$selectedTab"] = _tabs.SelectedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        foreach (int index in _loadedTabs)
        {
            if (index < 0 || index >= _tabs.TabPages.Count)
                continue;
            IWorkspaceStateHost? child = _tabs.TabPages[index].Controls
                .Cast<Control>()
                .OfType<IWorkspaceStateHost>()
                .FirstOrDefault();
            if (child is null)
                continue;
            foreach (var item in child.CaptureWorkspaceState())
                state[$"tab:{index}:{item.Key}"] = item.Value;
        }
        return state;
    }
    void IWorkspaceStateHost.RestoreWorkspaceState(IReadOnlyDictionary<string, string> state)
    {
        _pendingTabStates.Clear();
        foreach (var group in state
                     .Where(item => item.Key.StartsWith("tab:", StringComparison.Ordinal))
                     .Select(item => (item, parts: item.Key.Split(':', 3)))
                     .Where(x => x.parts.Length == 3 && int.TryParse(x.parts[1], out _))
                     .GroupBy(x => int.Parse(x.parts[1], System.Globalization.CultureInfo.InvariantCulture)))
        {
            _pendingTabStates[group.Key] = group.ToDictionary(
                x => x.parts[2],
                x => x.item.Value,
                StringComparer.Ordinal);
        }
        if (state.TryGetValue("$selectedTab", out string? selectedText) &&
            int.TryParse(selectedText, out int selected) &&
            selected >= 0 &&
            selected < _tabs.TabPages.Count)
        {
            _tabs.SelectedIndex = selected;
        }
        EnsureSelectedTab();
        int current = _tabs.SelectedIndex;
        if (current >= 0 &&
            current < _tabs.TabPages.Count &&
            _pendingTabStates.TryGetValue(current, out IReadOnlyDictionary<string, string>? pending))
        {
            IWorkspaceStateHost? child = _tabs.TabPages[current].Controls
                .Cast<Control>()
                .OfType<IWorkspaceStateHost>()
                .FirstOrDefault();
            if (child is not null)
            {
                child.RestoreWorkspaceState(pending);
                _pendingTabStates.Remove(current);
            }
        }
    }
    /// <summary>The shell asks the current page whether anything would be lost by leaving.</summary>
    public bool HasUnsavedChanges =>
        PendingTabs().FirstOrDefault() is not null;
    public string UnsavedChangesMessage =>
        PendingTabs().FirstOrDefault()?.UnsavedChangesMessage
        ?? "This screen has unsaved changes. Leaving will discard them.";
    private IEnumerable<IUnsavedChanges> PendingTabs() =>
        _tabs.TabPages
            .Cast<TabPage>()
            .SelectMany(tab => Descendants(tab))
            .OfType<IUnsavedChanges>()
            .Where(pending => pending.HasUnsavedChanges);
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child))
                yield return descendant;
        }
    }
}
