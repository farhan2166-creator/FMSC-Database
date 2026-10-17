using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Setup.Academics;
public sealed class ClassesAndSectionsWorkspacePage : UserControl, IBusyPage
{
    // Owner policy (UFA-03): small setup drafts stay prompt-free; submitted writes must finish.
    private readonly ClassesPage classes;
    private readonly SectionsPage sections;
    public bool IsBusy => classes.IsBusy || sections.IsBusy;
    public string BusyMessage => classes.IsBusy ? classes.BusyMessage : sections.BusyMessage;
    public ClassesAndSectionsWorkspacePage()
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.PageBackground;
        Padding = new Padding(AppMetrics.PagePadding);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, 2)
        };
        var split = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        classes = new ClassesPage(splitEditorAndRecords: false);
        sections = new SectionsPage(splitEditorAndRecords: false);
        classes.PrepareForEmbedding(keepActions: true);
        sections.PrepareForEmbedding(keepActions: true);
        split.Controls.Add(Wrap("CLASSES", classes), 0, 0);
        split.Controls.Add(Wrap("SECTIONS", sections), 1, 0);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(split, 0, 0);
        Controls.Add(root);
    }
    private static Control Wrap(string title, Control content)
    {
        var card = new RoundedPanel {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            ShowBorder = true,
            CornerRadius = AppTheme.CardRadius,
            Padding = new Padding(8),
            Margin = new Padding(0, 0, 8, 0)
        };
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max(16, AppMetrics.TextHeight(AppTheme.ButtonFont)) + 8));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,Padding=Padding.Empty};
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        heading.Controls.Add(new Label {
            Text = title,
            Dock = DockStyle.Fill,
            Font = AppTheme.ButtonFont,
            ForeColor = AppTheme.ModuleAccent("Setup"),
            Tag = "ThemeAccentText",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize=true,
            Padding = new Padding(8, 0, 0, 0)
        }, 0, 0);
        root.Controls.Add(heading,0,0);
        content.Dock = DockStyle.Fill;
        root.Controls.Add(content, 0, 1);
        card.Controls.Add(root);
        if(content is V2Page page)page.SetStatusHeaderHost(heading);
        return card;
    }
}
