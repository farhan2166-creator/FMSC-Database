using System.Runtime.InteropServices;

namespace FMSC_SMS.Core;

/// <summary>Compact caption replacement; the form retains its native resizing frame.</summary>
internal sealed class ApplicationTitleBar : Panel
{
    private readonly Form owner;
    private readonly Label title = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
    private readonly PictureBox logo = new() { Dock = DockStyle.Left, SizeMode = PictureBoxSizeMode.Zoom, Padding = new Padding(4), Margin = Padding.Empty };
    private readonly ModernButton minimize = new() { Text = "—", AccessibleName = "Minimize window" };
    private readonly ModernButton maximize = new() { Text = "□", AccessibleName = "Maximize window" };
    private readonly ModernButton close = new() { Text = "×", AccessibleName = "Close application" };
    private readonly ToolTip hints = new();
    private readonly Font glyphFont = new("Segoe UI", 12f, FontStyle.Regular);
    private readonly FlowLayoutPanel buttons = new() { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Margin = Padding.Empty };

    internal ApplicationTitleBar(Form form)
    {
        owner = form;
        Dock = DockStyle.Top;
        // Match the native caption height, rather than adding a second application header.
        Height = SystemInformation.CaptionHeight;
        title.Text = form.Text;
        title.Font = SystemFonts.CaptionFont;
        logo.Width = Height;
        if (form.Icon is not null) logo.Image = form.Icon.ToBitmap();
        foreach (var button in new[] { minimize, maximize, close })
        {
            button.AutoSize = false;
            button.Width = Math.Max(40, (int)Math.Round(44f * form.DeviceDpi / 96f));
            button.Height = Height;
            button.CornerRadius = 4;
            button.Font = glyphFont;
            button.Margin = new Padding(2, 0, 0, 0);
            buttons.Controls.Add(button);
        }
        Controls.Add(title); Controls.Add(logo); Controls.Add(buttons);
        minimize.Click += (_, _) => form.WindowState = FormWindowState.Minimized;
        maximize.Click += (_, _) => ToggleMaximize();
        close.Click += (_, _) => form.Close(); // Preserves the normal unsaved-edit guard.
        title.MouseDown += DragCaption; MouseDown += DragCaption;
        logo.MouseDown += (_, e) => {
            if (e.Button == MouseButtons.Left && e.Clicks == 2) { form.Close(); return; }
            if (e.Button is MouseButtons.Left or MouseButtons.Right)
            {
                Point point = logo.PointToScreen(new Point(0, logo.Height));
                int command = TrackPopupMenu(GetSystemMenu(form.Handle, false), 0x100, point.X, point.Y, 0, form.Handle, IntPtr.Zero);
                if (command != 0) SendMessage(form.Handle, 0x112, (IntPtr)command, IntPtr.Zero);
            }
        };
        form.TextChanged += UpdateTitle;
        form.Resize += UpdateWindowState;
        form.DpiChanged += UpdateDpi;
        AppTheme.ThemeChanged += UpdateTheme;
        ApplyTheme(); UpdateWindowState(null, EventArgs.Empty);
    }
    private void ToggleMaximize()
    {
        if (!owner.MaximizeBox || !owner.ControlBox) return;
        owner.WindowState = owner.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }
    private void DragCaption(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (e.Clicks == 2) { ToggleMaximize(); return; }
        ReleaseCapture();
        Point cursor = Cursor.Position;
        SendMessage(owner.Handle, 0xA1, (IntPtr)2, (IntPtr)((cursor.Y << 16) | (cursor.X & 0xffff)));
    }
    private void UpdateTitle(object? sender, EventArgs e) => title.Text = owner.Text;
    private void UpdateWindowState(object? sender, EventArgs e)
    {
        minimize.Visible = owner.ControlBox && owner.MinimizeBox;
        maximize.Visible = owner.ControlBox && owner.MaximizeBox;
        close.Visible = owner.ControlBox;
        bool restored = owner.WindowState == FormWindowState.Maximized;
        maximize.Text = restored ? "❐" : "□";
        maximize.AccessibleName = restored ? "Restore window" : "Maximize window";
        hints.SetToolTip(maximize, maximize.AccessibleName);
        hints.SetToolTip(minimize, "Minimize"); hints.SetToolTip(close, "Close");
    }
    private void UpdateDpi(object? sender, DpiChangedEventArgs e)
    {
        Height = Math.Max(24, (int)Math.Round(23f * e.DeviceDpiNew / 96f));
        logo.Width = Height;
        foreach (Control button in buttons.Controls) { button.Height = Height; button.Width = (int)Math.Round(44f * e.DeviceDpiNew / 96f); }
    }
    private void UpdateTheme(object? sender, EventArgs e) => ApplyTheme();
    private void ApplyTheme()
    {
        BackColor = AppTheme.SurfaceSecondary; title.ForeColor = AppTheme.Text;
        minimize.ThemeRole = maximize.ThemeRole = "Secondary";
        close.ThemeRole = "Danger";
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            owner.TextChanged -= UpdateTitle; owner.Resize -= UpdateWindowState; owner.DpiChanged -= UpdateDpi;
            AppTheme.ThemeChanged -= UpdateTheme; hints.Dispose(); logo.Image?.Dispose();
            glyphFont.Dispose();
        }
        base.Dispose(disposing);
    }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetSystemMenu(IntPtr hwnd, bool revert);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
}
