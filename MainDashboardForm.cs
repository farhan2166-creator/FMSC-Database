using FMSC_SMS.Core;
using FMSC_SMS.Pages.Administration;
using FMSC_SMS.Pages.Dashboard;
using FMSC_SMS.Pages.Finance.Billing;
using FMSC_SMS.Pages.Finance.Collection;
using FMSC_SMS.Pages.Finance.Configuration;
using FMSC_SMS.Pages.Finance.StudentAccounts;
using FMSC_SMS.Pages.Reports;
using FMSC_SMS.Pages.Setup;
using FMSC_SMS.Pages.Setup.Academics;
using FMSC_SMS.Pages.Staff;
using FMSC_SMS.Pages.Staff.Certificates;
using FMSC_SMS.Pages.Students.Admissions;
using FMSC_SMS.Pages.Students.Certificates;
using FMSC_SMS.Pages.Students.Enrollment;
using FMSC_SMS.Pages.Students.Maintenance;
using FMSC_SMS.Pages.Students.Profile;
using FMSC_SMS.Services.Backup;
namespace FMSC_SMS;
public sealed class MainDashboardForm : ApplicationWindowForm
{
    private readonly Panel content = new() {
        Dock = DockStyle.Fill,
        BackColor = AppTheme.PageBackground,
        Padding = new Padding(0)
    };
    private readonly Label status = new() {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = AppTheme.MutedText,
        Font = AppTheme.NormalFont
    };
    private Panel sidebar = null!;
    private Panel topBar = null!;
    private readonly ToolTip sidebarToolTip = new() {
        AutoPopDelay = 5000,
        InitialDelay = 350,
        ReshowDelay = 100,
        ShowAlways = true
    };
    private bool _sidebarCompact;
    private bool? _sidebarCompactOverride;
    private bool? _lastAutomaticCompactMode;
    private readonly List<DashboardNavButton> navButtons = new();
    private readonly List<NavigationCommand> navigationCommands = new();
    private readonly Dictionary<Keys,DashboardNavButton> navigationShortcutTargets=new();
    /* which module/title each sidebar entry opens, so a page reached from a Quick Action or from Open Report can highlight the same entry the sidebar would. */
    private readonly Dictionary<DashboardNavButton, string> navTargets = new();
    private Button? _commandButton;
    private ModernTextBox? _globalSearchBox;
    private Label? _topAcademicYearLabel;
    private Label? _locationLabel;
    private string _currentAcademicYearText = "Academic Year — Not Set";
    private Func<Control>? _currentFactory;
    private string _currentModule = "Dashboard";
    private string _currentTitle = "Overview";
    private bool _currentIsDashboard;
    private Control? _currentPageControl;
    private bool _navigationInProgress;
    private readonly Dictionary<string, Dictionary<string, string>> _workspaceStates =
        new(StringComparer.OrdinalIgnoreCase);
    /* Set while Sign Out is closing the window, so the unsaved-changes guard in FormClosing can clear the session only when it accepts the close. */
    private bool signOutRequested;
    private readonly Action<string, Exception>? navigationFailureReporter;
    private readonly System.Windows.Forms.Timer _scaleDebounceTimer = new() {
        Interval = 350
    };
    public MainDashboardForm() : this(null) { }
    internal MainDashboardForm(Action<string, Exception>? navigationFailureReporter)
    {
        this.navigationFailureReporter = navigationFailureReporter;
        // Every control size in this app is computed programmatically (AppMetrics/AppTheme, using DPI-aware TextRenderer measurements that already reflect the real font/DPI), never left to WinForms' built-in Font/Dpi auto-scaling.
        AutoScaleMode = AutoScaleMode.None;
        /* leaving the application is a page switch, so the same guard the in-app navigation check runs is applied on close. */
        FormClosing += (_, e) => {
            if (!ConfirmLeaveCurrentPage())
            {
                e.Cancel = true;
                return;
            }
            if (signOutRequested)
                CurrentUser.SignOut();
        };
        Text = "FMSC - School & College Management System";
        ShowIcon = true;
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(
            AppMetrics.DashboardMinimumWidth,
            AppMetrics.DashboardMinimumHeight);
        BackColor = AppTheme.PageBackground;
        // Establish the adaptive scale factor from the actual screen working area *before* any sidebar/topbar/page is built, using a deterministic estimate of the chrome this form is about to…
        PrimeInitialScale();
        // The shell establishes its metric context before constructing controls.
        CaptureLayoutDpi();
        Font = AppTheme.NormalFont;
        Build();
        KeyPreview = true;
        KeyDown += OnMainKeyDown;
    }
    internal static void PrimeInitialScale()
    {
        Rectangle workingArea =
            Screen.PrimaryScreen?.WorkingArea
            ?? new Rectangle(
                0,
                0,
                AppMetrics.DashboardMinimumWidth,
                AppMetrics.DashboardMinimumHeight);
        int dpi = UiDpi.SystemDpi;
        AppScale.Recompute(workingArea.Size, dpi);
        int sidebarWidth = workingArea.Width / DpiLayout.Factor(dpi) < AppMetrics.SidebarCompactBreakpoint
            ? AppScale.Scale(AppMetrics.SidebarCompactWidth)
            : AppScale.Scale(AppMetrics.SidebarExpandedWidth);
        var estimatedContent = new Size(
            Math.Max(1, workingArea.Width - sidebarWidth),
            Math.Max(1, workingArea.Height - DashboardTopBarHeight() - DashboardStatusBarHeight()));
        AppScale.Recompute(estimatedContent, dpi);
    }
    private void Build()
    {
        SuspendLayout();
        Controls.Clear();
        BuildSidebar();
        BuildTopBar();
        var statusBar = new Panel {
            Dock = DockStyle.Bottom,
            Height = DashboardStatusBarHeight(),
            BackColor = AppTheme.PageBackground,
            Padding = new Padding(4, 2, 4, 2)
        };
        statusBar.Controls.Add(status);
        Controls.Add(content);
        Controls.Add(statusBar);
        Controls.Add(topBar);
        Controls.Add(sidebar);
        AppTheme.ThemeChanged += OnThemeChanged;
        AppScale.ScaleChanged += OnScaleChanged;
        _scaleDebounceTimer.Tick += OnScaleDebounceTick;
        Resize += OnFormResize;

        // Recompute against the actual per-page working area (inside the sidebar/topbar/status bar chrome), not the outer window size.
        Shown += (_, _) =>
            AppScale.Recompute(content.ClientSize, DeviceDpi);
        Shown += (_, _) =>
            UpdateSidebarModeForWindowWidth();
        Shown += (_, _) =>
            _ = CheckBackupFreshnessAsync();
        Disposed += (_, _) => {
            AppTheme.ThemeChanged -= OnThemeChanged;
            AppScale.ScaleChanged -= OnScaleChanged;
            _scaleDebounceTimer.Tick -= OnScaleDebounceTick;
            _scaleDebounceTimer.Dispose();
            KeyDown -= OnMainKeyDown;
            sidebarToolTip.Dispose();
        };
        NavigateTo(
            () => new WelcomePage(
                factory => NavigateTo(factory),
                (factory, module, title) => NavigateTo(factory, module, title)),
            "Dashboard",
            "Overview");
        ResumeLayout(true);
    }
    private static int DashboardTopBarHeight() =>
        Math.Max(
            58,
            Math.Max(
                AppMetrics.TextHeight(AppTheme.PageTitleFont),
                AppMetrics.InputControlHeight) + 16);
    private static int DashboardStatusBarHeight() =>
        Math.Max(26, AppMetrics.TextHeight(AppTheme.NormalFont) + 4);
    private void OnFormResize(object? sender, EventArgs e)
    {
        UpdateSidebarModeForWindowWidth();
        _scaleDebounceTimer.Stop();
        _scaleDebounceTimer.Start();
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (e.Cancel) return;
        _scaleDebounceTimer.Stop();
        AppScale.Recompute(content.ClientSize, e.DeviceDpiNew);
        UpdateSidebarModeForWindowWidth();
    }
    private void OnScaleDebounceTick(object? sender, EventArgs e)
    {
        _scaleDebounceTimer.Stop();
        AppScale.Recompute(content.ClientSize, DeviceDpi);
    }
    private const int BackupStaleWarningDays = 14;
    private bool _backupFreshnessChecked;
    /// <summary>One-time-per-session check, shown after the dashboard first loads, that nudges whoever can create backups to take one if the most recent successful backup is missing or older than <see cref="BackupStaleWarningDays"/> days.</summary>
    private async Task CheckBackupFreshnessAsync()
    {
        if (_backupFreshnessChecked)
            return;
        _backupFreshnessChecked = true;
        if (CurrentUser.UserId <= 0 || !CurrentUser.HasPermission("Backup.Create"))
            return;
        try
        {
            DataTable history = await Task.Run(DatabaseBackupManager.GetHistory);
            if (IsDisposed)
                return;
            // The history grid's exact date-column name isn't relied on here (schema not guaranteed).
            DateTime? lastSuccess = history.AsEnumerable()
                .Where(r =>
                    history.Columns.Contains("Operation") &&
                    history.Columns.Contains("Status") &&
                    history.Columns.Contains("FilePath") &&
                    string.Equals(Convert.ToString(r["Operation"]), "BACKUP", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Convert.ToString(r["Status"]), "Success", StringComparison.OrdinalIgnoreCase) &&
                    r["FilePath"] != DBNull.Value &&
                    File.Exists(Convert.ToString(r["FilePath"])))
                .Select(r => (DateTime?)File.GetLastWriteTime(Convert.ToString(r["FilePath"])!))
                .OrderByDescending(d => d)
                .FirstOrDefault();
            bool stale = lastSuccess is null ||
                (DateTime.Now - lastSuccess.Value).TotalDays > BackupStaleWarningDays;
            if (!stale)
                return;
            string message = lastSuccess is null
                ? "No successful database backup has been recorded yet. Everything entered " +
                  "into FMSC - admissions, fees - only exists on this " +
                  "computer until a backup is taken."
                : $"The last successful database backup was {(DateTime.Now - lastSuccess.Value).Days} " +
                  "day(s) ago. It's a good idea to take a fresh backup regularly.";
            ModernMessageDialog.Show(
                this,
                message + "\n\nGo to System Maintenance > Backup / Restore to create one now.",
                "Backup Reminder",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            // Best-effort reminder only - never let this interrupt startup.
            AppExceptionHandler.LogNonFatal(ex, "Backup Freshness Check");
        }
    }
    private void OnScaleChanged(object? sender, EventArgs e)
    {
        AppMetrics.InvalidateScaleCache();
        Font = UiDpi.ForControl(this, AppTheme.NormalFont);
        status.Font = UiDpi.ForControl(status, AppTheme.NormalFont);
        RebuildSidebar();
        if (_currentIsDashboard && _currentFactory != null)
        {
            NavigateTo(
                _currentFactory,
                _currentModule,
                _currentTitle);
        }
        else if (_currentPageControl != null)
        {
            _currentPageControl.SuspendLayout();
            try
            {
                UiHelper.RefreshControlsForScaleChange(_currentPageControl);
                if (_currentPageControl is V2Page v2Page)
                    v2Page.ApplyScale();
                else if (_currentPageControl is WorkspacePage workspacePage)
                    workspacePage.ApplyScale();
            }
            finally
            {
                _currentPageControl.ResumeLayout(true);
            }
        }
        Invalidate(true);
    }
    private int SidebarWidth =>
        _sidebarCompact
            ? AppScale.Scale(AppMetrics.SidebarCompactWidth)
            : AppScale.Scale(AppMetrics.SidebarExpandedWidth);
    private int NavContentWidth => SidebarWidth - AppScale.Scale(20);
    private int ModuleRowWidth => NavContentWidth;
    private int ChildRowWidth => ModuleRowWidth - AppScale.Scale(18);
    private void UpdateSidebarModeForWindowWidth()
    {
        if (!IsHandleCreated || ClientSize.Width <= 0 || DeviceDpi <= 0)
            return;
        // PerMonitorV2 ClientSize is in device pixels; use logical width for this breakpoint.
        bool automaticCompact =
            ClientSize.Width / DpiLayout.Factor(DeviceDpi) < AppMetrics.SidebarCompactBreakpoint;
        if (_lastAutomaticCompactMode != automaticCompact)
        {
            _lastAutomaticCompactMode = automaticCompact;
            _sidebarCompactOverride = null;
        }
        SetSidebarCompact(_sidebarCompactOverride ?? automaticCompact);
    }
    private void SetSidebarCompact(bool compact, bool manual = false)
    {
        if (manual)
            _sidebarCompactOverride = compact;
        if (_sidebarCompact == compact)
            return;
        _sidebarCompact = compact;
        if (sidebar != null)
            RebuildSidebar();
    }
    private void OnMainKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyData == (Keys.Control | Keys.K))
        {
            e.SuppressKeyPress = true;
            ShowCommandPalette();
        }
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {
        if(navigationShortcutTargets.TryGetValue(keyData,out var target) && !target.IsDisposed && target.Enabled) {
            if(target.IsModule && _sidebarCompact) {
                SetSidebarCompact(false,manual:true);
                navigationShortcutTargets.TryGetValue(keyData,out target);
            }
            if(target is not null) {
                if(!target.IsModule || !target.IsExpanded)target.PerformClick();
                if(target.IsModule)target.Focus();
            }
            return true;
        }
        if(keyData==(Keys.Control|Keys.K)) {ShowCommandPalette();return true;}
        if(keyData == Keys.F1) { OpenHotKeysPageAfterAccountMenuCloses(); return true; }
        return base.ProcessCmdKey(ref msg,keyData);
    }
    private void RegisterNavigationShortcut(DashboardNavButton button,string caption)
    {
        var shortcut=AppHotKeys.DashboardNavigation.FirstOrDefault(s=>s.Caption==caption.Replace("&&","&"));
        if(shortcut.Caption is null)return;
        navigationShortcutTargets[shortcut.Keys]=button;
        sidebarToolTip.SetToolTip(button,$"{shortcut.Caption} ({shortcut.Hint})");
        ShortcutController.RegisterShortcut(button,shortcut.Keys,shortcut.Hint,()=>button.PerformClick());
    }
    private void ShowCommandPalette(string? query = null)
    {
        using var dialog = new CommandPaletteDialog(navigationCommands, query);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedCommand != null)
            dialog.SelectedCommand.Execute();
        ReleaseTopBarFocus();
    }
    /* The search box and the Ctrl+K button are shortcuts, not places to work. */
    private void ReleaseTopBarFocus()
    {
        if (IsDisposed || topBar == null || ActiveControl is not Control active ||
            !(active == topBar || topBar.Contains(active)))
        {
            return;
        }
        Control? next = GetNextControl(active, true);
        while (next != null && (topBar.Contains(next) || !next.TabStop || !next.CanFocus))
            next = GetNextControl(next, true);
        if (next != null)
            next.Focus();
        else
            ActiveControl = null;
    }
    private void BuildTopBar()
    {
        if (topBar == null)
        {
            topBar = new Panel {
                Dock = DockStyle.Top,
                Height = DashboardTopBarHeight(),
                BackColor = AppTheme.Surface,
                Padding = new Padding(18, 8, 16, 8)
            };
        }
        else
        {
            topBar.Height = DashboardTopBarHeight();
            foreach (Control oldChild in topBar.Controls.Cast<Control>().ToArray())
            {
                topBar.Controls.Remove(oldChild);
                oldChild.Dispose();
            }
            _globalSearchBox = null;
            _commandButton = null;
        }
        var header = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        header.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(
            new ColumnStyle(SizeType.AutoSize));
        var titlePanel = new Panel {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        var titleLabel = new Label {
            Name = "CurrentLocation",
            AccessibleName = "Current location",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = AppTheme.PageTitleFont,
            ForeColor = AppTheme.Text,
            Margin = new Padding(0)
        };
        titlePanel.Controls.Add(titleLabel);
        _locationLabel = titleLabel;
        var right = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = Math.Max(42, topBar.Height - 16),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        header.Controls.Add(titlePanel, 0, 0);
        header.Controls.Add(right, 1, 0);
        _globalSearchBox = new ModernTextBox {
            Width = AppScale.Scale(250, AppScale.ControlSizeScale),
            Height = AppMetrics.InputControlHeight,
            Margin = new Padding(6, 2, 8, 2),
            /* This box only opens Quick Navigation: it searches pages and features, never students, so the caption says so (it used to promise "students"). */
            PlaceholderText = "Search features...",
            /* Out of the Tab order: it is only reached by clicking it, never by tabbing through the form or by the form choosing a first control to focus. */
            TabStop = false
        };
        /* Feature / page names are text, so the box takes no digits: typed digits are refused (with a beep) and any that arrive by paste are stripped. */
        _globalSearchBox.KeyPress += (_, e) => {
            if (!char.IsDigit(e.KeyChar))
                return;
            e.Handled = true;
            System.Media.SystemSounds.Beep.Play();
        };
        _globalSearchBox.TextChanged += (_, _) => {
            string text = _globalSearchBox.Text;
            string clean = CommandPaletteDialog.StripDigits(text);
            if (string.Equals(clean, text, StringComparison.Ordinal))
                return;
            int caret = Math.Max(0, _globalSearchBox.SelectionStart - (text.Length - clean.Length));
            _globalSearchBox.Text = clean;
            _globalSearchBox.SelectionStart = Math.Min(caret, clean.Length);
        };
        _globalSearchBox.KeyDown += (_, e) => {
            if (e.KeyCode != Keys.Enter)
                return;
            e.SuppressKeyPress = true;
            ShowCommandPalette(_globalSearchBox.Text.Trim());
        };
        _commandButton = new ModernButton {
            Text = "⌘  Ctrl+K",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = AppMetrics.ButtonHeight,
            Margin = new Padding(0, 1, 6, 1),
            ThemeRole = "Secondary",
            /* Never takes focus: Tab skips it and clicking it does not pull the focus away from the field being typed in. */
            TakesFocus = false,
            Image = UiHelper.CreateGlyphIcon("⌕", AppTheme.MutedText, 18),
            ImageAlign = ContentAlignment.MiddleLeft,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Padding = new Padding(10, 0, 10, 0)
        };
        UiHelper.DisposeOwnedButtonImageOnDispose(_commandButton);
        _commandButton.Click += (_, _) =>
            ShowCommandPalette(_globalSearchBox?.Text.Trim());
        var today = new Label {
            Name = "TopBarDate",
            AutoSize = true,
            MinimumSize = new Size(
                AppScale.Scale(92, AppScale.ControlSizeScale),
                AppMetrics.ButtonHeight),
            Height = AppMetrics.ButtonHeight,
            Font = AppTheme.FieldCaptionFont,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = DateTime.Today.ToString("ddd, dd MMM yyyy"),
            Margin = new Padding(0, 0, 8, 0)
        };
        _topAcademicYearLabel = new Label {
            Name = "TopAcademicYear",
            AutoSize = true,
            MinimumSize = new Size(
                AppScale.Scale(125, AppScale.ControlSizeScale),
                AppMetrics.ButtonHeight),
            Height = AppMetrics.ButtonHeight,
            Font = AppTheme.ButtonFont,
            ForeColor = AppTheme.Primary,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            Text = _currentAcademicYearText,
            Margin = new Padding(0, 0, 8, 0)
        };
        right.Controls.Add(_globalSearchBox);
        right.Controls.Add(_commandButton);
        right.Controls.Add(today);
        right.Controls.Add(_topAcademicYearLabel);
        var divider = new Panel {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = AppTheme.Border
        };
        topBar.Controls.Add(header);
        topBar.Controls.Add(divider);
        UpdateTopBar(_currentModule, _currentTitle);
    }
    private void UpdateTopBar(string module, string title)
    {
        if (_locationLabel is null || _locationLabel.IsDisposed) return;
        string location = NavigationLocation.Format(module, title,
            (_currentPageControl as WorkspacePage)?.CurrentSection);
        _locationLabel.Text = location;
        sidebarToolTip.SetToolTip(_locationLabel, location);
    }
    private void OnCurrentSectionChanged(object? sender, EventArgs e) => UpdateTopBar(_currentModule, _currentTitle);
    internal void UpdateTopAcademicYearLabel(WelcomePage source, string text)
    {
        if (!ReferenceEquals(source, _currentPageControl) || source.IsDisposed) return;
        _currentAcademicYearText = string.IsNullOrWhiteSpace(text) ? "Academic Year — Not Set" : text;
        if (_topAcademicYearLabel is null) return;
        _topAcademicYearLabel.Text = _currentAcademicYearText;
        _topAcademicYearLabel.ForeColor = AppTheme.Primary;
    }
    private void BuildSidebar()
    {
        if (sidebar == null)
        {
            sidebar = new Panel {
                Dock = DockStyle.Left,
                Width = SidebarWidth,
                BackColor = AppTheme.MenuBackground,
                Padding = new Padding(0),
                BorderStyle = BorderStyle.None
            };
        }
        else
        {
            sidebar.Width = SidebarWidth;
            var oldControls = sidebar.Controls.Cast<Control>().ToArray();
            sidebar.Controls.Clear();
            foreach (var oldControl in oldControls)
                oldControl.Dispose();
        }
        var brand = BuildBrand();
        var user = BuildUserCard();
        var navigation = new Panel {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = AppTheme.MenuBackground,
            Padding = new Padding(12, 10, 12, 10)
        };
        var flow = new FlowLayoutPanel {
            Dock = DockStyle.Top,
            Width = NavContentWidth,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        AddMainNav(
            flow,
            "dashboard.png",
            "Dashboard",
            AppTheme.Primary,
            () => new WelcomePage(
                factory => NavigateTo(factory),
                (factory, module, title) => NavigateTo(factory, module, title)),
            "Dashboard",
            "Overview",
            fontOverride: AppTheme.HeadingFont);
        // One operator uses the whole application. Keep the common daily entry points
        // together and open this group by default; periodic and setup work stays collapsed.
        AddModule(
            flow,
            "academics.png",
            "Daily Work",
            AppTheme.Primary,
            new NavigationItem[]
            {
                new(
                    PageTitles.Admissions,
                    () => new StudentAdmissionPage(
                        (factory, module, title) => NavigateTo(factory, module, title)),
                    "Students",
                    PageTitles.Admissions,
                    "Students.Admit"),
                new(
                    "Student Profile",
                    () => new StudentProfilePage(
                        (factory, module, title) => NavigateTo(factory, module, title)),
                    "Students",
                    "Student Profile",
                    "Students.View"),
                new(
                    "Fee Collection",
                    () => new FeeCollectionPage(),
                    "Finance",
                    "Fee Collection",
                    "Fees.Collect"),
                new(
                    PageTitles.StudentPurchases,
                    () => new StudentPurchasesPage(),
                    "Finance",
                    PageTitles.StudentPurchases,
                    "Fees.Collect"),
                new(
                    PageTitles.StudentCertificates,
                    () => new StudentCertificatesPage(
                        (factory, module, title) => NavigateTo(factory, module, title)),
                    "Certificates",
                    PageTitles.StudentCertificates,
                    "Certificates.Create")
            },
            expandInitially: true);
        AddModule(
            flow,
            "students.png",
            "Student Management",
            AppTheme.Primary,
            new NavigationItem[]
            {
            Section("STUDENT DETAILS"),
            new(
                "Family / Siblings",
                () => new StudentFamilyPage(),
                "Students",
                "Family / Siblings",
                "Students.Family"),
            Section("PROMOTION"),
            new(
                "Bulk Promotion",
                () => new BulkPromotionPage(
                    (factory, module, title) => NavigateTo(factory, module, title)),
                "Promotion",
                "Bulk Promotion",
                "Enrollment.Manage"),
            new(
                "Promotion History",
                () => new PromotionHistoryPage(),
                "Promotion",
                "Promotion History",
                "Enrollment.Manage"),
            Section("ADMISSION & DATA REVIEW"),
            new(
                PageTitles.StudentRecordChecks,
                () => new StudentDataReviewPage((factory, module, title) => NavigateTo(factory, module, title)),
                "Students",
                PageTitles.StudentRecordChecks,
                "Students.View"),
            });
        AddModule(
            flow,
            "finance.png",
            "Fees & Billing",
            AppTheme.Primary,
            new NavigationItem[]
            {
            // Keep adjustments separate from daily collection because corrections, refunds and reversals change money already recorded.
            new(
                PageTitles.StudentFeeCorrections,
                () => new StudentFeeAdjustmentsPage(),
                "Finance",
                PageTitles.StudentFeeCorrections,
                "Fees.Collect"),
            // Manual monthly posting and its reversal: the operator picks the month and posts the fees they choose, or reverses a posting that has not been collected.
            new(
                "Daily Collection Closing",
                () => new DailyCollectionClosingPage(),
                "Finance",
                "Daily Collection Closing",
                "Fees.Collect"),
            new(
                PageTitles.FeePosting,
                () => FeePostingWorkspace.Create(),
                "Finance",
                PageTitles.FeePosting,
                "Fees.Collect"),
            Section("FEE SETUP"),
            new(
                PageTitles.FeeRateSetup,
                () => FeeConfigurationWorkspace.Create(),
                "Finance",
                PageTitles.FeeRateSetup,
                "Fees.Configuration.Manage"),
                        });
        AddModule(
            flow,
            "staff.png",
            "Staff",
            AppTheme.Primary,
            new NavigationItem[]
            {
            new(
                "Staff Management",
                () => new StaffManagementPage(),
                "Staff",
                "Staff Management",
                "Staff.View"),
            Section("CERTIFICATES"),
            // BUSINESS RULE: staff certificates belong with staff work. Do not duplicate
            // this route in the student Certificates Center.
            new(
                "Teacher Certificate",
                () => new RegularLecturerCertificatePage(),
                "Staff",
                "Regular Lecturer/Teacher Certificate",
                "Certificates.Create"),
            new(
                "Bank Account Certificate",
                () => new BankAccountOpeningCertificatePage(),
                "Staff",
                "Bank Account Opening Certificate",
                "Certificates.Create")
            });
        // Reports opens a single hub, like Certificates Center.
        AddMainNav(
            flow,
            "reports.png",
            "Reports",
            AppTheme.Primary,
            BuildReportsWorkspace,
            "Reports",
            "Reports",
            "Reports.View");
        // User and role administration lives in the permission-controlled Account menu.
        AddModule(
            flow,
            "setup.png",
            "School Setup",
            AppTheme.Primary,
            new NavigationItem[]
            {
            // Setup follows the order in which school data is normally entered.
            new(
                "Setup Readiness",
                () => new SetupReadinessPage((factory, module, title) => NavigateTo(factory, module, title)),
                "School Setup",
                "Setup Readiness",
                FMSC_SMS.Services.SetupReadiness.NavigationPermission),
            new(
                "School Profile",
                () => new WorkspacePage(
                    "School Profile",
                    "School identity used on reports, certificates, receipts and letters.",
                    ("School Information", () => new SchoolProfilePage()),
                    ("School Officials", () => new SchoolOfficialsPage())),
                "School Setup",
                "School Profile",
                "System.Setup"),
            new(
                "Campuses",
                () => new CampusesPage(),
                "School Setup",
                "Campuses",
                "System.Setup"),
            new(
                "Academic Years",
                () => new AcademicYearsPage(),
                "School Setup",
                "Academic Years",
                "Calendar.Manage"),
            new(
                "Classes & Sections",
                () => new ClassesAndSectionsWorkspacePage(),
                "School Setup",
                PageTitles.ClassesSections,
                "System.Setup"),
            new(
                PageTitles.CampusAssignments,
                () => new CampusClassSectionAssignmentsPage(),
                "School Setup",
                PageTitles.CampusAssignments,
                "System.Setup"),
                new(
                "Subjects",
                () => new SubjectsPage(),
                "School Setup",
                "Subjects",
                "System.Setup"),
            });
        AddModule(
            flow,
            "administration.png",
            "System Maintenance",
            AppTheme.Primary,
            new NavigationItem[]
            {
            new(
                "Audit Logs",
                () => new AuditLogsPage(),
                "System Maintenance",
                "Audit Logs",
                "System.Audit"),
            new(
                PageTitles.BackupRestore,
                () => new BackupRestorePage(),
                "System Maintenance",
                PageTitles.BackupRestore,
                "Backup.Create")
            });
        navigation.Controls.Add(flow);
        var account = BuildAccountPanel();
        sidebar.Controls.Add(navigation);
        sidebar.Controls.Add(account);
        sidebar.Controls.Add(user);
        sidebar.Controls.Add(brand);
    }
    private void AddMainNav(
        FlowLayoutPanel flow,
        string iconFile,
        string text,
        Color accent,
        Func<Control> factory,
        string module,
        string title,
        string? permission = null,
        Font? fontOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(permission) &&
            !CurrentUser.HasPermission(permission))
            return;
        var button = CreateNavButton(
            iconFile,
            text,
            accent,
            false,
            fontOverride);
        var command = new NavigationCommand(AppHotKeys.PlainCaption(text), () => {
            if (string.IsNullOrWhiteSpace(permission) || CurrentUser.HasPermission(permission))
                NavigateTo(factory, module, title);
        });
        navigationCommands.Add(command);
        button.Click += (_, _) => command.Execute();
        navTargets[button] = NavKey(module, title);
        navButtons.Add(button);
        flow.Controls.Add(button);
        RegisterNavigationShortcut(button,text);
    }
    private void AddModule(
        FlowLayoutPanel flow,
        string iconFile,
        string text,
        Color accent,
        NavigationItem[] items,
        bool expandInitially = false)
    {
        // A section label is kept only when at least one entry after it (up to the next label) is visible to this user.
        var visibleItems = new List<NavigationItem>();
        NavigationItem? pendingSection = null;
        foreach (var item in items)
        {
            if (item.IsHeading)
            {
                pendingSection = item;
                continue;
            }
            if (!string.IsNullOrWhiteSpace(item.Permission) &&
                !CurrentUser.HasPermission(item.Permission))
            {
                continue;
            }
            if (pendingSection is not null)
            {
                visibleItems.Add(pendingSection);
                pendingSection = null;
            }
            visibleItems.Add(item);
        }
        if (visibleItems.Count == 0)
            return;
        var container = new Panel {
            Width = ModuleRowWidth,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 2),
            Padding = new Padding(0),
            BackColor = AppTheme.MenuBackground
        };
        var moduleAccent = AppTheme.ModuleAccent(text);
        // Main module keeps its icon.
        var header = CreateNavButton(
            iconFile,
            text,
            moduleAccent,
            true);
        container.Controls.Add(header);
        var children = new FlowLayoutPanel {
            Location = new Point(0, header.Bottom),
            Width = ModuleRowWidth,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(18, 2, 0, 4),
            Margin = new Padding(0),
            BackColor = AppTheme.MenuHover,
            Visible = expandInitially && !_sidebarCompact
        };
        foreach (NavigationItem item in visibleItems)
        {
            if (item.IsHeading)
            {
                var sectionLabel = new Label {
                    Text = item.Text.ToUpperInvariant(),
                    AutoSize = false,
                    Width = ChildRowWidth,
                    Height = AppMetrics.SidebarChildHeight * 3 / 5,
                    Margin = new Padding(0, 6, 0, 0),
                    Padding = new Padding(8, 0, 0, 2),
                    TextAlign = ContentAlignment.BottomLeft,
                    Font = AppTheme.FontSmallBold,
                    ForeColor = moduleAccent,
                    BackColor = AppTheme.MenuHover
                };
                children.Controls.Add(sectionLabel);
                continue;
            }
            // Submenu items intentionally have NO icon.
            var child = CreateChildNavButton(
                item.Text,
                moduleAccent);
            sidebarToolTip.SetToolTip(child, item.Title switch
            {
                PageTitles.Admissions => "Admissions, section/class/campus transfers and readmission. Confirm Return appears when a student is awaiting return.",
                "Staff Management" => "Staff details and records. Open Employment for a selected staff member.",
                _ => item.Title
            });
            var command = new NavigationCommand(AppHotKeys.PlainCaption(item.Text), () => {
                if (!string.IsNullOrWhiteSpace(item.Permission) && !CurrentUser.HasPermission(item.Permission)) return;
                NavigateTo(
                    item.Factory,
                    item.Module,
                    item.Title);
            });
            navigationCommands.Add(command);
            child.Click += (_, _) => command.Execute();
            navTargets[child] = NavKey(item.Module, item.Title);
            navButtons.Add(child);
            children.Controls.Add(child);
        }
        container.Controls.Add(children);
        header.IsExpanded = children.Visible;
        header.Click += (_, _) => {
            if (_sidebarCompact)
            {
                SetSidebarCompact(false, manual: true);
                return;
            }
            bool shouldExpand = !children.Visible;
            foreach (Control control in flow.Controls)
            {
                if (control is not Panel otherContainer ||
                    ReferenceEquals(otherContainer, container))
                {
                    continue;
                }
                foreach (Control childControl in otherContainer.Controls)
                {
                    if (childControl is not DashboardNavButton otherHeader)
                        continue;
                    if (!otherHeader.IsModule)
                        continue;
                    foreach (Control moduleControl in otherContainer.Controls)
                    {
                        if (moduleControl is FlowLayoutPanel otherChildren)
                        {
                            otherChildren.Visible = false;
                            otherHeader.IsExpanded = false;
                            break;
                        }
                    }
                    break;
                }
            }
            children.Visible = shouldExpand;
            header.IsExpanded = shouldExpand;
            container.PerformLayout();
            flow.PerformLayout();
            if (shouldExpand)
                EnsureVisible(container, flow);
        };
        flow.Controls.Add(container);
        RegisterNavigationShortcut(header,text);
    }
    private sealed record NavigationItem(
    string Text,
    Func<Control> Factory,
    string Module,
    string Title,
    string? Permission = null,
    bool IsHeading = false);
    /// <summary>A small non-clickable label that groups the entries under it (see the Finance module).</summary>
    private static NavigationItem Section(string text) =>
        new(text, () => new Panel(), string.Empty, string.Empty, null, true);
    private DashboardNavButton CreateNavButton(
    string iconFile,
    string text,
    Color accent,
    bool module,
    Font? fontOverride = null)
    {
        var button = new DashboardNavButton {
            Text = _sidebarCompact ? string.Empty : text,
            Width = ModuleRowWidth,
            Height = AppMetrics.SidebarModuleHeight,
            Margin = new Padding(0, 2, 0, 2),
            AccentColor = accent,
            Font = fontOverride
                ?? AppTheme.ButtonFont,
            IsModule = module,
            ShowChevron = module && !_sidebarCompact,
            Image = LoadSidebarIcon(iconFile, accent),
            ImageAlign = _sidebarCompact
                ? ContentAlignment.MiddleCenter
                : ContentAlignment.MiddleLeft,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = _sidebarCompact
                ? Padding.Empty
                : new Padding(10, 0, 30, 0)
        };
        sidebarToolTip.SetToolTip(button, text);
        return button;
    }
    private DashboardNavButton CreateChildNavButton(
        string text,
        Color accent)
    {
        return new DashboardNavButton {
            Text = text,
            Width = ChildRowWidth,
            Height = AppMetrics.SidebarChildHeight,
            Margin = new Padding(0, 1, 0, 1),
            AccentColor = accent,
            Font = AppTheme.NormalFont,
            IsChild = true,
            // No icon for submenu items.
            Image = null,
            ImageAlign = ContentAlignment.MiddleLeft,
            TextImageRelation = TextImageRelation.Overlay,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0)
        };
    }
    private static Bitmap? LoadSidebarIcon(string fileName, Color accent)
    {
        string? glyph = fileName switch {
            "dashboard.png" => "\uE80F",
            "academics.png" => "\uE787",
            "students.png" => "\uE716",
            "finance.png" => "\uE8EF",
            "staff.png" => "\uE77B",
            "reports.png" => "\uE9D9",
            "setup.png" => "\uE713",
            "administration.png" => "\uE90F",
            "promotion.png" => "\uE8FB",
            _ => null
        };
        if (glyph is not null)
        {
            int size = AppMetrics.SidebarIconSize;
            var image = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(image);
            graphics.Clear(Color.Transparent);
            using var font = new Font("Segoe MDL2 Assets", size - 4, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(accent);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), format);
            return image;
        }
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            fileName);
        if (!File.Exists(path))
            return null;
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var source = new Bitmap(stream);
            var icon = new Bitmap(
                AppMetrics.SidebarIconSize,
                AppMetrics.SidebarIconSize,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(icon))
            {
                graphics.Clear(Color.Transparent);
                graphics.InterpolationMode =
                    System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode =
                    System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.PixelOffsetMode =
                    System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.DrawImage(
                    source,
                    new Rectangle(
                        1,
                        1,
                        AppMetrics.SidebarIconSize - 2,
                        AppMetrics.SidebarIconSize - 2));
            }
            return icon;
        }
        catch (Exception nonFatalException84)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException84, "MainDashboardForm: LoadSidebarIcon fallback");
            return null;
        }
    }
    private static void EnsureVisible(
        Control control,
        FlowLayoutPanel flow)
    {
        try
        {
            flow.ScrollControlIntoView(control);
        }
        catch (Exception nonFatalException85)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException85, "MainDashboardForm: EnsureVisible fallback");
        }
    }
    private Panel BuildBrand()
    {
        var panel = new Panel {
            Dock = DockStyle.Top,
            Height = AppMetrics.SidebarBrandHeight,
            BackColor = AppTheme.MenuBackground,
            Padding = _sidebarCompact
                ? Padding.Empty
                : new Padding(16, 12, 12, 8)
        };
        var logo = new PictureBox {
            Width = AppMetrics.SidebarLogoSize,
            Height = AppMetrics.SidebarLogoSize,
            Location = new Point(AppScale.Scale(_sidebarCompact ? 15 : 16), AppScale.Scale(14)),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        UiHelper.DisposePictureImageOnDispose(logo);
        string logoPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "logo.png");
        if (File.Exists(logoPath))
        {
            try
            {
                logo.Image = UiHelper.LoadImageUnlocked(logoPath);
            }
            catch (Exception nonFatalException86)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException86, "MainDashboardForm: BuildBrand fallback");
            }
        }
        panel.Controls.Add(logo);
        if (!_sidebarCompact)
        {
            panel.Controls.Add(new Label {
                Text = "FMSC",
                Location = new Point(AppScale.Scale(66), AppScale.Scale(14)),
                Size = new Size(AppScale.Scale(155), AppScale.Scale(42)),
                Font = AppTheme.HeadingFont,
                ForeColor = AppTheme.Text,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            });
        }
        var toggle = new Button {
            Text = _sidebarCompact ? "›" : "‹",
            FlatStyle = FlatStyle.Flat,
            BackColor = AppTheme.MenuBackground,
            ForeColor = AppTheme.MutedText,
            Font = AppTheme.HeadingFont,
            Size = new Size(
                AppMetrics.SidebarToggleSize,
                AppMetrics.SidebarToggleSize),
            Location = new Point(
                _sidebarCompact ? 20 : SidebarWidth - 46,
                18),
            Cursor = Cursors.Hand,
            AccessibleName = _sidebarCompact
                ? "Expand navigation"
                : "Collapse navigation"
        };
        toggle.FlatAppearance.BorderSize = 0;
        sidebarToolTip.SetToolTip(toggle, toggle.AccessibleName);
        toggle.Click += (_, _) =>
            SetSidebarCompact(!_sidebarCompact, manual: true);
        panel.Controls.Add(toggle);
        return panel;
    }
    private Panel BuildUserCard()
    {
        var panel = new Panel {
            Dock = DockStyle.Bottom,
            Height = _sidebarCompact
                ? AppMetrics.SidebarCompactUserHeight
                : AppMetrics.SidebarUserHeight,
            BackColor = AppTheme.MenuCardBackground,
            Padding = new Padding(16, 8, 12, 8)
        };
        var avatar = new RoundedPanel {
            Width = AppMetrics.SidebarAvatarSize,
            Height = AppMetrics.SidebarAvatarSize,
            Location = new Point(
                _sidebarCompact ? 17 : 16,
                _sidebarCompact ? 8 : 15),
            BackColor = AppTheme.PrimaryLight,
            BorderColor = AppTheme.PrimaryLight,
            ShowBorder = false,
            CornerRadius = 19
        };
        avatar.Controls.Add(new Label {
            Text = GetUserInitial(),
            Dock = DockStyle.Fill,
            Font = AppTheme.HeadingFont,
            ForeColor = AppTheme.Primary,
            TextAlign = ContentAlignment.MiddleCenter
        });
        panel.Controls.Add(avatar);
        if (!_sidebarCompact)
        {
            var identity = new TableLayoutPanel {
                Location = new Point(AppScale.Scale(62), AppScale.Scale(10)),
                Size = new Size(Math.Max(AppScale.Scale(120), SidebarWidth - AppScale.Scale(92)), AppScale.Scale(48)),
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            identity.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
            identity.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
            identity.Controls.Add(new Label {
                Text = string.IsNullOrWhiteSpace(CurrentUser.Username)
                    ? "Account"
                    : CurrentUser.Username,
                Dock = DockStyle.Fill,
                Font = AppTheme.ButtonFont,
                ForeColor = AppTheme.Text,
                TextAlign = ContentAlignment.BottomLeft,
                AutoEllipsis = true,
                Margin = Padding.Empty
            }, 0, 0);
            identity.Controls.Add(new Label {
                Text = string.IsNullOrWhiteSpace(CurrentUser.RoleName)
                    ? "User"
                    : CurrentUser.RoleName,
                Dock = DockStyle.Fill,
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.MutedText,
                TextAlign = ContentAlignment.TopLeft,
                AutoEllipsis = true,
                Margin = Padding.Empty
            }, 0, 1);
            panel.Controls.Add(identity);
        }
        sidebarToolTip.SetToolTip(avatar, $"{CurrentUser.Username} · {CurrentUser.RoleName}");
        return panel;
    }
    private Panel BuildAccountPanel()
    {
        var panel = new Panel {
            Dock = DockStyle.Bottom,
            Height = AppMetrics.SidebarAccountHeight,
            BackColor = AppTheme.MenuBackground,
            Padding = new Padding(12, 8, 12, 12)
        };
        var line = new Panel {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = AppTheme.MenuSeparator
        };
        panel.Controls.Add(line);
        var button = new ModernButton {
            Text = _sidebarCompact ? "⚙" : "⚙  Account",
            Dock = DockStyle.Top,
            Height = AppMetrics.ButtonHeight,
            ThemeRole = "Secondary"
        };
        button.Click += (_, _) =>
            ShowAccountMenu(button);
        sidebarToolTip.SetToolTip(button, "Account menu");
        panel.Controls.Add(button);
        return panel;
    }
    private void ShowAccountMenu(Control owner)
    {
        var menu = new StyledContextMenu {
            Font = AppTheme.NormalFont
        };
        menu.Items.Add(
            new ToolStripMenuItem(
                "Change Username",
                null,
                (_, _) => {
                    using var form = new ChangeUsernameForm();
                    /* The sidebar is rebuilt only when the rename actually happened. */
                    if (form.ShowDialog(this) == DialogResult.OK)
                        RebuildSidebar();
                }));
        menu.Items.Add(
            new ToolStripMenuItem(
                "Change Password",
                null,
                (_, _) => {
                    using var form = new ChangePasswordForm();
                    form.ShowDialog(this);
                }));
        var access = AccessRules.For(CurrentUser.HasPermission);
        if (access.CanReadUsers || access.CanReadRoles)
        {
            menu.Items.Add(new ToolStripSeparator());
            if (access.CanReadUsers)
                menu.Items.Add(new ToolStripMenuItem("Users", null,
                    (_, _) => OpenAccountAdministrationAfterMenuCloses(AccessEditorKind.Users)) {
                    ToolTipText = access.CanManageUsers
                        ? "Create user accounts, assign Accountant access and reset passwords."
                        : "View user accounts and their assigned access."
                });
            if (access.CanReadRoles)
                menu.Items.Add(new ToolStripMenuItem("Roles && Permissions", null,
                    (_, _) => OpenAccountAdministrationAfterMenuCloses(AccessEditorKind.Roles)) {
                    ToolTipText = "Review Accountant permissions. Administrator always has full authority."
                });
            menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add(
            new ToolStripMenuItem(
                "Hot Keys",
                null,
                (_, _) => OpenHotKeysPageAfterAccountMenuCloses()));
        menu.Items.Add(new ToolStripMenuItem("Database - Connection Help", null, (_, _) => {
            using var connection = new DatabaseConnectionForm(startup: false);
            connection.ShowDialog(this);
        }));
        var theme =
            new ToolStripMenuItem(
                "Change - Appearance/Theme");
        AppThemeType? previousFamily=null;
        foreach (AppThemeType value in AppTheme.SelectableThemes)
        {
            var family=ThemeAppearance.BaseTheme(value);
            if(previousFamily is not null&&previousFamily!=family)
                theme.DropDownItems.Add(new ToolStripSeparator());
            previousFamily=family;
            var item =
                new ToolStripMenuItem(
                    AppTheme.GetThemeName(value)) {
                    Checked =
                        AppTheme.CurrentTheme == value,
                    ToolTipText = AppTheme.IsLightweight(value)
                        ? "Strong colors, flat controls and instant feedback. The same layout and workflows, with fewer visual effects."
                        : "Original themed appearance with rounded controls and decorative effects."
                };
            item.Click += (_, _) =>
                ChangeAppearanceAfterAccountMenuCloses(value);
            theme.DropDownItems.Add(item);
        }
        menu.Items.Add(theme);
        menu.Items.Add(new ToolStripMenuItem("About FMSC", null, (_, _) => {
            using var about = new AboutForm();
            about.ShowDialog(this);
        }));
        menu.Items.Add(
            new ToolStripSeparator());
        menu.Items.Add(
            new ToolStripMenuItem(
                "Sign Out",
                null,
                (_, _) => SignOut()));
        menu.Closed += (_, _) => {
            /* ToolStripDropDown still accesses its handle after Closed while completing an
             * item click. Immediate disposal here broke every account-menu command. */
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new MethodInvoker(menu.Dispose));
            else
                menu.Dispose();
        };
        menu.Show(
            owner,
            new Point(0, -menu.Height));
    }
    private void ChangeAppearanceAfterAccountMenuCloses(AppThemeType theme)
    {
        if(IsDisposed||Disposing||!IsHandleCreated)return;
        int actor=CurrentUser.UserId;
        // Theme refresh rebuilds the sidebar; finish the dropdown click before
        // retiring its owner button, just as other Account navigation does.
        BeginInvoke(new MethodInvoker(()=>{
            if(!IsDisposed&&!Disposing&&CurrentUser.UserId==actor)AppTheme.SetTheme(theme);
        }));
    }
    private void OpenAccountAdministrationAfterMenuCloses(AccessEditorKind kind)
    {
        if (IsDisposed || Disposing || !IsHandleCreated || CurrentUser.UserId <= 0)
            return;
        int actor = CurrentUser.UserId;
        // Defer navigation until the menu click has finished, then recheck the session
        // and read access. NavigateTo retains the current page's busy/dirty guards.
        BeginInvoke(new MethodInvoker(() => {
            if (IsDisposed || Disposing || CurrentUser.UserId != actor)
                return;
            var access = AccessRules.For(CurrentUser.HasPermission);
            if (kind == AccessEditorKind.Users && access.CanReadUsers)
                NavigateTo(() => new UsersPage(), "Account", "Users");
            else if (kind == AccessEditorKind.Roles && access.CanReadRoles)
                NavigateTo(() => new RolesPage(), "Account", "Roles & Permissions");
        }));
    }
    private void OpenHotKeysPageAfterAccountMenuCloses()
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
            return;
        BeginInvoke(new MethodInvoker(() => {
            if (!IsDisposed && !Disposing)
            {
                NavigateTo(
                    () => new HotKeysPage(),
                    "Account",
                    "Hot Keys");
            }
        }));
    }
    public void GoHome()
    {
        NavigateTo(
            () => new WelcomePage(
                factory => NavigateTo(factory),
                (factory, module, title) => NavigateTo(factory, module, title)),
            "Dashboard",
            "Overview");
    }
    private void NavigateTo(Func<Control> factory, string module = "Dashboard", string title = "Overview")
    {
        if (factory is null || IsDisposed || Disposing || content.IsDisposed || _navigationInProgress) return;
        if (!ConfirmLeaveCurrentPage()) return;
        var previous = (Page: _currentPageControl, Factory: _currentFactory, Module: _currentModule,
            Title: _currentTitle, Dashboard: _currentIsDashboard, Year: _currentAcademicYearText);
        Control[] oldPages = content.Controls.Cast<Control>().ToArray();
        Control? page = null;
        bool activated = false;
        _navigationInProgress = true;
        try
        {
            SaveCurrentWorkspaceState();
            page = factory();
            if (page is null || page.IsDisposed || IsDisposed || Disposing || content.IsDisposed) return;
            // Factories normally create new pages. Never retire an already hosted page.
            if (oldPages.Contains(page)) { page = null; return; }
            page.Dock = DockStyle.Fill;
            page.Margin = Padding.Empty;
            if (page is StudentAdmissionPage && content.ClientSize.Width > 0 && content.ClientSize.Height > 0)
                page.Size = content.ClientSize;
            // Validate saved state while the previous page is still attached and usable.
            RestoreWorkspaceState(page, NavKey(module, title));
            content.SuspendLayout();
            try
            {
                content.Controls.Add(page);
                page.BringToFront();
                if (previous.Page is WorkspacePage previousWorkspace)
                    previousWorkspace.CurrentSectionChanged -= OnCurrentSectionChanged;
                _currentPageControl = page;
                _currentFactory = factory;
                _currentModule = module;
                _currentTitle = title;
                _currentIsDashboard = page is WelcomePage;
                if (page is WorkspacePage workspace)
                    workspace.CurrentSectionChanged += OnCurrentSectionChanged;
                status.Text = _currentIsDashboard
                    ? $"{CurrentUser.Username}  •  {CurrentUser.RoleName}"
                    : $"{module}  •  {title}";
                UpdateTopBar(module, title);
                SyncActiveNav(module, title);
                foreach (Control oldPage in oldPages) content.Controls.Remove(oldPage);
            }
            finally { content.ResumeLayout(true); }
            activated = true;
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing && !content.IsDisposed)
            {
                // Activation can fail during parenting or layout too. Restore the
                // previous controls and route before reporting the failed destination.
                content.SuspendLayout();
                try
                {
                    if (page is WorkspacePage failedWorkspace)
                        failedWorkspace.CurrentSectionChanged -= OnCurrentSectionChanged;
                    if (page is not null) content.Controls.Remove(page);
                    foreach (Control oldPage in oldPages)
                        if (!oldPage.IsDisposed && !content.Controls.Contains(oldPage)) content.Controls.Add(oldPage);
                    _currentPageControl = previous.Page;
                    _currentFactory = previous.Factory;
                    _currentModule = previous.Module;
                    _currentTitle = previous.Title;
                    _currentIsDashboard = previous.Dashboard;
                    _currentAcademicYearText = previous.Year;
                    if (previous.Page is WorkspacePage previousWorkspace)
                    {
                        previousWorkspace.CurrentSectionChanged -= OnCurrentSectionChanged;
                        previousWorkspace.CurrentSectionChanged += OnCurrentSectionChanged;
                    }
                    previous.Page?.BringToFront();
                    if (_topAcademicYearLabel is not null) _topAcademicYearLabel.Text = previous.Year;
                    UpdateTopBar(previous.Module, previous.Title);
                    SyncActiveNav(previous.Module, previous.Title);
                }
                finally { content.ResumeLayout(false); }
                DisposeNavigationPage(page, "Failed navigation page disposal");
                status.Text = $"Could not open {title}. {DbMessage.UserMessage(ex)}";
                if (navigationFailureReporter is not null) navigationFailureReporter(title, ex);
                else ModernMessageDialog.ShowWarning(this, $"Unable to open {title}.\r\n\r\n{DbMessage.UserMessage(ex)}", "FMSC");
            }
            AppExceptionHandler.LogNonFatal(ex, $"Dashboard Menu: {module} / {title}");
        }
        finally
        {
            _navigationInProgress = false;
            if (!activated) DisposeNavigationPage(page, "Failed navigation page disposal");
        }
        if (!activated) return;
        foreach (Control oldPage in oldPages)
            DisposeNavigationPage(oldPage, "Retired navigation page disposal");
        try
        {
            Control openedPage = page!;
            FormFlow.ScheduleInitialFocus(this, openedPage, () => ReferenceEquals(openedPage, _currentPageControl));
        }
        catch (Exception ex) { AppExceptionHandler.LogNonFatal(ex, "Navigation initial focus"); }
    }
    private static void DisposeNavigationPage(Control? page, string context)
    {
        if (page is null || page.IsDisposed) return;
        try { page.Dispose(); }
        catch (Exception ex) { AppExceptionHandler.LogNonFatal(ex, context); }
    }
    private void SaveCurrentWorkspaceState()
    {
        if (_currentPageControl is not IWorkspaceStateHost stateHost ||
            _currentPageControl.IsDisposed)
        {
            return;
        }
        _workspaceStates[NavKey(_currentModule, _currentTitle)] =
            stateHost.CaptureWorkspaceState();
    }
    private void RestoreWorkspaceState(Control page, string destinationKey)
    {
        if (page is IWorkspaceStateHost stateHost &&
            _workspaceStates.TryGetValue(destinationKey, out Dictionary<string, string>? state))
        {
            stateHost.RestoreWorkspaceState(state);
        }
    }
    private static string NavKey(string module, string title) =>
        $"{module}|{title}";
    /// <summary>Use the exact destination when available, otherwise keep report and student-certificate pages under their hubs.</summary>
    private void SyncActiveNav(string module, string title)
    {
        string key = NavKey(module, title);
        if (!navTargets.Values.Any(target => string.Equals(target, key, StringComparison.OrdinalIgnoreCase)))
        {
            if (_currentPageControl?.GetType().Namespace?.StartsWith("FMSC_SMS.Pages.Reports", StringComparison.Ordinal) == true)
                key = NavKey("Reports", "Reports");
            else if (string.Equals(module, "Certificates", StringComparison.OrdinalIgnoreCase))
                key = NavKey("Certificates", PageTitles.StudentCertificates);
        }
        foreach (var button in navButtons)
        {
            button.IsActive =
                navTargets.TryGetValue(button, out string? target) &&
                string.Equals(target, key, StringComparison.OrdinalIgnoreCase);
        }
    }
    /* The explicit parameter types pick the factory-taking constructor: the report page is then built by NavigateTo, after its leave check has passed. */
    private Control BuildReportsWorkspace() { return new ReportsHomePage((Func<Control> factory, string module, string title) => NavigateTo(factory, module, title)); }
    private void OnThemeChanged(
        object? sender,
        EventArgs e)
    {
        BackColor = AppTheme.PageBackground;
        content.BackColor = AppTheme.PageBackground;
        status.ForeColor = AppTheme.MutedText;
        BuildTopBar();
        RebuildSidebar();
        Invalidate(true);
    }
    private void RebuildSidebar()
    {
        var oldChildren =
            sidebar.Controls
                .Cast<Control>()
                .ToArray();
        SuspendLayout();
        try
        {
            navButtons.Clear();
            navigationCommands.Clear();
            navigationShortcutTargets.Clear();ShortcutController.ClearLocalShortcuts();
            /* The entries are rebuilt below, so the old buttons' targets go with them rather than keeping disposed buttons alive in this map. */
            navTargets.Clear();
            BuildSidebar();
        }
        finally
        {
            ResumeLayout(true);
        }
        foreach (Control oldChild in oldChildren)
        {
            if (oldChild is DashboardNavButton navButton &&
                navButton.Image is Image image)
            {
                navButton.Image = null;
                image.Dispose();
            }
            oldChild.Dispose();
        }
        SyncActiveNav(_currentModule, _currentTitle);
    }
    /// <summary>The one unsaved-changes question, asked by every path that leaves the current page: in-app navigation and closing the application both run it.</summary>
    private bool ConfirmLeaveCurrentPage()
    {
        /* some work cannot survive the page being disposed or the application exiting. */
        if (_currentPageControl is IBusyPage busy &&
            busy.IsBusy)
        {
            ModernMessageDialog.ShowWarning(
                this,
                busy.BusyMessage,
                "FMSC");
            return false;
        }
        if (_currentPageControl is not IUnsavedChanges pending ||
            !pending.HasUnsavedChanges)
        {
            return true;
        }
        return ModernMessageDialog.Confirm(
            this,
            pending.UnsavedChangesMessage,
            UiText.Dialogs.UnsavedChanges);
    }
    private void SignOut()
    {
        /* The close below runs the unsaved-changes guard, so the session is cleared only when that guard accepts. */
        signOutRequested = true;
        Close();
        signOutRequested = false;
    }
    private static string GetUserInitial()
    {
        if (string.IsNullOrWhiteSpace(
                CurrentUser.Username))
        {
            return "U";
        }
        return CurrentUser.Username
            .Trim()
            .Substring(0, 1)
            .ToUpperInvariant();
    }
    internal sealed class DashboardNavButton : Button
    {
        protected override bool ShowKeyboardCues=>true;
        protected override bool ProcessMnemonic(char charCode)=>false;
        private bool _active;
        private bool _expanded;
        public Color AccentColor { get; set; } =
            AppTheme.Primary;
        public bool IsModule { get; set; }
        public bool IsChild { get; set; }
        public bool ShowChevron { get; set; }
        public bool IsExpanded
        {
            get => _expanded;
            set
            {
                _expanded = value;
                Invalidate();
            }
        }
        public bool IsActive
        {
            get => _active;
            set
            {
                _active = value;
                ApplyState();
                Invalidate();
            }
        }
        public DashboardNavButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = AppTheme.MenuBackground;
            ForeColor = AppTheme.Text;
            Cursor = Cursors.Hand;
            TabStop = false;
            TextAlign =
                ContentAlignment.MiddleLeft;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);
            MouseEnter += (_, _) => {
                if (!IsActive)
                {
                    BackColor = AppTheme.MenuHover;
                    ForeColor = AppTheme.Text;
                }
            };
            MouseLeave += (_, _) =>
                ApplyState();
            Paint += OnPaintChevron;
            Paint += OnPaintActiveIndicator;
        }
        internal void RefreshTheme()
        {
            AccentColor = AppTheme.Primary;
            ApplyState();
            Invalidate();
        }
        private void ApplyState()
        {
            if (IsActive)
            {
                BackColor = AppTheme.MenuActive;
                ForeColor = AppTheme.Primary;
            }
            else
            {
                BackColor = AppTheme.MenuBackground;
                ForeColor = IsChild
                    ? AppTheme.MutedText
                    : AppTheme.Text;
            }
        }
        private void OnPaintChevron(
            object? sender,
            PaintEventArgs e)
        {
            if (!ShowChevron)
                return;
            string symbol =
                IsExpanded
                    ? "⌃"
                    : "⌄";
            using var brush =
                new SolidBrush(
                    IsActive
                        ? AccentColor
                        : AppTheme.MutedText);
            Font font = UiDesignSystem.Typography.Caption;
            var rect = new Rectangle(
                Width - 28,
                0,
                22,
                Height);
            using var format = new StringFormat {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            e.Graphics.DrawString(
                symbol,
                font,
                brush,
                rect,
                format);
        }
        private void OnPaintActiveIndicator(object? sender, PaintEventArgs e)
        {
            if (!IsActive || _sidebarCompactSafe())
                return;
            int width = Math.Max(3, AppScale.Scale(4, AppScale.SpacingScale));
            int height = Math.Max(18, Height - 12);
            int y = (Height - height) / 2;
            using var brush = new SolidBrush(
                AccentColor);
            e.Graphics.FillRectangle(brush, 0, y, width, height);
        }
        private bool _sidebarCompactSafe() => Width <= AppMetrics.SidebarCompactWidth;
    }
}

