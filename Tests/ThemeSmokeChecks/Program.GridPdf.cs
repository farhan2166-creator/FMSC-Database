using System.Data;
using System.Xml.Linq;
using FMSC_SMS.Core;
using Microsoft.Reporting.WinForms;

internal static partial class Program
{
    private static void CheckGridPdfReports()
    {
        var oversizedIdentity=new[]{new GridReportColumns.Column("Student",new[]{0},false,12,1)};
        Check(GridReportColumns.Bands(oversizedIdentity,10).Single().SequenceEqual(new[]{0}),"An oversized identity column remains present in the report");
        string folder=Path.GetFullPath("artifacts/grid-pdf");Directory.CreateDirectory(folder);
        XNamespace ns="http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";
        int Render(string name,string title,string subtitle,DataTable source)
        {
            string before=source.GetXmlForTest();
            var built=GridReportPreview.Build("Fictional Meadow School & College",title,subtitle,source,"Example Operator");
            Check(source.GetXmlForTest()==before,"Printing must not modify source rows, values or types");
            var columns=GridReportColumns.Create(source,"#,##0.##");
            Check(columns.SelectMany(c=>c.Fields).OrderBy(i=>i).SequenceEqual(Enumerable.Range(0,source.Columns.Count)),"Every selected source field appears exactly once in the print projection");
            Check(built.Data.Rows.Count==source.Rows.Count,"Print layout preserves the number of financial records");
            for(int row=0;row<source.Rows.Count;row++)
                for(int column=0;column<source.Columns.Count;column++)
                {
                    string value=GridReportColumns.Value(source.Rows[row],column,"#,##0.##");
                    if(value.Length>0)Check(built.Data.Rows[row].ItemArray.Any(cell=>Convert.ToString(cell)!.Contains(value)),"Printed data preserves each source value");
                }
            var definition=XDocument.Parse(built.Definition);
            Check(definition.Descendants(ns+"FontSize").Any(element=>element.Value=="9pt"),"Table text remains readable");
            foreach(var table in definition.Descendants(ns+"Tablix"))
            {
                double width=table.Element(ns+"TablixBody")!.Element(ns+"TablixColumns")!.Elements().Sum(c=>double.Parse(c.Element(ns+"Width")!.Value.Replace("in",""),System.Globalization.CultureInfo.InvariantCulture));
                double body=double.Parse(definition.Root!.Element(ns+"Width")!.Value.Replace("in",""),System.Globalization.CultureInfo.InvariantCulture);
                Check(width<=body+.001,"Printed columns stay inside the printable page");
                Check(table.Descendants(ns+"RepeatOnNewPage").Any(e=>e.Value=="true"),"Every section repeats its table headings");
            }
            using var report=new LocalReport();report.LoadReportDefinition(new StringReader(built.Definition));report.DataSources.Add(new ReportDataSource("ReportData",built.Data));
            byte[] pdf=report.Render("PDF");File.WriteAllBytes(Path.Combine(folder,name+".pdf"),pdf);
            File.WriteAllText(Path.Combine(folder,name+".rdlc"),built.Definition);
            using var parsed=PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdf),PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            Check(parsed.PageCount>0&&pdf.Length>1000,"The shared RDLC generates a valid PDF");
            built.Data.Dispose();return parsed.PageCount;
        }
        using var receipts=new DataTable();
        foreach(string name in new[]{"Receipt / Reference","Date","Scope","Student / Family","Campus","Class / Section","Payment Method","Money Received","Refunds","Corrections","Credit Applied","Adjusted Collection","Received By"})
            receipts.Columns.Add(name,name=="Date"?typeof(DateTime):new[]{"Money Received","Refunds","Corrections","Credit Applied","Adjusted Collection"}.Contains(name)?typeof(decimal):typeof(string));
        for(int index=0;index<10;index++)
        {
            decimal money=index switch{0=>2800m,1=>1600m,2 or 3=>200m,7=>10000m,_=>0m};
            decimal refund=index==5?1200m:0m,correction=index==4?1300m:index==6?150m:0m,credit=index==8?2250m:index==9?8700m:0m;
            receipts.Rows.Add(index==8?"Credit #19":$"FMSC-2026-{index+9:D4}",new DateTime(2026,10,index==9?19:18),index%3==0?"Family":"Individual",
                index%3==0?"Example Student, Example Sibling":"Example Student","Campus Boys","Play Group - F",
                credit>0?"Account Credit":refund>0?"Refund":correction>0?"Correction":"Cash",money,refund,correction,credit,money-refund-correction,"operator");
        }
        string totals="View: Receipts   Period: Any start to Any end   Campus: Campus Boys   Class: All   Section: All   Receipts: 10     Money Received: Rs. 14,800     Refunds: Rs. 1,200     Corrections: Rs. 1,450     Credit Applied: Rs. 10,950     Adjusted Collection: Rs. 12,150";
        Check(Render("collections-receipts","Collections & Receipts",totals,receipts)==1,"Ten ordinary receipts fit one readable A4 landscape page");
        var receiptColumns=GridReportColumns.Create(receipts,"#,##0.##");
        Check(receiptColumns.Count==9,"Receipt identity fields share cells instead of squeezing thirteen columns");
        Check(receiptColumns.Single(c=>c.Caption=="Receipt / Date").Minimum>=1.3,"Receipt references and dates have a readable minimum width");
        using var outstanding=new DataTable();
        foreach(string name in new[]{"Adm. / QR No.","Student","Campus","Class / Section","Open Charges (Net)","Settled on Open Charges","Outstanding","Oldest Due Date","Aging"})
            outstanding.Columns.Add(name,name=="Oldest Due Date"?typeof(DateTime):new[]{"Open Charges (Net)","Settled on Open Charges","Outstanding"}.Contains(name)?typeof(decimal):typeof(string));
        outstanding.Rows.Add("101","Example Student","Campus Boys","Play Group - F",6900m,0m,6900m,new DateTime(2026,10,19),"Due Today");
        outstanding.Rows.Add("QR-102","Example Sibling","Campus Girls","Play Group - F",6900m,0m,6900m,new DateTime(2026,10,19),"Due Today");
        outstanding.Rows.Add("103","Another Student","Campus Girls","Class 10 - A",2500m,1700m,800m,new DateTime(2027,1,1),"Not Yet Due");
        Check(Render("outstanding-aging","Outstanding & Aging","View: Students   Campus: All   Students Owing: 3     Total Outstanding: Rs. 14,600     Overdue: Rs. 0     90+ Days: Rs. 0",outstanding)==1,"Outstanding report fits one clear page");
        using var wide=new DataTable();wide.Columns.Add("Admission Number");wide.Columns.Add("Student");
        for(int column=0;column<16;column++)wide.Columns.Add("Detailed Amount "+column,typeof(decimal));
        for(int row=0;row<36;row++)wide.Rows.Add(new object[]{(row+1).ToString(),"Example student "+(row+1)}.Concat(Enumerable.Range(0,16).Select(column=>(object)(1234567.89m+row+column))).ToArray());
        var plan=GridReportColumns.Create(wide,"#,##0.##");
        Check(GridReportColumns.Bands(plan,10.67).Count>1,"Extra-wide reports split into identified sections instead of shrinking to tiny text");
        Check(Render("wide-multipage","Detailed Financial Report","Campus: All   Records: 36",wide)>1,"Wide reports paginate without overflowing the sheet");
    }
}

internal static class GridPdfFixtureXml
{
    internal static string GetXmlForTest(this DataTable table)
    {using var copy=table.Copy();copy.TableName="Fixture";using var text=new StringWriter();copy.WriteXml(text,XmlWriteMode.WriteSchema);return text.ToString();}
}
