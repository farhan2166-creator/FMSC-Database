using System.Globalization;

namespace FMSC_SMS.Core;

internal static class MasterGridLayout
{
    internal static bool IsInternal(DataGridViewColumn column) =>
        column.Name is "__OrderUp" or "__OrderDown" or "__Delete";

    internal static void Apply(DataGridView grid, Func<DataGridViewColumn, bool> isBoolean)
    {
        if (grid.Columns.Count == 0) return;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        var flexible = new List<DataGridViewColumn>();
        var measurements = new TextWidthMeasurementSession();
        int fixedWidth = 0;
        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (!column.Visible) continue;
            if (IsInternal(column)) { fixedWidth += Math.Max(column.Width, column.MinimumWidth); continue; }
            if (isBoolean(column))
            {
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.MinimumWidth = column.Width = AppScale.Scale(70);
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                fixedWidth += column.Width;
                continue;
            }
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.MinimumWidth = column.Width = RequiredWidth(grid, column, measurements);
            flexible.Add(column);
        }
        int remaining = grid.DisplayRectangle.Width - fixedWidth - flexible.Sum(column => column.Width);
        if (remaining <= 0 || flexible.Count == 0) return;
        int each = remaining / flexible.Count;
        int remainder = remaining % flexible.Count;
        for (int index = 0; index < flexible.Count; index++)
            flexible[index].Width += each + (index < remainder ? 1 : 0);
    }

    private static int RequiredWidth(DataGridView grid, DataGridViewColumn column,
        TextWidthMeasurementSession measurements)
    {
        int padding = AppScale.Scale(24);
        int ceiling = AppScale.Scale(600);
        int width = AppScale.Scale(70);
        Font headerFont = column.HeaderCell.InheritedStyle.Font ?? grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font;
        if (!string.IsNullOrWhiteSpace(column.HeaderText))
            width = Math.Max(width, measurements.Measure(column.HeaderText, headerFont) + padding);
        if (width >= ceiling) return ceiling;
        Font cellFont = column.DefaultCellStyle.Font ?? grid.DefaultCellStyle.Font ?? grid.Font;
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow || row.Cells[column.Index].Value is null or DBNull) continue;
            string text = Format(row.Cells[column.Index].Value!);
            if (text.Length > 0)
                width = Math.Max(width, measurements.Measure(text, cellFont) + padding);
            // Further rows cannot change the result after reaching the existing ceiling.
            if (width >= ceiling) return ceiling;
        }
        return Math.Min(ceiling, width);
    }

    private static string Format(object value) => value switch {
        DateTime date => date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
        TimeSpan time => time.ToString(),
        bool boolean => boolean ? "Yes" : "No",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
}
