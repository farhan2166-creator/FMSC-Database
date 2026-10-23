namespace FMSC_SMS.Core;

internal sealed record OutstandingReportFilter(bool OverdueOnly, decimal MinimumBalance, int MinimumDays, int? MaximumDays)
{
    internal bool Active => OverdueOnly || MinimumBalance > 0 || MinimumDays > 0 || MaximumDays is not null;
    internal DataTable Apply(DataTable source)
    {
        var result = source.Clone();
        foreach (DataRow row in source.Rows)
        {
            if (row["Outstanding"] is DBNull || Convert.ToDecimal(row["Outstanding"]) < MinimumBalance) continue;
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
        totals.Rows.Add(rows.Rows.Count, rows.AsEnumerable().Sum(r => r.IsNull("Outstanding") ? 0m : Convert.ToDecimal(r["Outstanding"])));
        return totals;
    }
}
