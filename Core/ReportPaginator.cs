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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        int total = source.Rows.Count;
        int pageCount = total == 0 ? 1 : (total - 1) / pageSize + 1;
        int index = Math.Clamp(requestedIndex, 0, pageCount - 1);
        int start = index * pageSize;
        int end = start + Math.Min(pageSize, total - start);
        DataTable rows = source.Clone();
        rows.BeginLoadData();
        try
        {
            // Index directly: Skip enumerates every preceding row on late pages.
            for (int i = start; i < end; i++) rows.ImportRow(source.Rows[i]);
        }
        finally { rows.EndLoadData(); }
        string range = total == 0
            ? "No records"
            : $"Showing {start + 1:N0}–{end:N0} of {total:N0}";
        string status = total == 0
            ? "0 record(s)"
            : $"{total:N0} record(s) • Page {index + 1:N0} of {pageCount:N0}";
        return new Page(rows, index, pageCount, total, range, status, index > 0, index < pageCount - 1);
    }
}
