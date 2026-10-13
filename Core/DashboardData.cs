namespace FMSC_SMS.Core;

/// <summary>The dashboard's display contract, independent of its controls.</summary>
internal static class DashboardData
{
    internal sealed record Counts(int TotalStudents, int ActiveStudents, int TotalClasses,
        int ActiveStaff, int NewAdmissions);

    internal static Counts ReadCounts(DataRow row)
    {
        int Count(string column)
        {
            if (!row.Table.Columns.Contains(column) || row[column] is not int count || count < 0)
                throw new InvalidDataException($"Dashboard count '{column}' is missing or invalid.");
            return count;
        }
        return new Counts(Count("TotalStudents"), Count("ActiveStudents"), Count("TotalClasses"),
            Count("ActiveStaff"), Count("NewAdmissionsThisMonth"));
    }

    internal static string AcademicYearLabel(DataTable years)
    {
        if (!years.Columns.Contains("IsCurrent") || !years.Columns.Contains("YearName"))
            throw new InvalidDataException("Academic-year data is missing its current-year columns.");
        var current = years.AsEnumerable().Where(row => row["IsCurrent"] != DBNull.Value &&
            Convert.ToBoolean(row["IsCurrent"])).Take(2).ToArray();
        if (current.Length > 1) return "Academic Year — Review Setup";
        string name = current.Length == 0 ? string.Empty : Convert.ToString(current[0]["YearName"])?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(name) ? "Academic Year — Not Set" : $"Academic Year — {name}";
    }
}
