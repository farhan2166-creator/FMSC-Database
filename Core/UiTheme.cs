using System.Runtime.CompilerServices;

namespace FMSC_SMS.Core;

/// <summary>Shared semantic bindings, used for initial presentation and every theme refresh.</summary>
internal static class UiTheme
{
    private sealed class Style
    {
        internal Func<Font>? Font;
        internal Func<Color>? Foreground;
        internal Func<Color>? Background;
        internal Func<Color>? Border;
        internal Func<Color>? GridAccent;
        internal bool Applying;
    }
    private static readonly ConditionalWeakTable<Control, Style> Styles = new();
    private static readonly ConditionalWeakTable<Form, object> InitializedForms = new();
    internal static void InitializeOpenForms()
    {
        foreach (Form form in Application.OpenForms)
            if (!InitializedForms.TryGetValue(form, out _)) UiHelper.ApplyThemeToForm(form);
    }

    internal static void CaptureOpenForms()
    {
        foreach (Form form in Application.OpenForms) CaptureTree(form);
    }
    private static bool OwnsChildren(Control control) => control is IAccessibleInput ||
        control is DataGridView || control is MainDashboardForm.DashboardNavButton;

    private static Style Capture(Control control)
    {
        if (Styles.TryGetValue(control, out var existing)) return existing;
        var style = new Style {
            Font = FontRole(control.Font),
            Foreground = ColorRole(control.ForeColor),
            Background = control is Form ? () => AppTheme.PageBackground :
                IsHeaderContainer(control) ? () => Color.Transparent : ColorRole(control.BackColor),
            Border = control is RoundedPanel panel ? ColorRole(panel.BorderColor) : null,
            GridAccent = control is DataGridView grid ? ColorRole(UiHelper.SharedGridAccent(grid)) : null
        };
        Styles.Add(control, style);
        // A validation or status update may deliberately choose a different shared role.
        bool CanRebind() => !style.Applying && !AppTheme.IsChangingTheme && !control.IsDisposed;
        control.FontChanged += (_, _) => { if (CanRebind()) style.Font = FontRole(control.Font); };
        control.ForeColorChanged += (_, _) => { if (CanRebind()) style.Foreground = ColorRole(control.ForeColor); };
        control.BackColorChanged += (_, _) => { if (CanRebind()) style.Background = ColorRole(control.BackColor); };
        if (!OwnsChildren(control))
            control.ControlAdded += (_, e) => { if (e.Control is not null) ApplyTree(e.Control); };
        return style;
    }
    private static void CaptureTree(Control root)
    {
        if (root.IsDisposed) return;
        Capture(root);
        if (OwnsChildren(root)) return;
        foreach (Control child in root.Controls) CaptureTree(child);
    }
    private static bool IsHeaderContainer(Control control)
    {
        // Composite inputs also derive from Panel, but their native child inputs
        // require an opaque background. Only layout containers inherit the tint.
        if (OwnsChildren(control) || control is not Panel ||
            (control is not TableLayoutPanel && control.Controls.Count == 0)) return false;
        for (Control? parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.Tag is "ThemeCardHeader" or "ThemePageHeader") return true;
            if (parent is HumanCard || parent is V2Page || parent is Form) break;
        }
        return false;
    }
    internal static void ApplyTree(Control root)
    {
        if (root.IsDisposed) return;
        if (root is Form form) InitializedForms.GetValue(form, _ => new object());
        ApplyControl(root);
        if (OwnsChildren(root)) return;
        foreach (Control child in root.Controls) ApplyTree(child);
        root.Invalidate();
    }
    internal static void ApplyControl(Control control)
    {
        if (control.IsDisposed) return;
        // Captions contain literal punctuation; shortcuts are handled separately.
        if (control is Label label) label.UseMnemonic = false;
        if (control is ButtonBase action) action.UseMnemonic = false;
        var style = Capture(control);
        style.Applying = true;
        try
        {
            // These controls render their shared action and navigation roles themselves.
            if (control is ModernButton button) { button.ApplyTheme(); return; }
            if (control is MainDashboardForm.DashboardNavButton nav) { nav.RefreshTheme(); return; }
            if (control is DataGridView grid)
            {
                grid.BackgroundColor = AppTheme.Surface;
                grid.GridColor = AppTheme.Border;
                HumanUi.ApplyGridStyle(grid, style.GridAccent?.Invoke() ?? UiHelper.SharedGridAccent(grid));
                return;
            }
            if (style.Font is not null) control.Font = style.Font();
            if (style.Foreground is not null) control.ForeColor = style.Foreground();
            if (style.Background is not null) control.BackColor = style.Background();
            switch (control.Tag as string)
            {
                case "ThemeCardHeader": control.BackColor = AppTheme.SectionHeaderBackground; break;
                case "ThemePageHeader": control.BackColor = AppTheme.PageTitleBackground; break;
                case "ThemeHeaderTitle":
                    control.BackColor = Color.Transparent;
                    control.ForeColor = AppTheme.SectionHeadingText;
                    break;
                case "ThemeHeaderGuidance":
                    control.BackColor = Color.Transparent;
                    control.ForeColor = AppTheme.SectionGuidanceText;
                    break;
                case "ThemeHeaderContainer": control.BackColor = Color.Transparent; break;
            }
            if (control is RoundedPanel panel)
            {
                if (style.Border is not null) panel.BorderColor = style.Border();
                panel.RefreshRendering();
            }
            if (control is TabControl tabs) TabStrip.Repaint(tabs);
        }
        finally { style.Applying = false; }
    }
    private static Func<Font>? FontRole(Font font)
    {
        Func<Font>[] roles = {
            () => AppTheme.NormalFont, () => AppTheme.BodyBoldFont, () => AppTheme.ButtonFont,
            () => AppTheme.FieldCaptionFont, () => AppTheme.NavigationFont,
            () => AppTheme.HeadingFont, () => AppTheme.PageTitleFont, () => AppTheme.DisplayFont,
            () => AppTheme.SmallFont, () => AppTheme.FontSmallBold, () => AppTheme.FontTitle
        };
        foreach (var role in roles) if (ReferenceEquals(font, role())) return role;
        return font.Equals(Control.DefaultFont) ? () => AppTheme.NormalFont : null;
    }
    private static Func<Color>? ColorRole(Color color)
    {
        if (color.A == 0) return null;
        if (color.IsSystemColor)
        {
            if (color == SystemColors.ControlText || color == SystemColors.WindowText) return () => AppTheme.Text;
            if (color == SystemColors.Control || color == SystemColors.Window) return () => AppTheme.Surface;
        }
        Func<Color>[] roles = {
            () => AppTheme.Primary, () => AppTheme.Success, () => AppTheme.Warning, () => AppTheme.Danger,
            () => AppTheme.PrimaryLight, () => AppTheme.PageBackground, () => AppTheme.Surface,
            () => AppTheme.SurfaceSecondary, () => AppTheme.SurfaceElevated,
            () => AppTheme.HeadingText, () => AppTheme.Text, () => AppTheme.MutedText,
            () => AppTheme.Border, () => AppTheme.InputBorder, () => AppTheme.MenuHover,
            () => AppTheme.MenuText, () => AppTheme.MenuMutedText
        };
        foreach (var role in roles) if (color.ToArgb() == role().ToArgb()) return role;
        return null;
    }
}
