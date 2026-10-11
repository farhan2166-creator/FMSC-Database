using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FMSC_SMS.Pages.Reports.Finance;
using Microsoft.Reporting.NETCore;

if (args.Length == 2 && args[0] == "--write-sql")
{
    File.WriteAllText(args[1], "SET ANSI_NULLS ON;\nSET QUOTED_IDENTIFIER ON;\nGO\n" +
        "CREATE PROCEDURE #ClassOutstanding @UserId int,@CampusId int=0,@AcademicYearId int=0,@ClassId int=0,@SectionId int=0 AS BEGIN\n" +
        ClassOutstandingReportData.Query + "\nEND;\nGO\n");
    return;
}

int checks = 0;
void Check(bool result, string description) { if (!result) throw new Exception(description); checks++; }
var source = new DataTable();
foreach (string name in new[] { "CampusId", "CampusOrder", "ClassId", "ClassOrder", "SectionId", "SectionOrder", "AdmissionId" }) source.Columns.Add(name, typeof(int));
foreach (string name in new[] { "CampusName", "ClassName", "SectionName", "AdmissionNumber", "StudentName", "FatherName" }) source.Columns.Add(name);
source.Columns.Add("Outstanding", typeof(decimal));
void Add(int campus, int klass, int section, int admission, decimal outstanding) => source.Rows.Add(campus, campus, klass, klass,
    section, section, admission, "Campus " + campus, "Class " + klass, "Section " + section,
    "ADM-" + admission, "Student " + admission.ToString("000"), "Father " + admission.ToString("000"), outstanding);
// Deliberately shuffled groups, and a class long enough to require physical continuation.
Add(2, 1, 1, 203, 300m); Add(1, 1, 2, 202, 200m); Add(1, 2, 1, 201, 100m);
for (int i = 61; i > 0; i--) Add(1, 1, 1, i, 10.25m);
Add(1, 1, 1, 204, 0m);
var data = ClassOutstandingReportData.Create(source, "Sample School", "All academic years", new DateTime(2026, 10, 10, 12, 30, 0));
Check(data.Rows.Count == 64, "Positive outstanding only");
Check(data.AsEnumerable().Select(row => row.Field<string>("GroupKey")).Distinct().Count() == 4, "Campus/class/section identities stay separate");
Check(data.Rows[0].Field<string>("GroupKey") == "1:1:1" && data.Rows[63].Field<string>("GroupKey") == "2:1:1", "Catalog group order");
foreach (var group in data.AsEnumerable().GroupBy(row => row.Field<string>("GroupKey")))
{
    int serial = 0;
    foreach (var row in group) Check(row.Field<int>("SerialNumber") == ++serial, "Serial resets per class and section");
}
Check(data.AsEnumerable().Sum(row => row.Field<decimal>("Outstanding")) == 1225.25m, "Money preserved exactly");
Check(data.AsEnumerable().All(row => row.Field<string>("Remarks") == ""), "Remarks stay blank");
Check(data.AsEnumerable().All(row => row.Field<string>("YearSelection") == "All academic years" && row.Field<string>("SchoolName") == "Sample School"), "Captured report context");
Check(ClassOutstandingReportData.Create(source.Clone(), "School", "Year", DateTime.Today).Rows.Count == 0, "Empty reports have no artificial groups");
var sameNames = source.AsEnumerable().Where(row => row.Field<int>("CampusId") == 1 &&
    row.Field<int>("ClassId") == 1 && row.Field<int>("SectionId") == 1).Take(2).CopyToDataTable();
sameNames.Rows[0]["StudentName"] = sameNames.Rows[1]["StudentName"] = "Same Student";
sameNames.Rows[0]["FatherName"] = "Zulu Father"; sameNames.Rows[0]["AdmissionNumber"] = "001";
sameNames.Rows[1]["FatherName"] = "Alpha Father"; sameNames.Rows[1]["AdmissionNumber"] = "999";
var sortedNames = ClassOutstandingReportData.Create(sameNames, "School", "Year", DateTime.Today);
Check(sortedNames.Rows[0].Field<string>("FatherName") == "Alpha Father" &&
    sortedNames.Rows[1].Field<string>("FatherName") == "Zulu Father", "Father name sorts matching student names before admission number");
string path = Path.Combine(AppContext.BaseDirectory, "Reports", "ClassOutstandingCharges.rdlc");
XNamespace ns = "http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";
var xml = XDocument.Load(path);
Check(xml.Descendants(ns + "PageWidth").Single().Value == "21cm" && xml.Descendants(ns + "PageHeight").Single().Value == "29.7cm", "Full portrait A4");
Check(xml.Descendants(ns + "BreakLocation").Single().Value == "Between", "Separate group pages");
Check(xml.Descendants(ns + "RepeatOnNewPage").Count(value => value.Value == "true") == 5, "All group and column headings repeat on continuation pages");
using var report = new LocalReport { ReportPath = path };
report.DataSources.Add(new ReportDataSource("ClassOutstandingData", data));
string html = Encoding.UTF8.GetString(report.Render("HTML4.0", null, out _, out _, out _, out _, out var warnings));
Check(warnings.Length == 0, "RDLC processes without warnings");
for (int i = 1; i <= 61; i++) Check(Regex.Matches(html, "Student " + i.ToString("000") + "(?![0-9])").Count == 1, "Student printed once");
Check(html.Contains("Father 001") && html.Contains("ADM-1"), "Father and admission number printed");
Check(html.Contains("625.25") && html.Contains("Class / Section Total (61 students)"), "Large group subtotal");
Check(Regex.Matches(html, "Class / Section Total").Count == 4, "One subtotal per placement");
Check(!html.Contains("Student 204"), "Zero balance omitted");
if (args.Length > 0) File.WriteAllText(args[0], html);
Console.WriteLine($"PASS {checks} class outstanding checks, including RDLC processing with no warnings.");
