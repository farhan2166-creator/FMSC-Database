using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FMSC_SMS.Core;

/// <summary>Sizes the receipt paper from its table content, keeping the configured paper width.</summary>
internal static class FeeDocumentLayout
{
    // The complete layout lives in RDLC. Only the roll-paper length follows its content.
    internal static string SizeToContent(string path,DataTable data,bool annualSheet=false) => FitContent(XDocument.Load(path),data,annualSheet);

    private static string FitContent(XDocument document,DataTable data,bool annualSheet)
    {
        XNamespace ns=document.Root!.Name.Namespace;
        var table=document.Descendants(ns+"Tablix").First();
        var columns = table.Descendants(ns + "TablixColumn").Select(column => Inches(column.Element(ns + "Width")!.Value)).ToArray();
        var rows = table.Element(ns + "TablixBody")!.Element(ns + "TablixRows")!.Elements(ns + "TablixRow").ToArray();
        var members = table.Element(ns + "TablixRowHierarchy")!.Element(ns + "TablixMembers")!
            .Element(ns + "TablixMember")!.Element(ns + "TablixMembers")!
            .Descendants(ns + "TablixMember").Where(member=>member.Element(ns+"TablixMembers") is null).ToArray();
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.PageUnit = GraphicsUnit.Pixel;
        double longest = 0;
        foreach (var receipt in data.AsEnumerable().GroupBy(row => row["ReceiptPageKey"]))
        {
            var first = receipt.First();
            double height = 0;
            for (int index = 0; index < rows.Length; index++)
            {
                string hidden = members[index].Element(ns + "Visibility")?.Element(ns + "Hidden")?.Value ?? "";
                if(hidden.Equals("true",StringComparison.OrdinalIgnoreCase)) continue;
                bool detail = members[index].Element(ns + "Group") is not null;
                // Static headings within a student group repeat once per child,
                // whereas its detail member repeats for every actual fee.
                string? headingKey=members[index].Ancestors(ns+"TablixMember")
                    .Select(member=>member.Element(ns+"Group")?.Element(ns+"GroupExpressions")?.Element(ns+"GroupExpression")?.Value)
                    .Where(expression=>expression is not null&&expression!="=Fields!ReceiptPageKey.Value")
                    .Select(expression=>Regex.Match(expression!,@"^=Fields!(\w+)\.Value$").Groups[1].Value)
                    .FirstOrDefault(key=>!string.IsNullOrEmpty(key));
                var records=detail?receipt:headingKey is not null
                    ?receipt.GroupBy(record=>record[headingKey]).Select(group=>group.First()):new[]{first}.AsEnumerable();
                foreach (var record in records)
                {
                    // A child footer's visibility belongs to that child, not
                    // necessarily the first child on the family document.
                    if(hidden.Contains(" Or ") && Regex.Matches(hidden,@"Not First\(Fields!(\w+)\.Value")
                        .Any(match=>!Convert.ToBoolean(record[match.Groups[1].Value]))) continue;
                    var field = Regex.Match(hidden, @"Fields!(\w+)\.Value");
                    if (field.Success && (hidden.Contains("Len(")
                        ? string.IsNullOrWhiteSpace(Convert.ToString(record[field.Groups[1].Value]))
                        : !Convert.ToBoolean(record[field.Groups[1].Value]))) continue;
                    double rowHeight = Inches(rows[index].Element(ns + "Height")!.Value);
                    int columnIndex = 0;
                    foreach (var cell in rows[index].Descendants(ns + "CellContents"))
                    {
                        int span = (int?)cell.Element(ns + "ColSpan") ?? 1;
                        var textBox = cell.Element(ns + "Textbox")!;
                        string expression = textBox.Descendants(ns + "Value").First().Value;
                        string text = expression;
                        if (expression.StartsWith('='))
                        {
                            string key = Regex.Match(expression, @"Fields!(\w+)\.Value").Groups[1].Value;
                            text = expression.StartsWith("=Format(")
                                ? (Convert.ToDecimal(record[key]) * (expression.StartsWith("=Format(-") ? -1 : 1)).ToString(
                                    expression.Contains("\"N2\"") ? "N2" : "N0", CultureInfo.CurrentCulture)
                                : Convert.ToString(record[key]) ?? "";
                            if (expression.StartsWith("=IIf(") && key == "ShowAdjustments")
                                text = Convert.ToBoolean(record[key]) ? "NET SETTLED" : "TOTAL SETTLED";
                            if((string?)textBox.Attribute("Name")=="TotalPaid" && key=="ShowSettlement")
                                text=Convert.ToDecimal(record[Convert.ToBoolean(record["ShowSettlement"])?"TotalPaid":"NetPaid"]).ToString("N0");
                            if((string?)textBox.Attribute("Name")=="TotalLabel" && key=="ShowSettlement")
                                text=Convert.ToBoolean(record["ShowSettlement"])?"TOTAL SETTLED":Convert.ToBoolean(record["ShowAdjustments"])?"NET SETTLED":"TOTAL SETTLED";
                            if ((string?)textBox.Attribute("Name") == "AdmissionNumberTextLabel")
                                text = (Convert.ToString(record["AdmissionNumberText"]) ?? "").Contains("QR-",StringComparison.OrdinalIgnoreCase)
                                    ? "Registration #" : "Admission #";
                            if ((string?)textBox.Attribute("Name") == "ReceiptHeading")
                                text += " (" + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Convert.ToString(record["CopyLabel"])!.ToLowerInvariant()) + ")";
                            if ((string?)textBox.Attribute("Name") == "CampusHeading") text = "(" + text + ")";
                        }
                        float points = float.Parse(textBox.Descendants(ns + "FontSize").First().Value.Replace("pt", ""), CultureInfo.InvariantCulture);
                        bool bold=textBox.Descendants(ns + "FontWeight").Any(weight => weight.Value == "Bold")
                            || (data.Columns.Contains("FamilyRowKind")&&(string?)textBox.Attribute("Name")=="FeeDescription"&&Convert.ToString(record["FamilyRowKind"])!="Fee");
                        using var font = new Font("Segoe UI", points, bold ? FontStyle.Bold : FontStyle.Regular);
                        var style = textBox.Element(ns + "Style");
                        double PaddingPoints(string name) => double.Parse(
                            (style?.Element(ns + name)?.Value ?? "0pt").Replace("pt", ""),CultureInfo.InvariantCulture);
                        double horizontalPadding=PaddingPoints("PaddingLeft")+PaddingPoints("PaddingRight");
                        double verticalPadding=PaddingPoints("PaddingTop")+PaddingPoints("PaddingBottom");
                        float width = (float)(columns.Skip(columnIndex).Take(span).Sum() * graphics.DpiX - horizontalPadding * graphics.DpiX / 72);
                        var size = graphics.MeasureString(text, font, new SizeF(Math.Max(1, width), 100000), StringFormat.GenericTypographic);
                        rowHeight = Math.Max(rowHeight, size.Height / graphics.DpiY + verticalPadding / 72);
                        columnIndex += span;
                    }
                    height += rowHeight;
                }
            }
            longest = Math.Max(longest, height);
        }
        var page = document.Root.Element(ns + "Page")!;
        double margins = Inches(page.Element(ns + "TopMargin")!.Value) + Inches(page.Element(ns + "BottomMargin")!.Value);
        double paperHeight = Math.Max(1, Math.Ceiling((longest + margins + 0.15) * 10) / 10);
        page.Element(ns + "PageHeight")!.Value = paperHeight.ToString("0.0", CultureInfo.InvariantCulture) + "in";
        document.Root.Element(ns + "Body")!.Element(ns + "Height")!.Value = "0.01in";
        if (annualSheet)
        {
            var footer = table.Descendants(ns + "Textbox").Last();
            footer.Element(ns + "Style")!.Element(ns + "PaddingBottom")!.Value = "8pt";
            FeeDocumentSheetLayout.Apply(document);
        }
        return document.ToString();
    }

    private static double Inches(string value) => double.Parse(value.Replace("in", ""), CultureInfo.InvariantCulture);
}
