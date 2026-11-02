using System.Reflection;
using ClosedXML.Excel;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports.Finance;

internal static partial class Program
{
    private static void CheckSpreadsheetExport()
    {
        using var source=new DataTable();
        source.Columns.Add("AdmissionNumber");source.Columns.Add("StudentName");source.Columns.Add("BirthDate",typeof(DateTime));
        source.Columns.Add("PaymentDate",typeof(DateTime));source.Columns.Add("Amount",typeof(decimal));source.Columns.Add("AdmissionId",typeof(int));
        for(int row=0;row<125;row++)source.Rows.Add("00"+row,"Student "+row,new DateTime(2008,10,21,7,30,0),new DateTime(2026,11,2,13,45,12),1234.50m+row,row+1);
        using var grid=new DataGridView {AutoGenerateColumns=false};
        foreach(var (name,caption) in new[]{("AdmissionNumber","Admission Number"),("StudentName","Student"),("BirthDate","Date of Birth"),
            ("PaymentDate","Payment Date"),("Amount","Amount"),("AdmissionId","Internal ID")})
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name=name,DataPropertyName=name,HeaderText=caption});
        grid.Columns["AdmissionId"].Visible=false;
        grid.Columns["StudentName"].DisplayIndex=0;
        using var excel=ReportExportBuilder.Build(source,grid.Columns.Cast<DataGridViewColumn>(),new[]{"BirthDate"},preserveDateTypes:true);
        Check(excel.Rows.Count==125 && excel.Columns.Count==5 && excel.Columns[0].ColumnName=="Student","Spreadsheet projection retains all report rows and the selected column order");
        Check(!excel.Columns.Contains("Internal ID"),"Hidden internal columns are excluded from Excel");
        Check(excel.Columns["Date of Birth"]!.DataType==typeof(DateTime) && excel.Rows[0].Field<DateTime>("Date of Birth")==new DateTime(2008,10,21),"Date-only fields remain typed dates without an unwanted time");
        Check(excel.Rows[0].Field<DateTime>("Payment Date")==new DateTime(2026,11,2,13,45,12),"Timestamp columns retain their time component");
        using var legacy=ReportExportBuilder.Build(source,grid.Columns.Cast<DataGridViewColumn>(),new[]{"BirthDate"});
        Check(legacy.Columns["Date of Birth"]!.DataType==typeof(string),"PDF and CSV keep their existing date projection");
        string path=Path.Combine(Path.GetTempPath(),"FMSC-selected-report-"+Guid.NewGuid().ToString("N")+".xlsx");
        try
        {
            XlsxTableExporter.Write(excel,path);
            using var workbook=new XLWorkbook(path);
            var sheet=workbook.Worksheet("Report");
            Check(sheet.LastRowUsed()!.RowNumber()==126 && sheet.Cell(126,1).GetString()=="Student 124","The workbook includes records beyond the first report page in their source order");
            Check(sheet.Cell(2,2).GetString()=="000" && sheet.Cell(2,5).GetDouble()==1234.5,"Selected references and amounts retain their value and type");
        }
        finally{if(File.Exists(path))File.Delete(path);}
        using var receipts=new DataTable();
        foreach(string column in new[]{"Receipt / Reference","Campus","Class / Section"})receipts.Columns.Add(column);
        foreach(string column in new[]{"Money Received","Refunds","Corrections","Credit Applied","Adjusted Collection"})receipts.Columns.Add(column,typeof(decimal));
        receipts.Rows.Add("R-1","Boys","Class 1 – A",100m,0m,0m,0m,100m);
        receipts.Rows.Add("R-2","Girls","Class 1 – A",200m,0m,0m,0m,200m);
        CollectionReceiptGrouping.Apply(receipts,"Campus");
        using var page=new CollectionsReceiptsReportPage();
        using var selected=(DataTable)typeof(CollectionsReceiptsReportPage).GetMethod("SelectExportRows",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(page,new object[]{receipts,true})!;
        Check(selected.Rows.Count==2 && selected.AsEnumerable().Sum(r=>r.Field<decimal>("Money Received"))==300m,"Spreadsheet data selection excludes receipt group headings and duplicate totals");
        Check(!page.IsHandleCreated,"Spreadsheet selection checks do not start SQL initialization");
    }
}
