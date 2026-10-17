namespace FMSC_SMS.Core;

internal static class StudentPickerColumnPlanner
{
    internal static List<StudentPicker.ColumnSpec> Apply(
        List<StudentPicker.Entry> entries,
        Font cellFont)
    {
        var candidates = new (int Index, StudentPicker.ColumnSpec Spec, bool Always)[]
        {
            (0, new("Admission Number", 90, rightAligned: true), true),
            // User requirement: registration is intentionally omitted from visible search results.
            // Internal matching data must not be turned into a visible column.
            (1, new("Student", 240, flex: true), true),
            (2, new("Father", 220, flex: true), false),
            (3, new("Class / Section", 170), false),
            (4, new("Campus", 150), false),
            (5, new("Status", 90), false)
        };
        var columns = new List<StudentPicker.ColumnSpec>();
        var indexes = new List<int>();
        var measurements = new TextWidthMeasurementSession();
        foreach ((int index, StudentPicker.ColumnSpec spec, bool always) in candidates)
        {
            Font font = index == 0 ? AppTheme.ButtonFont : cellFont;
            int limit = AppScale.Scale(spec.MaxWidth);
            int widest = measurements.Measure(spec.Header, AppTheme.FontSmallBold);
            bool hasValues = false;
            foreach (StudentPicker.Entry entry in entries)
            {
                if (entry.Cells.Length <= index || entry.Cells[index].Length == 0)
                    continue;
                hasValues = true;
                if (widest < limit)
                    widest = Math.Max(widest, measurements.Measure(entry.Cells[index], font));
                // Width is final and the optional column is known to have content.
                if (widest >= limit)
                    break;
            }
            if (!hasValues && !always)
                continue;
            spec.Width = Math.Min(limit, widest) + 4;
            columns.Add(spec);
            indexes.Add(index);
        }
        for (int row = 0; row < entries.Count; row++)
        {
            StudentPicker.Entry old = entries[row];
            string[] cells = indexes.Select(index =>
                index < old.Cells.Length ? old.Cells[index] : string.Empty).ToArray();
            entries[row] = new StudentPicker.Entry(old.Id, old.Display, old.AdmissionId, cells);
        }
        return columns;
    }
}
