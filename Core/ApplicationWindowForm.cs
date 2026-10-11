using System.Runtime.InteropServices;

namespace FMSC_SMS.Core;

/// <summary>Native resizing frame with a custom caption and monitor-safe maximize bounds.</summary>
public class ApplicationWindowForm : Form
{
    private ApplicationTitleBar? caption;
    private Icon? schoolIcon;
    private Size? requestedClientSize;
    private readonly ToolTip saveShortcutTip = new();
    private readonly PageActionShortcutController saveShortcutController;
    public ApplicationWindowForm()
    {
        saveShortcutController = new PageActionShortcutController(this, saveShortcutTip);
        KeyboardShortcutHints.Register(this);
    }
    internal PageActionShortcutController ShortcutController => saveShortcutController;
    protected void SetSaveShortcut(ModernButton button) => saveShortcutController.SetSaveButton(button);
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (PageActionShortcutController.ActiveContent(this).Controller.TryHandle(keyData))
            return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void SetClientSizeCore(int x, int y)
    {
        requestedClientSize = new Size(x, y);
        base.SetClientSizeCore(x, y);
        if (caption is not null && UsesCaption && WindowState == FormWindowState.Normal)
            Size = new Size(Width + x - ClientSize.Width, Height + y + caption.Height - ClientSize.Height);
    }
    private bool UsesCaption => TopLevel && FormBorderStyle != FormBorderStyle.None;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            if (UsesCaption) parameters.Style &= ~0x00C00000;
            return parameters;
        }
    }
    protected override void OnLoad(EventArgs e)
    {
        if (UsesCaption && caption is null)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "FMSC_Logo Icon.ico");
            if (File.Exists(path)) { schoolIcon = new Icon(path); Icon = schoolIcon; }
            caption = new ApplicationTitleBar(this);
            // Reclaim the native caption's space while preserving the requested
            // dialog content size. The overall window height matches a native caption.
            if (WindowState == FormWindowState.Normal)
            {
                Size requested = requestedClientSize ?? ClientSize;
                // Form's ClientSize setter still budgets a native caption even
                // though CreateParams removed it. Correct the outer size directly.
                Size = new Size(Width + requested.Width - ClientSize.Width,
                    Height + requested.Height + caption.Height - ClientSize.Height);
            }
            Controls.Add(caption);
        }
        UiHelper.ApplyThemeToForm(this);
        base.OnLoad(e);
        FitWorkingArea();
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        FitWorkingArea();
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FitWorkingArea();
        if (this is not FMSC_SMS.MainDashboardForm)
            BeginInvoke(new Action(() => {
                if (!IsDisposed && !FormFlow.HasInputFocus(this))
                    FormFlow.ScheduleInitialFocus(this, this, () => Visible);
            }));
    }
    protected override bool ProcessTabKey(bool forward)
    {
        Control scope = PageActionShortcutController.ActiveContent(this).Root;
        if (scope != this && scope.ContainsFocus)
        {
            Control focused = scope;
            while (focused.Controls.Cast<Control>().FirstOrDefault(child => child.ContainsFocus) is { } child)
                focused = child;
            // Wrap within the open page before the shell can select its navigation controls.
            return scope.SelectNextControl(focused, forward, true, true, true);
        }
        return base.ProcessTabKey(forward);
    }
    protected override bool ProcessDialogKey(Keys keyData)
    {
        // Controls first handle Escape for their own open dropdown or edit.
        // A dialog without a Cancel button then closes through FormClosing,
        // so unsaved-change and in-progress-operation guards still run.
        if (keyData == Keys.Escape && TopLevel &&
            (Modal || Owner is not null && !ShowInTaskbar) && CancelButton is null)
        {
            Close();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }
    private void FitWorkingArea()
    {
        if (!TopLevel || WindowState != FormWindowState.Normal) return;
        Rectangle workingArea = Screen.FromControl(this).WorkingArea;
        Size available = workingArea.Size;
        available = new Size(Math.Max(1, available.Width - 16), Math.Max(1, available.Height - 16));
        MinimumSize = new Size(Math.Min(MinimumSize.Width, available.Width), Math.Min(MinimumSize.Height, available.Height));
        Size = new Size(Math.Min(Width, available.Width), Math.Min(Height, available.Height));
        Location = new Point(Math.Clamp(Left, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - Width)),
            Math.Clamp(Top, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - Height)));
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { KeyboardShortcutHints.Unregister(this); schoolIcon?.Dispose(); schoolIcon = null; saveShortcutTip.Dispose(); }
    }
    protected override void WndProc(ref Message message)
    {
        if (UsesCaption && message.Msg == 0x86) // WM_NCACTIVATE
        {
            // Modal close activates the owner. Let Windows update activation
            // without painting a legacy caption over our client title bar.
            message.LParam = new IntPtr(-1);
            base.WndProc(ref message);
            caption?.Invalidate(true);
            return;
        }
        if (UsesCaption && message.Msg == 0x85 && IsZoomed(message.HWnd)) // WM_NCPAINT
        {
            message.Result = IntPtr.Zero;
            return;
        }
        // WinForms updates native styles when a modal report disables and
        // re-enables its owner. Enforce the custom caption on those updates,
        // not just during initial handle creation.
        if (UsesCaption && message.Msg == 0x7C && message.WParam.ToInt64() == -16
            && message.LParam != IntPtr.Zero) // WM_STYLECHANGING / GWL_STYLE
        {
            var styles = Marshal.PtrToStructure<WindowStyles>(message.LParam);
            styles.NewStyle &= ~0x00C00000;
            Marshal.StructureToPtr(styles, message.LParam, false);
        }
        // A maximized custom-caption window must use the entire work area as
        // its client surface. Keeping the native resize frame here leaves
        // colored strips along the sides and bottom. Normal windows retain
        // that frame so native resizing and shadows still work.
        if (UsesCaption && message.Msg == 0x83 && message.WParam != IntPtr.Zero
            && IsZoomed(message.HWnd)) // WM_NCCALCSIZE; native state updates before Form.WindowState
        {
            message.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref message);
        if (!UsesCaption || message.Msg != 0x24 || message.LParam == IntPtr.Zero) return; // WM_GETMINMAXINFO
        // Removing the native caption changes Windows' frame calculations.
        // Constrain the OUTER window to the work area, rather than allowing
        // its resize borders/content to spill past the screen or taskbar.
        var screen = Screen.FromHandle(Handle);
        var bounds = Marshal.PtrToStructure<MinMaxInfo>(message.LParam);
        bounds.MaxPosition = new Point(screen.WorkingArea.Left - screen.Bounds.Left,
            screen.WorkingArea.Top - screen.Bounds.Top);
        bounds.MaxSize = screen.WorkingArea.Size;
        Marshal.StructureToPtr(bounds, message.LParam, false);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Size MaxSize;
        public Point MaxPosition;
        public Size MinTrackSize;
        public Size MaxTrackSize;
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr window);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowStyles
    {
        public int OldStyle;
        public int NewStyle;
    }
}
