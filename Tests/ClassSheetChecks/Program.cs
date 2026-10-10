using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FMSC_SMS.Pages.Reports.Students;
using Microsoft.Reporting.NETCore;

int checks = 0;
void Check(bool passed, string description)
{
    if (!passed) throw new Exception(description);
    checks++;
}
DataTable Students(int count)
{
    var table = new DataTable();
    foreach (string name in new[] { "EnrollmentId", "AdmissionId" }) table.Columns.Add(name, typeof(int));
    foreach (string name in new[] { "EnrollmentStatus", "StudentName", "AdmissionNumber",
        "CampusName", "ClassName", "SectionName", "YearName" }) table.Columns.Add(name);
    for (int i = count; i > 0; i--)
        table.Rows.Add(i, i, "Active", $"Student {i:000}", i.ToString(), "Campus One", "Five", "A", "2026–2027");
    table.Rows.Add(900, 900, "Left", "Former student", "900", "Campus One", "Five", "A", "2026–2027");
    table.Rows.Add(DBNull.Value, 901, "", "No enrollment", "901", "Campus One", "Five", "A", "2026–2027");
    return table;
}
foreach (int count in new[] { 0, 1, 29, 30, 31, 60, 61, 101 })
{
    var pages = ClassSheetData.Create(Students(count), "School", "", "", "", "");
    Check(pages.Rows.Count == (count + 29) / 30, $"Page count for {count}");
    int serial = 0;
    foreach (DataRow page in pages.Rows)
        for (int slot = 1; slot <= 30; slot++)
        {
            string number = (string)page[$"Number{slot:00}"], name = (string)page[$"Name{slot:00}"];
            if (++serial <= count)
                Check(number == serial.ToString() && name == $"Student {serial:000}", "Continuous numbering and stable sorting");
            else Check(number == "" && name == "", "Unused slots stay blank");
        }
}
var choices = ClassSheetData.CreateRoster(Students(31));
Check(choices.Rows.Count == 31 && choices.AsEnumerable().All(row => row.Field<bool>("Include")), "Only active students, initially all checked");
choices.Rows[0]["Include"] = false;
choices.Rows[15]["Include"] = false;
choices.DefaultView.RowFilter = "StudentName LIKE '%031%'";
var selectedPages = ClassSheetData.Create(choices, "School", "", "", "", "");
Check(selectedPages.Rows.Count == 1 && (string)selectedPages.Rows[0]["Number29"] == "29", "Excluded students reduce pagination");
Check((string)selectedPages.Rows[0]["Name01"] == "Student 002" && (string)selectedPages.Rows[0]["Name15"] == "Student 017", "Unchecked students omitted with compact serials");
Check((string)selectedPages.Rows[0]["Name29"] == "Student 031", "Search does not drop checked hidden students");
foreach (DataRow row in choices.Rows) row["Include"] = false;
Check(ClassSheetData.Create(choices, "School", "", "", "", "").Rows.Count == 0, "All unchecked creates no pages");
var roster = Students(61);
var data = ClassSheetData.Create(roster, "Sample School", "  Midterm  ", " English ", "", " Ms. Teacher ");
Check((string)data.Rows[0]["Exam"] == "Midterm" && (string)data.Rows[0]["Teacher"] == "Ms. Teacher", "Optional details trimmed");
Check((string)data.Rows[0]["ExamDate"] == "", "Date can remain blank");
foreach (string kind in new[] { "Marks", "Attendance" })
{
    string path = Path.Combine(AppContext.BaseDirectory, "Reports", $"Class{kind}Sheet.rdlc");
    XNamespace ns = "http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";
    var definition = XDocument.Load(path);
    Check(definition.Descendants(ns + "PageWidth").Single().Value == "21cm" &&
        definition.Descendants(ns + "PageHeight").Single().Value == "29.7cm", "Portrait A4");
    Check(definition.Descendants(ns + "BreakLocation").Single().Value == "Between", "Explicit page boundaries");
    Check(definition.Descendants(ns + "Textbox").Count(box => ((string?)box.Attribute("Name"))?.EndsWith("TeacherSign") == true) == 2,
        "Teacher signature on both copies");
    var headings = definition.Descendants(ns + "Textbox")
        .Where(box => ((string?)box.Attribute("Name"))?.Contains("Header") == true)
        .SelectMany(box => box.Descendants(ns + "Value")).Select(value => value.Value).ToArray();
    if (kind == "Attendance") Check(!headings.Contains("Present") && !headings.Contains("Absent") && headings.Contains("Signature"), "Signature-only attendance");
    using var report = new LocalReport { ReportPath = path };
    report.DataSources.Add(new ReportDataSource("ClassSheetData", data));
    string html = Encoding.UTF8.GetString(report.Render("HTML4.0", null,
        out _, out _, out _, out _, out var warnings));
    Check(warnings.Length == 0, "No report processing warnings");
    for (int serial = 1; serial <= 61; serial++)
        Check(Regex.Matches(html, $"Student {serial:000}(?![0-9])").Count == 2, "Every student appears once in each copy");
    Check(!html.Contains("Former student") && !html.Contains("No enrollment"), "Excluded students are absent");
    Check(Regex.Matches(html, "Page 3 of 3").Count == 2, "Continuation footer on both copies");
    Check(html.Contains("Midterm") && html.Contains("English") && html.Contains("Ms. Teacher"), "Optional details rendered");
}
Console.WriteLine($"PASS {checks} class-sheet checks, including both RDLC templates processed with no warnings.");
