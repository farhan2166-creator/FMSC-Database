using System.Drawing.Imaging;
using Microsoft.Reporting.WinForms;

namespace FMSC_SMS.Core;

/// <summary>A screen-only view of the original printable pages. Never changes the report definition.</summary>
internal sealed class ReportPageOverview : ScrollableControl
{
    private readonly List<(MemoryStream Stream, Metafile Image)> pages = new();
    private readonly List<Rectangle> rectangles = new();
    private int selectedPage = 1;
    private ZoomMode zoomMode = ZoomMode.FullPage;
    private int zoomPercent = 100;
    internal event Action<int>? PageSelected;
    internal int EffectiveZoomPercent { get; private set; } = 100;
    internal bool HasPages => pages.Count > 0;

    internal ReportPageOverview()
    {
        Dock = DockStyle.Fill; AutoScroll = true; BackColor = AppTheme.Surface;
        DoubleBuffered = true; TabStop = true;
        AccessibleName = "Multiple report pages";
    }

    internal void LoadPages(ReportViewer viewer)
    {
        ClearPages();
        var streams = RdlcReportPrinter.RenderPages(viewer.LocalReport, viewer.GetPageSettings());
        try
        {
            foreach (var stream in streams) pages.Add((stream, new Metafile(stream)));
        }
        catch
        {
            ClearPages();
            foreach (var stream in streams) stream.Dispose();
            throw;
        }
        UpdateLayout();
    }

    internal void ClearPages()
    {
        foreach (var page in pages) { page.Image.Dispose(); page.Stream.Dispose(); }
        pages.Clear(); rectangles.Clear(); AutoScrollMinSize = Size.Empty; Invalidate();
    }

    internal void SetZoom(ZoomMode mode, int percent)
    {
        zoomMode = mode; zoomPercent = percent; UpdateLayout();
    }

    internal void SelectPage(int page)
    {
        selectedPage = page;
        if (page > 0 && page <= rectangles.Count)
            AutoScrollPosition = new Point(0, Math.Max(0, rectangles[page - 1].Top - AppScale.Scale(24)));
        Invalidate();
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateLayout(); }

    private void UpdateLayout()
    {
        if (pages.Count == 0 || ClientSize.Width <= 0) return;
        int gap = AppScale.Scale(24), labelHeight = AppScale.Scale(24);
        float maxWidth = pages.Max(page => page.Image.Width / page.Image.HorizontalResolution * DeviceDpi);
        float maxHeight = pages.Max(page => page.Image.Height / page.Image.VerticalResolution * DeviceDpi);
        var arrangement = ReportPageArrangement.Calculate(pages.Count, maxWidth, maxHeight, ClientSize,
            gap, labelHeight, SystemInformation.VerticalScrollBarWidth, zoomPercent / 100f,
            zoomMode == ZoomMode.PageWidth, zoomMode == ZoomMode.FullPage);
        rectangles.Clear(); rectangles.AddRange(arrangement.Pages);
        EffectiveZoomPercent = (int)Math.Round(arrangement.Scale * 100);
        AutoScrollMinSize = arrangement.Extent;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        for (int index = 0; index < pages.Count; index++)
        {
            var rectangle = rectangles[index]; rectangle.Offset(AutoScrollPosition);
            var label = new Rectangle(rectangle.Left, rectangle.Bottom + 3, rectangle.Width, AppScale.Scale(24));
            if (!rectangle.IntersectsWith(e.ClipRectangle) && !label.IntersectsWith(e.ClipRectangle)) continue;
            e.Graphics.FillRectangle(Brushes.White, rectangle);
            e.Graphics.DrawImage(pages[index].Image, rectangle);
            using var border = new Pen(index + 1 == selectedPage ? AppTheme.Text : Color.Gray, index + 1 == selectedPage ? 2 : 1);
            e.Graphics.DrawRectangle(border, rectangle);
            TextRenderer.DrawText(e.Graphics, $"Page {index + 1}", Font, label, AppTheme.Text, TextFormatFlags.HorizontalCenter);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        var location = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
        int index = rectangles.FindIndex(rectangle => rectangle.Contains(location));
        if (index < 0) return;
        selectedPage = index + 1; Invalidate(); PageSelected?.Invoke(selectedPage);
    }

    protected override void Dispose(bool disposing) { if (disposing) ClearPages(); base.Dispose(disposing); }
}
