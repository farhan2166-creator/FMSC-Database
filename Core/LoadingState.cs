namespace FMSC_SMS.Core;
public sealed class LoadingState : Panel
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 80 };
    private int _phase;
    public LoadingState(string title = "Loading", string? subtitle = null)
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.Surface;
        DoubleBuffered = true;
        Padding = new Padding(24);
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        var spinner = new Label {
            Text = "●  ●  ●",
            Dock = DockStyle.Fill,
            Font = new Font(AppTheme.NormalFont.FontFamily, 13f, FontStyle.Bold),
            ForeColor = AppTheme.Primary,
            TextAlign = ContentAlignment.BottomCenter
        };
        var heading = new Label {
            Text = title,
            Dock = DockStyle.Fill,
            Font = AppTheme.HeadingFont,
            ForeColor = AppTheme.HeadingText,
            TextAlign = ContentAlignment.MiddleCenter
        };
        var detail = new Label {
            Text = subtitle ?? "Preparing this section...",
            Dock = DockStyle.Fill,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.TopCenter
        };
        layout.Controls.Add(spinner, 0, 0);
        layout.Controls.Add(heading, 0, 1);
        layout.Controls.Add(detail, 0, 2);
        Controls.Add(layout);
        _timer.Tick += (_, _) => {
            _phase = (_phase + 1) % 4;
            spinner.Text = new string('●', _phase + 1) + "  " + new string('·', 3 - _phase);
        };
        _timer.Start();
        Disposed += (_, _) => {
            _timer.Stop();
            _timer.Dispose();
        };
        AppTheme.ThemeChanged += OnThemeChanged;
        Disposed += (_, _) => AppTheme.ThemeChanged -= OnThemeChanged;
    }
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        BackColor = AppTheme.Surface;
        Invalidate(true);
    }
}
