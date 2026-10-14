namespace FMSC_SMS.Core;

/// <summary>Shared admission-style tab buttons, retaining native tab selection and shortcuts.</summary>
public static class TabStrip
{
    public static void Apply(TabControl tabs, Color accent)
    {
        tabs.Font = UiDpi.ForControl(tabs, AppTheme.ButtonFont);
        tabs.Multiline = false;
        tabs.ShowToolTips = true;
    }

    public static Control CreateHost(TabControl tabs) => new TabHost(tabs);

    public static void ApplyButtonTheme(Button button, bool active)
    {
        if (button is not ModernButton modern) return;
        modern.Font = UiDpi.ForControl(modern, AppTheme.ButtonFont);
        // Tab captions such as "Reverse a Posting" are navigation, not destructive actions.
        modern.ThemeRole = active ? "TabActive" : "TabInactive";
        modern.ApplyTheme();
    }

    public static void Repaint(TabControl tabs)
    {
        tabs.Font = UiDpi.ForControl(tabs, AppTheme.ButtonFont);
        if (tabs.Parent?.Parent is TabHost host) host.RefreshAppearance();
    }

    private sealed class TabHost : TableLayoutPanel
    {
        private readonly TabControl tabs;
        private readonly TableLayoutPanel buttons = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
        private readonly Panel viewport = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
        private readonly ToolTip hints = new();
        private readonly List<(TabPage Page, ModernButton Button)> entries = new();
        private bool arranging;
        private int columns;

        public TabHost(TabControl tabs)
        {
            this.tabs = tabs;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            ColumnCount = 1;
            RowCount = 2;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
            RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(buttons, 0, 0);
            Controls.Add(viewport, 0, 1);
            tabs.Dock = DockStyle.None;
            viewport.Controls.Add(tabs);
            tabs.SelectedIndexChanged += SelectionChanged;
            tabs.ControlAdded += PagesChanged;
            tabs.ControlRemoved += PagesChanged;
            tabs.HandleCreated += TabHandleCreated;
            tabs.EnabledChanged += PageChanged;
            viewport.SizeChanged += ViewportChanged;
            if (tabs.SelectedIndex < 0 && tabs.TabPages.Count > 0) tabs.SelectedIndex = 0;
            RebuildButtons();
        }

        private void RebuildButtons()
        {
            foreach (var entry in entries)
            {
                entry.Page.TextChanged -= PageChanged;
                entry.Page.EnabledChanged -= PageChanged;
                entry.Button.Dispose();
            }
            entries.Clear();
            foreach (TabPage page in tabs.TabPages)
            {
                var button = new ModernButton { Text = page.Text, Dock = DockStyle.Fill };
                button.Click += (_, _) => {
                    if (page.Enabled) tabs.SelectedTab = page;
                    // Selecting handlers may veto the change while a save is running.
                    RefreshAppearance();
                };
                page.TextChanged += PageChanged;
                page.EnabledChanged += PageChanged;
                entries.Add((page, button));
            }
            columns = 0;
            RefreshAppearance();
        }

        public void RefreshAppearance()
        {
            BackColor = buttons.BackColor = viewport.BackColor = AppTheme.Surface;
            for (int index = 0; index < entries.Count; index++)
            {
                var (page, button) = entries[index];
                button.Text = page.Text;
                button.Enabled = tabs.Enabled && page.Enabled;
                button.AccessibleName = page.Text;
                button.AccessibleDescription = page == tabs.SelectedTab ? "Selected tab" : "Open tab";
                ApplyButtonTheme(button, page == tabs.SelectedTab);
                string direct = index < 9 ? $"Ctrl+{index + 1}; " : string.Empty;
                page.ToolTipText = $"{page.Text} ({direct}Ctrl+Tab / Ctrl+PageUp)";
                hints.SetToolTip(button, page.ToolTipText);
            }
            ArrangeButtons();
            FitTabContent();
        }

        private void ArrangeButtons()
        {
            if (arranging || entries.Count == 0) return;
            arranging = true;
            try
            {
                int minimumWidth = entries.Max(entry => AppMetrics.TextWidth(entry.Page.Text, AppTheme.ButtonFont, AppScale.Scale(40)));
                int count = Math.Min(entries.Count, Math.Max(1, ClientSize.Width / Math.Max(1, minimumWidth)));
                int rows = (entries.Count + count - 1) / count;
                if (columns != count)
                {
                    buttons.SuspendLayout();
                    buttons.Controls.Clear();
                    buttons.ColumnStyles.Clear();
                    buttons.RowStyles.Clear();
                    buttons.ColumnCount = count;
                    buttons.RowCount = rows;
                    for (int c = 0; c < count; c++) buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / count));
                    for (int r = 0; r < rows; r++) buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
                    for (int i = 0; i < entries.Count; i++) buttons.Controls.Add(entries[i].Button, i % count, i / count);
                    columns = count;
                    buttons.ResumeLayout(true);
                }
                RowStyles[0].Height = rows * AppMetrics.ButtonRowHeight;
            }
            finally { arranging = false; }
        }

        private void FitTabContent()
        {
            if (!tabs.IsHandleCreated || viewport.ClientSize.Width <= 0 || viewport.ClientSize.Height <= 0) return;
            // Clip the native header inside the viewport. The actual TabControl still owns
            // pages, selection events, lazy loading and keyboard navigation.
            for (int pass = 0; pass < 2; pass++)
            {
                Rectangle display = tabs.DisplayRectangle;
                int right = tabs.ClientSize.Width - display.Right;
                int bottom = tabs.ClientSize.Height - display.Bottom;
                tabs.SetBounds(-display.X, -display.Y,
                    viewport.ClientSize.Width + display.X + right,
                    viewport.ClientSize.Height + display.Y + bottom);
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ArrangeButtons();
            FitTabContent();
        }
        private void SelectionChanged(object? sender, EventArgs e) => RefreshAppearance();
        private void PagesChanged(object? sender, ControlEventArgs e)
        {
            // TabPages removal completes after ControlRemoved is raised.
            if (tabs.IsHandleCreated)
                tabs.BeginInvoke(new Action(() => { if (!IsDisposed) RebuildButtons(); }));
            else RebuildButtons();
        }
        private void PageChanged(object? sender, EventArgs e) => RefreshAppearance();
        private void TabHandleCreated(object? sender, EventArgs e)
        {
            FitTabContent();
            tabs.BeginInvoke(new Action(() => { if (!IsDisposed) RefreshAppearance(); }));
        }
        private void ViewportChanged(object? sender, EventArgs e) => FitTabContent();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tabs.SelectedIndexChanged -= SelectionChanged;
                tabs.ControlAdded -= PagesChanged;
                tabs.ControlRemoved -= PagesChanged;
                tabs.HandleCreated -= TabHandleCreated;
                tabs.EnabledChanged -= PageChanged;
                viewport.SizeChanged -= ViewportChanged;
                foreach (var entry in entries)
                {
                    entry.Page.TextChanged -= PageChanged;
                    entry.Page.EnabledChanged -= PageChanged;
                }
                hints.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
