using System.Globalization;

namespace FMSC_SMS.Core;

/// <summary>Pure filtering and summary logic for MasterPage record lists.</summary>
internal static class MasterRecordFilter
{
    internal sealed record Result(DataTable Table, string CountText, string StatusText, string EmptyText);

    internal static Result Apply(DataTable source, string term)
    {
        DataTable result = term.Length > 0 ? Filter(source, term) : source;
        string emptyText = source.Rows.Count == 0
            ? "No records have been created yet."
            : "No records match the current filters.";
        string countText = result.Rows.Count == 0
            ? (source.Rows.Count == 0 ? "No records" : "No matches")
            : result.Rows.Count == source.Rows.Count
                ? $"{result.Rows.Count:N0} record(s)"
                : $"{result.Rows.Count:N0} of {source.Rows.Count:N0}";
        string statusText = result.Rows.Count == 0 && source.Rows.Count > 0
            ? "No records match the current filters."
            : $"{result.Rows.Count:N0} record(s)";
        return new Result(result, countText, statusText, emptyText);
    }

    private static DataTable Filter(DataTable source, string term)
    {
        DataTable result = source.Clone();
        DataColumn[] searchableColumns = source.Columns.Cast<DataColumn>()
            .Where(column => !string.Equals(column.ColumnName, "Id", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (DataRow row in source.Rows)
        {
            bool textMatches = term.Length == 0 || searchableColumns.Any(column => {
                object value = row[column];
                return value != DBNull.Value &&
                    (Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)
                    .Contains(term, StringComparison.OrdinalIgnoreCase);
            });
            if (textMatches)
                result.ImportRow(row);
        }
        return result;
    }
}
