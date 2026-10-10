namespace FMSC_SMS.Core;

/// <summary>Builds a detached report page and its display metadata without touching UI controls.</summary>
internal static class ReportPaginator
{
    internal sealed record Page(
        DataTable Rows,
        int Index,
        int PageCount,
        int TotalRows,
        string RangeText,
        string StatusText,
        bool HasPrevious,
        bool HasNext);

    internal static Page Build(DataTable source, int requestedIndex, int pageSize)
    {
        int total = source.Rows.Count;
        int pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        int index = Math.Clamp(requestedIndex, 0, pageCount - 1);
        DataTable rows = source.Clone();
        foreach (DataRow row in source.Rows.Cast<DataRow>().Skip(index * pageSize).Take(pageSize))
            rows.ImportRow(row);
        string range = total == 0
            ? "No records"
            : $"Showing {index * pageSize + 1:N0}–{Math.Min((index + 1) * pageSize, total):N0} of {total:N0}";
        string status = total == 0
            ? "0 record(s)"
            : $"{total:N0} record(s) • Page {index + 1:N0} of {pageCount:N0}";
        return new Page(rows, index, pageCount, total, range, status, index > 0, index < pageCount - 1);
    }
}
