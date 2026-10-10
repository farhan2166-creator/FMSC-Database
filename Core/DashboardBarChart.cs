using System.Drawing.Drawing2D;
namespace FMSC_SMS.Core;
public sealed class DashboardBarChart : Control
{
    public sealed record Bar(string Label, decimal Value);
    private IReadOnlyList<Bar> _bars = Array.Empty<Bar>();
    public IReadOnlyList<Bar> Bars
    {
        get => _bars;
        set { _bars = value ?? Array.Empty<Bar>(); Invalidate(); }
    }
    public Color BarColor { get; set; } = AppTheme.Primary;
    public DashboardBarChart()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                  ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = AppTheme.Surface;
        Font = AppTheme.NormalFont;
        MinimumSize = new Size(
            AppScale.Scale(180, AppScale.ControlSizeScale),
            AppScale.Scale(120, AppScale.ControlSizeScale));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);
        if (_bars.Count == 0)
        {
            TextRenderer.DrawText(g, "No data available yet.", Font, ClientRectangle,
                AppTheme.MutedText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        int left = 8, right = 8, top = 12, bottom = 34;
        int width = Math.Max(1, Width - left - right);
        int height = Math.Max(1, Height - top - bottom);
        decimal max = _bars.Max(x => x.Value);
        if (max <= 0) max = 1;
        using var grid = new Pen(AppTheme.Border, 1f);
        for (int i = 0; i <= 3; i++)
        {
            int y = top + height - (height * i / 3);
            g.DrawLine(grid, left, y, left + width, y);
        }
        int slot = Math.Max(1, width / _bars.Count);
        int barWidth = Math.Max(10, (int)(slot * .48));
        using var brush = new SolidBrush(BarColor);
        for (int i = 0; i < _bars.Count; i++)
        {
            var bar = _bars[i];
            int x = left + i * slot + (slot - barWidth) / 2;
            int barHeight = (int)(height * (double)(bar.Value / max));
            int y = top + height - barHeight;
            if (barHeight > 0)
            {
                var rect = new Rectangle(x, y, barWidth, barHeight);
                using var path = RoundedTop(rect, Math.Min(5, barWidth / 2));
                g.FillPath(brush, path);
            }
            TextRenderer.DrawText(g, bar.Label, Font,
                new Rectangle(left + i * slot, top + height + 5, slot, bottom - 5),
                AppTheme.MutedText, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        }
    }
    private static GraphicsPath RoundedTop(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || rect.Height <= radius * 2) { path.AddRectangle(rect); return path; }
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddLine(rect.Right, rect.Y + radius, rect.Right, rect.Bottom);
        path.AddLine(rect.Right, rect.Bottom, rect.Left, rect.Bottom);
        path.AddLine(rect.Left, rect.Bottom, rect.Left, rect.Y + radius);
        path.CloseFigure();
        return path;
    }
}
