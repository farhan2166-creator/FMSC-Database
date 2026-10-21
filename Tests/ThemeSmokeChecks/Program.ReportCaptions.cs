using System.Data;
using System.Xml.Linq;
using Microsoft.Reporting.WinForms;

internal static partial class Program
{
    private static void CheckReportCaptions()
    {
        string output=Path.GetFullPath("artifacts/report-captions");Directory.CreateDirectory(output);
        foreach(string name in new[]{"AdmissionForm","ClassMarksSheet","ClassAttendanceSheet",
            "ClassMonthlyAttendanceSheet","ClassOutstandingCharges","ClassSectionSummary","FeeAgingReport"})
        {
            string path=Path.Combine(AppContext.BaseDirectory,"Reports",name+".rdlc");
            var document=XDocument.Load(path);XNamespace ns=document.Root!.Name.Namespace;
            using var report=new LocalReport();report.LoadReportDefinition(new StringReader(document.ToString()));
            foreach(var dataset in document.Descendants(ns+"DataSet"))
            {
                var data=new DataTable();
                foreach(var field in dataset.Element(ns+"Fields")!.Elements(ns+"Field"))
                {
                    string typeName=field.Elements().FirstOrDefault(element=>element.Name.LocalName=="TypeName")?.Value??"System.String";
                    data.Columns.Add(field.Element(ns+"DataField")!.Value,Type.GetType(typeName,true)!);
                }
                var row=data.NewRow();
                foreach(DataColumn column in data.Columns)
                    row[column]=column.DataType==typeof(string)?"Example":column.DataType==typeof(DateTime)?new DateTime(2026,10,21):
                        column.DataType==typeof(bool)?true:Convert.ChangeType(1,column.DataType);
                data.Rows.Add(row);
                report.DataSources.Add(new ReportDataSource((string)dataset.Attribute("Name")!,data));
            }
            byte[] pdf=report.Render("PDF");File.WriteAllBytes(Path.Combine(output,name+".pdf"),pdf);
            byte[] png=report.Render("IMAGE","<DeviceInfo><OutputFormat>PNG</OutputFormat><DpiX>110</DpiX><DpiY>110</DpiY></DeviceInfo>");
            File.WriteAllBytes(Path.Combine(output,name+".png"),png);
            Check(pdf.Length>1000&&png.Length>1000,name+" renders its split text/data runs with representative typed fixture values.");
        }
    }
}
