namespace FMSC_SMS.Core;

internal static class CertificateRecordOrder
{
    internal static void FitColumns(DataGridView grid)
    {
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        UiHelper.FitColumnsToWidth(grid);
    }

    internal static DataTable NewestFirst(DataTable table, string key)
    {
        if (!table.Columns.Contains(key)) return table.Copy();
        string date = table.Columns.Contains("IssuedAt") ? "IssuedAt"
            : table.Columns.Contains("CertificateDate") ? "CertificateDate" : "";
        var view = new DataView(table) { Sort = date.Length == 0 ? $"[{key}] DESC" : $"[{date}] DESC, [{key}] DESC" };
        return view.ToTable();
    }

    internal static void ArrangeColumns(DataGridView grid, string key)
    {
        int index = 0;
        foreach (string name in new[] { "IssuedAt", "CertificateDate", key, "AdmissionNumber", "StaffName",
            "StudentName", "FatherName", "ClassName", "CollectionStatus" })
            if (grid.Columns.Contains(name)) grid.Columns[name].DisplayIndex = index++;
    }

    internal static void Select(DataGridView grid, string key, int id, Action<int> loadRow)
    {
        var rows = grid.Rows.Cast<DataGridViewRow>().Where(row => row.DataBoundItem is DataRowView).ToList();
        if (id <= 0 && rows.Count > 0)
            id = rows.Max(row => Convert.ToInt32(((DataRowView)row.DataBoundItem)[key]));
        var selected = rows.FirstOrDefault(row => Convert.ToInt32(((DataRowView)row.DataBoundItem)[key]) == id);
        if (selected is null) return;
        grid.ClearSelection();
        grid.CurrentCell = selected.Cells.Cast<DataGridViewCell>().First(cell => cell.Visible);
        selected.Selected = true;
        loadRow(selected.Index);
    }
}
