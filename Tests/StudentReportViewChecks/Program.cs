using System.Data;
using System.Text.RegularExpressions;
using FMSC_SMS.Pages.Reports.Students;

int checks = 0;
void Check(bool passed, string message)
{
    if (!passed) throw new Exception(message);
    checks++;
}
var students = new DataTable();
students.Columns.Add("AdmissionId", typeof(int));
students.Columns.Add("StudentName");
students.Columns.Add("Transport");
students.Columns.Add("OutstandingBalance", typeof(decimal));
students.Rows.Add(1, "Alpha", "Yes", 100m);
students.Rows.Add(2, "Beta", "No", 200m);
students.Rows.Add(3, "Gamma", DBNull.Value, 300m);
students.Rows.Add(4, "Delta", " yes ", 400m);
students.Rows.Add(5, "Epsilon", "", 500m);
var transport = StudentReportQuickViews.FilterTransport(students, "yes");
Check(transport.AsEnumerable().Select(row => row.Field<int>("AdmissionId")).SequenceEqual(new[] { 1, 4 }),
    "Transport list includes only confirmed transport students");
Check(transport.Rows.Count == 2 && transport.AsEnumerable().Sum(row => row.Field<decimal>("OutstandingBalance")) == 500m,
    "Transport selection retains correct student amounts");
Check(StudentReportQuickViews.FilterTransport(students, "no").Rows.Count == 1,
    "No transport does not classify unknown values as No");
Check(StudentReportQuickViews.FilterTransport(students, "").Rows.Count == 5,
    "All students includes transport, non-transport and unknown records");
Check(students.Rows.Count == 5 && students.Rows[2].IsNull("Transport"), "Filtering does not modify the loaded roster");
var noTransportColumn = students.Copy();
noTransportColumn.Columns.Remove("Transport");
Check(StudentReportQuickViews.FilterTransport(noTransportColumn, "yes").Rows.Count == 0,
    "Unavailable transport information does not produce an invented transport list");
Check(StudentReportQuickViews.FilterTransport(noTransportColumn, "").Rows.Count == 5,
    "Ordinary reports work without transport information");
string shape = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "StudentReportsPage.Shape.cs"));
int catalogStart = shape.IndexOf("RegisterCatalog =");
int catalogEnd = shape.IndexOf("ChangesCatalog =", catalogStart);
string catalog = shape[catalogStart..catalogEnd];
var valid = Regex.Matches(catalog, "new\\(\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToHashSet();
foreach (var view in StudentReportQuickViews.All)
{
    string[] columns = view.Columns.Split(',');
    Check(columns.All(valid.Contains), $"All columns in {view.Name} belong to the existing student report catalog");
    Check(columns.Contains("AdmissionNumber") && columns.Contains("StudentName") && columns.Contains("FatherName"),
        "Each ready view identifies the student clearly");
    Check(!columns.Contains("OutstandingBalance"), "Ready student lists do not request unnecessary balances");
}
Check(StudentReportQuickViews.All.Single(view => view.Key == "transport").Transport == "yes",
    "Transport preset selects the transport filter");
Check(StudentReportQuickViews.All.Where(view => view.Key != "transport").All(view => view.Transport == ""),
    "Contact and register views reset transport filtering");
Console.WriteLine($"PASS {checks} student report view checks.");
