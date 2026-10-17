namespace FMSC_SMS.Core;

/// <summary>Places separate page previews in rows without changing their print geometry.</summary>
internal static class ReportPageArrangement
{
    internal static (List<Rectangle> Pages, Size Extent, float Scale) Calculate(
        int count, float pageWidth, float pageHeight, Size viewport, int gap, int labelHeight,
        int scrollbarWidth, float scale, bool fitWidth, bool fitPage)
    {
        if (count <= 0) return (new(), Size.Empty, scale);
        int fitColumns = Math.Min(2, count);
        if (fitWidth || fitPage)
        {
            scale = Math.Max(0.1f, (viewport.Width - gap * (fitColumns + 1) - scrollbarWidth) / (pageWidth * fitColumns));
            if (fitPage) scale = Math.Min(scale, Math.Max(0.1f, (viewport.Height - gap * 2 - labelHeight) / pageHeight));
        }
        int width = Math.Max(1, (int)Math.Floor(pageWidth * scale));
        int height = Math.Max(1, (int)Math.Ceiling(pageHeight * scale));
        int columns = Math.Min(count, Math.Max(1, (viewport.Width - gap - scrollbarWidth) / (width + gap)));
        var rectangles = new List<Rectangle>(count);
        for (int index = 0; index < count; index++)
            rectangles.Add(new Rectangle(gap + index % columns * (width + gap), gap + index / columns * (height + labelHeight + gap), width, height));
        return (rectangles, new Size(columns == 1 ? width + gap * 2 : 0, rectangles[^1].Bottom + labelHeight + gap), scale);
    }
}
