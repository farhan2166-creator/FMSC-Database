using System.Data;

namespace FMSC_SMS.Pages.Reports.Students;

internal static class ClassSectionSummaryData
{
    internal static DataTable Create(DataTable students, DataTable balances, string school, string scope, DateTime generated)
    {
        var result = new DataTable("ClassSummaryData");
        foreach (string field in new[] { "SerialNumber", "ActiveStudents", "Boys", "Girls", "Other" })
            result.Columns.Add(field, typeof(int));
        result.Columns.Add("Outstanding", typeof(decimal));
        foreach (string field in new[] { "SchoolName", "Scope", "Generated", "CampusName", "ClassName", "SectionName" })
            result.Columns.Add(field);
        var groups = new Dictionary<(int Campus, int Class, int Section), Group>();
        Group For(DataRow row)
        {
            var key = (Convert.ToInt32(row["CampusId"]), Convert.ToInt32(row["ClassId"]), Convert.ToInt32(row["SectionId"]));
            if (!groups.TryGetValue(key, out var group))
            {
                group = new Group {
                    Campus = Convert.ToString(row["CampusName"]) ?? "",
                    Class = Convert.ToString(row["ClassName"]) ?? "",
                    Section = Convert.ToString(row["SectionName"]) ?? "",
                    ClassOrder = row.Table.Columns.Contains("ClassOrder") && !row.IsNull("ClassOrder")
                        ? Convert.ToInt32(row["ClassOrder"]) : int.MaxValue
                };
                groups.Add(key, group);
            }
            return group;
        }
        foreach (DataRow row in students.Rows)
        {
            if (row.IsNull("EnrollmentId") || Convert.ToString(row["EnrollmentStatus"]) != "Active") continue;
            var group = For(row);
            if (!group.Students.Add(Convert.ToInt32(row["AdmissionId"]))) continue;
            string gender = (Convert.ToString(row["Gender"]) ?? "").Trim().ToUpperInvariant();
            if (gender is "MALE" or "M") group.Boys++;
            else if (gender is "FEMALE" or "F") group.Girls++;
            else group.Other++;
        }
        foreach (DataRow row in balances.Rows)
        {
            decimal outstanding = Convert.ToDecimal(row["Outstanding"]);
            if (outstanding > 0) For(row).Outstanding += outstanding;
        }
        int serial = 0;
        foreach (var pair in groups.OrderBy(pair => pair.Value.Campus, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(pair => pair.Key.Campus).ThenBy(pair => pair.Value.ClassOrder)
            .ThenBy(pair => pair.Value.Class, StringComparer.CurrentCultureIgnoreCase).ThenBy(pair => pair.Key.Class)
            .ThenBy(pair => pair.Value.Section, StringComparer.CurrentCultureIgnoreCase).ThenBy(pair => pair.Key.Section))
        {
            var group = pair.Value;
            result.Rows.Add(++serial, group.Students.Count, group.Boys, group.Girls, group.Other, group.Outstanding,
                school, scope, generated.ToString("dd-MMM-yyyy HH:mm"), group.Campus, group.Class, group.Section);
        }
        return result;
    }

    private sealed class Group
    {
        internal string Campus = "", Class = "", Section = "";
        internal int ClassOrder, Boys, Girls, Other;
        internal decimal Outstanding;
        internal readonly HashSet<int> Students = new();
    }
}
