using System.Runtime.InteropServices;
using System.Windows.Forms.VisualStyles;

namespace FMSC_SMS.Core;

/// <summary>Two-line family choices retaining CheckedListBox keyboard and check behavior.</summary>
internal sealed class FamilyStudentList : CheckedListBox
{
    /// <summary>Row clicks select for review; only the drawn checkbox changes inclusion.</summary>
    internal bool CheckBoxOnly { get; set; }
    private bool _mouseOutsideCheckBox;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr index, IntPtr height);
    protected override void WndProc(ref Message m)
    {
        // CheckedListBox normally toggles a selected item on a text click too,
        // even with CheckOnClick disabled. Limit mouse toggles to our drawn glyph.
        if (CheckBoxOnly && m.Msg is 0x0201 or 0x0202 or 0x0203)
        {
            var point = new Point(unchecked((short)m.LParam.ToInt64()), unchecked((short)(m.LParam.ToInt64() >> 16)));
            int index = IndexFromPoint(point);
            bool previous = _mouseOutsideCheckBox;
            _mouseOutsideCheckBox = index < 0 || !CheckBoxBounds(index).Contains(point);
            try { base.WndProc(ref m); }
            finally { _mouseOutsideCheckBox = previous; }
            return;
        }
        base.WndProc(ref m);
    }
    protected override void OnItemCheck(ItemCheckEventArgs e)
    {
        if (_mouseOutsideCheckBox)
        {
            e.NewValue = e.CurrentValue;
            return;
        }
        base.OnItemCheck(e);
    }
    internal Rectangle CheckBoxBounds(int index)
    {
        using var graphics = CreateGraphics();
        return CheckBoxBounds(index, graphics);
    }
    private Rectangle CheckBoxBounds(int index, Graphics graphics)
    {
        Rectangle row = GetItemRectangle(index);
        int inset = AppScale.Scale(8);
        var size = CheckBoxRenderer.GetGlyphSize(graphics, CheckBoxState.UncheckedNormal);
        return new Rectangle(row.Left + inset, row.Top + inset, size.Width, size.Height);
    }
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
        CheckBoxRenderer.DrawCheckBox(e.Graphics, CheckBoxBounds(e.Index, e.Graphics).Location, state);
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
