using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Finance.StudentAccounts;

internal static class FeeAssignmentPresentation
{
    internal static DateTime PostingMonth(DataTable assignments)
    {
        DateTime month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        foreach (DataRow row in assignments.Rows)
            if (row["LastBilledMonth"] != DBNull.Value && Convert.ToDateTime(row["LastBilledMonth"]) > month)
                month = Convert.ToDateTime(row["LastBilledMonth"]);
        return new DateTime(month.Year, month.Month, 1);
    }
    internal static IEnumerable<DataRow> Latest(DataTable assignments) => Rates(assignments)
        .GroupBy(row => Convert.ToInt32(row["FeeTypeId"]))
        .Select(group => group.OrderByDescending(Start).ThenByDescending(row => Convert.ToInt32(row["StudentFeeAssignmentId"])).First())
        .Where(row => row["EffectiveTo"] == DBNull.Value || Convert.ToDateTime(row["EffectiveTo"]) >= PostingMonth(assignments));
    private static IEnumerable<DataRow> Rates(DataTable table) => table.AsEnumerable().Where(row =>
        !string.Equals(Convert.ToString(row["Frequency"]), "As Needed", StringComparison.OrdinalIgnoreCase) &&
        (!table.Columns.Contains("CancelledAt") || row["CancelledAt"] == DBNull.Value));
    private static DateTime Start(DataRow row) => row["EffectiveFrom"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["EffectiveFrom"]);
    internal static string CompactSummary(DataTable assignments, DateTime month)
    {
        return "Net Rs. — " + string.Join("  |  ", Rates(assignments).GroupBy(row => Convert.ToInt32(row["FeeTypeId"])).Select(group => {
            var periods = group.OrderBy(Start).ToArray();
            var applicable = periods.LastOrDefault(row => Start(row) <= month &&
                (row["EffectiveTo"] == DBNull.Value || Convert.ToDateTime(row["EffectiveTo"]) >= month));
            var future = periods.Where(row => Start(row) > month).ToArray();
            var anchor = applicable ?? future.FirstOrDefault() ?? periods.Last();
            decimal net = Convert.ToDecimal(anchor["NetAmount"]);
            var previous = periods.LastOrDefault(row => Start(row) < Start(anchor) && Convert.ToDecimal(row["NetAmount"]) != net);
            var notes = new List<string>();
            if (previous != null) notes.Add($"was {Convert.ToDecimal(previous["NetAmount"]):N0}");
            foreach (var row in applicable == null ? future : new[] { applicable }.Concat(future)) {
                decimal value = Convert.ToDecimal(row["NetAmount"]);
                string amount = value == 0 ? "No fee" : value.ToString("N0");
                if (row["EffectiveTo"] != DBNull.Value) {
                    string to = Convert.ToDateTime(row["EffectiveTo"]).ToString("MMM yyyy");
                    notes.Add(row == applicable ? $"{amount} through {to}" : $"{amount} {Start(row):MMM}–{to}");
                } else notes.Add(row == applicable ? $"{amount} continues" : $"{amount} from {Start(row):MMM yyyy}");
            }
            if (notes.Count == 0 || (applicable == null && future.Length == 0)) notes.Add("stopped");
            string name = Convert.ToString(anchor["FeeTypeName"])?.Replace(" Fee", "") ?? "Fee";
            return name + ": " + string.Join("; ", notes);
        }));
    }
    internal static string Summary(DataTable assignments, DateTime month)
    {
        return string.Join(Environment.NewLine, Rates(assignments).GroupBy(row => Convert.ToInt32(row["FeeTypeId"])).Select(group => {
            var periods = group.OrderBy(Start).ThenBy(row => Convert.ToInt32(row["StudentFeeAssignmentId"])).ToArray();
            return Convert.ToString(periods[0]["FeeTypeName"]) + ": " + string.Join(" → ", periods.Select(row => {
                DateTime from = Start(row);
                DateTime? to = row["EffectiveTo"] == DBNull.Value ? null : Convert.ToDateTime(row["EffectiveTo"]);
                string state = to < month ? "Previously" : from > month ? "Future" : "Still applies";
                decimal net = Convert.ToDecimal(row["NetAmount"]);
                string value = net == 0 ? "No fee" : $"Rs. {net:N0}";
                string range = (from == DateTime.MinValue ? "from start" : "from " + from.ToString("MMM yyyy")) +
                    (to.HasValue ? " through " + to.Value.ToString("MMM yyyy") : " onward");
                return $"{state}: {value} {range}";
            }));
        }));
    }
}
