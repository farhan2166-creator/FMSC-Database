namespace FMSC_SMS.Core;
/// <summary>Shared themed tab-strip rendering.</summary>
public static class TabStrip
{
    /// <summary>Applies the shared strip: the fonts, the fixed tab size, the owner-drawn selected/inactive treatment, all tinted with the module accent.</summary>
    public static void Apply(TabControl tabs, Color accent)
    {
        tabs.Tag = accent;
        tabs.Font = AppTheme.ButtonFont;
        tabs.Padding = new Point(AppScale.Scale(14), AppScale.Scale(7));
        tabs.ItemSize = new Size(AppScale.Scale(138), Math.Max(AppScale.Scale(38), AppMetrics.TextHeight(tabs.Font) + AppScale.Scale(14)));
        tabs.SizeMode = TabSizeMode.FillToRight;
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.BackColor = AppTheme.Surface;
        tabs.ShowToolTips = true;
        tabs.DrawItem -= DrawItem;
        tabs.DrawItem += DrawItem;
        tabs.ControlAdded -= OnTabAdded;
        tabs.ControlAdded += OnTabAdded;
        RefreshShortcutToolTips(tabs);
    }
    /// <summary>Re-reads the theme after <see cref="AppTheme.ThemeChanged"/>. The strip's own colours are read at paint time, so invalidating is enough.</summary>
    public static void Repaint(TabControl tabs)
    {
        tabs.Font = AppTheme.ButtonFont;
        tabs.Invalidate();
    }
    private static void OnTabAdded(object? sender, ControlEventArgs e)
    {
        if (sender is TabControl tabs)
            RefreshShortcutToolTips(tabs);
    }
    private static void RefreshShortcutToolTips(TabControl tabs)
    {
        for (int index = 0; index < tabs.TabPages.Count; index++)
        {
            string direct = index < 9 ? $"Ctrl+{index + 1}; " : string.Empty;
            tabs.TabPages[index].ToolTipText =
                $"{tabs.TabPages[index].Text} ({direct}Ctrl+Tab / Ctrl+Shift+Tab)";
        }
    }
    private static void DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs ||
            e.Index < 0 ||
            e.Index >= tabs.TabPages.Count)
        {
            return;
        }
        Color accent =
            tabs.Tag is Color tint ? tint : AppTheme.Primary;
        bool selected =
            e.Index == tabs.SelectedIndex;
        Rectangle rect = e.Bounds;
        rect.Inflate(-2, -2);
        using var back = new SolidBrush(
            selected
                ? AppTheme.Tint(accent, 0.88f)
                : AppTheme.Surface);
        using var border = new Pen(
            selected
                ? accent
                : AppTheme.Border,
            selected ? 2f : 1f);
        e.Graphics.FillRectangle(back, rect);
        e.Graphics.DrawRectangle(border, rect);
        TextRenderer.DrawText(
            e.Graphics,
            tabs.TabPages[e.Index].Text,
            AppTheme.ButtonFont,
            rect,
            selected ? accent : AppTheme.Text,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);
    }
}
