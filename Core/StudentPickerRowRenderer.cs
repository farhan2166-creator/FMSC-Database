namespace FMSC_SMS.Core;

internal static class StudentPickerRowRenderer
{
    internal static void Draw(DrawItemEventArgs e, StudentPicker.Entry entry,
        IReadOnlyList<StudentPicker.ColumnSpec> columns, Font font)
    {
        bool selected = (e.State & DrawItemState.Selected) != 0 && entry.Id > 0;
        Color back = selected ? AppTheme.SelectionBackground : AppTheme.SurfaceElevated;
        Color fore = entry.Id == 0 ? AppTheme.MutedText : selected ? AppTheme.SelectionText : AppTheme.Text;
        using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
        if (entry.Id == 0 || entry.Cells.Length == 0)
        {
            TextRenderer.DrawText(e.Graphics, entry.Display, font, Rectangle.Inflate(e.Bounds, -6, 0), fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            return;
        }
        int x = e.Bounds.Left + 6;
        for (int index = 0; index < columns.Count && index < entry.Cells.Length; index++)
        {
            StudentPicker.ColumnSpec column = columns[index];
            var cell = new Rectangle(x, e.Bounds.Top, column.Width, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, entry.Cells[index], index == 0 ? AppTheme.ButtonFont : font,
                cell, fore, (column.RightAligned ? TextFormatFlags.Right : TextFormatFlags.Left) |
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            x += column.Width + 14;
        }
    }
}
