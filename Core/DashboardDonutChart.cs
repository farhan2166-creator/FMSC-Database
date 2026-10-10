using System.Drawing.Drawing2D;
namespace FMSC_SMS.Core;
public sealed class DashboardDonutChart : Control
{
    public decimal PrimaryValue { get; set; }
    public decimal SecondaryValue { get; set; }
    public string CenterText { get; set; } = "—";
    public string CenterCaption { get; set; } = "";
    public Color PrimaryColor { get; set; } = AppTheme.Primary;
    public Color SecondaryColor { get; set; } = AppTheme.Border;
    public DashboardDonutChart()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                  ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = AppTheme.Surface;
        Font = AppTheme.NormalFont;
        MinimumSize = new Size(
            AppScale.Scale(150, AppScale.ControlSizeScale),
            AppScale.Scale(130, AppScale.ControlSizeScale));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);
        decimal total = Math.Max(0, PrimaryValue) + Math.Max(0, SecondaryValue);
        int size = Math.Min(Width, Height) - 24;
        if (size < 40) return;
        int x = (Width - size) / 2;
        int y = Math.Max(4, (Height - size) / 2 - 4);
        var rect = new Rectangle(x, y, size, size);
        using var track = new Pen(SecondaryColor, 12);
        g.DrawArc(track, rect, 0, 360);
        if (total > 0 && PrimaryValue > 0)
        {
            using var primary = new Pen(PrimaryColor, 12);
            float sweep = (float)(360m * PrimaryValue / total);
            g.DrawArc(primary, rect, -90, sweep);
        }
        Font valueFont = AppTheme.SharedFont(AppTheme.HeadingFont.FontFamily.Name, 16F, FontStyle.Bold);
        Font captionFont = AppTheme.SharedFont(AppTheme.NormalFont.FontFamily.Name, 9F);
        var center = new Rectangle(x, y + size / 3, size, size / 3);
        TextRenderer.DrawText(g, CenterText, valueFont, center, AppTheme.HeadingText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, CenterCaption, captionFont,
            new Rectangle(x - 10, y + size / 2 + 5, size + 20, 20), AppTheme.MutedText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
