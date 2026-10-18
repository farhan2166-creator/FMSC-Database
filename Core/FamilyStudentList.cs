using System.Runtime.InteropServices;
using System.Windows.Forms.VisualStyles;

namespace FMSC_SMS.Core;

/// <summary>Two-line family choices retaining CheckedListBox keyboard and check behavior.</summary>
internal sealed class FamilyStudentList : CheckedListBox
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr index, IntPtr height);
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); FitRows(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitRows(); }
    private void FitRows()
    {
        if (IsHandleCreated) SendMessage(Handle, 0x01A0, IntPtr.Zero, (IntPtr)(Font.Height * 2 + AppScale.Scale(24)));
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? AppTheme.SurfaceSecondary : AppTheme.Surface);
        e.Graphics.FillRectangle(background, e.Bounds);
        var state = GetItemChecked(e.Index) ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal;
        var size = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
        int inset = AppScale.Scale(8), gap = AppScale.Scale(8);
        CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(e.Bounds.Left + inset, e.Bounds.Top + inset), state);
        string[] lines = (GetItemText(Items[e.Index]) ?? "").Split('\n', 2);
        int left = e.Bounds.Left + inset + size.Width + gap;
        var first = new Rectangle(left, e.Bounds.Top + inset, Math.Max(1, e.Bounds.Right - left - inset), Font.Height + 4);
        TextRenderer.DrawText(e.Graphics, lines[0], AppTheme.BodyBoldFont, first, AppTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (lines.Length > 1)
        {
            var second = new Rectangle(first.Left, first.Bottom + 2, first.Width, Font.Height + 4);
            TextRenderer.DrawText(e.Graphics, lines[1], Font, second, AppTheme.MutedText,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
}
