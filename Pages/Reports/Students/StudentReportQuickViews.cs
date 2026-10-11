using System.Data;

namespace FMSC_SMS.Pages.Reports.Students;

internal static class StudentReportQuickViews
{
    internal sealed record View(string Key, string Name, string Columns, string Transport = "", string Completion = "");
    internal static readonly View[] All =
    {
        new("contacts", "Parent Contact List",
            "AdmissionNumber,StudentName,FatherName,ContactNumber,AlternateContactNumber,GuardianName,GuardianRelation,CampusName,ClassSection"),
        new("register", "Class Register",
            "AdmissionNumber,StudentName,FatherName,Gender,DateOfBirth,CampusName,ClassSection,YearName"),
        new("transport", "Transport Students",
            "AdmissionNumber,StudentName,FatherName,ContactNumber,CampusName,ClassSection,Transport", "yes"),
        new("completion", "Record Completion",
            "AdmissionNumber,StudentName,FatherName,CampusName,ClassSection,MissingDetails,ContactNumber,AlternateContactNumber,DateOfBirth,Address", Completion: "any")
    };

    internal static DataTable FilterCompletion(DataTable students, string choice, bool canViewDetails)
    {
        var result = students.Clone();
        if (!result.Columns.Contains("MissingDetails")) result.Columns.Add("MissingDetails", typeof(string));
        foreach (DataRow row in students.Rows)
        {
            bool Blank(string column) => students.Columns.Contains(column)
                && string.IsNullOrWhiteSpace(Convert.ToString(row[column]));
            var missing = new List<(string Key, string Text)>();
            if (Blank("ContactNumber") && (!canViewDetails || Blank("AlternateContactNumber")))
                missing.Add(("contact", canViewDetails ? "Contact number" : "Primary contact number"));
            if (canViewDetails && Blank("DateOfBirth")) missing.Add(("birth", "Date of birth"));
            if (canViewDetails && Blank("Address")) missing.Add(("address", "Address"));
            if (choice.Length > 0 && !(choice == "any" ? missing.Count > 0 : missing.Any(item => item.Key == choice))) continue;
            result.ImportRow(row);
            result.Rows[result.Rows.Count - 1]["MissingDetails"] = string.Join(", ", missing.Select(item => item.Text));
        }
        return result;
    }

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
