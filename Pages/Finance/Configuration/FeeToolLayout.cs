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
        summary.Margin = Padding.Empty;
        summary.TextChanged += (_, _) => tip.SetToolTip(summary, summary.Text);
        var actions = new FlowLayoutPanel {
            Name = "FeeToolActions", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty
        };
        foreach (Control button in buttons)
        {
            if (button is ModernButton)
            {
                button.Height = AppMetrics.ButtonHeight;
                button.Width = Math.Max(AppScale.Scale(110), Math.Max(button.Width,
                    AppMetrics.TextWidth(button.Text, button.Font, AppScale.Scale(32))));
            }
            button.Dock = DockStyle.None;
            button.Margin = new Padding(0, 0, AppScale.Scale(8), 0);
            actions.Controls.Add(button);
        }
        ActionButtonStyle.Arrange(actions);
        var actionHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        actionHost.Controls.Add(actions);
        void CenterActions() => actions.Location = new Point(
            Math.Max(0, (actionHost.ClientSize.Width - actions.Width) / 2),
            Math.Max(0, (actionHost.ClientSize.Height - actions.Height) / 2));
        actionHost.SizeChanged += (_, _) => CenterActions();
        actions.SizeChanged += (_, _) => CenterActions();
        int summaryHeight = AppMetrics.TextHeight(summary.Font) * 2 + 8;
        var toolbar = new TableLayoutPanel {
            Dock = DockStyle.Top, Height = AppMetrics.ButtonHeight + summaryHeight + 16,
            ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 4)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + 8));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toolbar.Controls.Add(actionHost, 0, 0);
        toolbar.Controls.Add(summary, 0, 1);
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
