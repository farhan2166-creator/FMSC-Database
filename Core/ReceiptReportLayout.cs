using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FMSC_SMS.Core;

/// <summary>Sizes the receipt paper from its table content, keeping the configured paper width.</summary>
internal static class ReceiptReportLayout
{
    internal static string Definition(string path, DataTable data, bool annualSheet = false, bool splitFeePeriod = true)
    {
        var document = XDocument.Load(path);
        XNamespace ns = document.Root!.Name.Namespace;
        var table = document.Descendants(ns + "Tablix").First();
        if(data.Columns.Contains("FamilyRowKind")){
            splitFeePeriod=false;
            var fields=document.Descendants(ns+"DataSet").First().Element(ns+"Fields")!;
            foreach(string key in new[]{"FamilyRowKind","FamilyAmountText"})
                fields.Add(new XElement(ns+"Field",new XAttribute("Name",key),new XElement(ns+"DataField",key)));
            var description=table.Descendants(ns+"Textbox").Single(box=>(string?)box.Attribute("Name")=="FeeDescription");
            description.Descendants(ns+"TextRun").First().Element(ns+"Style")!.Add(new XElement(ns+"FontWeight","=IIf(Fields!FamilyRowKind.Value = \"Fee\", \"Normal\", \"Bold\")"));
            table.Descendants(ns+"Textbox").Single(box=>(string?)box.Attribute("Name")=="LineAmount").Descendants(ns+"Value").First().Value="=Fields!FamilyAmountText.Value";
            table.Descendants(ns+"Textbox").Single(box=>(string?)box.Attribute("Name")=="FeeHeader").Descendants(ns+"Value").First().Value="Student / Month / Fee";
            table.Descendants(ns+"Textbox").Single(box=>(string?)box.Attribute("Name")=="AmountHeader").Descendants(ns+"Value").First().Value="Due (Rs.)";
            foreach(var value in table.Descendants(ns+"Value").Where(value=>value.Value=="TOTAL PAID"))value.Value="TOTAL DUE";
        }
        foreach (var label in document.Descendants(ns + "Textbox").Where(box => (string?)box.Attribute("Name") == "AdmissionNumberTextLabel"))
            label.Descendants(ns + "Value").First().Value = "=IIf(InStr(CStr(First(Fields!AdmissionNumberText.Value, \"ReceiptPageGroup\")), \"QR-\") > 0, \"Registration #\", \"Admission #\")";
        // Split only the fee header and detail cells; all other receipt rows keep their spans.
        foreach (string name in splitFeePeriod ? new[] { "FeeHeader", "FeeDescription" } : Array.Empty<string>())
        {
            var fee = table.Descendants(ns + "Textbox").Single(box => (string?)box.Attribute("Name") == name);
            var contents = fee.Parent!;
            var cell = contents.Parent!;
            contents.Element(ns + "ColSpan")!.Remove();
            var period = new XElement(fee);
            period.SetAttributeValue("Name", name == "FeeHeader" ? "PeriodHeader" : "FeePeriod");
            period.Descendants(ns + "Value").First().Value = name == "FeeHeader" ? "Month" : "=Fields!FeePeriod.Value";
            fee.Descendants(ns + "Value").First().Value = name == "FeeHeader" ? "Fee" : "=Fields!FeeDescription.Value";
            contents.ReplaceNodes(period);
            cell.ElementsAfterSelf(ns + "TablixCell").First().Add(new XElement(ns + "CellContents", fee));
        }
        if (splitFeePeriod) document.Descendants(ns + "DataSet").First().Element(ns + "Fields")!.Add(
            new XElement(ns + "Field", new XAttribute("Name", "FeePeriod"),
                new XElement(ns + "DataField", "FeePeriod"),
                new XElement(XName.Get("TypeName", "http://schemas.microsoft.com/SQLServer/reporting/reportdesigner"), "System.String")));
        if (Path.GetFileName(path).Equals("FeePaymentReceipt.rdlc",StringComparison.OrdinalIgnoreCase))
        {
            var total = table.Descendants(ns + "Textbox").Single(box => (string?)box.Attribute("Name") == "TotalPaid");
            var totalRow = total.Ancestors(ns + "TablixRow").First();
            int totalIndex = totalRow.ElementsBeforeSelf(ns + "TablixRow").Count();
            var rowMembers = table.Element(ns + "TablixRowHierarchy")!.Element(ns + "TablixMembers")!
                .Element(ns + "TablixMember")!.Element(ns + "TablixMembers")!;
            var totalMember = rowMembers.Elements(ns + "TablixMember").ElementAt(totalIndex);
            var fields = document.Descendants(ns + "DataSet").First().Element(ns + "Fields")!;
            foreach (var item in new[] { ("OriginalPaid","Originally Paid"), ("RefundAmount","Refund"),
                ("CorrectionAmount","Correction"), ("ReversedAmount","Reversed") })
            {
                fields.Add(new XElement(ns + "Field",new XAttribute("Name",item.Item1),new XElement(ns + "DataField",item.Item1)));
                var summaryRow = new XElement(totalRow);
                foreach (var box in summaryRow.Descendants(ns + "Textbox"))
                {
                    bool amount = (string?)box.Attribute("Name") == "TotalPaid";
                    box.SetAttributeValue("Name",(string?)box.Attribute("Name") + item.Item1);
                    box.Descendants(ns + "Value").First().Value = amount
                        ? $"=Format({(item.Item1 == "OriginalPaid" ? "" : "-")}First(Fields!{item.Item1}.Value, \"ReceiptPageGroup\"), \"N0\")"
                        : item.Item2;
                    foreach (var weight in box.Descendants(ns + "FontWeight")) weight.Value = "Normal";
                }
                totalRow.AddBeforeSelf(summaryRow);
                totalMember.AddBeforeSelf(new XElement(ns + "TablixMember",
                    new XElement(ns + "Visibility",new XElement(ns + "Hidden",item.Item1 == "OriginalPaid"
                        ? "=Not First(Fields!ShowAdjustments.Value, \"ReceiptPageGroup\")"
                        : $"=Not CBool(First(Fields!{item.Item1}.Value, \"ReceiptPageGroup\"))"))));
            }
            foreach (string fieldName in new[] { "NetPaid", "ShowAdjustments" })
                fields.Add(new XElement(ns + "Field",new XAttribute("Name",fieldName),new XElement(ns + "DataField",fieldName)));
            total.Descendants(ns + "Value").First().Value = "=Format(First(Fields!NetPaid.Value, \"ReceiptPageGroup\"), \"N0\")";
            totalRow.Descendants(ns + "Value").First(value => value.Value == "TOTAL PAID").Value =
                "=IIf(First(Fields!ShowAdjustments.Value, \"ReceiptPageGroup\"), \"NET PAID\", \"TOTAL PAID\")";
        }
        return FitContent(document,data,annualSheet);
    }

    // The complete layout lives in RDLC. Only the roll-paper length follows its content.
    internal static string SizeToContent(string path,DataTable data) => FitContent(XDocument.Load(path),data,false);

    private static string FitContent(XDocument document,DataTable data,bool annualSheet)
    {
        XNamespace ns=document.Root!.Name.Namespace;
        var table=document.Descendants(ns+"Tablix").First();
        var columns = table.Descendants(ns + "TablixColumn").Select(column => Inches(column.Element(ns + "Width")!.Value)).ToArray();
        var rows = table.Element(ns + "TablixBody")!.Element(ns + "TablixRows")!.Elements(ns + "TablixRow").ToArray();
        var members = table.Element(ns + "TablixRowHierarchy")!.Element(ns + "TablixMembers")!
            .Element(ns + "TablixMember")!.Element(ns + "TablixMembers")!.Elements(ns + "TablixMember").ToArray();
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
                var field = Regex.Match(hidden, @"Fields!(\w+)\.Value");
                if (field.Success && (hidden.Contains("Len(")
                    ? string.IsNullOrWhiteSpace(Convert.ToString(first[field.Groups[1].Value]))
                    : !Convert.ToBoolean(first[field.Groups[1].Value]))) continue;
                bool detail = members[index].Element(ns + "Group") is not null;
                foreach (var record in detail ? receipt : new[] { first }.AsEnumerable())
                {
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
                                text = Convert.ToBoolean(record[key]) ? "NET PAID" : "TOTAL PAID";
                            if((string?)textBox.Attribute("Name")=="TotalPaid" && key=="ShowSettlement")
                                text=Convert.ToDecimal(record[Convert.ToBoolean(record["ShowSettlement"])?"TotalPaid":"NetPaid"]).ToString("N0");
                            if((string?)textBox.Attribute("Name")=="TotalLabel" && key=="ShowSettlement")
                                text=Convert.ToBoolean(record["ShowSettlement"])?"TOTAL SETTLED":Convert.ToBoolean(record["ShowAdjustments"])?"NET PAID":"TOTAL PAID";
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
            AnnualReceiptReportLayout.Apply(document);
        }
        return document.ToString();
    }

    private static double Inches(string value) => double.Parse(value.Replace("in", ""), CultureInfo.InvariantCulture);
}
