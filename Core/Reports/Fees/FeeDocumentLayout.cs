using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FMSC_SMS.Core;

/// <summary>Sizes the receipt paper from its table content, keeping the configured paper width.</summary>
internal static class FeeDocumentLayout
{
    // The complete layout lives in RDLC. Only the roll-paper length follows its content.
    internal static string SizeToContent(string path,DataTable data,bool annualSheet=false)
    {
        string definition=FitContent(XDocument.Load(path),data,annualSheet);
        if(!annualSheet && Path.GetFileName(path).Equals(FamilyReceiptReport.TemplateFile,StringComparison.OrdinalIgnoreCase)
            && data.AsEnumerable().Select(row=>row["ReceiptPageKey"]).Distinct().Count()==1)
        {
            using var printData=RecordTextPolicy.PrintCopy(data);
            return FitSinglePage(definition,printData,FamilyReceiptReport.DataSetName);
        }
        return definition;
    }

    internal static string FitSinglePage(string definition,DataTable data,string dataSetName)
    {
        var document=XDocument.Parse(definition);XNamespace ns=document.Root!.Name.Namespace;
        var pageHeight=document.Root.Element(ns+"Page")!.Element(ns+"PageHeight")!;
        double estimated=double.Parse(pageHeight.Value.Replace("in",""),CultureInfo.InvariantCulture);
        using var report=new Microsoft.Reporting.WinForms.LocalReport();report.LoadReportDefinition(new StringReader(definition));
        report.DataSources.Add(new Microsoft.Reporting.WinForms.ReportDataSource(dataSetName,data));
        int Pages(double height)
        {
            var deviceInfo=new XElement("DeviceInfo",new XElement("PageHeight",height.ToString("0.00",CultureInfo.InvariantCulture)+"in"));
            using var stream=new MemoryStream(report.Render("PDF",deviceInfo.ToString()));
            using var pdf=PdfSharp.Pdf.IO.PdfReader.Open(stream,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            deviceInfo.Add(new XElement("OutputFormat","EMF"),new XElement("StartPage",0),new XElement("EndPage",0));
            var printedPages=new List<MemoryStream>();
            try
            {
                report.Render("IMAGE",deviceInfo.ToString(),(name,extension,encoding,mimeType,willSeek)=>{
                    var page=new MemoryStream();printedPages.Add(page);return page;
                },out _);
                if(printedPages.Count==0)throw new InvalidOperationException("The fee document did not produce a printable page.");
                return Math.Max(pdf.PageCount,printedPages.Count);
            }
            finally{foreach(var page in printedPages)page.Dispose();}
        }
        int pages=Pages(estimated);
        if(pages==1)return definition;

        // GDI text measurements can differ from RDLC's wrapped rows. Verify the
        // complete rendered document, then find its paper length without shrinking text.
        double tooShort=estimated,enough=estimated;
        for(int attempt=0;pages>1&&attempt<8;attempt++)
        {
            tooShort=enough;enough=Math.Ceiling((enough*Math.Max(2,pages)+0.1)*100)/100;
            pages=Pages(enough);
        }
        if(pages!=1)throw new InvalidOperationException("The complete fee document could not fit on continuous paper. Close and try printing again.");
        while(enough-tooShort>0.10)
        {
            double middle=Math.Floor((tooShort+enough)*50)/100;
            if(Pages(middle)==1)enough=middle;else tooShort=middle;
        }
        // Keep a little clearance for the printer's hundredth-inch page settings.
        pageHeight.Value=(enough+0.05).ToString("0.00",CultureInfo.InvariantCulture)+"in";
        return document.ToString();
    }



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
                    // A whole child slip may be hidden while its group summary
                    // remains visible on the same continuous paper.
                    if(members[index].Ancestors(ns+"TablixMember").Select(member=>member.Element(ns+"Visibility")?.Element(ns+"Hidden")?.Value)
                        .Any(expression=>expression is not null&&expression.Contains("Fields!ShowChildDocument.Value")
                            &&!Convert.ToBoolean(record["ShowChildDocument"])))continue;
                    // A child footer's visibility belongs to that child, not
                    // necessarily the first child on the family document.
                    if(hidden.Contains(" Or ") && Regex.Matches(hidden,@"Not First\(Fields!(\w+)\.Value")
                        .Any(match=>!Convert.ToBoolean(record[match.Groups[1].Value]))) continue;
                    var field = Regex.Match(hidden, @"Fields!(\w+)\.Value");
                    bool combinedLocation = hidden.Contains("Fields!SchoolAddress.Value") && hidden.Contains("Fields!CampusName.Value");
                    if (combinedLocation && string.IsNullOrWhiteSpace(Convert.ToString(record["SchoolAddress"]))
                        && string.IsNullOrWhiteSpace(Convert.ToString(record["CampusName"]))) continue;
                    if (!combinedLocation && field.Success && (hidden.Contains("Len(")
                        ? string.IsNullOrWhiteSpace(Convert.ToString(record[field.Groups[1].Value]))
                        : hidden.StartsWith("=First(",StringComparison.Ordinal)
                            ? Convert.ToBoolean(record[field.Groups[1].Value])
                            : !Convert.ToBoolean(record[field.Groups[1].Value]))) continue;
                    double rowHeight = Inches(rows[index].Element(ns + "Height")!.Value);
                    int columnIndex = 0;
                    foreach (var cell in rows[index].Descendants(ns + "CellContents"))
                    {
                        int span = (int?)cell.Element(ns + "ColSpan") ?? 1;
                        var textBox = cell.Element(ns + "Textbox")!;
                        // Captions live in literal RDLC text runs. Measure every
                        // run so editing a label also updates the required paper length.
                        string text=string.Concat(textBox.Descendants(ns+"TextRun").Select(run=>
                            MeasureValue(run.Element(ns+"Value")!.Value,record,(string?)textBox.Attribute("Name"))));
                        float points = float.Parse(textBox.Descendants(ns + "FontSize").First().Value.Replace("pt", ""), CultureInfo.InvariantCulture);
                        bool bold=textBox.Descendants(ns + "FontWeight").Any(weight => weight.Value == "Bold")
                            || (data.Columns.Contains("ChangeStrong")&&(string?)textBox.Attribute("Name") is "ChangeLabel" or "ChangeAmount"
                                &&Convert.ToBoolean(record["ChangeStrong"]))
                            || (data.Columns.Contains("FamilyRowKind")&&(string?)textBox.Attribute("Name")=="FeeDescription"&&Convert.ToString(record["FamilyRowKind"])!="Fee");
                        FontStyle fontStyle = bold ? FontStyle.Bold : FontStyle.Regular;
                        if (textBox.Descendants(ns + "FontStyle").Any(style => style.Value == "Italic"))
                            fontStyle |= FontStyle.Italic;
                        using var font = new Font("Segoe UI", points, fontStyle);
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

    private static string MeasureValue(string expression,DataRow record,string? name)
    {
        if(!expression.StartsWith('='))return expression;
        if(name is "ReceiptHeading" or "ThankYou" && expression.Contains("\"Fee changes\"",StringComparison.Ordinal))
        {
            bool acknowledgement=Convert.ToString(record["PaymentMethod"])=="Fee changes";
            return name=="ReceiptHeading"
                ?acknowledgement?"FEE CHANGE ACKNOWLEDGEMENT":"STUDENT PAYMENT RECEIPT"
                :acknowledgement?"Fee changes recorded; no payment received":"Thank you for your payment";
        }
        if(expression.Contains("Now()",StringComparison.Ordinal))
            return DateTime.Now.ToString("dd-MM-yyyy hh:mm tt",CultureInfo.CurrentCulture);
        string key=Regex.Match(expression,@"Fields!(\w+)\.Value").Groups[1].Value;
        if(string.IsNullOrEmpty(key))throw new InvalidOperationException("Unsupported fee-document measurement expression: "+name);
        if(name=="TotalPaid" && key.EndsWith("ShowSettlement",StringComparison.Ordinal))
        {
            string prefix=key=="ShowSettlement"?"":"Child";
            return Convert.ToDecimal(record[prefix+(Convert.ToBoolean(record[key])?"TotalPaid":"NetPaid")]).ToString("N0");
        }
        if(expression.StartsWith("=Format(",StringComparison.Ordinal))
            return (Convert.ToDecimal(record[key])*(expression.StartsWith("=Format(-",StringComparison.Ordinal)?-1:1))
                .ToString(expression.Contains("\"N2\"")?"N2":"N0",CultureInfo.CurrentCulture);
        string text=Convert.ToString(record[key])??"";
        if(name=="ReceiptNumber" && expression.Contains("Fields!PaymentMethod.Value",StringComparison.Ordinal))
        {
            string method=(Convert.ToString(record["PaymentMethod"])??"").Trim();
            text+=method.Length==0?"":" ("+method+")";
        }
        if(name=="SchoolAddress")
        {
            string campus=Convert.ToString(record["CampusName"])??"";
            text=(text+(string.IsNullOrWhiteSpace(campus)?"":" ("+campus+")")).Trim();
        }
        return text;
    }
}
