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

    internal static (List<Rectangle> Pages, Size Extent, float Scale) Calculate(
        IReadOnlyList<SizeF> sizes,Size viewport,int gap,int labelHeight,int scrollbarWidth,float scale,bool fitWidth,bool fitPage)
    {
        if(sizes.Count==0)return(new(),Size.Empty,scale);
        float maxWidth=sizes.Max(s=>s.Width),maxHeight=sizes.Max(s=>s.Height);
        if(fitWidth||fitPage)
        {
            int fitColumns=Math.Min(2,sizes.Count);
            scale=Math.Max(0.1f,(viewport.Width-gap*(fitColumns+1)-scrollbarWidth)/(maxWidth*fitColumns));
            if(fitPage)scale=Math.Min(scale,Math.Max(0.1f,(viewport.Height-gap*2-labelHeight)/maxHeight));
        }
        int cellWidth=Math.Max(1,(int)Math.Ceiling(maxWidth*scale));
        int columns=Math.Min(sizes.Count,Math.Max(1,(viewport.Width-gap-scrollbarWidth)/(cellWidth+gap)));
        var rectangles=new List<Rectangle>();int top=gap;
        for(int start=0;start<sizes.Count;start+=columns)
        {
            int rowHeight=0;
            for(int i=start;i<Math.Min(start+columns,sizes.Count);i++)
            {
                var size=sizes[i];int width=Math.Max(1,(int)Math.Ceiling(size.Width*scale)),height=Math.Max(1,(int)Math.Ceiling(size.Height*scale));
                rectangles.Add(new Rectangle(gap+(i-start)*(cellWidth+gap),top,width,height));rowHeight=Math.Max(rowHeight,height);
            }
            top+=rowHeight+labelHeight+gap;
        }
        return(rectangles,new Size(columns==1?cellWidth+gap*2:0,top),scale);
    }
}
