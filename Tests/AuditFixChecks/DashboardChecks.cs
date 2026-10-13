using FMSC_SMS.Core;

internal static class DashboardChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }
        void Reject(Action action)
        {
            try { action(); throw new Exception("Malformed dashboard data was accepted"); }
            catch (InvalidDataException) { checks++; }
        }
        using var counts = new DataTable();
        foreach (string column in new[] { "TotalStudents", "ActiveStudents", "TotalClasses", "ActiveStaff", "NewAdmissionsThisMonth" })
            counts.Columns.Add(column, typeof(int));
        counts.Rows.Add(0, 0, 0, 0, 0);
        Check(DashboardData.ReadCounts(counts.Rows[0]) == new DashboardData.Counts(0, 0, 0, 0, 0), "All valid zero counts remain zero");
        counts.Rows.Add(100, 85, 12, 8, 3);
        Check(DashboardData.ReadCounts(counts.Rows[1]) == new DashboardData.Counts(100, 85, 12, 8, 3), "School totals and monthly admissions retain their own columns");
        foreach (DataColumn column in counts.Columns)
        {
            using var missing = counts.Copy(); missing.Columns.Remove(column.ColumnName);
            Reject(() => DashboardData.ReadCounts(missing.Rows[0]));
            counts.Rows[0][column] = DBNull.Value;
            Reject(() => DashboardData.ReadCounts(counts.Rows[0]));
            counts.Rows[0][column] = -1;
            Reject(() => DashboardData.ReadCounts(counts.Rows[0]));
            counts.Rows[0][column] = 0;
        }
        using var years = new DataTable();
        years.Columns.Add("YearName", typeof(string)); years.Columns.Add("IsCurrent", typeof(bool));
        Check(DashboardData.AcademicYearLabel(years) == "Academic Year — Not Set", "An empty year list does not select a year");
        years.Rows.Add("2027–28", false); years.Rows.Add(" 2026–27 ", true);
        Check(DashboardData.AcademicYearLabel(years) == "Academic Year — 2026–27", "The marked year is selected instead of the first/future row");
        years.Rows[1]["IsCurrent"] = false;
        Check(DashboardData.AcademicYearLabel(years) == "Academic Year — Not Set", "No marked year never falls back to a historical/future year");
        years.Rows[0]["IsCurrent"] = years.Rows[1]["IsCurrent"] = true;
        Check(DashboardData.AcademicYearLabel(years) == "Academic Year — Review Setup", "Ambiguous current years are not silently chosen");
        years.Rows[0]["IsCurrent"] = false; years.Rows[1]["YearName"] = DBNull.Value;
        Check(DashboardData.AcademicYearLabel(years) == "Academic Year — Not Set", "An unnamed current year has no display identity");
        years.Columns.Remove("IsCurrent");
        Reject(() => DashboardData.AcademicYearLabel(years));
        return checks;
    }
}
