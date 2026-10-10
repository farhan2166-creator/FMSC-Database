namespace FMSC_SMS.Core;

internal static class ReportPagerView
{
    internal sealed record Controls(Panel Panel, Label Label, ModernButton Previous, ModernButton Next);

    internal static Controls Create(Action previous, Action next)
    {
        ModernButton Button(string text)
        {
            var button = new ModernButton {
                Text = text,
                Width = AppScale.Scale(100, AppScale.ControlSizeScale),
                Height = AppMetrics.ButtonHeight,
                ThemeRole = "Outline"
            };
            button.SetOutlineColors(AppTheme.Border, AppTheme.Text, AppTheme.Surface);
            return button;
        }
        var previousButton = Button("Previous");
        var nextButton = Button("Next");
        var label = new Label {
            AutoSize = true,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(12, 0, 12, 0)
        };
        previousButton.Click += (_, _) => previous();
        nextButton.Click += (_, _) => next();
        var panel = new Panel {
            Dock = DockStyle.Bottom,
            Height = AppMetrics.ButtonHeight + 12,
            Padding = new Padding(0, 6, 0, 0),
            BackColor = AppTheme.Surface
        };
        var row = new FlowLayoutPanel {
            Dock = DockStyle.Right,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent
        };
        row.Controls.Add(label);
        row.Controls.Add(previousButton);
        row.Controls.Add(nextButton);
        panel.Controls.Add(row);
        return new Controls(panel, label, previousButton, nextButton);
    }
}
