using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Students.Certificates;
public sealed class StudentCertificatesPage : V2Page
{
    // UI TUNING: certificate-card glyphs are intentionally larger than toolbar icons because
    // they are the visual identity of each card, not a small decoration beside a command.
    private static Font CertificateCardIconFont =>
        AppTheme.SharedFont(
            "Segoe UI Emoji",
            27F * AppScale.HeadingFontScale,
            FontStyle.Regular);
    private readonly Action<Func<Control>, string, string>? navigate;
    public StudentCertificatesPage(Action<Func<Control>, string, string>? navigate = null)
        : base(PageTitles.StudentCertificates, "Choose a certificate to create, issue or print.")
    {
        this.navigate = navigate;
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.Scale(92)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.Scale(74)));
        root.Controls.Add(BuildIntro(), 0, 0);
        root.Controls.Add(BuildCards(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);
        SetContent(root);
        Defer(ApplyScale);
    }
    private Control BuildIntro()
    {
        var card = HumanUi.Card(
            PageTitles.StudentCertificates,
            "Choose a certificate type, then select a student to get started.",
            AppTheme.ModuleAccent("Certificates"));
        card.Margin = new Padding(0, 0, 0, AppMetrics.SectionGap);
        return card;
    }
    private Control BuildCards()
    {
        var grid = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        for (int i = 0; i < 3; i++)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333f));
        for (int i = 0; i < 3; i++)
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3333f));
        AddCard(grid, 0, 0, "Regular Student", "Proof of current placement", () => new RegularStudentCertificatePage());
        AddCard(grid, 1, 0, "Character", "Student character certificate", () => new CharacterCertificatePage());
        AddCard(grid, 2, 0, "Leave", "Student leave certificate", () => new LeaveCertificatePage());
        AddCard(grid, 0, 1, "College Leaving", "College leaving certificate", () => new CollegeLeaveCertificatePage());
        AddCard(grid, 1, 1, "Date of Birth", "Student date of birth certificate", () => new DateOfBirthCertificatePage());
        AddCard(grid, 2, 1, "Sports", "Student sports certificate", () => new SportsCertificatePage());
        AddCard(grid, 0, 2, "No Objection", "No objection certificate", () => new NoObjectionCertificatePage());
        AddCard(grid, 1, 2, "Provisional", "Student provisional certificate", () => new ProvisionalCertificatePage());
        return grid;
    }
    private void AddCard(TableLayoutPanel grid, int column, int row, string title, string subtitle, Func<Control> factory)
    {
        var open = new ModernButton {
            Text = "Open",
            Width = AppScale.Scale(96),
            Height = AppMetrics.ButtonHeight,
            ThemeRole = UiDesignSystem.Actions.Primary,
            Margin = new Padding(0)
        };
        open.Click += (_, _) => Open(factory, title);
        var card = HumanUi.Card(title, subtitle, AppTheme.ModuleAccent("Certificates"), open);
        open.Image = UiHelper.CreateGlyphIcon("→", AppTheme.ModuleAccent("Certificates"), 21);
        open.ImageAlign = ContentAlignment.MiddleLeft;
        open.TextImageRelation = TextImageRelation.ImageBeforeText;
        open.Padding = new Padding(8, 0, 8, 0);
        // Familiar pictograms make the choices understandable before the operator reads every
        // subtitle. The visible title remains the authoritative label and accessibility fallback.
        string glyph = title switch {
            "Regular Student" => "🎓",
            "Character" => "★ ✓",
            "Leave" => "🚪 →",
            "College Leaving" => "🏫 →",
            "Date of Birth" => "🎂",
            "Sports" => "🏆",
            "No Objection" => "✅",
            "Provisional" => "⏳",
            _ => "📄"
        };
        var certificateIcon = new Label {
            Text = glyph,
            Dock = DockStyle.Top,
            Height = AppScale.Scale(44),
            Font = CertificateCardIconFont,
            ForeColor = AppTheme.ModuleAccent("Certificates"),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(2, 2, 0, 0),
            AccessibleName = $"{title} certificate",
            AccessibleDescription = subtitle
        };
        card.Content.Controls.Add(certificateIcon);
        void OpenCard(object? sender, EventArgs e) => Open(factory, title);
        card.Click += OpenCard;
        card.Header.Click += OpenCard;
        card.Content.Click += OpenCard;
        foreach (Control child in card.Header.Controls)
            if (child != open)
                child.Click += OpenCard;
        foreach (Control child in card.Content.Controls)
            child.Click += OpenCard;
        card.Cursor = Cursors.Hand;
        card.Margin = new Padding(0, 0, AppMetrics.SectionGap, AppMetrics.SectionGap);
        grid.Controls.Add(card, column, row);
    }
    private Control BuildFooter()
    {
        var label = new Label {
            Dock = DockStyle.Fill,
            Text = "Tip: use Ctrl + K to quickly jump to any certificate workflow.",
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(2, 0, 2, 0)
        };
        return label;
    }
    private void Open(Func<Control> factory, string title)
    {
        if (navigate != null)
        {
            navigate(factory, "Certificates", title);
            return;
        }
        using var dialog = new ApplicationWindowForm {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            Width = AppScale.Scale(1200),
            Height = AppScale.Scale(760),
            MinimumSize = new Size(AppScale.Scale(980), AppScale.Scale(620)),
            BackColor = AppTheme.PageBackground
        };
        var control = factory();
        control.Dock = DockStyle.Fill;
        dialog.Controls.Add(control);
        ModalPageCloseGuard.Attach(dialog,control);
        dialog.ShowDialog(FindForm());
    }
}

