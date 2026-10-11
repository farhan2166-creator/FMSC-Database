using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FMSC_SMS.Pages.Reports.Students;
using Microsoft.Reporting.NETCore;
int checks = 0;
void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); checks++; }
var students = new DataTable();
foreach (string field in new[] { "CampusId", "ClassId", "SectionId", "ClassOrder", "AdmissionId", "EnrollmentId" }) students.Columns.Add(field, typeof(int));
foreach (string field in new[] { "CampusName", "ClassName", "SectionName", "EnrollmentStatus", "Gender" }) students.Columns.Add(field);
students.Rows.Add(1, 1, 1, 1, 1, 1, "Campus", "One", "A", "Active", "Male");
students.Rows.Add(1, 1, 1, 1, 2, 2, "Campus", "One", "A", "Active", "Female");
students.Rows.Add(1, 1, 1, 1, 2, 2, "Campus", "One", "A", "Active", "Female");
students.Rows.Add(1, 1, 1, 1, 3, 3, "Campus", "One", "A", "Active", DBNull.Value);
students.Rows.Add(1, 1, 2, 1, 4, 4, "Campus", "One", "B", "Active", " M ");
students.Rows.Add(2, 1, 1, 1, 5, 5, "Campus", "One", "A", "Active", "F");
students.Rows.Add(1, 2, 1, 2, 6, 6, "Campus", "Two", "A", "Left", "Male");
students.Rows.Add(1, 2, 1, 2, 7, DBNull.Value, "Campus", "Two", "A", "", "Male");
var balances = students.Clone(); balances.Columns.Add("Outstanding", typeof(decimal));
foreach (var item in new[] { (Index: 0, Amount: 20.25m), (Index: 1, Amount: 30.10m), (Index: 6, Amount: 40.55m), (Index: 7, Amount: 0m) })
{ balances.ImportRow(students.Rows[item.Index]); balances.Rows[balances.Rows.Count - 1]["Outstanding"] = item.Amount; }
var data = ClassSectionSummaryData.Create(students, balances, "Sample School", "Campus: All | Year: 2026", DateTime.Today);
Check(data.Rows.Count == 4, "Active placements and former debt placements appear separately");
Check(data.Rows[0].Field<int>("ActiveStudents") == 3 && data.Rows[0].Field<int>("Boys") == 1 && data.Rows[0].Field<int>("Girls") == 1 && data.Rows[0].Field<int>("Other") == 1,
    "Unknown gender is counted without losing students; duplicate student rows do not inflate counts");
Check(data.Rows[0].Field<decimal>("Outstanding") == 50.35m, "Balances aggregate precisely");
Check(data.Rows[1].Field<int>("ActiveStudents") == 1 && data.Rows[1].Field<decimal>("Outstanding") == 0,
    "A section with no debt remains visible");
Check(data.Rows[2].Field<int>("ActiveStudents") == 0 && data.Rows[2].Field<decimal>("Outstanding") == 40.55m,
    "Former placement debt remains visible without counting former students as active");
Check(data.Rows[3].Field<int>("Girls") == 1, "Matching placement labels across campuses do not merge");
Check(data.AsEnumerable().Sum(row => row.Field<int>("ActiveStudents")) == 5 && data.AsEnumerable().Sum(row => row.Field<decimal>("Outstanding")) == 90.90m,
    "School totals include active students and all positive scoped outstanding charges");
Check(data.AsEnumerable().All(row => row.Field<int>("ActiveStudents") == row.Field<int>("Boys") + row.Field<int>("Girls") + row.Field<int>("Other")), "Gender subtotals reconcile");
Check(ClassSectionSummaryData.Create(students.Clone(), balances.Clone(), "School", "All", DateTime.Today).Rows.Count == 0, "Empty source produces no invented classes");
Check(students.Rows.Count == 8 && !students.Columns.Contains("Outstanding"), "Sources are unchanged");
string path = Path.Combine(AppContext.BaseDirectory, "Reports", "ClassSectionSummary.rdlc");
XNamespace ns = "http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";
var xml = XDocument.Load(path);
Check(xml.Descendants(ns + "PageWidth").Single().Value == "21cm" && xml.Descendants(ns + "PageHeight").Single().Value == "29.7cm", "Portrait A4 dimensions");
Check(xml.Descendants(ns + "RepeatOnNewPage").Single().Value == "true", "Column headings repeat on continuation pages");
using var report = new LocalReport { ReportPath = path };
report.DataSources.Add(new ReportDataSource("ClassSummaryData", data));
string html = Encoding.UTF8.GetString(report.Render("HTML4.0", null, out _, out _, out _, out _, out var warnings));
Check(warnings.Length == 0 && html.Contains("Sample School") && html.Contains("Class &amp; Section Summary"), "Actual RDLC renders with school identity and no warnings");
Check(html.Contains("50.35") && html.Contains("40.55") && html.Contains("90.90") && html.Contains("Total"), "Printed detail and school balance total retain cents");
Check(html.Contains("including former placements") && html.Contains("other Student Report filters are not applied"), "Report explains counting and filter scopes");
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ClassSummaryPreview.html"), html);
var manyClasses = students.Clone();
for (int i = 1; i <= 80; i++)
    manyClasses.Rows.Add(1, i, 1, i, i, i, "Campus", $"Summary Class {i:000}", "A", "Active", "Male");
manyClasses.Rows[0]["ClassName"] = "A class with a long descriptive name that must remain complete in the printed summary";
var manyData = ClassSectionSummaryData.Create(manyClasses, balances.Clone(), "School", "All campuses and years", DateTime.Today);
report.DataSources.Clear(); report.DataSources.Add(new ReportDataSource("ClassSummaryData", manyData));
string manyHtml = Encoding.UTF8.GetString(report.Render("HTML4.0", null, out _, out _, out _, out _, out var manyWarnings));
Check(manyWarnings.Length == 0 && manyHtml.Contains((string)manyClasses.Rows[0]["ClassName"]),
    "Large summary retains complete long class names without render warnings");
for (int i = 2; i <= 80; i++) Check(Regex.Matches(manyHtml, $"Summary Class {i:000}(?![0-9])").Count == 1,
    "Every class appears once in the large summary");
Check(manyData.AsEnumerable().Sum(row => row.Field<int>("ActiveStudents")) == 80 && manyData.AsEnumerable().Sum(row => row.Field<decimal>("Outstanding")) == 0,
    "Large summary total remains correct with no outstanding charges");
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ClassSummaryLargePreview.html"), manyHtml);
Console.WriteLine($"PASS {checks} class summary checks, including actual RDLC rendering.");
