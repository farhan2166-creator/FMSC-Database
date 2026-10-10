
namespace FMSC_SMS.Core;

/// <summary>Handles gestures on the report canvas, including controls replaced during rendering.</summary>
internal sealed class ReportPreviewInteractionFilter : IMessageFilter, IDisposable
{
    private readonly Form window;
    private readonly Control viewer;
    private readonly Action<int> zoom;
    private readonly ContextMenuStrip menu;
    private ScrollableControl? canvas;
    private Point dragStart, scrollStart;
    private bool dragging, disposed;
    private Cursor? originalCursor;

    internal ReportPreviewInteractionFilter(Form window, Control viewer, Action<int> zoom, ContextMenuStrip menu)
    {
        this.window = window; this.viewer = viewer; this.zoom = zoom; this.menu = menu;
        Application.AddMessageFilter(this);
    }

    public bool PreFilterMessage(ref Message message)
    {
        if (disposed || window.IsDisposed || !window.Visible || !window.Enabled) { EndDrag(); return false; }
        var source = Control.FromChildHandle(message.HWnd);
        if (source is null || source.FindForm() != window || !InsideViewer(source)) return false;
        if (message.Msg == 0x001F || (message.Msg == 0x0215 && source == canvas)) { EndDrag(); return false; } // WM_CANCELMODE / WM_CAPTURECHANGED
        if (message.Msg == 0x020A && (Control.ModifierKeys & Keys.Control) != 0) // WM_MOUSEWHEEL
        {
            int delta = (short)((message.WParam.ToInt64() >> 16) & 0xffff);
            if (delta != 0) zoom(Math.Sign(delta));
            return true;
        }
        var surface = ScrollSurface(source);
        if (surface is null || source is ScrollBar) return false; // Leave toolbars, scrollbars and other controls alone.
        if (message.Msg is 0x0205 or 0x007B) // WM_RBUTTONUP / WM_CONTEXTMENU
        {
            EndDrag();
            menu.Show(source, source.PointToClient(Cursor.Position));
            return true;
        }
        if (message.Msg == 0x0201) // WM_LBUTTONDOWN
        {
            EndDrag(); canvas = surface; dragStart = Cursor.Position;
            scrollStart = new Point(-surface.AutoScrollPosition.X, -surface.AutoScrollPosition.Y);
            return false; // A click without dragging keeps report links working.
        }
        if (message.Msg == 0x0200 && canvas is not null) // WM_MOUSEMOVE
        {
            if ((Control.MouseButtons & MouseButtons.Left) == 0) { EndDrag(); return false; }
            var current = Cursor.Position;
            int dx = current.X - dragStart.X, dy = current.Y - dragStart.Y;
            if (!dragging && Math.Abs(dx) < SystemInformation.DragSize.Width / 2 && Math.Abs(dy) < SystemInformation.DragSize.Height / 2)
                return false;
            if (!dragging) { originalCursor = canvas.Cursor; dragging = true; canvas.Capture = true; canvas.Cursor = Cursors.Hand; }
            canvas.AutoScrollPosition = new Point(Math.Max(0, scrollStart.X - dx), Math.Max(0, scrollStart.Y - dy));
            return true;
        }
        if (message.Msg == 0x0202) // WM_LBUTTONUP
        {
            bool handled = dragging; EndDrag(); return handled;
        }
        return false;
    }

    private bool InsideViewer(Control source)
    {
        for (Control? control = source; control is not null; control = control.Parent)
            if (control == viewer) return true;
        return false;
    }

    private ScrollableControl? ScrollSurface(Control source)
    {
        for (Control? control = source; control is not null && control != viewer; control = control.Parent)
            if (control is ScrollableControl scroll && scroll.AutoScroll) return scroll;
        return null;
    }

    private void EndDrag()
    {
        var surface = canvas;
        bool wasDragging = dragging;
        canvas = null; dragging = false;
        if (surface is { IsDisposed: false } && wasDragging) { surface.Capture = false; surface.Cursor = originalCursor ?? Cursors.Default; }
        originalCursor = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        EndDrag(); disposed = true; Application.RemoveMessageFilter(this); menu.Dispose();
    }
}
