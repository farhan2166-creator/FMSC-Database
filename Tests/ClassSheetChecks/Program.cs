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
var twins = Students(3);
twins.Columns.Add("FatherName");
twins.Rows[0]["StudentName"] = "Same Student";
twins.Rows[0]["FatherName"] = "Zafar Father";
twins.Rows[1]["StudentName"] = "Same Student";
twins.Rows[1]["FatherName"] = "Alpha Father";
twins.Rows[2]["StudentName"] = "A Unique Student";
twins.Rows[2]["FatherName"] = "Unique Father";
var twinRoster = ClassSheetData.CreateRoster(twins);
var twinMarks = ClassSheetData.Create(twinRoster, "School", "", "", "", "", showFatherForMatchingNames: true);
Check((string)twinMarks.Rows[0]["Name01"] == "A Unique Student", "Unique names stay plain and sort first by student name");
Check((string)twinMarks.Rows[0]["Name02"] == "Same Student (Alpha Father)" &&
    (string)twinMarks.Rows[0]["Name03"] == "Same Student (Zafar Father)", "Matching names show fathers and sort by father within student name");
Check((string)ClassSheetData.Create(twinRoster, "School", "", "", "", "").Rows[0]["Name02"] == "Same Student",
    "Attendance retains plain student names");
twinRoster.Rows[2]["Include"] = false;
Check((string)ClassSheetData.Create(twinRoster, "School", "", "", "", "", true).Rows[0]["Name02"] == "Same Student",
    "Excluded students do not trigger matching-name labels");
twinRoster.Rows[2]["Include"] = true;
twinRoster.Rows[2]["StudentName"] = " same student ";
Check((string)ClassSheetData.Create(twinRoster, "School", "", "", "", "", true).Rows[0]["Name03"] == "same student (Zafar Father)",
    "Matching-name detection ignores case and surrounding spaces");
twinRoster.Rows[2]["FatherName"] = DBNull.Value;
Check(ClassSheetData.Create(twinRoster, "School", "", "", "", "", true).Rows[0].ItemArray.Contains("same student"),
    "Missing father name stays plain without empty brackets");
Check((string)ClassSheetData.Create(Students(1), "School", "", "", "", "", true).Rows[0]["Name01"] == "Student 001",
    "A roster without father information remains printable");
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
    Check(definition.Descendants(ns + "Textbox").Where(box =>
        ((string?)box.Attribute("Name"))?.Contains("Cell1") == true)
        .All(box => box.Element(ns + "CanGrow")?.Value == "true"), "Student name cells allow complete wrapped names");
    var longRoster = Students(1);
    const string longName = "Muhammad Abdullah Abdul Rehman Khan Muhammad Ali Hassan";
    longRoster.Rows[0]["StudentName"] = longName;
    var longData = ClassSheetData.Create(longRoster, "Sample School", "Annual Examination for the Academic Session",
        "English Language and Literature", "Friday, 12 October 2026", "Teacher Name With Several Words");
    using var longReport = new LocalReport { ReportPath = path };
    longReport.DataSources.Add(new ReportDataSource("ClassSheetData", longData));
    string longHtml = Encoding.UTF8.GetString(longReport.Render("HTML4.0", null,
        out _, out _, out _, out _, out var longWarnings));
    Check(longWarnings.Length == 0 && Regex.Matches(longHtml, longName).Count == 2,
        "Long names and entered headers render in both copies");
    using var twinReport = new LocalReport { ReportPath = path };
    twinReport.DataSources.Add(new ReportDataSource("ClassSheetData",
        ClassSheetData.Create(twins, "School", "", "", "", "", showFatherForMatchingNames: kind == "Marks")));
    string twinHtml = Encoding.UTF8.GetString(twinReport.Render("HTML4.0", null,
        out _, out _, out _, out _, out var twinWarnings));
    Check(twinWarnings.Length == 0, "Matching student names render without warnings");
    if (kind == "Marks")
        Check(Regex.Matches(twinHtml, Regex.Escape("Same Student (Alpha Father)")).Count == 2 &&
            Regex.Matches(twinHtml, Regex.Escape("Same Student (Zafar Father)")).Count == 2,
            "Father names appear in both marks-sheet copies");
    else Check(!twinHtml.Contains("Alpha Father") && !twinHtml.Contains("Zafar Father"),
        "Attendance rendering retains plain names");

    for (int serial = 1; serial <= 61; serial++)
        Check(Regex.Matches(html, $"Student {serial:000}(?![0-9])").Count == 2, "Every student appears once in each copy");
    Check(!html.Contains("Former student") && !html.Contains("No enrollment"), "Excluded students are absent");
    Check(Regex.Matches(html, "Page 3 of 3").Count == 2, "Continuation footer on both copies");
    Check(html.Contains("Midterm") && html.Contains("English") && html.Contains("Ms. Teacher"), "Optional details rendered");
}
foreach (var (month, days) in new[] { (new DateTime(2025, 2, 15), 28), (new DateTime(2024, 2, 1), 29),
    (new DateTime(2026, 4, 30), 30), (new DateTime(2026, 1, 1), 31) })
{
    var monthly = MonthlyAttendanceSheetData.Create(Students(31), "School", month, "Teacher");
    Check(monthly.Rows.Count == 60 && monthly.AsEnumerable().Select(row => row.Field<int>("PageKey")).Distinct().Count() == 2,
        "Monthly register keeps 30 rows per page including blank final rows");
    for (int day = 1; day <= 31; day++)
        Check(monthly.AsEnumerable().All(row => row.Field<bool>($"DayEnabled{day:00}") == (day <= days)),
            "Nonexistent dates are disabled on every page");
    Check((string)monthly.Rows[30]["SerialNumber"] == "31" && (string)monthly.Rows[31]["StudentName"] == "",
        "Monthly register preserves continuation numbering and blank slots");
}
Check(MonthlyAttendanceSheetData.Create(Students(0), "School", DateTime.Today, "").Rows.Count == 0,
    "Empty monthly roster produces no pages");
var monthlyChoices = ClassSheetData.CreateRoster(Students(31));
monthlyChoices.Rows[0]["Include"] = false;
monthlyChoices.DefaultView.RowFilter = "StudentName LIKE '%031%'";
var monthlyData = MonthlyAttendanceSheetData.Create(monthlyChoices, "Sample School", new DateTime(2026, 1, 15), "Ms. Teacher");
Check(monthlyData.Rows.Count == 30 && (string)monthlyData.Rows[0]["StudentName"] == "Student 002" &&
    (string)monthlyData.Rows[29]["StudentName"] == "Student 031", "Monthly register honors exclusions and checked hidden students");
Check((string)monthlyData.Rows[0]["DayHeader01"] == "1\nThu" && monthlyData.Rows[0].Field<bool>("DayEnabled03"),
    "Weekdays are correct and weekend dates remain available");
string monthlyPath = Path.Combine(AppContext.BaseDirectory, "Reports", "ClassMonthlyAttendanceSheet.rdlc");
XNamespace reportNs = "http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";
var monthlyDefinition = XDocument.Load(monthlyPath);
Check(monthlyDefinition.Descendants(reportNs + "PageWidth").Single().Value == "29.7cm" &&
    monthlyDefinition.Descendants(reportNs + "PageHeight").Single().Value == "21cm", "Monthly attendance is landscape A4");
Check(monthlyDefinition.Descendants(reportNs + "BreakLocation").Single().Value == "Between", "Monthly page groups have explicit breaks");
using var monthlyReport = new LocalReport { ReportPath = monthlyPath };
monthlyReport.DataSources.Add(new ReportDataSource("ClassSheetData", monthlyData));
string monthlyHtml = Encoding.UTF8.GetString(monthlyReport.Render("HTML4.0", null,
    out _, out _, out _, out _, out var monthlyWarnings));
Check(monthlyWarnings.Length == 0, "Monthly RDLC processes without warnings");
for (int serial = 2; serial <= 31; serial++)
    Check(Regex.Matches(monthlyHtml, $"Student {serial:000}(?![0-9])").Count == 1, "Each selected student appears once on the monthly register");
Check(!monthlyHtml.Contains("Student 001") && !monthlyHtml.Contains("Former student") && !monthlyHtml.Contains("No enrollment"),
    "Monthly print excludes unchecked, former and unenrolled students");
Check(monthlyHtml.Contains("January 2026") && monthlyHtml.Contains("Ms. Teacher") && monthlyHtml.Contains("Signature"),
    "Monthly headings, teacher and signature render");
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "MonthlyAttendancePreview.html"), monthlyHtml);
var monthlyLongData = MonthlyAttendanceSheetData.Create(Students(61), "Sample School", new DateTime(2025, 2, 1), "Ms. Teacher");
const string monthlyLongName = "Muhammad Abdullah Abdul Rehman Khan Muhammad Ali Hassan";
monthlyLongData.Rows[0]["StudentName"] = monthlyLongName;
monthlyReport.DataSources.Clear();
monthlyReport.DataSources.Add(new ReportDataSource("ClassSheetData", monthlyLongData));
string monthlyLongHtml = Encoding.UTF8.GetString(monthlyReport.Render("HTML4.0", null,
    out _, out _, out _, out _, out var monthlyLongWarnings));
Check(monthlyLongWarnings.Length == 0 && Regex.Matches(monthlyLongHtml, monthlyLongName).Count == 1,
    "Long student names render completely on the monthly register");
for (int serial = 2; serial <= 61; serial++)
    Check(Regex.Matches(monthlyLongHtml, $"Student {serial:000}(?![0-9])").Count == 1,
        "Monthly continuation pages contain each student once");
Check(Regex.Matches(monthlyLongHtml, "Monthly Attendance Register").Count == 3 &&
    Regex.Matches(monthlyLongHtml, "Page 3 of 3").Count == 1 && Regex.Matches(monthlyLongHtml, "Ms. Teacher").Count == 3,
    "Monthly headings and teacher lines repeat on each page");
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "MonthlyAttendanceLongPreview.html"), monthlyLongHtml);
// Multiple placements must never share a page, even when their labels match.
var batchRoster = ClassSheetData.CreateRoster(Students(63));
foreach (string id in new[] { "CampusId", "AcademicYearId", "ClassId", "SectionId", "ClassOrder" })
    batchRoster.Columns.Add(id, typeof(int));
for (int i = 0; i < batchRoster.Rows.Count; i++)
{
    var row = batchRoster.Rows[i];
    row["CampusId"] = row["AcademicYearId"] = 1;
    row["ClassId"] = i < 32 ? 1 : 2;
    row["SectionId"] = i < 31 ? 1 : i == 31 ? 2 : 3;
    row["ClassOrder"] = i < 32 ? 1 : 2;
    row["StudentName"] = "Same name";
}
batchRoster.Columns.Add("FatherName");
foreach (DataRow row in batchRoster.Rows) row["FatherName"] = "Father";
var batchMarks = ClassSheetBatchData.Create(batchRoster,
    group => ClassSheetData.Create(group, "School", "", "", "", "", true));
Check(batchMarks.Rows.Count == 5, "Three placements produce five separate pages");
Check(batchMarks.AsEnumerable().Select(row => row.Field<int>("PageKey")).SequenceEqual(new[] { 1, 2, 3, 4, 5 }),
    "Batch page keys are unique and sequential");
Check((string)batchMarks.Rows[2]["Number01"] == "1" && (string)batchMarks.Rows[3]["Number01"] == "1",
    "Numbering restarts for each placement");
Check((string)batchMarks.Rows[2]["Name01"] == "Same name" &&
    (string)batchMarks.Rows[0]["Name01"] == "Same name (Father)",
    "Duplicate-name disambiguation stays within each placement");
Check((string)batchMarks.Rows[2]["PageLabel"] == "Page 1 of 1 · 1 students",
    "Page totals describe the placement, not the batch");
foreach (string template in new[] { "ClassMarksSheet.rdlc", "ClassAttendanceSheet.rdlc" })
{
    using var batchReport = new LocalReport { ReportPath = Path.Combine(AppContext.BaseDirectory, "Reports", template) };
    batchReport.DataSources.Add(new ReportDataSource("ClassSheetData", batchMarks));
    string html = Encoding.UTF8.GetString(batchReport.Render("HTML4.0", null,
        out _, out _, out _, out _, out var warnings));
    Check(warnings.Length == 0 && Regex.Matches(html, "Page 1 of 1").Count == 2 &&
        Regex.Matches(html, "Page 2 of 2").Count == 4,
        template + " renders both copies with separate placement pagination");
}
batchRoster.Rows[31]["Include"] = false;
var batchMonthly = ClassSheetBatchData.Create(batchRoster,
    group => MonthlyAttendanceSheetData.Create(group, "School", new DateTime(2026, 2, 1), "Teacher"));
Check(batchMonthly.Rows.Count == 120 && batchMonthly.AsEnumerable().Select(row => row.Field<int>("PageKey")).Distinct().Count() == 4,
    "Unchecked placements disappear; monthly pages retain thirty rows per unique key");
Check((string)batchMonthly.Rows[60]["SerialNumber"] == "1", "Monthly numbering restarts at next placement");
monthlyReport.DataSources.Clear();
monthlyReport.DataSources.Add(new ReportDataSource("ClassSheetData", batchMonthly));
string batchHtml = Encoding.UTF8.GetString(monthlyReport.Render("HTML4.0", null,
    out _, out _, out _, out _, out var batchWarnings));
Check(batchWarnings.Length == 0 && Regex.Matches(batchHtml, "Monthly Attendance Register").Count == 4,
    "Real monthly RDLC renders every batch page without warnings");
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "BatchMonthlyPreview.html"), batchHtml);
foreach (DataRow row in batchRoster.Rows) row["Include"] = false;
Check(ClassSheetBatchData.Create(batchRoster, group => ClassSheetData.Create(group, "School", "", "", "", "")).Rows.Count == 0,
    "Empty batch selection produces no pages");
Console.WriteLine($"PASS {checks} class-sheet checks, including all three RDLC templates processed with no warnings.");
