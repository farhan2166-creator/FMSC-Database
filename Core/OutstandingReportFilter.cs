namespace FMSC_SMS.Core;

internal sealed record OutstandingReportFilter(bool OverdueOnly, decimal MinimumBalance, int MinimumDays, int? MaximumDays)
{
    internal string DueStatus { get; init; } = "None";
    internal bool Active => DueStatus != "None" || OverdueOnly || MinimumBalance > 0 || MinimumDays > 0 || MaximumDays is not null;
    internal DataTable Apply(DataTable source)
    {
        var result = source.Clone();
        foreach (DataRow row in source.Rows)
        {
            if (row["Outstanding"] is DBNull || Convert.ToDecimal(row["Outstanding"]) < MinimumBalance) continue;
            string? category=DueStatus switch {
                "Overdue"=>"Overdue Amount","Due Today"=>"Due Today","Not Yet Due"=>"Not Yet Due","No Due Date"=>"Due Date Missing",_=>null
            };
            if(category is not null && (!source.Columns.Contains(category)||row.IsNull(category)||Convert.ToDecimal(row[category])<=0m))continue;
            if (row["Days Overdue"] is DBNull)
            {
                if (OverdueOnly || MinimumDays > 0 || MaximumDays is not null) continue;
            }
            else
            {
                int days = Convert.ToInt32(row["Days Overdue"]);
                if ((OverdueOnly && days <= 0) || days < MinimumDays || (MaximumDays is int max && days > max)) continue;
            }
            result.ImportRow(row);
        }
        return result;
    }
    internal static DataTable Totals(DataTable rows)
    {
        var totals = new DataTable();
        totals.Columns.Add("Rows shown", typeof(int)); totals.Columns.Add("Outstanding shown", typeof(decimal));
        var total = totals.NewRow();
        total["Rows shown"] = rows.Rows.Count;
        total["Outstanding shown"] = rows.AsEnumerable().Sum(r => r.IsNull("Outstanding") ? 0m : Convert.ToDecimal(r["Outstanding"]));
        foreach (string field in new[] { "Overdue Amount", "Due Today", "Not Yet Due", "Due Date Missing" })
            if (rows.Columns.Contains(field))
            {
                totals.Columns.Add(field, typeof(decimal));
                total[field] = rows.AsEnumerable().Sum(r => r.IsNull(field) ? 0m : Convert.ToDecimal(r[field]));
            }
        totals.Rows.Add(total);
        return totals;
    }
}
