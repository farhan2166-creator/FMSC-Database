namespace FMSC_SMS.Core;
/// <summary>Staff finder using the shared picker interaction and popup.</summary>
public sealed class StaffPicker : StudentPicker
{
    public new const string SearchPlaceholder = "Staff no., staff or father name";
    public StaffPicker()
    {
        IdColumn = "StaffId";
        PlaceholderText = SearchPlaceholder;
        NoMatchText = "No matching staff";
        ShowDropDownButton = true;
        DisplayFormatter = row => string.Join(" - ", new[] { Read(row, "StaffNumber"), Read(row, "StaffName") }.Where(value => value.Length > 0));
    }
    public void SetStaff(int id, string display) => SetStudent(id, display);
    public void SetStaff(DataRow row) => SetStudent(row);
    internal override string EntityPlural => "staff members";
    // Staff selection remains independent of the assignment feature retired
    // on 2026-10-03; use the preserved staff master for officials/certificates.
    internal override Task<DataTable> LoadSourceAsync() => DatabaseHelper.QueryUserAsync("GetStaff");
    internal override string[] CellsFor(DataRow row) => new[] { Read(row, "StaffNumber"), Read(row, "StaffName"), Read(row, "FatherName"), Read(row, "Status") };
    internal override StudentPickerMatchBuilder.Result FindMatches(DataTable all, string term)
    {
        string[] words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = all.AsEnumerable().Where(row =>
            (RowFilter is null || RowFilter(row)) &&
            words.All(word => new[] { "StaffNumber", "StaffName", "FatherName", "CNIC", "ContactNumber" }
                .Any(column => Read(row, column).Contains(word, StringComparison.OrdinalIgnoreCase))))
            .GroupBy(row => Read(row, IdColumn)).Select(group => group.First()).Take(151).ToList();
        var entries = matches.Take(150).Where(row => int.TryParse(Read(row, IdColumn), out int id) && id > 0)
            .Select(row => new Entry(int.Parse(Read(row, IdColumn)), DisplayFormatter!(row), cells: CellsFor(row))).ToList();
        var exact = entries.Select((entry, index) => (entry, index)).Where(item => string.Equals(item.entry.Cells[0], term, StringComparison.OrdinalIgnoreCase)).ToList();
        int highlight = exact.Count == 1 ? exact[0].index : -1;
        if (highlight < 0 && entries.Count == 1) highlight = 0;
        if (term.Length == 0) highlight = entries.FindIndex(entry => entry.Id == SelectedId);
        return new(entries, highlight, matches.Count > 150);
    }
    internal override List<ColumnSpec> PlanColumns(List<Entry> entries, Font font)
    {
        var columns = new List<ColumnSpec> { new("Staff Number", 110), new("Staff", 240, flex: true), new("Father", 220, flex: true), new("Status", 100) };
        for (int index = 0; index < columns.Count; index++)
        {
            ColumnSpec column = columns[index];
            int widest = TextRenderer.MeasureText(column.Header, AppTheme.FontSmallBold).Width;
            foreach (Entry entry in entries)
                widest = Math.Max(widest, TextRenderer.MeasureText(entry.Cells[index], font).Width);
            column.Width = Math.Min(AppScale.Scale(column.MaxWidth), widest) + 4;
        }
        return columns;
    }
    private static string Read(DataRow row, string column) => row.Table.Columns.Contains(column) && row[column] != DBNull.Value
        ? Convert.ToString(row[column]) ?? string.Empty : string.Empty;
}
