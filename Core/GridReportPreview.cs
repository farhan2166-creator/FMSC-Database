using System.Globalization;
using System.Xml.Linq;

namespace FMSC_SMS.Core;

internal static class GridReportPreview
{
    internal sealed record Report(string Definition, DataTable Data);
    internal static Report Build(string institution, string title, string subtitle, DataTable source, string printedBy, string moneyFormat = "#,##0.##")
    {
        XNamespace ns = "http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";
        XElement E(string name, params object[] content) => new(ns + name, content);
        var data = new DataTable("ReportData");
        for (int i = 0; i < source.Columns.Count; i++) data.Columns.Add("C" + i);
        foreach (DataRow row in source.Rows)
            data.Rows.Add(row.ItemArray.Select(value => value switch {
                DBNull => "", decimal number => number.ToString(moneyFormat, CultureInfo.CurrentCulture),
                DateTime date => date.ToString("dd-MMM-yyyy", CultureInfo.CurrentCulture),
                _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? ""
            }).ToArray());
        bool landscape = source.Columns.Count > 6;
        double width = landscape ? 10.67 : 7.25;
        string Inch(double value) => value.ToString("0.####", CultureInfo.InvariantCulture) + "in";
        var weights = source.Columns.Cast<DataColumn>().Select(column =>
            Math.Clamp(Math.Max(column.ColumnName.Length, source.AsEnumerable().Take(100)
                .Select(row => Convert.ToString(row[column])?.Length ?? 0).DefaultIfEmpty(0).Max()), 8, 32)).ToArray();
        double totalWeight = weights.Sum();
        XElement Text(string name, string value, bool bold = false, string align = "Left", string size = "8pt") =>
            E("Textbox", new XAttribute("Name", name), E("CanGrow", true), E("CanShrink", true),
                E("Paragraphs", E("Paragraph", E("TextRuns", E("TextRun", E("Value", value),
                    E("Style", E("FontFamily", "Segoe UI"), E("FontSize", size), E("FontWeight", bold ? "Bold" : "Normal"), E("Color", "#172B3A")))), E("Style", E("TextAlign", align)))),
                E("Style", E("PaddingLeft", "3pt"), E("PaddingRight", "3pt"), E("PaddingTop", "2pt"), E("PaddingBottom", "2pt")));
        var header = new List<XElement>(); var details = new List<XElement>();
        for (int index = 0; index < source.Columns.Count; index++)
        {
            bool numeric = source.Columns[index].DataType == typeof(decimal) || source.Columns[index].DataType == typeof(int) || source.Columns[index].DataType == typeof(long);
            var heading = Text("Heading" + index, source.Columns[index].ColumnName, true, numeric ? "Right" : "Left");
            heading.Element(ns + "Style")!.Add(E("BackgroundColor", "#E8F1EE"),
                E("BottomBorder", E("Style", "Solid"), E("Width", "0.75pt"), E("Color", "#1F6F5F")));
            header.Add(E("TablixCell", E("CellContents", heading)));
            var detail = Text("Value" + index, "=Fields!C" + index + ".Value", false, numeric ? "Right" : "Left");
            detail.Descendants(ns + "FontWeight").First().Value = "=IIf(Left(Fields!C0.Value, 5) = \"TOTAL\" Or Left(Fields!C0.Value, 3) = \"NET\", \"Bold\", \"Normal\")";
            detail.Element(ns + "Style")!.Add(E("BottomBorder", E("Style", "Solid"), E("Width", "0.25pt"), E("Color", "#D8E2E7")),
                E("BackgroundColor", "=IIf(RowNumber(Nothing) Mod 2 = 0, \"#F6F9FA\", \"White\")"));
            details.Add(E("TablixCell", E("CellContents", detail)));
        }
        var tablix = E("Tablix", new XAttribute("Name", "ReportRows"),
            E("TablixBody", E("TablixColumns", weights.Select(weight => E("TablixColumn", E("Width", Inch(width * weight / totalWeight))))),
                E("TablixRows", E("TablixRow", E("Height", "0.22in"), E("TablixCells", header)), E("TablixRow", E("Height", "0.2in"), E("TablixCells", details)))),
            E("TablixColumnHierarchy", E("TablixMembers", weights.Select(_ => E("TablixMember")))),
            E("TablixRowHierarchy", E("TablixMembers", E("TablixMember", E("KeepWithGroup", "After"), E("RepeatOnNewPage", true)), E("TablixMember", E("Group", new XAttribute("Name", "DetailRows"))))),
            E("DataSetName", "ReportData"), E("Top", "0in"), E("Left", "0in"), E("Height", "0.42in"), E("Width", Inch(width)), E("Style"));
        XElement Positioned(string name, string value, string top, string height, string size, bool bold = false)
        {
            var text = Text(name, value, bold, "Left", size);
            text.Descendants(ns + "Color").First().Value = name == "Institution" ? "#1F6F5F"
                : name is "Filters" or "Stamp" ? "#58707E" : "#172B3A";
            text.Add(E("Top", top), E("Left", "0in"), E("Height", height), E("Width", Inch(width)));
            return text;
        }
        var document = new XDocument(E("Report",
            E("DataSources", E("DataSource", new XAttribute("Name", "Source"), E("ConnectionProperties", E("DataProvider", "System.Data.DataSet"), E("ConnectString", "")))),
            E("DataSets", E("DataSet", new XAttribute("Name", "ReportData"), E("Query", E("DataSourceName", "Source"), E("CommandText", "")),
                E("Fields", data.Columns.Cast<DataColumn>().Select(column => E("Field", new XAttribute("Name", column.ColumnName), E("DataField", column.ColumnName)))))),
            E("Body", E("ReportItems", tablix), E("Height", "0.42in"), E("Style")), E("Width", Inch(width)),
            E("Page", E("PageHeader", E("Height", "0.95in"), E("PrintOnFirstPage", true), E("PrintOnLastPage", true), E("ReportItems",
                    Positioned("Institution", institution, "0in", "0.24in", "12pt", true),
                    Positioned("ReportTitle", title, "0.25in", "0.24in", "11pt", true),
                    Positioned("Filters", subtitle, "0.5in", "0.4in", "8pt")), E("Style")),
                E("PageFooter", E("Height", "0.3in"), E("PrintOnFirstPage", true), E("PrintOnLastPage", true), E("ReportItems",
                    Positioned("Stamp", "=\"Printed " + DateTime.Now.ToString("dd-MMM-yyyy HH:mm") + " by " + printedBy.Replace("\"", "\"\"") + "  |  Page \" & Globals!PageNumber & \" of \" & Globals!TotalPages", "0in", "0.3in", "7pt")), E("Style")),
                E("PageHeight", landscape ? "8.27in" : "11.69in"), E("PageWidth", landscape ? "11.69in" : "8.27in"),
                E("LeftMargin", "0.5in"), E("RightMargin", "0.5in"), E("TopMargin", "0.4in"), E("BottomMargin", "0.4in"), E("Style")),
            E("ConsumeContainerWhitespace", true)));
        return new(document.ToString(), data);
    }
    internal static void Show(IWin32Window? owner, string institution, string title, string subtitle, DataTable source, string printedBy, string moneyFormat = "#,##0.##")
    {
        var report = Build(institution, title, subtitle, source, printedBy, moneyFormat);
        RdlcReportPreview.Show(owner, title, "", "ReportData", report.Data, reportDefinition: report.Definition);
    }
}
