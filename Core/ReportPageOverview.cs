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
    private bool singlePage;
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
        var streams = RdlcReportPrinter.RenderPages(viewer.LocalReport, viewer.GetPageSettings());
        LoadPages(streams);
    }

    internal void LoadPages(IEnumerable<MemoryStream> streams,bool clone=false)
    {
        ClearPages();
        var owned=streams.Select(stream=>clone?new MemoryStream(stream.ToArray()):stream).ToList();
        try
        {
            foreach (var stream in owned){stream.Position=0;pages.Add((stream, new Metafile(stream)));}
        }
        catch
        {
            ClearPages();
            foreach (var stream in owned) stream.Dispose();
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
        if(singlePage)UpdateLayout();
        if (page > 0 && page <= rectangles.Count)
            AutoScrollPosition = new Point(0, Math.Max(0, rectangles[page - 1].Top - AppScale.Scale(24)));
        Invalidate();
    }

    internal void SetSinglePage(bool single){singlePage=single;UpdateLayout();}

    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateLayout(); }

    private void UpdateLayout()
    {
        if (pages.Count == 0 || ClientSize.Width <= 0) return;
        int gap = AppScale.Scale(24), labelHeight = AppScale.Scale(24);
        var indices=Enumerable.Range(0,pages.Count).Where(i=>!singlePage||i==selectedPage-1).ToArray();
        var sizes=indices.Select(i=>new SizeF(pages[i].Image.Width/pages[i].Image.HorizontalResolution*DeviceDpi,
            pages[i].Image.Height/pages[i].Image.VerticalResolution*DeviceDpi)).ToArray();
        var arrangement = ReportPageArrangement.Calculate(sizes, ClientSize,
            gap, labelHeight, SystemInformation.VerticalScrollBarWidth, zoomPercent / 100f,
            zoomMode == ZoomMode.PageWidth, zoomMode == ZoomMode.FullPage);
        rectangles.Clear();rectangles.AddRange(Enumerable.Repeat(Rectangle.Empty,pages.Count));
        for(int i=0;i<indices.Length;i++)rectangles[indices[i]]=arrangement.Pages[i];
        EffectiveZoomPercent = (int)Math.Round(arrangement.Scale * 100);
        AutoScrollMinSize = arrangement.Extent;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        for (int index = 0; index < pages.Count; index++)
        {
            if(rectangles[index].IsEmpty)continue;
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
        SelectAt(e.Location);
    }

    internal void SelectAt(Point position)
    {
        var location = new Point(position.X - AutoScrollPosition.X, position.Y - AutoScrollPosition.Y);
        int index = rectangles.FindIndex(rectangle => !rectangle.IsEmpty&&rectangle.Contains(location));
        if (index < 0) return;
        selectedPage = index + 1; Invalidate(); PageSelected?.Invoke(selectedPage);
    }

    protected override void Dispose(bool disposing) { if (disposing) ClearPages(); base.Dispose(disposing); }
}
