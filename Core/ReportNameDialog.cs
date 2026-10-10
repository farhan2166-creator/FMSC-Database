namespace FMSC_SMS.Core;

/// <summary>A one-line "what should it be called" prompt.</summary>
public sealed class ReportNameDialog : ApplicationWindowForm
{
    private readonly ModernTextBox name = HumanUi.TextBox(false, 60);
    public string ResultName { get; private set; } = string.Empty;
    private ReportNameDialog(string title, string caption, string initial)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(AppScale.S(440), AppScale.S(170));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        var card = new RoundedPanel {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            Padding = new Padding(AppScale.S(18))
        };
        var label = new Label {
            Dock = DockStyle.Top,
            Height = AppScale.S(28),
            Text = caption,
            ForeColor = AppTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        };
        name.Dock = DockStyle.Top;
        name.Text = initial;
        var ok = new ModernButton {
            Text = "Save",
            ThemeRole = "Primary",
            Width = AppScale.S(100),
            Height = AppMetrics.ButtonHeight
        };
        var cancel = new ModernButton {
            Text = "Cancel",
            ThemeRole = "Secondary",
            Width = AppScale.S(100),
            Height = AppMetrics.ButtonHeight,
            Margin = new Padding(AppScale.S(8), 0, 0, 0)
        };
        ok.Click += (_, _) => {
            string text = name.Text.Trim();
            if (text.Length == 0)
                return;
            ResultName = text;
            DialogResult = DialogResult.OK;
        };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        var row = new FlowLayoutPanel {
            Dock = DockStyle.Bottom,
            Height = AppMetrics.ButtonHeight + AppScale.S(8),
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Color.Transparent
        };
        row.Controls.Add(ok);
        row.Controls.Add(cancel);
        card.Controls.Add(name);
        card.Controls.Add(label);
        card.Controls.Add(row);
        Controls.Add(card);
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => name.Focus();
    }
    /// <summary>Returns the entered name, or null when the operator cancelled.</summary>
    public static string? Prompt(IWin32Window? owner, string title, string caption, string initial = "")
    {
        using var dialog = new ReportNameDialog(title, caption, initial);
        return dialog.ShowDialog(owner) == DialogResult.OK
            ? dialog.ResultName
            : null;
    }
}



