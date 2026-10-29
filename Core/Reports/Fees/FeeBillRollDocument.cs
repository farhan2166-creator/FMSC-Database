using System.Drawing.Printing;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Reporting.WinForms;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FMSC_SMS.Core;

// RDLC has one paper size per render. Render each numbered thermal bill separately
// so preview, export and printer all retain that bill's own content height.
internal sealed class FeeBillRollDocument : IDisposable
{
    internal sealed record Page(MemoryStream Image,byte[] Pdf,PageSettings Settings);
    internal List<Page> Pages { get; }=new();
    internal static FeeBillRollDocument Render(string path,string dataSetName,DataTable display)
    {
        var result=new FeeBillRollDocument();
        try
        {
            foreach(var group in display.AsEnumerable().GroupBy(r=>r["ReceiptPageKey"]))
            {
                using var rows=display.Clone();foreach(var row in group)rows.ImportRow(row);
                using var data=RecordTextPolicy.PrintCopy(rows);
                string definition=FeeDocumentLayout.SizeToContent(path,data);
                using var report=new LocalReport();report.LoadReportDefinition(new StringReader(definition));
                report.DataSources.Add(new ReportDataSource(dataSetName,data));
                var xml=XDocument.Parse(definition);XNamespace ns=xml.Root!.Name.Namespace;
                var page=xml.Root.Element(ns+"Page")!;
                double Inches(string name)=>double.Parse(page.Element(ns+name)!.Value.Replace("in",""),CultureInfo.InvariantCulture);
                double estimated=Inches("PageHeight"),width=Inches("PageWidth");
                string Device(double height,bool image=false)=>new XElement("DeviceInfo",
                    new XElement("PageHeight",height.ToString("0.00",CultureInfo.InvariantCulture)+"in"),
                    image?new XElement("OutputFormat","EMF"):null,
                    image?new XElement("StartPage",0):null,image?new XElement("EndPage",0):null).ToString();
                int PdfPages(double height)
                {
                    using var stream=new MemoryStream(report.Render("PDF",Device(height)));
                    using var pdf=PdfReader.Open(stream,PdfDocumentOpenMode.Import);return pdf.PageCount;
                }
                List<MemoryStream> Images(double height)
                {
                    var images=new List<MemoryStream>();
                    try
                    {
                        report.Render("IMAGE",Device(height,true),(name,extension,encoding,mime,seek)=>{
                            var stream=new MemoryStream();images.Add(stream);return stream;
                        },out _);
                        foreach(var stream in images)stream.Position=0;return images;
                    }
                    catch{foreach(var stream in images)stream.Dispose();throw;}
                }
                // Ask the real renderer rather than trusting GDI's estimate. Find
                // the last line's required space, then leave a small clearance.
                double low=Math.Max(0.5,Inches("TopMargin")+Inches("BottomMargin")+0.1),high=Math.Max(low,estimated);
                for(int attempt=0;PdfPages(high)>1;attempt++)
                {
                    if(attempt==8)throw new InvalidOperationException("The complete bill could not fit on continuous paper.");
                    low=high;high=Math.Ceiling(high*1.5*100)/100;
                }
                while(high-low>0.08)
                {
                    double middle=Math.Floor((low+high)*50)/100;
                    if(PdfPages(middle)==1)high=middle;else low=middle;
                }
                double fitted=Math.Ceiling((high+0.08)*100)/100;
                List<MemoryStream>? printed=null;
                try
                {
                    // PDF and GDI can wrap differently. Never allow a totals-only
                    // continuation page to become a second receipt or be discarded.
                    for(int attempt=0;attempt<10;attempt++)
                    {
                        printed=Images(fitted);
                        if(printed.Count==1&&PdfPages(fitted)==1)break;
                        foreach(var stream in printed)stream.Dispose();printed=null;
                        fitted=Math.Ceiling((fitted+Math.Max(0.1,fitted*0.03))*100)/100;
                    }
                    if(printed is null||printed.Count!=1)throw new InvalidOperationException("The complete bill could not be rendered on one slip.");
                    var settings=new PageSettings {
                        PaperSize=new PaperSize("Bill",(int)Math.Round(width*100),(int)Math.Round(fitted*100)),
                        Margins=new Margins((int)Math.Round(Inches("LeftMargin")*100),(int)Math.Round(Inches("RightMargin")*100),
                            (int)Math.Round(Inches("TopMargin")*100),(int)Math.Round(Inches("BottomMargin")*100)),Landscape=false
                    };
                    result.Pages.Add(new Page(printed[0],report.Render("PDF",Device(fitted)),settings));printed=null;
                }
                finally{if(printed is not null)foreach(var stream in printed)stream.Dispose();}
            }
            return result;
        }
        catch{result.Dispose();throw;}
    }

    internal byte[] ExportPdf()
    {
        using var combined=new PdfDocument();
        foreach(var page in Pages)
        {
            using var stream=new MemoryStream(page.Pdf);using var source=PdfReader.Open(stream,PdfDocumentOpenMode.Import);
            foreach(var sheet in source.Pages)combined.AddPage(sheet);
        }
        using var output=new MemoryStream();combined.Save(output,false);return output.ToArray();
    }
    internal RdlcPrintDocument CreatePrintDocument(string title)=>new(Pages.Select(p=>new MemoryStream(p.Image.ToArray())).ToList(),
        Pages.Select(p=>p.Settings).ToArray()){DocumentName=title,DefaultPageSettings=(PageSettings)Pages[0].Settings.Clone(),OriginAtMargins=false};
    public void Dispose(){foreach(var page in Pages)page.Image.Dispose();Pages.Clear();}
}
