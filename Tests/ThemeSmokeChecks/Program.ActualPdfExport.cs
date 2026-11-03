using System.Data;
using System.Reflection;
using System.Xml.Linq;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckActualPdfExportContext()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        string folder=Path.GetFullPath("artifacts/actual-export-button");Directory.CreateDirectory(folder);
        using var receipts=new DataTable();
        foreach(string name in new[]{"Receipt / Reference","Scope","Student / Family","Campus","Class / Section","Payment Method","Received By"})receipts.Columns.Add(name);
        receipts.Columns.Add("Date",typeof(DateTime));
        foreach(string name in new[]{"Money Received","Refunds","Corrections","Credit Applied","Adjusted Collection"})receipts.Columns.Add(name,typeof(decimal));
        var numbers=new[]{"R-2026-00001","FR-2026-00002","R-2026-00003"};
        for(int index=0;index<numbers.Length;index++)receipts.Rows.Add(numbers[index],"Individual","Hamid s/o Ali",index==2?"Campus Girls":"Campus Boys","One – A","Cash","Fixture",new DateTime(2026,11,3),100m,0m,0m,0m,100m);
        var sample=ReceiptExportReview(receipts,"Campus",(page,projection)=>{
            // This is the method called by the actual Export button after its file picker and identity checks.
            var document=page.BuildPdfExportDocument(projection,"Fictional Meadow School & College","Fixture");
            using var printed=document.Data;
            Check(document.Title=="Collections & Receipts — Receipts","The real export handler uses the report page's current title and view");
            var definition=XDocument.Parse(document.Definition);
            Check(definition.Descendants().Where(element=>element.Name.LocalName=="Value")
                .Any(element=>element.Value.Replace('\n',' ')=="Payment Received"),"The real export handler keeps the page's payment caption");
            foreach(string number in numbers)
                Check(printed.AsEnumerable().Any(row=>row.ItemArray.OfType<string>().Any(value=>value.Contains(number,StringComparison.Ordinal))),
                    "The real export handler preserves the exact displayed receipt number "+number);
            string path=Path.Combine(folder,"collections-receipts.pdf");
            typeof(V2Page).GetMethod("WritePdfExport",flags)!.Invoke(page,new object[]{path,document});
            var pdf=ReportPageSearchIndex.FromPdf(File.ReadAllBytes(path));
            foreach(string number in numbers)Check(pdf.FindPages(number).Length>0,"The file writer used by Export prints the original reference "+number);
        });
        sample.Rows.Dispose();

        using var students=new DataTable();students.Columns.Add("AdmissionNumber");students.Columns.Add("StudentName");students.Columns.Add("Campus");
        students.Rows.Add("00163","Hamid","Campus Boys");students.Rows.Add("00164","Kausar","Campus Boys");
        using var scoped=new PdfContextFixturePage("Campus Boys") {BindingContext=new BindingContext()};scoped.DataGrid.DataSource=students;
        using var raw=ReportExportBuilder.Build(students,scoped.DataGrid.Columns.Cast<DataGridViewColumn>(),Array.Empty<string>());
        var result=scoped.BuildPdfExportDocument(raw,"Fictional Meadow School & College","Fixture");
        using(result.Data)
        {
            XNamespace ns="http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";
            var xml=XDocument.Parse(result.Definition);
            Check(!xml.Descendants(ns+"Textbox").Where(box=>((string?)box.Attribute("Name"))?.StartsWith("Heading")==true)
                .SelectMany(box=>box.Descendants(ns+"Value")).Any(value=>value.Value=="Campus"),"A direct student-report PDF omits a campus column already supplied in its heading");
            Check(xml.Descendants(ns+"Value").Any(value=>value.Value.Contains("Campus: Campus Boys")),"The omitted campus remains explicit in the actual exported document");
            Check(raw.Columns.Contains("Campus")&&raw.Rows[0].Field<string>("AdmissionNumber")=="00163", "Raw CSV/Excel projections keep campus and leading-zero references");
        }
        using var legacy=raw.Copy();legacy.Rows[0]["AdmissionNumber"]="FMSC-legacy-reference";
        var preserved=scoped.BuildPdfExportDocument(legacy,"Fictional Meadow School & College","Fixture");
        using(preserved.Data)Check(preserved.Data.AsEnumerable().Any(row=>row.ItemArray.OfType<string>().Any(value=>value.Contains("FMSC-legacy-reference",StringComparison.Ordinal))),
            "An export never renumbers a stored legacy reference to manufacture a different identifier");
    }

    private sealed class PdfContextFixturePage(string campus) : V2Page("Student List","Student report")
    {
        internal DataGridView DataGrid=>Grid;
        protected override string? ReportCampusContext=>campus;
        protected override string ReportExportSubtitle()=>"Campus: "+campus;
    }
}
