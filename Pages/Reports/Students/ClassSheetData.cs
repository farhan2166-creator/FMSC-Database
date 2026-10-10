using System.Data;

namespace FMSC_SMS.Pages.Reports.Students;

internal static class ClassSheetData
{
    internal const int StudentsPerPage = 25;

    internal static DataTable Create(DataTable students, string schoolName,
        string exam, string subject, string examDate, string teacher)
    {
        var data = new DataTable("ClassSheetData");
        data.Columns.Add("PageKey", typeof(int));
        foreach (string name in new[] { "SchoolName", "CampusName", "ClassName", "SectionName",
            "YearName", "Exam", "Subject", "ExamDate", "Teacher", "PageLabel" })
            data.Columns.Add(name, typeof(string));
        for (int i = 1; i <= StudentsPerPage; i++)
        {
            data.Columns.Add($"Number{i:00}", typeof(string));
            data.Columns.Add($"Name{i:00}", typeof(string));
        }
        var roster = students.AsEnumerable()
            .Where(row => !row.IsNull("EnrollmentId") && Convert.ToString(row["EnrollmentStatus"]) == "Active")
            .OrderBy(row => Convert.ToString(row["StudentName"]), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => Convert.ToString(row["AdmissionNumber"]), StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => Convert.ToInt32(row["AdmissionId"]))
            .ToArray();
        int pages = (roster.Length + StudentsPerPage - 1) / StudentsPerPage;
        for (int page = 0; page < pages; page++)
        {
            var row = data.NewRow();
            row["PageKey"] = page + 1;
            row["SchoolName"] = schoolName;
            foreach (string name in new[] { "CampusName", "ClassName", "SectionName", "YearName" })
                row[name] = Convert.ToString(roster[page * StudentsPerPage][name]) ?? "";
            row["Exam"] = exam.Trim(); row["Subject"] = subject.Trim();
            row["ExamDate"] = examDate.Trim(); row["Teacher"] = teacher.Trim();
            row["PageLabel"] = $"Page {page + 1} of {pages} · {roster.Length} students";
            for (int slot = 0; slot < StudentsPerPage; slot++)
            {
                int index = page * StudentsPerPage + slot;
                row[$"Number{slot + 1:00}"] = index < roster.Length ? (index + 1).ToString() : "";
                row[$"Name{slot + 1:00}"] = index < roster.Length
                    ? Convert.ToString(roster[index]["StudentName"]) ?? "" : "";
            }
            data.Rows.Add(row);
        }
        return data;
    }
}
