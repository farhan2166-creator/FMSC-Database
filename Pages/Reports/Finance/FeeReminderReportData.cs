using System.Data;
using System.Globalization;
using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Reports.Finance;

internal static class FeeReminderReportData
{
    internal static void ApplySearch(DataTable roster, string search)
    {
        const string matchColumn = "SearchMatch";
        if (!roster.Columns.Contains(matchColumn)) roster.Columns.Add(matchColumn, typeof(bool));
        string term = search.Trim();
        foreach (DataRow row in roster.Rows)
            row[matchColumn] = term.Length == 0 || new[] { "StudentName", "FatherName", "AdmissionNumber" }
                .Any(column => (Convert.ToString(row[column]) ?? "").Contains(term, StringComparison.CurrentCultureIgnoreCase));
        // Search text is compared as literal text, never inserted into a filter expression.
        roster.DefaultView.RowFilter = term.Length == 0 ? "" : matchColumn + " = true";
    }

    internal static DataTable CreateRoster(DataTable balances)
    {
        var roster = new DataTable();
        roster.Columns.Add("Include", typeof(bool));
        roster.Columns.Add("AdmissionId", typeof(int));
        foreach (string name in new[] { "AdmissionNumber", "StudentName", "FatherName" }) roster.Columns.Add(name);
        roster.Columns.Add("Outstanding", typeof(decimal));
        var students = balances.AsEnumerable().Where(row => Convert.ToDecimal(row["Outstanding"]) > 0)
            .GroupBy(row => Convert.ToInt32(row["AdmissionId"]))
            .OrderBy(group => Convert.ToString(group.First()["StudentName"]), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(group => Convert.ToString(group.First()["FatherName"]), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(group => group.Key);
        foreach (var student in students)
        {
            var first = student.First();
            roster.Rows.Add(true, student.Key, first["AdmissionNumber"], first["StudentName"], first["FatherName"],
                student.Sum(row => Convert.ToDecimal(row["Outstanding"])));
        }
        return roster;
    }

    internal static DataTable Create(DataTable balances, IReadOnlySet<int> selected, string school,
        string contact, string address, string scope, DateTime prepared, DateTime? requestedDate, string message)
    {
        var data = ReceiptReportData.Create();
        int key = 0;
        foreach (DataRow student in CreateRoster(balances).Rows)
        {
            if (!selected.Contains(student.Field<int>("AdmissionId"))) continue;
            data.Rows.Add(++key, school, "FEE REMINDER", "", "", student["StudentName"], student["AdmissionNumber"],
                scope, prepared.ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture),
                requestedDate.HasValue ? "Requested payment date: " + requestedDate.Value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) : "", "", 0m, 0m,
                student["Outstanding"], 0m, "", message.Trim(), false, false, contact, address, "", student["FatherName"], "");
        }
        return data;
    }
}
