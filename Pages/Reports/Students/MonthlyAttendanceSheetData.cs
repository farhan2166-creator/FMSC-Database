using System.Data;
using System.Globalization;

namespace FMSC_SMS.Pages.Reports.Students;

internal static class MonthlyAttendanceSheetData
{
    internal static DataTable Create(DataTable students, string school, DateTime month, string teacher)
    {
        var pages = ClassSheetData.Create(students, school, "", "",
            month.ToString("MMMM yyyy", CultureInfo.InvariantCulture), teacher);
        var data = new DataTable("ClassSheetData");
        data.Columns.Add("PageKey", typeof(int));
        foreach (string field in new[] { "SchoolName", "CampusName", "ClassName", "SectionName", "YearName",
            "Exam", "Subject", "ExamDate", "Teacher", "PageLabel", "SerialNumber", "StudentName" })
            data.Columns.Add(field, typeof(string));
        int days = DateTime.DaysInMonth(month.Year, month.Month);
        for (int day = 1; day <= 31; day++)
        {
            data.Columns.Add($"DayHeader{day:00}", typeof(string));
            data.Columns.Add($"DayEnabled{day:00}", typeof(bool));
        }
        foreach (DataRow page in pages.Rows)
            for (int slot = 1; slot <= ClassSheetData.StudentsPerPage; slot++)
            {
                var row = data.NewRow();
                foreach (DataColumn column in pages.Columns)
                    if (data.Columns.Contains(column.ColumnName)) row[column.ColumnName] = page[column];
                row["SerialNumber"] = page[$"Number{slot:00}"];
                row["StudentName"] = page[$"Name{slot:00}"];
                for (int day = 1; day <= 31; day++)
                {
                    bool enabled = day <= days;
                    row[$"DayHeader{day:00}"] = enabled
                        ? day + "\n" + new DateTime(month.Year, month.Month, day).ToString("ddd", CultureInfo.InvariantCulture)
                        : day.ToString(CultureInfo.InvariantCulture);
                    row[$"DayEnabled{day:00}"] = enabled;
                }
                data.Rows.Add(row);
            }
        return data;
    }
}
