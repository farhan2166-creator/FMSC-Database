namespace FMSC_SMS.Core;

// Presentation only: keep each returned receipt intact, including mixed-location families.
internal static class CollectionReceiptGrouping
{
    private static readonly string[] Amounts = {
        "Money Received", "Refunds", "Corrections", "Credit Applied", "Adjusted Collection"
    };

    internal static void Apply(DataTable data, string grouping)
    {
        if (data.Rows.Count == 0) return;
        if (grouping is not ("Whole School" or "Campus" or "Class" or "Section"))
            throw new ArgumentException("Unsupported receipt grouping.", nameof(grouping));
        var records = data.Copy();
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
        void Total(string label, IEnumerable<DataRow> rows)
        {
            DataRow total = data.NewRow();
            total["Receipt / Reference"] = label;
            foreach (string amount in Amounts)
                total[amount] = rows.Sum(row => row.IsNull(amount) ? 0m : Convert.ToDecimal(row[amount]));
            data.Rows.Add(total);
        }
        if (grouping == "Whole School")
        {
            foreach (DataRow row in records.Rows) data.ImportRow(row);
        }
        else
        {
            foreach (var group in records.AsEnumerable().GroupBy(Key))
            {
                var key = group.Key;
                string label = string.Join(" / ", new[] { key.Campus, key.Class, key.Section }.Where(s => s.Length > 0));
                DataRow heading = data.NewRow();
                heading["Receipt / Reference"] = grouping + ": " + label;
                data.Rows.Add(heading);
                foreach (DataRow row in group) data.ImportRow(row);
                Total("TOTAL — " + label, group);
            }
        }
        // Sum original records only, never the inserted headings or subtotals.
        Total("TOTAL — Selected Receipts", records.AsEnumerable());
    }
}
