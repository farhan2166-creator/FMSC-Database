using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Security;
public sealed class ChangePasswordHostPage : UserControl
{
    public ChangePasswordHostPage()
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.PageBackground;
        var card = new RoundedPanel {
            Width = AppScale.Scale(520, AppScale.ControlSizeScale),
            Height = AppScale.Scale(190, AppScale.ControlSizeScale),
            Anchor = AnchorStyles.None,
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            ShowBorder = true,
            CornerRadius = AppTheme.CardRadius,
            Padding = new Padding(AppMetrics.SpacingXxl)
        };
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        layout.Controls.Add(new Label {
            Text = "Change your account password",
            Dock = DockStyle.Fill,
            Font = AppTheme.HeadingFont,
            ForeColor = AppTheme.HeadingText,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        var button = new ModernButton {
            Text = "Open Change Password",
            Height = AppMetrics.LargeButtonHeight,
            Anchor = AnchorStyles.Left,
            ThemeRole = "Primary"
        };
        AppMetrics.FitButtonWidth(button, minWidth: 220);
        button.Click += (_, _) => {
            using var form = new ChangePasswordForm();
            form.ShowDialog(FindForm());
        };
        layout.Controls.Add(button, 0, 1);
        card.Controls.Add(layout);
        Controls.Add(card);
        Resize += (_, _) => {
            card.Left = Math.Max(0, (ClientSize.Width - card.Width) / 2);
            card.Top = Math.Max(0, (ClientSize.Height - card.Height) / 2);
        };
        PerformLayout();
    }
}
