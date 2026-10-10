using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports.Finance;
using FMSC_SMS.Pages.Reports.Students;
namespace FMSC_SMS.Pages.Reports;
public sealed class ReportsHomePage : V2Page
{
    /* The report page is handed over as a FACTORY, not as a built page. */
    private readonly Action<Func<Control>, string, string> _navigate;
    private readonly List<(
        Control Category,
        string CategoryText,
        TableLayoutPanel Grid,
        List<(Control Card, string SearchText)> Reports)> _categories = new();
    private ModernTextBox? _searchBox;
    private Label? _resultCount;
    public ReportsHomePage(Action<Control, string, string> navigate)
        : this(BuildPageBeforeNavigating(navigate))
    {
    }
    /* The old contract, kept working: the page is built here, immediately before the shell is called, exactly as it was. */
    private static Action<Func<Control>, string, string> BuildPageBeforeNavigating(
        Action<Control, string, string> navigate)
    {
        if (navigate is null)
            throw new ArgumentNullException(nameof(navigate));
        return (factory, module, title) => navigate(factory(), module, title);
    }
    public ReportsHomePage(Action<Func<Control>, string, string> navigate)
        : base("Reports", "All school reports in one place. Select a report to open it.")
    {
        _navigate =
            navigate ??
            throw new ArgumentNullException(nameof(navigate));
        BuildUi();
    }
    private void BuildUi()
    {
        var scroll = new Panel {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = AppTheme.PageBackground,
            Padding = new Padding(AppMetrics.PagePadding, AppMetrics.SpacingSm, AppMetrics.PagePadding, AppMetrics.PagePadding)
        };
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ColumnCount = 1,
            RowCount = 0,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = AppTheme.PageBackground
        };
        root.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        bool hasReports = false;
        var toolbar = BuildReportToolbar();
        scroll.Controls.Add(toolbar);
        if (CurrentUser.HasPermission("Reports.View"))
        {
            AddCategory(
                root,
                "Students & Admissions",
                "Student information, admissions, strength and movement.",
                AppTheme.ModuleAccent("Students"),
                new ReportDefinition[]
                {
                    new(
                        "Student Reports",
                        "Student lists, admissions, strength and movement in one report - group, sort, compare years and choose your own columns.",
                        "Students",
                        () => new StudentReportsPage())
                });
            hasReports = true;
        }
        if (CurrentUser.HasPermission("Reports.View"))
        {
            AddCategory(
                root,
                "Finance",
                "Collections, balances, billing and student financial documents.",
                AppTheme.ModuleAccent("Finance"),
                new ReportDefinition[]
                {
                    new(
                        "Collections & Receipts",
                        "Receipts, payment allocations and summaries from one reconciled collection dataset.",
                        "Finance",
                        () => new CollectionsReceiptsReportPage()),
                    new(
                        "Outstanding & Aging",
                        "Student, charge, aging and family views of the same current balances.",
                        "Finance",
                        () => new OutstandingAgingReportPage()),
                    new(
                        "Billing & Discounts",
                        "Charges created and discounts actually applied, kept separate from cash received.",
                        "Finance",
                        () => new BillingDiscountsReportPage()),
                    new(
                        PageTitles.StudentAccountStatement,
                        "A selected student's financial transaction and account history.",
                        "Finance",
                        () => new StudentAccountStatementPage()),
                    new(
                        PageTitles.AnnualFeeReceipts,
                        "Print a student's yearly payment receipts in a compact two-column sheet.",
                        "Finance",
                        () => new AnnualFeeReceiptsPage())
                }.Where(report => CurrentUser.HasPermission("Fees.Statement.View") ||
                    report.Title != PageTitles.AnnualFeeReceipts).ToArray());
            hasReports = true;
        }
        if (!hasReports)
        {
            AddCategoryRow(root, BuildEmptyState());
        }
        scroll.Controls.Add(root);
        toolbar.BringToFront();
        scroll.Resize += (_, _) => {
            root.Width = Math.Max(
                600,
                scroll.ClientSize.Width -
                scroll.Padding.Left -
                scroll.Padding.Right);
        };
        root.Width = Math.Max(
            600,
            scroll.ClientSize.Width -
            scroll.Padding.Left -
            scroll.Padding.Right);
        ApplyReportSearch();
        SetContent(scroll);
    }
    private Control BuildReportToolbar()
    {
        var bar = new Panel {
            Dock = DockStyle.Top,
            Height = AppMetrics.PageActionBarHeight,
            Padding = new Padding(0, 0, 0, AppMetrics.SpacingSm),
            BackColor = AppTheme.PageBackground
        };
        _searchBox = HumanUi.TextBox(false, 200);
        WorkspaceState.Remember(_searchBox, "ReportsHome:Search");
        _searchBox.PlaceholderText = "Search reports...";
        _searchBox.Dock = DockStyle.Left;
        _searchBox.Width = Math.Max(280, AppMetrics.TextWidth("Search reports...", AppTheme.NormalFont, 44));
        _resultCount = new Label {
            Dock = DockStyle.Right,
            AutoSize = false,
            Width = AppScale.Scale(150, AppScale.ControlSizeScale),
            TextAlign = ContentAlignment.MiddleRight,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText
        };
        _searchBox.TextChanged += (_, _) => ApplyReportSearch();
        bar.Controls.Add(_resultCount);
        bar.Controls.Add(_searchBox);
        return bar;
    }
    private void ApplyReportSearch()
    {
        string query = (_searchBox?.Text ?? string.Empty).Trim().ToLowerInvariant();
        int visible = 0;
        foreach (var (category, categoryText, grid, reports) in _categories)
        {
            var matches = reports
                .Where(report =>
                    query.Length == 0 ||
                    categoryText.Contains(query, StringComparison.Ordinal) ||
                    report.SearchText.Contains(query, StringComparison.Ordinal))
                .ToArray();
            category.Visible = matches.Length > 0;
            visible += matches.Length;
            LayoutReportCards(grid, matches.Select(match => match.Card).ToArray());
        }
        if (_resultCount != null)
            _resultCount.Text = $"{visible} report{(visible == 1 ? "" : "s")}";
    }
    private void AddCategory(TableLayoutPanel root, string title, string description, Color accent, ReportDefinition[] reports)
    {
        var category = BuildCategory(
            title,
            description,
            accent,
            reports,
            out TableLayoutPanel grid,
            out List<(Control Card, string SearchText)> reportCards);
        _categories.Add((
            category,
            (title + " " + description).ToLowerInvariant(),
            grid,
            reportCards));
        AddCategoryRow(root, category);
    }
    private static void AddCategoryRow(TableLayoutPanel root, Control control)
    {
        int row = root.RowCount;
        root.RowCount++;
        root.RowStyles.Add(
            new RowStyle(SizeType.AutoSize));
        root.Controls.Add(control, 0, row);
    }
    private Control BuildCategory(
        string title,
        string description,
        Color accent,
        ReportDefinition[] reports,
        out TableLayoutPanel reportGrid,
        out List<(Control Card, string SearchText)> reportCards)
    {
        int headerHeight =
            Math.Max(
                62,
                AppMetrics.TextHeight(AppTheme.HeadingFont) +
                AppMetrics.TextHeight(AppTheme.NormalFont) +
                AppMetrics.SpacingMd +
                AppMetrics.SpacingSm);
        var category = new RoundedPanel {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            ShowBorder = true,
            CornerRadius = AppTheme.CardRadius,
            Margin = new Padding(
                0,
                0,
                0,
                AppMetrics.SectionGap),
            Padding = new Padding(AppMetrics.SectionPadding)
        };
        var content = new TableLayoutPanel {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        content.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        content.RowStyles.Add(
            new RowStyle(SizeType.Absolute, headerHeight));
        content.RowStyles.Add(
            new RowStyle(SizeType.AutoSize));
        content.Controls.Add(
            BuildCategoryHeader(title, description, accent, reports.Length), 0, 0);
        reportGrid = BuildReportGrid(reports, accent, out reportCards);
        content.Controls.Add(reportGrid, 0, 1);
        category.Controls.Add(content);
        return category;
    }
    private Control BuildCategoryHeader(string title, string description, Color accent, int reportCount)
    {
        var header = new Panel {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, AppMetrics.SpacingSm)
        };
        var accentBar = new Panel {
            Dock = DockStyle.Left,
            Width = AppScale.Scale(5, AppScale.ControlSizeScale),
            BackColor = accent,
            Margin = new Padding(0)
        };
        header.Controls.Add(accentBar);
        var textArea = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(
                AppMetrics.SpacingMd,
                0,
                0,
                0),
            Padding = new Padding(0)
        };
        textArea.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        textArea.ColumnStyles.Add(
            new ColumnStyle(SizeType.AutoSize));
        textArea.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                Math.Max(
                    24,
                    AppMetrics.TextHeight(AppTheme.HeadingFont))));
        textArea.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        textArea.Controls.Add(
            new Label {
                Text = title,
                Dock = DockStyle.Fill,
                Font = AppTheme.HeadingFont,
                ForeColor = AppTheme.HeadingText,
                TextAlign =
                    ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(0)
            },
            0,
            0);
        var countLabel = new Label {
            Text = $"{reportCount} Reports",
            AutoSize = true,
            Font = AppTheme.NormalFont,
            ForeColor = accent,
            TextAlign =
                ContentAlignment.MiddleRight,
            Anchor =
                AnchorStyles.Right |
                AnchorStyles.Top,
            Margin = new Padding(AppMetrics.SpacingMd, 3, 0, 0)
        };
        textArea.Controls.Add(countLabel, 1, 0);
        textArea.Controls.Add(
            new Label {
                Text = description,
                Dock = DockStyle.Fill,
                Font = AppTheme.NormalFont,
                ForeColor = AppTheme.MutedText,
                TextAlign =
                    ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(0)
            },
            0,
            1);
        textArea.SetColumnSpan(textArea.GetControlFromPosition(0, 1)!, 2);
        header.Controls.Add(textArea);
        return header;
    }
    private TableLayoutPanel BuildReportGrid(
        ReportDefinition[] reports,
        Color accent,
        out List<(Control Card, string SearchText)> reportCards)
    {
        int columns =
            GetColumnCount(reports.Length);
        int rows =
            (int)Math.Ceiling(
                reports.Length /
                (double)columns);
        int cardHeight =
            AppScale.Scale(112, AppScale.ControlSizeScale);
        var grid = new TableLayoutPanel {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = columns,
            RowCount = rows,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        for (int column = 0;
             column < columns;
             column++)
        {
            grid.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100f / columns));
        }
        for (int row = 0;
             row < rows;
             row++)
        {
            grid.RowStyles.Add(
                new RowStyle(SizeType.Absolute, cardHeight));
        }
        reportCards = new List<(Control Card, string SearchText)>();
        for (int i = 0; i < reports.Length; i++)
        {
            int row = i / columns;
            int column = i % columns;
            Control card = BuildReportCard(reports[i], accent);
            reportCards.Add((
                card,
                (reports[i].Title + " " + reports[i].Description + " " + reports[i].Module)
                    .ToLowerInvariant()));
            grid.Controls.Add(card, column, row);
        }
        return grid;
    }
    private static void LayoutReportCards(TableLayoutPanel grid, Control[] cards)
    {
        int columns = GetColumnCount(Math.Max(1, cards.Length));
        int rows = Math.Max(1, (int)Math.Ceiling(cards.Length / (double)columns));
        int cardHeight = AppScale.Scale(112, AppScale.ControlSizeScale);
        grid.SuspendLayout();
        try
        {
            grid.Controls.Clear();
            grid.ColumnStyles.Clear();
            grid.RowStyles.Clear();
            grid.ColumnCount = columns;
            grid.RowCount = rows;
            for (int column = 0; column < columns; column++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            for (int row = 0; row < rows; row++)
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, cardHeight));
            for (int index = 0; index < cards.Length; index++)
                grid.Controls.Add(cards[index], index % columns, index / columns);
        }
        finally
        {
            grid.ResumeLayout(true);
        }
    }
    private static int GetColumnCount(int reportCount)
    {
        if (reportCount >= 5)
            return 5;
        if (reportCount == 4)
            return 4;
        if (reportCount == 3)
            return 3;
        return Math.Max(1, reportCount);
    }
    private Control BuildReportCard(ReportDefinition report, Color accent)
    {
        var card = new RoundedPanel {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.SurfaceSecondary,
            BorderColor = AppTheme.Border,
            ShowBorder = true,
            CornerRadius =
                Math.Max(
                    6,
                    AppTheme.CardRadius - 2),
            Margin = new Padding(
                0,
                0,
                AppMetrics.SpacingSm,
                AppMetrics.SpacingSm),
            Padding = new Padding(AppMetrics.SpacingMd)
        };
        var content = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        int titleHeight =
            Math.Max(
                22,
                AppMetrics.TextHeight(AppTheme.ButtonFont));
        int buttonHeight =
            Math.Min(
                AppMetrics.ButtonHeight,
                AppScale.Scale(32, AppScale.ControlSizeScale));
        content.RowStyles.Add(
            new RowStyle(SizeType.Absolute, titleHeight));
        content.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(
            new RowStyle(SizeType.Absolute, buttonHeight));
        content.Controls.Add(
            new Label {
                Text = report.Title,
                Dock = DockStyle.Fill,
                Font = AppTheme.ButtonFont,
                ForeColor = AppTheme.HeadingText,
                TextAlign =
                    ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(0)
            },
            0,
            0);
        content.Controls.Add(
            new Label {
                Text = report.Description,
                Dock = DockStyle.Fill,
                Font = AppTheme.NormalFont,
                ForeColor = AppTheme.MutedText,
                TextAlign =
                    ContentAlignment.TopLeft,
                AutoEllipsis = true,
                Margin = new Padding(0)
            },
            0,
            1);
        var button = new ModernButton {
            Text = "Open Report",
            Height = buttonHeight,
            ThemeRole =
                UiDesignSystem.Actions.Primary,
            Margin = new Padding(0)
        };
        AppMetrics.FitButtonWidth(button, minWidth: 112);
        button.SetColors(accent);
        button.Click += (_, _) => {
            /* The page is built by the shell, AFTER its leave check has passed. */
            _navigate(report.Factory, report.Module, report.Title);
        };
        content.Controls.Add(button, 0, 2);
        card.Controls.Add(content);
        var accentStrip = new Panel {
            Dock = DockStyle.Left,
            Width = 3,
            BackColor = accent,
            Margin = new Padding(0)
        };
        card.Controls.Add(accentStrip);
        return card;
    }
    private Control BuildEmptyState()
    {
        var panel = new RoundedPanel {
            Dock = DockStyle.Fill,
            Height = AppScale.Scale(
                160,
                AppScale.ControlSizeScale),
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            ShowBorder = true,
            CornerRadius = AppTheme.CardRadius,
            Padding = new Padding(
                AppMetrics.SectionPadding),
            Margin = new Padding(0)
        };
        panel.Controls.Add(
            new Label {
                Text =
                    "You do not have permission to view any reports.",
                Dock = DockStyle.Fill,
                Font = AppTheme.NormalFont,
                ForeColor = AppTheme.MutedText,
                TextAlign =
                    ContentAlignment.MiddleCenter
            });
        return panel;
    }
    private sealed record ReportDefinition(string Title, string Description, string Module, Func<Control> Factory);
}
