using ClosedXML.Excel;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckDeductionSigns()
    {
        foreach(string name in new[]{"Discount","Discounts","Discount Amount","Discounts Applied","Applied Discount",
            "Assigned Discount / Cycle (Rs.)","Waived Amount","Concession Amount","Refunds","Corrections"})
        {
            var column=new DataColumn(name,typeof(decimal));
            Check(ReportMoneyPresentation.Amount(column,1250.50m)==-1250.50m,name+" prints as a deduction");
            Check(ReportMoneyPresentation.Amount(column,0m).ToString("#,##0.##")=="0",name+" never prints minus zero");
            Check(ReportMoneyPresentation.Amount(column,-20m)==20m,name+" reversal remains an increase rather than another deduction");
        }
        foreach(string name in new[]{"Amount","Net Charge","Net Billed","Discount Rate (%)","Discount Count",
            "Discount withdrawn","Credit Applied","Available Credit","Credit held","Balance Remaining","Cash Received"})
            Check(ReportMoneyPresentation.Amount(new DataColumn(name,typeof(decimal)),-50m)==-50m
                &&ReportMoneyPresentation.Amount(new DataColumn(name,typeof(decimal)),50m)==50m,
                name+" retains its own financial meaning and existing sign");

        var renamed=new DataColumn("School concession",typeof(decimal));
        renamed.ExtendedProperties[ReportContextProjection.SourceColumnKey]="DiscountAmount";
        Check(ReportMoneyPresentation.Amount(renamed,150m)==-150m,"A selected column's friendly caption does not lose deduction semantics");
        renamed.ColumnName="Discount";renamed.ExtendedProperties[ReportContextProjection.SourceColumnKey]="DiscountPercent";
        Check(ReportMoneyPresentation.Amount(renamed,25m)==25m,"The original field protects percentages from a misleading friendly caption");

        using var rows=new DataTable();rows.Columns.Add("Group");
        foreach(string name in new[]{"Gross Billed","Discounts","Net Billed","Credit Applied"})rows.Columns.Add(name,typeof(decimal));
        rows.Rows.Add("Concession",1000m,200m,800m,100m);
        rows.Rows.Add("Reversal",1000m,-50m,1050m,0m);
        rows.Rows.Add("No concession",0m,0m,0m,0m);
        rows.Rows.Add("TOTAL",2000m,150m,1850m,100m);
        rows.Rows.Add("Unknown",DBNull.Value,DBNull.Value,DBNull.Value,DBNull.Value);
        var before=rows.AsEnumerable().Select(row=>row.ItemArray).ToArray();
        string path=Path.Combine(Path.GetTempPath(),"FMSC-deductions-"+Guid.NewGuid().ToString("N")+".xlsx");
        try
        {
            XlsxTableExporter.Write(rows,path);
            using var book=new XLWorkbook(path);var sheet=book.Worksheet(1);
            Check(sheet.Cell(2,3).DataType==XLDataType.Number&&sheet.Cell(2,3).GetDouble()==-200,
                "Excel stores a deduction as a negative number, not misleading positive data with a minus-only format");
            Check(sheet.Cell(3,3).GetDouble()==50&&sheet.Cell(4,3).GetDouble()==0&&sheet.Cell(5,3).GetDouble()==-150,
                "Excel distinguishes deduction reversals, zero and authoritative totals");
            Check(sheet.Cell(2,2).GetDouble()+sheet.Cell(2,3).GetDouble()==sheet.Cell(2,4).GetDouble()
                &&sheet.Cell(3,2).GetDouble()+sheet.Cell(3,3).GetDouble()==sheet.Cell(3,4).GetDouble(),
                "Exported gross plus signed deduction reconciles to net for both a concession and its reversal");
            Check(sheet.Cell(5,3).GetDouble()==Enumerable.Range(2,3).Sum(row=>sheet.Cell(row,3).GetDouble()),
                "Excel's signed deduction total reconciles to the exported detail rows");
            Check(sheet.Cell(2,5).GetDouble()==100&&sheet.Cell(6,3).IsEmpty(),"Credit payments stay positive and null deductions stay blank");
            Check(before.Select((values,index)=>values.SequenceEqual(rows.Rows[index].ItemArray)).All(same=>same),
                "Exporting signed deductions never changes source data or accounting totals");
        }
        finally{if(File.Exists(path))File.Delete(path);}

        using var totals=new DataTable();
        foreach(string name in new[]{"Discounts","Refunds","Corrections","Credit Applied","Net Billed"})totals.Columns.Add(name,typeof(decimal));
        totals.Rows.Add(200m,50m,0m,100m,800m);
        var metrics=ReportPrintSummary.FromTotals(totals);
        Check(metrics[0].Value=="Rs. -200"&&metrics[1].Value=="Rs. -50"&&metrics[2].Value=="Rs. 0",
            "Printed summary cards use the same signs as detail and total rows");
        Check(metrics[3].Value=="Rs. 100"&&metrics[4].Value=="Rs. 800"&&totals.Rows[0].Field<decimal>("Discounts")==200m,
            "Payment sources, net totals and authoritative input magnitudes remain intact");
        var legacy=ReportPrintSummary.Split("Campus: All   Discounts: Rs. 200   Refunds: Rs. -50   Corrections: Rs. 0");
        Check(legacy.Metrics.Select(metric=>metric.Value).SequenceEqual(new[]{"Rs. -200","Rs. -50","Rs. 0"}),
            "Legacy summary text gains missing minus signs without doubling existing signs or signing zero");
        Check(ReportPrintSummary.Split("Discounts: Rs. 200",metrics).Metrics[0].Value=="Rs. -200",
            "Already structured print metrics are never signed twice");
    }
}
