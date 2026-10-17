using System.Drawing.Drawing2D;

namespace FMSC_SMS.Core;

/// <summary>Flat drawing avoids allocating a rounded path in the simple theme.
/// Shapes affect decoration only; control bounds and content insets stay unchanged.</summary>
internal readonly struct UiPaintShape : IDisposable
{
    private readonly Rectangle bounds;
    private readonly GraphicsPath? path;

    internal UiPaintShape(Rectangle bounds, int radius)
    {
        this.bounds = bounds;
        path = null;
        if (AppTheme.UseSimpleRendering || radius <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return;
        path = new GraphicsPath();
        int diameter = radius * 2;
        if (diameter > bounds.Width || diameter > bounds.Height) path.AddRectangle(bounds);
        else
        {
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
        }
    }

    internal void Fill(Graphics graphics, Brush brush)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (path is null) graphics.FillRectangle(brush, bounds);
        else graphics.FillPath(brush, path);
    }

    internal void Draw(Graphics graphics, Pen pen)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (path is null) graphics.DrawRectangle(pen, bounds);
        else graphics.DrawPath(pen, path);
    }

    public void Dispose() => path?.Dispose();
}
