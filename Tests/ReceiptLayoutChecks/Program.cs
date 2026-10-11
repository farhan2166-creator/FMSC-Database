using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FMSC_SMS.Core;
using Microsoft.Reporting.NETCore;

int checks = 0;
void Check(bool passed, string message)
{
    if (!passed) throw new Exception(message);
    checks++;
}
string path = Path.Combine(AppContext.BaseDirectory, "Reports", "FeePaymentReceipt.rdlc");
var source = XDocument.Load(path);
string unchangedSource = source.ToString();
var annual = new XDocument(source);
AnnualReceiptReportLayout.Apply(annual);
XNamespace ns = annual.Root!.Name.Namespace;
var page = annual.Root.Element(ns + "Page")!;
Check(page.Element(ns + "Columns")!.Value == "3" && page.Element(ns + "PageWidth")!.Value == "8.27in" &&
    page.Element(ns + "PageHeight")!.Value == "11.69in", "Three-column A4 retained");
Check(annual.Descendants(ns + "PageBreak").Count() == 0, "Receipts flow without forced breaks");
Check(source.ToString() == unchangedSource && source.Descendants(ns + "PageBreak").Count() == 1,
    "Original thermal receipt layout stays unchanged");
var block = annual.Descendants(ns + "Rectangle").Single();
Check(block.Element(ns + "KeepTogether")!.Value == "true", "Each receipt is an indivisible block when it fits");
Check(annual.Descendants(ns + "Group").Count(g => (string?)g.Attribute("Name") == "ReceiptPageGroup") == 1,
    "Receipt grouping scope occurs exactly once");

foreach (int[] lengths in new[] { new[] { 1 }, new[] { 2, 18, 3, 27 }, new[] { 80, 1 } })
{
    var data = ReceiptReportData.Create();
    for (int receipt = 1; receipt <= lengths.Length; receipt++)
        for (int line = 1; line <= lengths[receipt - 1]; line++)
            data.Rows.Add(receipt, "Sample School", "CASH RECEIPT", "STUDENT COPY", $"Receipt-{receipt:00}",
                $"Student-{receipt:00}", receipt.ToString(), "Five - A", "12-Oct-2026", "",
                $"Charge-{receipt:00}-{line:000} with a long description", 10m, 0m, lengths[receipt - 1] * 10m,
                0m, "Cash", "", false, false, "", "", "Main Campus", $"Father-{receipt:00}", "Oct-2026");
    using var report = new LocalReport();
    using var definition = new StringReader(annual.ToString());
    report.LoadReportDefinition(definition);
    report.DataSources.Add(new ReportDataSource("FeeReceiptData", data));
    string html = Encoding.UTF8.GetString(report.Render("HTML4.0", null,
        out _, out _, out _, out _, out var warnings));
    Check(warnings.Length == 0, "Annual receipt definition processes without warnings");
    for (int receipt = 1; receipt <= lengths.Length; receipt++)
    {
        Check(Regex.Matches(html, $"Receipt-{receipt:00}").Count == 1, "Receipt header appears once");
        Check(Regex.Matches(html, $"Student-{receipt:00}").Count == 1 && Regex.Matches(html, $"Father-{receipt:00}").Count == 1,
            "Header fields retain the correct receipt scope");
        for (int line = 1; line <= lengths[receipt - 1]; line++)
            Check(Regex.Matches(html, $"Charge-{receipt:00}-{line:000}").Count == 1, "Every charge appears exactly once");
        Check(html.IndexOf($"Receipt-{receipt:00}") < html.IndexOf($"Charge-{receipt:00}-001"), "Header precedes its charge lines");
    }
}
Console.WriteLine($"PASS {checks} annual receipt layout checks, including variable-length and oversized receipts rendered without warnings.");
