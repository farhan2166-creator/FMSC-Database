using System.Data;

namespace FMSC_SMS.Pages.Reports.Students;

internal static class StudentReportQuickViews
{
    internal sealed record View(string Key, string Name, string Columns, string Transport = "");
    internal static readonly View[] All =
    {
        new("contacts", "Parent Contact List",
            "AdmissionNumber,StudentName,FatherName,ContactNumber,AlternateContactNumber,GuardianName,GuardianRelation,CampusName,ClassSection"),
        new("register", "Class Register",
            "AdmissionNumber,StudentName,FatherName,Gender,DateOfBirth,CampusName,ClassSection,YearName"),
        new("transport", "Transport Students",
            "AdmissionNumber,StudentName,FatherName,ContactNumber,CampusName,ClassSection,Transport", "yes")
    };

    internal static DataTable FilterTransport(DataTable students, string choice)
    {
        if (choice is not ("yes" or "no")) return students;
        var result = students.Clone();
        if (!students.Columns.Contains("Transport")) return result;
        string expected = choice == "yes" ? "Yes" : "No";
        foreach (DataRow row in students.Rows)
            if (string.Equals(Convert.ToString(row["Transport"])?.Trim(), expected, StringComparison.OrdinalIgnoreCase))
                result.ImportRow(row);
        return result;
    }
}
