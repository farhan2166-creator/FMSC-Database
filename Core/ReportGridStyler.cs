namespace FMSC_SMS.Core;

internal static class ReportGridStyler
{
    internal static void Apply(DataGridView grid, Color accent)
    {
        if (grid.IsDisposed)
            return;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        HumanUi.ApplyGridStyle(grid, accent);
        UiHelper.FitColumnsToWidth(grid);
    }
}
