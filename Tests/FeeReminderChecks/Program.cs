using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FMSC_SMS.Pages.Reports.Finance;
using FMSC_SMS.Core;
using Microsoft.Reporting.NETCore;

int checks = 0;
void Check(bool passed, string description)
{
    if (!passed) throw new Exception(description);
    checks++;
}
var balances = new DataTable();
balances.Columns.Add("AdmissionId", typeof(int));
foreach (string name in new[] { "AdmissionNumber", "StudentName", "FatherName" }) balances.Columns.Add(name);
balances.Columns.Add("Outstanding", typeof(decimal));
balances.Rows.Add(2, "2a", "Same Student", "Zafar Father", 30.25m);
balances.Rows.Add(1, "1", "Same Student", "Alpha Father", 100.10m);
balances.Rows.Add(1, "1", "Same Student", "Alpha Father", 25.45m);
balances.Rows.Add(3, "3", "Paid Student", "Paid Father", 0m);
balances.Rows.Add(4, "4", "Unselected Student", "Other Father", 40.05m);
balances.Rows.Add(5, "5", "Credit Student", "Credit Father", -15m);
var roster = FeeReminderReportData.CreateRoster(balances);
Check(roster.Rows.Count == 3 && roster.AsEnumerable().All(row => row.Field<bool>("Include")), "Only students with positive outstanding amounts are selected initially");
Check(roster.Rows[0].Field<int>("AdmissionId") == 1 && roster.Rows[0].Field<decimal>("Outstanding") == 125.55m,
    "Balances from multiple placements aggregate per student and same names sort by father");
var selected = new HashSet<int> { 1, 2, 3 };
DateTime prepared = new(2026, 10, 11, 12, 30, 0);
const string scope = "Campus: Main; Academic year: All; Class: Five; Section: A";
var data = FeeReminderReportData.Create(balances, selected, "Sample School", "School Contact", "School Address",
    scope, prepared, null, "");
Check(data.Rows.Count == 2, "Unchecked and paid students do not get reminders");
Check(data.Rows[0].Field<decimal>("TotalPaid") == 125.55m && data.Rows[1].Field<decimal>("TotalPaid") == 30.25m,
    "Outstanding amounts preserve decimal precision");
Check(data.AsEnumerable().All(row => row.Field<string>("ClassSection") == scope), "Every reminder states the selected charge scope");
Check(data.AsEnumerable().All(row => row.Field<string>("FamilyBatch") == "" && row.Field<string>("ReceiptStatus") == ""),
    "Optional date and message may stay blank");
Check(balances.Rows.Count == 6 && balances.Rows[1].Field<decimal>("Outstanding") == 100.10m, "Source balances remain unchanged");
var paidSinceSelection = balances.Copy();
foreach (DataRow row in paidSinceSelection.Rows) if (row.Field<int>("AdmissionId") == 1) row["Outstanding"] = 0m;
Check(FeeReminderReportData.Create(paidSinceSelection, selected, "School", "", "", scope, prepared, null, "").Rows.Count == 1,
    "Refreshed balances exclude a student who paid after selection");
Check(FeeReminderReportData.Create(balances, new HashSet<int>(), "School", "", "", scope, prepared, null, "").Rows.Count == 0,
    "Empty selection creates no reminders");
string path = Path.Combine(AppContext.BaseDirectory, "Reports", "FeeReminder.rdlc");
XNamespace ns = "http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";
var definition = XDocument.Load(path);
Check(definition.Descendants(ns + "PageWidth").Single().Value == "2.28in", "Reminder retains the existing receipt-printer paper width");
Check(definition.Descendants(ns + "BreakLocation").Single().Value == "Between", "Each reminder prints on a separate receipt page");
using var report = new LocalReport { ReportPath = path };
report.DataSources.Add(new ReportDataSource("FeeReceiptData", data));
string html = Encoding.UTF8.GetString(report.Render("HTML4.0", null, out _, out _, out _, out _, out var warnings));
Check(warnings.Length == 0, "Reminder RDLC processes without warnings");
Check(Regex.Matches(html, "Same Student").Count == 2 && html.Contains("Alpha Father") && html.Contains("Zafar Father"),
    "Same-name students retain distinct identity details");
Check(html.Contains("125.55") && html.Contains("30.25"), "Printed amounts preserve cents");
Check(!html.Contains("Unselected Student") && !html.Contains("Paid Student") && !html.Contains("Credit Student"),
    "Excluded students do not appear in the printout");
Check(!html.Contains("Requested payment date:") && !html.Contains("TOTAL PAYABLE") && !html.Contains("Bill No."),
    "Blank optional date stays absent and reminder has no bill or receipt number");
const string longName = "Muhammad Abdullah Abdul Rehman Khan Muhammad Ali Hassan";
string longMessage = string.Join(" ", Enumerable.Repeat("Please contact the school office to discuss payment.", 7));
balances.Rows[1]["StudentName"] = balances.Rows[2]["StudentName"] = longName;
var entered = FeeReminderReportData.Create(balances, new HashSet<int> { 1 }, "Sample School", "School Contact", "School Address",
    scope, prepared, new DateTime(2026, 10, 20), "  " + longMessage + "  ");
Check(entered.Rows[0].Field<string>("FamilyBatch") == "Requested payment date: 20-Oct-2026" &&
    entered.Rows[0].Field<string>("ReceiptStatus") == longMessage, "Requested date is only print text and message is trimmed");
report.DataSources.Clear(); report.DataSources.Add(new ReportDataSource("FeeReceiptData", entered));
string longHtml = Encoding.UTF8.GetString(report.Render("HTML4.0", null, out _, out _, out _, out _, out var longWarnings));
Check(longWarnings.Length == 0 && longHtml.Contains(longName) && longHtml.Contains(longMessage), "Complete long names and messages render without warnings");
Check(longHtml.Contains("20-Oct-2026") && longHtml.Contains("School Contact") && longHtml.Contains("School Address"),
    "Requested date and school contact details render");
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "FeeReminderPreview.html"), longHtml);
string a4Definition = FeeReminderPrintLayout.A4Definition(path);
var a4Xml = XDocument.Parse(a4Definition);
Check(a4Xml.Descendants(ns + "PageWidth").Single().Value == "8.27in" &&
    a4Xml.Descendants(ns + "PageHeight").Single().Value == "11.69in" &&
    a4Xml.Descendants(ns + "Columns").Single().Value == "3", "A4 uses three columns on portrait A4");
Check(!a4Xml.Descendants(ns + "PageBreak").Any(), "A4 reminders flow without one page break per student");
Check(a4Xml.Descendants(ns + "Rectangle").Single().Element(ns + "KeepTogether")!.Value == "true",
    "A4 keeps complete reminder blocks together where they fit");
Check(XDocument.Load(path).Descendants(ns + "PageWidth").Single().Value == "2.28in",
    "Choosing A4 does not modify the thermal template");
using var a4Report = new LocalReport();
a4Report.LoadReportDefinition(new StringReader(a4Definition));
a4Report.DataSources.Add(new ReportDataSource("FeeReceiptData", entered));
string a4LongHtml = Encoding.UTF8.GetString(a4Report.Render("HTML4.0", null,
    out _, out _, out _, out _, out var a4LongWarnings));
Check(a4LongWarnings.Length == 0 && a4LongHtml.Contains(longName) && a4LongHtml.Contains(longMessage)
    && a4LongHtml.Contains("125.55") && a4LongHtml.Contains("20-Oct-2026"),
    "A4 retains complete long names, messages, date and precise balance");
var largeBalances = balances.Clone();
for (int i = 1; i <= 100; i++) largeBalances.Rows.Add(i, i.ToString(), $"Batch Student {i:000}", $"Father {i:000}", i + .25m);
var largeData = FeeReminderReportData.Create(largeBalances, Enumerable.Range(1, 100).ToHashSet(),
    "School", "Contact", "Address", scope, prepared, null, "");
a4Report.DataSources.Clear(); a4Report.DataSources.Add(new ReportDataSource("FeeReceiptData", largeData));
string a4Html = Encoding.UTF8.GetString(a4Report.Render("HTML4.0", null,
    out _, out _, out _, out _, out var a4Warnings));
Check(a4Warnings.Length == 0 && Regex.Matches(a4Html, "FEE REMINDER").Count == 100,
    "One hundred reminders render on A4 without duplicates or omissions");
for (int i = 1; i <= 100; i++) Check(Regex.Matches(a4Html, $"Batch Student {i:000}(?![0-9])").Count == 1,
    "Each selected student appears exactly once in the A4 batch");
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "FeeReminderA4Preview.html"), a4LongHtml);
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "FeeReminderA4BatchPreview.html"), a4Html);
Console.WriteLine($"PASS {checks} fee reminder checks, including the real RDLC rendered without warnings.");
