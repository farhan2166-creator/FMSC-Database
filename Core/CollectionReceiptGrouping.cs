namespace FMSC_SMS.Core;

// Presentation only: keep each returned receipt intact, including mixed-location families.
internal static class CollectionReceiptGrouping
{
    internal const string LayoutKey = "Fmsc.CollectionReceiptLayout";
    internal sealed record RowLayout(string Kind, string GroupKey, string Label);
    internal static RowLayout[]? Layout(DataTable data) =>
        data.ExtendedProperties[LayoutKey] is RowLayout[] rows && rows.Length == data.Rows.Count ? rows : null;

    internal static DataTable DetailsOnly(DataTable source)
    {
        var result = source.Clone();
        result.ExtendedProperties.Remove(LayoutKey);
        var layout = Layout(source);
        for (int i = 0; i < source.Rows.Count; i++)
            if (layout is null || layout[i].Kind == "Detail") result.ImportRow(source.Rows[i]);
        return result;
    }

    internal static ReportPaginator.Page Page(DataTable source, int requestedIndex, int pageSize)
    {
        var layout = Layout(source);
        if (layout is null) return ReportPaginator.Build(source, requestedIndex, pageSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        int[] details = Enumerable.Range(0, layout.Length).Where(i => layout[i].Kind == "Detail").ToArray();
        int pages = Math.Max(1, (details.Length + pageSize - 1) / pageSize);
        int index = Math.Clamp(requestedIndex, 0, pages - 1), start = index * pageSize;
        int end = Math.Min(details.Length, start + pageSize);
        var rows = source.Clone();
        var pageLayout = new List<RowLayout>();
        void Add(int i) { rows.ImportRow(source.Rows[i]); pageLayout.Add(layout[i]); }
        string previous = "";
        for (int n = start; n < end; n++)
        {
            int i = details[n];
            if (layout[i].GroupKey != previous)
            {
                int heading = Array.FindIndex(layout, r => r.Kind == "Heading" && r.GroupKey == layout[i].GroupKey);
                if (heading >= 0) Add(heading);
                previous = layout[i].GroupKey;
            }
            Add(i);
            if (n + 1 == details.Length || layout[details[n + 1]].GroupKey != layout[i].GroupKey)
            {
                int subtotal = Array.FindIndex(layout, r => r.Kind == "Subtotal" && r.GroupKey == layout[i].GroupKey);
                if (subtotal >= 0) Add(subtotal);
            }
        }
        if (index == pages - 1)
            for (int i = 0; i < layout.Length; i++) if (layout[i].Kind == "GrandTotal") Add(i);
        rows.ExtendedProperties[LayoutKey] = pageLayout.ToArray();
        string range = details.Length == 0 ? "No receipts" : $"Receipts {start + 1:N0}–{end:N0} of {details.Length:N0}";
        return new(rows, index, pages, details.Length, range,
            $"{details.Length:N0} receipt entries • Page {index + 1:N0} of {pages:N0}", index > 0, index < pages - 1);
    }

    private static readonly string[] Amounts = {
        "Money Received", "Refunds", "Corrections", "Credit Applied", "Adjusted Collection"
    };

    internal static void Apply(DataTable data, string grouping)
    {
        if (data.Rows.Count == 0) return;
        if (grouping is not ("Whole School" or "Campus" or "Class" or "Section"))
            throw new ArgumentException("Unsupported receipt grouping.", nameof(grouping));
        var records = data.Copy();
        var layout = new List<RowLayout>();
        data.Rows.Clear();
        string Text(DataRow row, string column) => Convert.ToString(row[column])?.Trim() ?? "";
        (string Campus, string Class, string Section) Key(DataRow row)
        {
            string campus = Text(row, "Campus");
            if (campus.Length == 0) campus = "Unassigned campus";
            if (grouping == "Campus") return (campus, "", "");
            string placement = Text(row, "Class / Section");
            int separator = placement.LastIndexOf(" – ", StringComparison.Ordinal);
            string cls = separator < 0 ? placement : placement[..separator];
            string section = separator < 0 ? "" : placement[(separator + 3)..];
            if (cls.Length == 0) cls = "Unassigned class";
            if (cls == "Multiple") cls = "Multiple classes / sections";
            return (campus, cls, grouping == "Section" ? section : "");
        }
        void Total(string label, IEnumerable<DataRow> rows, string kind, string key, string groupLabel)
        {
            DataRow total = data.NewRow();
            total["Receipt / Reference"] = label;
            foreach (string amount in Amounts)
                total[amount] = rows.Sum(row => row.IsNull(amount) ? 0m : Convert.ToDecimal(row[amount]));
            data.Rows.Add(total);
            layout.Add(new(kind, key, groupLabel));
        }
        if (grouping == "Whole School")
        {
            foreach (DataRow row in records.Rows)
            {
                data.ImportRow(row);
                layout.Add(new("Detail", "all", ""));
            }
        }
        else
        {
            int groupIndex = 0;
            foreach (var group in records.AsEnumerable().GroupBy(Key))
            {
                var key = group.Key;
                string groupKey = "Group" + groupIndex++;
                string label = string.Join(" / ", new[] { key.Campus, key.Class, key.Section }.Where(s => s.Length > 0));
                DataRow heading = data.NewRow();
                heading["Receipt / Reference"] = grouping + ": " + label;
                data.Rows.Add(heading);
                layout.Add(new("Heading", groupKey, grouping + ": " + label));
                foreach (DataRow row in group)
                {
                    data.ImportRow(row);
                    layout.Add(new("Detail", groupKey, grouping + ": " + label));
                }
                Total("TOTAL — " + label, group, "Subtotal", groupKey, grouping + ": " + label);
            }
        }
        // Sum original records only, never the inserted headings or subtotals.
        Total("TOTAL — Selected Receipts", records.AsEnumerable(), "GrandTotal", "grand", "");
        data.ExtendedProperties[LayoutKey] = layout.ToArray();
    }
}
