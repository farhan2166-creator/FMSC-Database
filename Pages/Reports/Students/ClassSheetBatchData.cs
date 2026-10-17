using System.Data;

namespace FMSC_SMS.Pages.Reports.Students;

internal static class ClassSheetBatchData
{
    // Placement IDs distinguish groups even when their displayed names match.
    internal static DataTable Create(DataTable students, Func<DataTable, DataTable> createSheet)
    {
        string Key(DataRow row, string id, string name) =>
            Convert.ToString(row[students.Columns.Contains(id) ? id : name]) ?? "";
        var groups = students.AsEnumerable()
            .Where(row => !row.IsNull("EnrollmentId") && Convert.ToString(row["EnrollmentStatus"]) == "Active"
                && (!students.Columns.Contains("Include") || row.Field<bool>("Include")))
            .GroupBy(row => (Key(row, "CampusId", "CampusName"), Key(row, "AcademicYearId", "YearName"),
                Key(row, "ClassId", "ClassName"), Key(row, "SectionId", "SectionName")))
            .OrderBy(group => Convert.ToString(group.First()["CampusName"]), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(group => students.Columns.Contains("ClassOrder") && !group.First().IsNull("ClassOrder")
                ? Convert.ToInt32(group.First()["ClassOrder"]) : int.MaxValue)
            .ThenBy(group => Convert.ToString(group.First()["ClassName"]), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(group => Convert.ToString(group.First()["SectionName"]), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(group => group.Key);
        DataTable? result = null;
        int pageKey = 0;
        foreach (var group in groups)
        {
            var roster = students.Clone();
            foreach (var row in group) roster.ImportRow(row);
            var sheet = createSheet(roster);
            result ??= sheet.Clone();
            var keys = new Dictionary<int, int>();
            foreach (DataRow row in sheet.Rows)
            {
                int originalKey = Convert.ToInt32(row["PageKey"]);
                if (!keys.TryGetValue(originalKey, out int key)) keys[originalKey] = key = ++pageKey;
                result.ImportRow(row);
                result.Rows[result.Rows.Count - 1]["PageKey"] = key;
            }
        }
        return result ?? createSheet(students.Clone());
    }
}
