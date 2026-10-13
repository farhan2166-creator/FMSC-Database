using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;
using FMSC_SMS.Pages.Finance.Configuration;
using FMSC_SMS.Pages.Reports;
using FMSC_SMS.Pages.Reports.Students;
using FMSC_SMS.Pages.Setup;
using FMSC_SMS.Pages.Setup.Academics;
using FMSC_SMS.Pages.Students.Admissions;
using FMSC_SMS.Pages.Students.Profile;
namespace FMSC_SMS.Pages.Dashboard;
public sealed class WelcomePage : UserControl
{
    private enum Kpi
    {
        TotalStudents,
        ActiveStudents,
        TotalClasses,
        ActiveStaff,
        NewAdmissions,
    }
    private readonly Action<Func<Control>> navigate;
    /* A quick action opens a page of its own module, so the shell is told which one instead of navigating with the dashboard's own module and title. */
    private readonly Action<Func<Control>, string, string>? navigateInModule;
    private readonly Dictionary<Kpi, (Label Value, Label Caption)> kpiLabels = new();
    private readonly Label academicYearLabel;
    private DashboardDonutChart? studentStatusChart;
    private DashboardBarChart? academicChart;
    private readonly List<Control> dashboardPageSurfaces = new();
    private Control? setupJourneyCard;
    private RowStyle? setupJourneyRowStyle;
    private RowStyle? dashboardChartsRowStyle;
    private RowStyle? quickActionsRowStyle;
    private bool setupJourneyShown;
    private static int GreetingTitleRowHeight =>
        Math.Max(32, AppMetrics.TextHeight(AppTheme.PageTitleFont) + 6);
    private static int GreetingSubtitleRowHeight =>
        Math.Max(22, AppMetrics.TextHeight(AppTheme.NormalFont) + 4);
    private static Font KpiValueFont { get; } = new("Segoe UI", 17F, FontStyle.Bold);
    // UI TUNING: icons inside dashboard KPI cards. Kept separate from compact toolbar/sidebar icons.
    private static Font KpiCardIconFont =>
        AppTheme.IconFont(27F * AppScale.HeadingFontScale);
    private static int KpiTitleRowHeight =>
        Math.Max(
            AppMetrics.TextHeight(KpiCardIconFont) + 2,
            AppMetrics.TextHeight(AppTheme.NormalFont) + 4);
    private static int KpiValueRowHeight =>
        Math.Max(28, AppMetrics.TextHeight(KpiValueFont) + 4);
    private static int KpiCaptionRowHeight =>
        Math.Max(22, AppMetrics.TextHeight(AppTheme.NormalFont) + 4);
    private static int KpiGridRowHeight =>
        24 + 10 + KpiTitleRowHeight + KpiValueRowHeight + KpiCaptionRowHeight;
    public WelcomePage(
        Action<Func<Control>> navigate,
        Action<Func<Control>, string, string>? navigateInModule = null)
    {
        this.navigate = navigate;
        this.navigateInModule = navigateInModule;
        academicYearLabel = new Label {
            Dock = DockStyle.Fill,
            Font = AppTheme.ButtonFont,
            ForeColor = AppTheme.Primary,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            Text = "Academic Year — Loading..."
        };
        Dock = DockStyle.Fill;
        BackColor = AppTheme.PageBackground;
        Padding = new Padding(
            UiDesignSystem.Spacing.Xl,
            AppMetrics.PageTopPadding,
            UiDesignSystem.Spacing.Xl,
            UiDesignSystem.Spacing.Xl);
        AutoScroll = true;
        Build();
        AppTheme.ThemeChanged += OnDashboardThemeChanged;
        Disposed += (_, _) => AppTheme.ThemeChanged -= OnDashboardThemeChanged;
        Defer(() => _ = LoadDashboardDataAsync());
    }
    private T DashboardPageSurface<T>(T control) where T : Control
    {
        control.BackColor = AppTheme.PageBackground;
        dashboardPageSurfaces.Add(control);
        return control;
    }
    private void OnDashboardThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
            return;
        BackColor = AppTheme.PageBackground;
        foreach (Control surface in dashboardPageSurfaces)
        {
            if (!surface.IsDisposed)
                surface.BackColor = AppTheme.PageBackground;
        }
        Invalidate(true);
    }
    private void Build()
    {
        var root = DashboardPageSurface(new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0),
            Margin = new Padding(0)
        });
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                GreetingTitleRowHeight + GreetingSubtitleRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, KpiGridRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildDashboardHeader(), 0, 0);
        root.Controls.Add(BuildKpiGrid(), 0, 1);
        root.Controls.Add(BuildBody(), 0, 2);
        Controls.Add(root);
    }
    private Control BuildDashboardHeader()
    {
        var layout = DashboardPageSurface(new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(2, 0, 2, 0)
        });
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, GreetingTitleRowHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label {
            Text = $"Good morning, {CurrentUser.Username}",
            Dock = DockStyle.Fill,
            Font = AppTheme.PageTitleFont,
            ForeColor = AppTheme.HeadingText,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 0);
        layout.Controls.Add(new Label {
            Text = "Here is your school overview for the current academic year.",
            Dock = DockStyle.Fill,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true
        }, 0, 1);
        return layout;
    }
    private Control BuildKpiGrid()
    {
        var grid = DashboardPageSurface(new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Padding = new Padding(0, 4, 0, 4),
            Margin = new Padding(0)
        });
        for (int i = 0; i < 5; i++)
            grid.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 20F));
        Color studentAccent = AppTheme.Primary;
        Color activeAccent = Color.FromArgb(124, 58, 237);
        Color academicAccent = Color.FromArgb(6, 182, 212);
        Color staffAccent = Color.FromArgb(16, 185, 129);
        Color admissionAccent = Color.FromArgb(245, 158, 11);
        PlaceKpi(
            grid,
            Kpi.TotalStudents,
            0,
            "Total Students",
            studentAccent);
        PlaceKpi(
            grid,
            Kpi.ActiveStudents,
            1,
            "Active Students",
            activeAccent);
        PlaceKpi(
            grid,
            Kpi.TotalClasses,
            2,
            "Total Classes",
            academicAccent);
        PlaceKpi(
            grid,
            Kpi.ActiveStaff,
            3,
            "Active Staff",
            staffAccent);
        PlaceKpi(
            grid,
            Kpi.NewAdmissions,
            4,
            "New Admissions",
            admissionAccent);
        return grid;
    }
    private async Task LoadAcademicYearAsync(DataTable? preloaded = null)
    {
        try
        {
            if (academicYearLabel == null)
                return;
            DataTable years = preloaded ?? await DatabaseHelper.QueryUserAsync("GetAcademicYears");
            if (years == null ||
                years.Columns == null ||
                years.Rows.Count == 0)
            {
                academicYearLabel.Text = "Academic Year — Not Set";
                return;
            }
            if (!years.Columns.Contains("YearName"))
            {
                academicYearLabel.Text = "Academic Year — Not Set";
                return;
            }
            DataRow? currentYear = null;
            if (years.Columns.Contains("IsCurrent"))
            {
                currentYear = years.AsEnumerable()
                    .FirstOrDefault(row =>
                        row != null &&
                        row["IsCurrent"] != DBNull.Value &&
                        Convert.ToBoolean(row["IsCurrent"]));
            }
            currentYear ??= years.Rows[0];
            if (currentYear == null)
            {
                academicYearLabel.Text = "Academic Year — Not Set";
                return;
            }
            string yearName = currentYear["YearName"] == DBNull.Value
                ? string.Empty
                : Convert.ToString(currentYear["YearName"])?.Trim() ?? string.Empty;
            string text = string.IsNullOrWhiteSpace(yearName)
                ? "Academic Year — Not Set"
                : $"Academic Year — {yearName}";
            academicYearLabel.Text = text;
            if (FindForm() is MainDashboardForm dashboard)
                dashboard.UpdateTopAcademicYearLabel(text);
        }
        catch (Exception ex)
        {
            if (academicYearLabel != null)
                academicYearLabel.Text = "Academic Year — Unavailable";
            System.Diagnostics.Debug.WriteLine(
                $"LoadAcademicYear failed: {ex}");
        }
    }
    private void PlaceKpi(
        TableLayoutPanel grid,
        Kpi kpi,
        int column,
        string title,
        Color accent)
    {
        var (card, value, caption) =
            BuildKpiCard(title, accent);
        kpiLabels[kpi] = (value, caption);
        grid.Controls.Add(card, column, 0);
    }
    private (RoundedPanel Card, Label Value, Label Caption)
        BuildKpiCard(
            string title,
            Color accent)
    {
        var card = new RoundedPanel {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            ShowBorder = true,
            CornerRadius = AppTheme.CardRadius,
            Padding = new Padding(12, 10, 10, 8),
            Margin = new Padding(0, 0, 8, 0)
        };
        var accentBar = new Panel {
            Dock = DockStyle.Left,
            Width = AppScale.Scale(5, AppScale.ControlSizeScale),
            BackColor = accent,
            Margin = new Padding(0, 0, 8, 0)
        };
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Margin = new Padding(4, 0, 0, 0),
            Padding = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 76));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, KpiTitleRowHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, KpiValueRowHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var titleLabel = new Label {
            Text = title,
            Dock = DockStyle.Fill,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        var valueLabel = new Label {
            Text = "—",
            Dock = DockStyle.Fill,
            Font = KpiValueFont,
            ForeColor = accent,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        var captionLabel = new Label {
            Text = "Loading...",
            Dock = DockStyle.Fill,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true
        };
        var icon = title switch {
            "Total Students" => "\uE716",
            "Active Students" => "\uE716",
            "Total Classes" => "\uE80A",
            "Active Staff" => "\uE77B",
            "New Admissions" => "\uE8FA",
            _ => "\uE80F"
        };
        var iconLabel = new Label {
            Text = icon,
            Dock = DockStyle.Fill,
            Font = KpiCardIconFont,
            ForeColor = accent,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(0, 2, 0, 0)
        };
        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(iconLabel, 1, 0);
        layout.Controls.Add(valueLabel, 0, 1);
        layout.SetColumnSpan(valueLabel, 2);
        layout.Controls.Add(captionLabel, 0, 2);
        layout.SetColumnSpan(captionLabel, 2);
        card.Controls.Add(layout);
        card.Controls.Add(accentBar);
        return (card, valueLabel, captionLabel);
    }
    private Control BuildBody()
    {
        bool showSetupJourney =
            CurrentUser.HasPermission("System.Setup") ||
            CurrentUser.HasPermission("Calendar.Manage") ||
            CurrentUser.HasPermission("Fees.Configuration.Manage");
        setupJourneyShown = showSetupJourney;
        var body = DashboardPageSurface(new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = showSetupJourney ? 3 : 2,
            Padding = new Padding(0, 8, 0, 0),
            Margin = new Padding(0)
        });
        body.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        body.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        dashboardChartsRowStyle = new RowStyle(SizeType.Percent, showSetupJourney ? 40 : 55);
        body.RowStyles.Add(dashboardChartsRowStyle);
        if (showSetupJourney)
        {
            setupJourneyRowStyle = new RowStyle(SizeType.Percent, 30);
            body.RowStyles.Add(setupJourneyRowStyle);
        }
        quickActionsRowStyle = new RowStyle(SizeType.Percent, showSetupJourney ? 30 : 45);
        body.RowStyles.Add(quickActionsRowStyle);
        body.Controls.Add(
            BuildStudentStatusCard(),
            0,
            0);
        body.Controls.Add(
            BuildAcademicSnapshotCard(),
            1,
            0);
        int quickActionsRow = 1;
        if (showSetupJourney)
        {
            var setupJourney = BuildSetupJourneyCard();
            setupJourneyCard = setupJourney;
            setupJourney.Margin = new Padding(0, 8, 8, 0);
            body.Controls.Add(setupJourney, 0, 1);
            body.SetColumnSpan(setupJourney, 2);
            quickActionsRow = 2;
        }
        var quickActions = BuildQuickActionsCard();
        quickActions.Margin = new Padding(0, 8, 8, 0);
        body.Controls.Add(
            quickActions,
            0,
            quickActionsRow);
        body.SetColumnSpan(
            quickActions,
            2);
        if (showSetupJourney)
            body.SizeChanged += (_, _) => SetSetupJourneySpace(setupJourneyShown);
        return body;
    }
    private Control BuildSetupJourneyCard()
    {
        var card = HumanUi.Card(
            "School Setup — Recommended Order",
            "Complete these once before daily entry. You can return to any step later.",
            AppTheme.Warning);
        var steps = new (string Text, string Module, Func<Control> Factory, string Permission, string Title)[]
        {
            ("Check Readiness", "School Setup", () => new SetupReadinessPage(
                (factory, module, title) => { if (navigateInModule is not null) navigateInModule(factory, module, title); else navigate(factory); }),
                FMSC_SMS.Services.SetupReadiness.NavigationPermission, "Setup Readiness"),
            ("1  School Profile", "School Setup", () => new WorkspacePage(
                "School Profile",
                "School identity used on reports, certificates, receipts and letters.",
                ("School Information", () => new SchoolProfilePage()),
                ("School Officials", () => new SchoolOfficialsPage())), "System.Setup", "School Profile"),
            ("2  Campuses", "School Setup", () => new CampusesPage(), "System.Setup", "Campuses"),
            ("3  Academic Years", "School Setup", () => new AcademicYearsPage(), "Calendar.Manage", "Academic Years"),
            ("4  Classes & Sections", "School Setup", () => new ClassesAndSectionsWorkspacePage(), "System.Setup", PageTitles.ClassesSections),
            ("5  Campus Assignments", "School Setup", () => new CampusClassSectionAssignmentsPage(), "System.Setup", PageTitles.CampusAssignments),
            ("6  Fee Rate Setup", "Finance", () => FeeConfigurationWorkspace.Create(), "Fees.Configuration.Manage", PageTitles.FeeRateSetup)
        };
        var allowed = steps.Where(step => CurrentUser.HasPermission(step.Permission)).ToArray();
        var flow = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        foreach (var step in allowed)
        {
            var button = CreateQuickActionButton(step.Text, step.Module, step.Factory, step.Title);
            button.Width = AppScale.Scale(175);
            button.Height = AppMetrics.LargeButtonHeight;
            flow.Controls.Add(button);
        }
        card.Content.Controls.Add(flow);
        return card;
    }
    private Control BuildStudentStatusCard()
    {
        var card = HumanUi.Card(
            "Student Status",
            "Active students compared with the total.");
        studentStatusChart = new DashboardDonutChart {
            Dock = DockStyle.Fill,
            PrimaryColor = AppTheme.Primary,
            SecondaryColor = AppTheme.Border,
            Margin = new Padding(4, 6, 4, 4)
        };
        card.Content.Controls.Add(studentStatusChart);
        return card;
    }
    private Control BuildAcademicSnapshotCard()
    {
        var card = HumanUi.Card(
            "Academic Snapshot",
            "Key figures for the current academic year.");
        academicChart = new DashboardBarChart {
            Dock = DockStyle.Fill,
            BarColor = AppTheme.Primary,
            Margin = new Padding(4, 8, 4, 4)
        };
        card.Content.Controls.Add(academicChart);
        return card;
    }
    private Control BuildQuickActionsCard()
    {
        var card = HumanUi.Card(
            "Quick Actions",
            "Frequently used school operations.");
        card.Margin = new Padding(0, 0, 0, 8);
        var table = BuildQuickActionsGrid();
        card.Content.Controls.Add(table);
        PopulateQuickActions(table);
        return card;
    }
    private TableLayoutPanel BuildQuickActionsGrid()
    {
        var table = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(2),
            Margin = new Padding(0)
        };
        // Configure equal column widths
        for (int i = 0; i < 3; i++)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        }
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        return table;
    }
    private void PopulateQuickActions(TableLayoutPanel table)
    {
        var oldControls = table.Controls.Cast<Control>().ToArray();
        table.Controls.Clear();
        foreach (var oldControl in oldControls)
            oldControl.Dispose();
        var actions = new (string Text, string Module, Func<Control> Factory, Func<bool> Allowed)[]
        {
        (PageTitles.Admissions, "Students", () => new StudentAdmissionPage(), () => CurrentUser.HasPermission("Students.Admit")),
        ("Quick Registration", "Students", () => new QuickRegistrationPage((factory, module, title) =>
        {
            if (navigateInModule is not null) navigateInModule(factory, module, title);
            else navigate(factory);
        }), () => QuickRegistrationAccess.CanUse(CurrentUser.HasPermission)),
        ("Fee Collection", "Finance", () => new FeeCollectionPage(), () => CurrentUser.HasPermission("Fees.Collect")),
        ("Student Profile", "Students", () => new StudentProfilePage(), () => CurrentUser.HasPermission("Students.View")),
        ("Student Reports", "Students", () => new StudentReportsPage(), () => CurrentUser.HasPermission("Students.View")),
        ("Reports", "Reports", () => new ReportsHomePage((Func<Control> factory, string module, string title) => navigateInModule?.Invoke(factory, module, title)), () => CurrentUser.HasPermission("Reports.View"))
        };
        var visibleActions = actions
            .Where(action => action.Allowed())
            .ToArray();
        int rows = Math.Max(1, (int)Math.Ceiling(visibleActions.Length / 3d));
        table.RowCount = rows;
        table.RowStyles.Clear();
        for (int row = 0; row < rows; row++)
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / rows));
        for (int i = 0; i < visibleActions.Length; i++)
        {
            int row = i / 3;
            int col = i % 3;
            var action = visibleActions[i];
            var button = CreateQuickActionButton(action.Text,
                                              action.Module,
                                              action.Factory,
                                              action.Text);
            // Set dock style to fill the cell completely
            button.Dock = DockStyle.Fill;
            // Remove any fixed sizing that might constrain it
            button.Size = new Size(0, button.Height); // Allow dynamic width
            // Set minimum size for height only
            button.MinimumSize = new Size(0, AppMetrics.LargeButtonHeight);
            // Add to table with full cell occupation
            table.Controls.Add(button, col, row);
        }
    }
    private ModernButton CreateQuickActionButton(string text, string module, Func<Control> factory, string? title = null)
    {
        var iconGlyph = text switch {
            PageTitles.Admissions => "＋",
            "Fee Collection" => "Rs",
            "Student Reports" => "☷",
            "Bulk Promotion" => "↑",
            "Promotion History" => "◷",
            PageTitles.StudentRecordChecks => "●",
            "Reports" => "▤",
            _ => "•"
        };
        var button = new ModernButton {
            Text = text,
            Height = AppMetrics.LargeButtonHeight,
            Margin = new Padding(4),
            ThemeRole = "Secondary",
            // UI TUNING: dashboard/setup card-action icon size.
            Image = UiHelper.CreateGlyphIcon(iconGlyph, AppTheme.ModuleAccent(module), 25),
            ImageAlign = ContentAlignment.MiddleLeft,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Padding = new Padding(10, 0, 10, 0)
        };
        // Dashboard shortcuts navigate, like supporting actions on forms.
        // Keep the shared Secondary style; module colors belong to card accents,
        // not a separate button palette.
        button.ApplyTheme();
        // Numbered setup steps and action labels use the destination's canonical title.
        button.Click += (_, _) => {
            if (navigateInModule is not null)
            {
                navigateInModule(factory, module, title ?? text);
                return;
            }
            navigate(factory);
        };
        return button;
    }
    private async Task LoadDashboardDataAsync()
    {
        try
        {
            var summaryTask = DatabaseHelper.QueryUserAsync("GetDashboardSummary");
            var yearsTask = DatabaseHelper.QueryUserAsync("GetAcademicYears");
            var results = await Task.WhenAll(summaryTask, yearsTask);
            var summary = results[0];
            await LoadAcademicYearAsync(results[1]);
            await UpdateSetupJourneyVisibilityAsync(results[1]);
            if (summary.Rows.Count == 0)
            {
                ShowEmptyState();
                return;
            }
            var row = summary.Rows[0];
            int totalStudents = GetInt(
                row,
                "TotalStudents",
                "StudentCount",
                "TotalStudentCount");
            int activeStudents = GetInt(
                row,
                "ActiveStudents",
                "ActiveStudentCount");
            int totalClasses = GetInt(
                row,
                "TotalClasses",
                "ClassCount",
                "TotalClassCount");
            int activeStaff = GetInt(
                row,
                "ActiveStaff",
                "ActiveStaffCount",
                "StaffCount");
            int newAdmissions = GetInt(
                row,
                "NewAdmissionsThisMonth",
                "NewAdmissions",
                "AdmissionsThisMonth");
            SetKpi(
                Kpi.TotalStudents,
                totalStudents > 0
                    ? totalStudents.ToString("N0")
                    : "—",
                totalStudents > 0
                    ? "All registered students"
                    : "Not available");
            SetKpi(
                Kpi.ActiveStudents,
                activeStudents > 0
                    ? activeStudents.ToString("N0")
                    : "—",
                activeStudents > 0
                    ? "Currently active"
                    : "Not available");
            SetKpi(
                Kpi.TotalClasses,
                totalClasses > 0
                    ? totalClasses.ToString("N0")
                    : "—",
                totalClasses > 0
                    ? "Configured classes"
                    : "Not available");
            SetKpi(
                Kpi.ActiveStaff,
                activeStaff > 0
                    ? activeStaff.ToString("N0")
                    : "—",
                activeStaff > 0
                    ? "Currently employed"
                    : "Not available");
            SetKpi(
                Kpi.NewAdmissions,
                newAdmissions.ToString("N0"),
                $"Since {new DateTime(
                    DateTime.Today.Year,
                    DateTime.Today.Month,
                    1):dd MMM}");
            UpdateDashboardVisuals(
                totalStudents,
                activeStudents,
                totalClasses,
                activeStaff,
                newAdmissions);
        }
        catch (Exception ex)
        {
            ShowLoadError(ex);
        }
    }
    private async Task UpdateSetupJourneyVisibilityAsync(DataTable academicYears)
    {
        if (setupJourneyCard is null || setupJourneyRowStyle is null)
            return;
        try
        {
            var issues = await FMSC_SMS.Services.SetupReadiness.CheckAsync();
            if (IsDisposed) return;
            bool complete = issues.Rows.Count == 0;
            setupJourneyCard.Visible = !complete;
            SetSetupJourneySpace(!complete);
        }
        catch (Exception nonFatalException29)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException29, "WelcomePage: UpdateSetupJourneyVisibilityAsync fallback");
            // Setup guidance is non-critical. If readiness cannot be checked, leave it visible so
            // an administrator still has a safe route through the recommended setup sequence.
            setupJourneyCard.Visible = true;
            SetSetupJourneySpace(true);
        }
    }
    private void SetSetupJourneySpace(bool visible)
    {
        if (setupJourneyRowStyle is null || dashboardChartsRowStyle is null || quickActionsRowStyle is null) return;
        setupJourneyShown = visible;
        setupJourneyRowStyle.SizeType = SizeType.Absolute;
        quickActionsRowStyle.SizeType = visible ? SizeType.Absolute : SizeType.Percent;
        dashboardChartsRowStyle.Height = visible ? 100 : 55;
        int available = setupJourneyCard?.Parent?.ClientSize.Height ?? 0;
        int minimum = AppMetrics.FieldCardHeight(0) + 2 * (AppMetrics.LargeButtonHeight + AppScale.Scale(8)) + AppScale.Scale(12);
        float height = Math.Max(minimum, available * 0.30f);
        setupJourneyRowStyle.Height = visible ? height : 0;
        quickActionsRowStyle.Height = visible ? height : 45;
    }
    private void UpdateDashboardVisuals(
        int totalStudents,
        int activeStudents,
        int totalClasses,
        int activeStaff,
        int newAdmissions)
    {
        if (studentStatusChart is not null)
        {
            studentStatusChart.PrimaryValue = activeStudents;
            studentStatusChart.SecondaryValue = Math.Max(0, totalStudents - activeStudents);
            studentStatusChart.CenterText = totalStudents > 0
                ? $"{activeStudents * 100m / totalStudents:0}%"
                : "—";
            studentStatusChart.CenterCaption = "active";
            studentStatusChart.Invalidate();
        }
        if (academicChart is not null)
        {
            academicChart.Bars = new[] {
                new DashboardBarChart.Bar("Classes", totalClasses),
                new DashboardBarChart.Bar("Staff", activeStaff),
                new DashboardBarChart.Bar("Admissions", newAdmissions)
            };
        }
    }
    private void SetKpi(
        Kpi kpi,
        string value,
        string caption)
    {
        if (!kpiLabels.TryGetValue(
                kpi,
                out var labels))
        {
            return;
        }
        labels.Value.Text = value;
        labels.Caption.Text = caption;
    }
    private static int GetInt(
        DataRow row,
        params string[] possibleColumns)
    {
        foreach (string column in possibleColumns)
        {
            if (!row.Table.Columns.Contains(column))
                continue;
            if (row[column] == DBNull.Value)
                continue;
            if (int.TryParse(
                    Convert.ToString(row[column]),
                    out int value))
            {
                return value;
            }
        }
        return 0;
    }
    private void ShowEmptyState()
    {
        foreach (Kpi kpi in Enum.GetValues<Kpi>())
            SetKpi(kpi, "—", "No dashboard data available");
        UpdateDashboardVisuals(0, 0, 0, 0, 0);
    }
    private void ShowLoadError(Exception ex)
    {
        /* consistency: classify by SQL Server error number, not by matching English message text. */
        bool procedureMissing =
            ex is Microsoft.Data.SqlClient.SqlException sql &&
            sql.Number is 2812 or 208;
        string message = procedureMissing
            ? "Dashboard procedure is unavailable."
            : "Dashboard data is temporarily unavailable.";
        foreach (Kpi kpi in Enum.GetValues<Kpi>())
            SetKpi(kpi, "—", message);
        UpdateDashboardVisuals(0, 0, 0, 0, 0);
    }
    private void Defer(Action action)
    {
        if (IsDisposed)
            return;
        if (IsHandleCreated)
        {
            BeginInvoke(new MethodInvoker(() => {
                if (!IsDisposed)
                    action();
            }));
            return;
        }
        EventHandler? handler = null;
        handler = (_, _) => {
            HandleCreated -= handler;
            if (IsDisposed)
                return;
            BeginInvoke(new MethodInvoker(() => {
                if (!IsDisposed)
                    action();
            }));
        };
        HandleCreated += handler;
    }
}
