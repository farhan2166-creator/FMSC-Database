using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FMSC_SMS.Core;

internal sealed record ReportPrintMetric(string Label, string Value);

// Shared presentation metadata. Never recalculate money from subtotal/detail rows.
internal static class ReportPrintSummary
{
    internal static string Caption(string label)=>label=="Money Received"?"Payment Received":label;
    private static readonly HashSet<string> KnownMetrics = new(StringComparer.OrdinalIgnoreCase) {
        "Receipts", "Money Received", "Payment Received", "Gross Received", "Refunds", "Corrections", "Credit Applied", "Adjusted Collection",
        "Net Collection", "Students Owing", "Total Outstanding", "Overdue", "90+ Days", "Gross Billed", "Discounts", "Net Billed",
        "Charges", "Students", "Rows shown", "Outstanding shown", "Students holding credit", "Credit held", "Assignments",
        "Overdue Amount", "Due Today", "Not Yet Due", "Due Date Missing", "Net Fees", "Settled", "Excess Settlement",
        "Logged issues", "Saved records only", "Awaiting collection"
    };

    internal static IReadOnlyList<ReportPrintMetric> FromTotals(DataTable? totals)
    {
        if (totals is null || totals.Rows.Count == 0) return Array.Empty<ReportPrintMetric>();
        return totals.Columns.Cast<DataColumn>().Select(column => {
            object value = totals.Rows[0][column];
            string text = value is DBNull ? "0" : value is decimal money
                ? "Rs. " + money.ToString("#,##0.##", CultureInfo.CurrentCulture)
                : Convert.ToString(value, CultureInfo.CurrentCulture) ?? "0";
            return new ReportPrintMetric(Caption(column.ColumnName), text);
        }).ToArray();
    }

    internal static (string Scope, IReadOnlyList<ReportPrintMetric> Metrics) Split(string subtitle,
        IReadOnlyList<ReportPrintMetric>? authoritative = null)
    {
        var labels = authoritative is null ? KnownMetrics : authoritative.Select(m => m.Label).Concat(new[]{"Money Received"}).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var metrics = new List<ReportPrintMetric>();
        var scope = new List<string>();
        foreach (string part in Regex.Split(subtitle.Trim(), @"\s{3,}"))
        {
            int colon = part.IndexOf(':');
            string key = colon >= 0 ? part[..colon].Trim() : "";
            if (labels.Contains(key))
            {
                if (authoritative is null) metrics.Add(new(Caption(key), part[(colon + 1)..].Trim()));
            }
            else if (part.Length > 0) scope.Add(part.Replace("Period: Any start to Any end", "Period: All dates", StringComparison.OrdinalIgnoreCase));
        }
        return (string.Join("   |   ", scope), authoritative ?? metrics);
    }

    internal static bool IsMoneyColumn(DataColumn column) => column.DataType==typeof(decimal)
        && Regex.IsMatch(column.ColumnName,@"amount|balance|billed|charges|paid|payable|credit|outstanding|discount|fees|settled|refund|collection|received|\(rs",RegexOptions.IgnoreCase);

    internal static bool IsTotalRow(DataRow row)
    {
        // Total markers are textual; a hidden/reordered numeric first column must
        // not make the authoritative total look like an ordinary transaction.
        return row.Table.Columns.Cast<DataColumn>().Where(c => c.DataType == typeof(string))
            .Select(c => Convert.ToString(row[c])?.Trim() ?? "")
            .Any(text => text.Equals("TOTAL", StringComparison.OrdinalIgnoreCase)
                || text.Equals("NET TOTAL", StringComparison.OrdinalIgnoreCase));
    }

    internal static int DetailCount(DataTable source) => CollectionReceiptGrouping.Layout(source) is { } layout
        ? layout.Count(row => row.Kind == "Detail")
        : source.AsEnumerable().Count(row => !IsTotalRow(row));
}
