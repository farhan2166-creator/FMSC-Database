using System.Globalization;
using System.Text;
using System.Xml.Linq;
using FMSC_SMS.Core;
using Microsoft.Reporting.NETCore;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
var source = new System.Data.DataTable();
source.Columns.Add("Student");
source.Columns.Add("Outstanding", typeof(decimal));
source.Columns.Add("Due Date", typeof(DateTime));
source.Columns.Add("Score", typeof(double));
source.Columns.Add("Days", typeof(short));
source.Columns.Add("Remarks");
string name = "A student with a long complete name that must wrap instead of being cut off";
source.Rows.Add(name, 1234567.89m, new DateTime(2026, 10, 11), 98.125d, (short)42, DBNull.Value);
var generated = GridReportPreview.Build("Sample School", "Outstanding Report", "Campus: Main; Class: All", source, "Operator");
XNamespace ns = "http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";
var xml = XDocument.Parse(generated.Definition);
Check(source.Rows[0].Field<decimal>("Outstanding") == 1234567.89m && source.Columns.Count == 6,
    "Generating a report preserves the source schema and financial values");
Check(generated.Data.Rows[0].Field<string>("C1") == "1,234,567.89", "Amounts retain cents and grouping");
Check(generated.Data.Rows[0].Field<string>("C2") == "11-Oct-2026", "Dates use the standard readable format");
Check(generated.Data.Rows[0].Field<string>("C5") == "", "Null remarks print as blanks");
string Alignment(string cell) => xml.Descendants(ns + "Textbox").Single(text => (string?)text.Attribute("Name") == cell)
    .Descendants(ns + "TextAlign").Single().Value;
Check(Alignment("Value1") == "Right" && Alignment("Value3") == "Right" && Alignment("Value4") == "Right", "Numeric types align consistently");
Check(Alignment("Value0") == "Left" && Alignment("Value2") == "Center", "Names and dates keep readable alignment");
Check(xml.Descendants(ns + "RepeatOnNewPage").Single().Value == "true", "Column headings repeat on later pages");
Check(xml.Descendants(ns + "Textbox").All(text => text.Element(ns + "CanGrow")?.Value == "true"), "Long names and headings can grow");
Check(xml.Descendants(ns + "PageWidth").Single().Value == "8.27in", "Six-column report retains portrait A4");
Check(xml.Descendants(ns + "Value").Any(value => value.Value.Contains("Globals!PageNumber") && value.Value.Contains("Globals!TotalPages")), "Footer includes current and total page numbers");
using var report = new LocalReport();
using var definition = new StringReader(generated.Definition);
report.LoadReportDefinition(definition);
report.DataSources.Add(new ReportDataSource("ReportData", generated.Data));
string html = Encoding.UTF8.GetString(report.Render("HTML4.0", null, out _, out _, out _, out _, out var warnings));
Check(warnings.Length == 0 && html.Contains("Sample School") && html.Contains("Outstanding Report") && html.Contains("Campus: Main"), "Actual report renders school identity, title and filters without warnings");
Check(html.Contains(name) && html.Contains("1,234,567.89"), "Rendered report retains complete names and financial precision");
source.Columns.Add("Campus");
var wide = XDocument.Parse(GridReportPreview.Build("School", "Wide Report", "", source, "Operator").Definition);
Check(wide.Descendants(ns + "PageWidth").Single().Value == "11.69in", "Wider reports retain landscape A4");
Console.WriteLine($"Passed {checks} grid report checks.");
