namespace FMSC_SMS.Core;

internal static class WorkingAreaLayout
{
    internal sealed record Result(Rectangle Bounds,Size MinimumSize);

    internal static Result Fit(Rectangle current,Size minimum,Rectangle workingArea,int dpi = 96)
    {
        int inset = DpiLayout.Pixels(16, dpi);
        var available=new Size(Math.Max(1,workingArea.Width-inset),Math.Max(1,workingArea.Height-inset));
        var min=new Size(Math.Clamp(minimum.Width,0,available.Width),Math.Clamp(minimum.Height,0,available.Height));
        var size=new Size(Math.Clamp(current.Width,Math.Max(1,min.Width),available.Width),
            Math.Clamp(current.Height,Math.Max(1,min.Height),available.Height));
        var location=new Point(Math.Clamp(current.Left,workingArea.Left,Math.Max(workingArea.Left,workingArea.Right-size.Width)),
            Math.Clamp(current.Top,workingArea.Top,Math.Max(workingArea.Top,workingArea.Bottom-size.Height)));
        return new Result(new Rectangle(location,size),min);
    }
}
