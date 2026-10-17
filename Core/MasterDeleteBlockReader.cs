using System.Globalization;

namespace FMSC_SMS.Core;

internal static class MasterDeleteBlockReader
{
    internal static HashSet<int>? Query(string? procedure, bool configured)
    {
        if (!configured || string.IsNullOrWhiteSpace(procedure))
            return null;
        try
        {
            DataTable table = DatabaseHelper.QueryUser(procedure);
            return table.Rows.Cast<DataRow>()
                .Select(row => row[0])
                .Where(value => value != DBNull.Value)
                .Select(value => int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : 0)
                .Where(id => id > 0)
                .ToHashSet();
        }
        catch (Exception nonFatalException7)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException7, "MasterDeleteBlockReader: Query fallback");
            return null;
        }
    }
}
