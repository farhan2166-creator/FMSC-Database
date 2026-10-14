namespace FMSC_SMS.Core;

/// <summary>A titled surface used to group related page content.</summary>
public class HumanCard : RoundedPanel
{
    public Panel Header { get; }
    private readonly Label _titleLabel;
    private readonly Label? _subtitleLabel;
    public Panel Content { get; } = new() {
        Dock = DockStyle.Fill,
        Padding = new Padding(0, UiDesignSystem.Spacing.Sm, 0, 0)
    };

    public HumanCard(
        string title,
        string? subtitle = null,
        Color? accent = null,
        Control? accessory = null)
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.Surface;
        BorderColor = AppTheme.Border;
        ShowBorder = true;
        CornerRadius = AppTheme.CardRadius;
        Padding = new Padding(UiDesignSystem.Spacing.Md);
        Margin = new Padding(0);
        Color moduleAccent = accent ?? AppTheme.ModuleAccent(title);
        int titleLineHeight =
            Math.Max(16, AppMetrics.TextHeight(UiDesignSystem.Typography.Action));
        int subtitleLineHeight = string.IsNullOrWhiteSpace(subtitle)
            ? 0
            : Math.Max(12, AppMetrics.TextHeight(AppTheme.NormalFont));
        int accessoryHeadroom = accessory == null
            ? 0
            : Math.Max(0, AppMetrics.ButtonHeight + UiDesignSystem.Spacing.Xs * 2
                - (titleLineHeight + subtitleLineHeight + UiDesignSystem.Spacing.Xs * 2));
        Header = new Panel {
            Dock = DockStyle.Top,
            Height = titleLineHeight + subtitleLineHeight + UiDesignSystem.Spacing.Xs * 2 + accessoryHeadroom,
            BackColor = AppTheme.Surface,
            Tag = "ThemeCardHeader",
            Padding = new Padding(
                UiDesignSystem.Spacing.Md,
                UiDesignSystem.Spacing.Xs,
                UiDesignSystem.Spacing.Md,
                UiDesignSystem.Spacing.Xs)
        };
        if (accessory is not null)
        {
            accessory.Dock = DockStyle.Right;
            accessory.Margin = new Padding(0);
            Header.Controls.Add(accessory);
        }
        _titleLabel = new Label {
            Tag = "ThemeHeaderTitle",
            Text = title,
            Dock = DockStyle.Top,
            Height = titleLineHeight,
            Font = UiDesignSystem.Typography.Action,
            ForeColor = AppTheme.HeadingText,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false
        };
        Header.Controls.Add(_titleLabel);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            _subtitleLabel = new Label {
                Tag = "ThemeHeaderGuidance",
                Text = subtitle,
                Dock = DockStyle.Bottom,
                Height = subtitleLineHeight,
                Font = UiDesignSystem.Typography.Body,
                ForeColor = AppTheme.MutedText,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                UseMnemonic = false
            };
            Header.Controls.Add(_subtitleLabel);
        }
        Header.Controls.Add(new Panel {
            Dock = DockStyle.Left,
            Width = 3,
            BackColor = moduleAccent
        });
        Controls.Add(Content);
        Controls.Add(Header);
        UiTheme.ApplyTree(Header);
    }

    public void SetHeaderText(string title, string? subtitle = null)
    {
        _titleLabel.Text = title;
        if (_subtitleLabel is not null && subtitle is not null)
            _subtitleLabel.Text = subtitle;
    }
}
