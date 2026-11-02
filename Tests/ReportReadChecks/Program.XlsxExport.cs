using ClosedXML.Excel;
using FMSC_SMS.Core;
using System.IO.Compression;
using System.Xml.Linq;

internal static partial class Program
{
    private static void CheckXlsxExport()
    {
        string folder=Path.Combine(Path.GetTempPath(),"FMSC-xlsx-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"report.xlsx");
        try
        {
            using var rows=new DataTable();
            rows.Columns.Add("Admission Number",typeof(long));rows.Columns.Add("Student");rows.Columns.Add("CNIC");rows.Columns.Add("Phone");
            rows.Columns.Add("Amount",typeof(decimal));rows.Columns.Add("Payment Date",typeof(DateTime));rows.Columns.Add("Active",typeof(bool));
            rows.Columns.Add("Remarks");rows.Columns.Add("Receipt Count",typeof(int));
            rows.Rows.Add(12345678901234567L,"Student, \"Example\"","03139-1706125-4","+923001234567",1234.50m,new DateTime(2026,10,21,13,45,12),true,"=SUM(A1:A2)",2);
            rows.Rows.Add(2L,"اردو طالب علم","00123","03001234567",-1900m,new DateTime(2026,11,1),false,DBNull.Value,0);
            XlsxTableExporter.Write(rows,path);
            using(var workbook=new XLWorkbook(path))
            {
                var sheet=workbook.Worksheet("Report");
                Check(workbook.Worksheets.Count==1 && sheet.LastRowUsed()!.RowNumber()==3,"The workbook has one header row followed by every source record");
                Check(sheet.Cell(2,1).DataType==XLDataType.Text && sheet.Cell(2,1).GetString()=="12345678901234567","Long admission references are text and retain all digits");
                Check(sheet.Cell(3,3).GetString()=="00123" && sheet.Cell(3,4).GetString()=="03001234567","Identity and phone strings retain their leading zeros");
                Check(sheet.Cell(2,5).DataType==XLDataType.Number && sheet.Cell(2,5).GetDouble()==1234.5 && sheet.Cell(3,5).GetDouble()==-1900,"Signed amounts stay numeric and can be summed in Excel");
                Check(sheet.Cell(2,5).Style.NumberFormat.Format=="#,##0.##","Amount formatting preserves report decimal precision");
                Check(sheet.Cell(2,6).DataType==XLDataType.DateTime && sheet.Cell(2,6).GetDateTime()==new DateTime(2026,10,21,13,45,12),"Date/time cells retain usable dates and their time component");
                Check(sheet.Cell(3,6).Style.NumberFormat.Format=="dd-mmm-yyyy","Date-only values have a date-only display");
                Check(sheet.Cell(2,7).DataType==XLDataType.Boolean && sheet.Cell(2,7).GetBoolean(),"Boolean fields retain their type");
                Check(sheet.Cell(2,8).DataType==XLDataType.Text && sheet.Cell(2,8).GetString()=="=SUM(A1:A2)" && !sheet.Cell(2,8).HasFormula,"Formula-looking report text remains literal text");
                Check(sheet.Cell(3,8).IsEmpty(),"Database nulls remain blank cells");
                Check(sheet.Cell(2,9).DataType==XLDataType.Number,"Receipt counts are numeric, rather than being mistaken for receipt references");
                Check(sheet.Cell(3,2).GetString()=="اردو طالب علم","Unicode school data survives the workbook round trip");
                Check(sheet.SheetView.SplitRow==1 && sheet.AutoFilter.IsEnabled,"Headings are frozen and the sheet offers Excel filters");
                Check(sheet.Cell(1,1).Style.Font.Bold && sheet.AutoFilter.Range.RangeAddress.LastAddress.RowNumber==3,"Header styling and filters cover the complete result");
            }
            using(var zip=ZipFile.OpenRead(path))
            {
                XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                using var xml=zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
                Check(!XDocument.Load(xml).Descendants(ns+"f").Any(),"The produced worksheet contains no executable formulas");
            }
            byte[] original=File.ReadAllBytes(path);
            rows.Rows[0]["Remarks"]=new string('x',32768);
            bool refused=false;try{XlsxTableExporter.Write(rows,path);}catch(ArgumentException){refused=true;}
            Check(refused && File.ReadAllBytes(path).SequenceEqual(original),"An actual workbook-generation failure preserves the previous complete file");
            Check(!Directory.GetFiles(folder,"*.tmp").Any(),"Failed workbook generation removes temporary files");
            using var noColumns=new DataTable();
            refused=false;try{XlsxTableExporter.Write(noColumns,path);}catch(InvalidOperationException){refused=true;}
            Check(refused && File.ReadAllBytes(path).SequenceEqual(original),"A report without columns cannot replace an existing workbook");
            using var empty=rows.Clone();
            XlsxTableExporter.Write(empty,path);
            using(var workbook=new XLWorkbook(path))
                Check(workbook.Worksheet(1).LastRowUsed()!.RowNumber()==1,"An empty result creates a readable header-only workbook");
        }
        finally{Directory.Delete(folder,true);}
    }
}
