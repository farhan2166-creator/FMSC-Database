namespace FMSC_SMS.Core;

internal static class ReportAvailableColumns
{
    // The shared presentation's raw/technical fields stay excluded from the chooser and exports.
    // Preserve existing value formatting; this catalogue controls selection and ordering only.
    internal static IReadOnlyList<ReportColumnDef> FromGrid(DataGridView grid) => grid.Columns.Cast<DataGridViewColumn>()
        .Where(c => c is DataGridViewTextBoxColumn && !UiHelper.IsRawDatabaseColumn(c.Name))
        .Select(c => new ReportColumnDef(c.Name,c.HeaderText)).ToArray();
}
