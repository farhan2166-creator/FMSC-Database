using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Finance.Configuration;
/// <summary>The two layout pieces the Copy Fees and Adjust Fees tools share, so the tabs look and behave alike.</summary>
internal static class FeeToolLayout
{
    public static Label Help(string text, ToolTip tip)
    {
        var help = new Label {
            Text = text,
            Dock = DockStyle.Top,
            AutoSize = false,
            AutoEllipsis = true,
            Height = AppMetrics.TextHeight(AppTheme.NormalFont) + 8,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText
        };
        tip.SetToolTip(help, text);
        return help;
    }
    public static TableLayoutPanel Toolbar(Label summary, ToolTip tip, params Control[] buttons)
    {
        summary.AutoSize = false;
        summary.AutoEllipsis = true;
        summary.Dock = DockStyle.Fill;
        summary.TextAlign = ContentAlignment.MiddleLeft;
        summary.Margin = new Padding(0, 0, 12, 0);
        summary.TextChanged += (_, _) => tip.SetToolTip(summary, summary.Text);
        var actions = new FlowLayoutPanel {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0)
        };
        foreach (Control button in buttons)
        {
            if (button is ModernButton)
            {
                button.Height = AppMetrics.ButtonHeight;
                button.Width = Math.Max(
                    AppScale.Scale(110),
                    Math.Max(button.Width, AppMetrics.TextWidth(button.Text, button.Font, AppScale.Scale(32))));
            }
            button.Margin = new Padding(AppScale.Scale(8), 0, 0, 0);
            actions.Controls.Add(button);
        }
        ActionButtonStyle.Arrange(actions);
        var toolbar = new TableLayoutPanel {
            Dock = DockStyle.Top,
            Height = AppMetrics.ButtonHeight + 12,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0, 6, 0, 6)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toolbar.Controls.Add(summary, 0, 0);
        toolbar.Controls.Add(actions, 1, 0);
        return toolbar;
    }
    /// <summary>A row of equal-width fields, each a caption over its control.</summary>
    public static TableLayoutPanel FieldRow(params (string Caption, Control Control, bool Required)[] fields)
    {
        var row = new TableLayoutPanel {
            Dock = DockStyle.Top,
            ColumnCount = fields.Length,
            RowCount = 1,
            Height = AppMetrics.FieldRowHeight,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (int i = 0; i < fields.Length; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / fields.Length));
            row.Controls.Add(HumanUi.Field(fields[i].Caption, fields[i].Control, fields[i].Required), i, 0);
        }
        return row;
    }
}
