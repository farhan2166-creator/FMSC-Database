namespace FMSC_SMS.Core;

internal static class MasterGridActionColumns
{
    internal static void EnsureOrderButtons(DataGridView grid, Color accent)
    {
        Ensure(grid, "__OrderUp", "↑", 0);
        Ensure(grid, "__OrderDown", "↓", 1);
        if (grid.Columns.Contains("DisplayOrder"))
            grid.Columns["DisplayOrder"].Visible = false;
        foreach (string name in new[] { "__OrderUp", "__OrderDown" })
        {
            var column = (DataGridViewButtonColumn)grid.Columns[name];
            // Shared grid styling hides internal columns again on every bind.
            column.Visible = true;
            column.DisplayIndex = name == "__OrderUp" ? 0 : 1;
            column.Width = column.MinimumWidth = AppScale.Scale(34);
            column.FillWeight = 34;
            column.Resizable = DataGridViewTriState.False;
            column.Frozen = true;
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            column.DefaultCellStyle.Padding = Padding.Empty;
            column.DefaultCellStyle.Font = AppTheme.SymbolFont(11F, FontStyle.Bold);
            column.DefaultCellStyle.ForeColor = accent;
            column.DefaultCellStyle.BackColor = AppTheme.Surface;
            column.DefaultCellStyle.SelectionForeColor = accent;
            column.DefaultCellStyle.SelectionBackColor = AppTheme.PrimaryLight;
        }
    }

    private static void Ensure(DataGridView grid, string name, string glyph, int index)
    {
        if (grid.Columns.Contains(name))
            return;
        grid.Columns.Insert(index, new DataGridViewButtonColumn {
            Name = name,
            HeaderText = glyph,
            Text = glyph,
            UseColumnTextForButtonValue = true,
            Width = AppScale.Scale(34),
            MinimumWidth = AppScale.Scale(34),
            FillWeight = 34,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            FlatStyle = FlatStyle.Flat,
            Resizable = DataGridViewTriState.False,
            Frozen = true
        });
    }
}
