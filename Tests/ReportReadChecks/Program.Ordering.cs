using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports.Certificates;

internal static partial class Program
{
    private static void CheckReportOrdering()
    {
        var rows=new DataTable();
        rows.Columns.Add("Student");rows.Columns.Add("Admission",typeof(string));rows.Columns.Add("Amount",typeof(decimal));rows.Columns.Add("Date",typeof(DateTime));
        rows.Rows.Add("Ten","10",100m,new DateTime(2026,10,2));
        rows.Rows.Add("Two A","2a",200m,new DateTime(2026,9,30));
        rows.Rows.Add("Two","2",200m,new DateTime(2026,11,1));
        rows.Rows.Add("Unknown","QR-11",DBNull.Value,DBNull.Value);
        rows.Rows.Add("TOTAL","",500m,DBNull.Value);
        var high=ReportRowOrder.Apply(rows,new("Highest","Amount",true));
        Check(high.Rows[0]["Student"].Equals("Two A") && high.Rows[1]["Student"].Equals("Two"),"Descending numeric order keeps tied rows stable");
        Check(high.Rows[3]["Student"].Equals("Unknown") && high.Rows[4]["Student"].Equals("TOTAL"),"Missing values stay last and totals remain below all details");
        Check(high.Rows[4]["Amount"].Equals(500m) && rows.Rows[0]["Student"].Equals("Ten"),"Sorting preserves authoritative totals and leaves source data intact");
        var low=ReportRowOrder.Apply(rows,new("Lowest","Amount"));
        Check(low.Rows[0]["Amount"].Equals(100m) && low.Rows[3]["Student"].Equals("Unknown"),"Ascending amounts also keep missing values last");
        var admission=ReportRowOrder.Apply(rows,new("Admission","Admission",Kind:ReportRowOrder.ValueKind.Reference));
        Check(string.Join(",",admission.AsEnumerable().Take(3).Select(r=>r["Admission"]))=="2,2a,10","Admission numbers sort naturally, including letter suffixes");
        var date=ReportRowOrder.Apply(rows,new("Latest","Date",true));
        Check(date.Rows[0]["Student"].Equals("Two") && date.Rows[2]["Student"].Equals("Two A"),"Dates sort chronologically across month boundaries");
        Check(ReferenceEquals(rows,ReportRowOrder.Apply(rows,new("Default"))),"Default preserves the database's original order");
        bool refused=false;try{ReportRowOrder.Apply(rows,new("Wrong view","Missing"));}catch(InvalidOperationException){refused=true;}
        Check(refused,"Unavailable sort columns are refused rather than silently using another order");
        var month=new DataTable();month.Columns.Add("Billing Month");
        foreach(var day in new[]{new DateTime(2027,1,1),new DateTime(2026,12,1),new DateTime(2026,2,1)})month.Rows.Add(day.ToString("MMM yyyy"));
        var months=ReportRowOrder.Apply(month,new("Month","Billing Month",Kind:ReportRowOrder.ValueKind.Month));
        Check(months.Rows[0][0].Equals(new DateTime(2026,2,1).ToString("MMM yyyy")) && months.Rows[2][0].Equals(new DateTime(2027,1,1).ToString("MMM yyyy")),"Reconciliation months sort by calendar month, not month-name text");
        var records=ReceiptRows();
        for(int i=0;i<records.Rows.Count;i++) records.Rows[i]["Money Received"]=i;
        CollectionReceiptGrouping.Apply(records,"Campus");
        var grouped=ReportRowOrder.Apply(records,new("Received","Money Received",true));
        var before=CollectionReceiptGrouping.Layout(records)!;var layout=CollectionReceiptGrouping.Layout(grouped)!;
        Check(before.Where(r=>r.Kind!="Detail").SequenceEqual(layout.Where(r=>r.Kind!="Detail")),"Receipt headings, subtotals and grand totals retain their group positions");
        foreach(var group in layout.Where(r=>r.Kind=="Detail").Select(r=>r.GroupKey).Distinct())
        {
            var amounts=Enumerable.Range(0,layout.Length).Where(i=>layout[i].Kind=="Detail" && layout[i].GroupKey==group).Select(i=>grouped.Rows[i].Field<decimal>("Money Received")).ToArray();
            Check(amounts.SequenceEqual(amounts.OrderByDescending(v=>v)),"Receipts sort inside their own group without mixing campuses");
        }
        Check(grouped.Rows[^1]["Money Received"].Equals(records.Rows[^1]["Money Received"]),"Receipt grand total remains unchanged");
        var page=CollectionReceiptGrouping.Page(grouped,1,25);
        var details=CollectionReceiptGrouping.DetailsOnly(grouped);
        Check(page.TotalRows==100 && details.Rows.Count==100 && page.Rows.Rows[1]["Receipt / Reference"].Equals(details.Rows[25]["Receipt / Reference"]),"Pagination and data-only export follow the complete ordered result");
        var certificate=new DataTable();certificate.Columns.Add("Recorded On",typeof(DateTime));certificate.Columns.Add("Certificate No.");
        certificate.Rows.Add(new DateTime(2026,10,2),"EMP-10");certificate.Rows.Add(new DateTime(2026,9,30),"EMP-2");
        var cert=CertificateIssueRegisterData.Display(ReportRowOrder.Apply(certificate,new("Recorded","Recorded On")));
        Check(cert.Rows[0]["Certificate No."].Equals("EMP-2"),"Certificate dates are sorted before converting them to displayed date strings");

        void Schema(string report,string view,string grouping,params string[] columns)
        {
            var schema=new DataTable();foreach(string column in columns)schema.Columns.Add(column);
            var choices=ReportRowOrder.Choices(report,view,grouping);
            Check(choices.Count>1 && choices.Select(c=>c.Label).Distinct().Count()==choices.Count,view+": choices have unique labels");
            foreach(var choice in choices) { ReportRowOrder.Apply(schema,choice);Check(choice.Column.Length==0 || columns.Contains(choice.Column),view+": sort uses a column returned by that view"); }
        }
        Schema("GetCollectionsAndReceiptsReport","Receipts","","Date","Receipt / Reference","Student / Family","Class / Section","Money Received","Adjusted Collection");
        Schema("GetCollectionsAndReceiptsReport","Detailed Transactions","","Date","Receipt / Reference","Student","Class / Section","Collection Amount");
        Schema("GetCollectionsAndReceiptsReport","Summary","","Group","Money Received","Adjusted Collection");
        foreach(string view in new[]{"Students","Charges","Families","Aging"})
            Schema("GetOutstandingAndAgingReport",view,"","Student","Adm. / QR No.","Family","Campus","Class / Section","Outstanding","Days Overdue",view=="Charges"?"Due Date":"Oldest Due Date");
        foreach(string view in new[]{"Students","Families"})
            Schema("CreditBalanceRegister",view,"","Student","Adm. / QR No.","Family / Student","Campus","Class / Section","Available Credit","Last Activity");
        foreach(string grouping in new[]{"Day","Week","Month","Class","Campus","Fee Type"})
            Schema("GetBillingAndDiscountsReport","Billing Summary",grouping,grouping switch {"Day"=>"Charge Date","Week"=>"Charge Week Starting","Month"=>"Charge Month",_=>grouping},"Net Billed","Discounts");
        Schema("GetBillingAndDiscountsReport","Concession Summary","","Campus","Class","Fee Type","Concession","Students");
        Schema("GetBillingAndDiscountsReport","Concession Assignments","","Student","Admission Number","Class","Fee Type","Discount","Effective From");
        foreach(string view in new[]{"Billing Detail","Discount Detail"})
            Schema("GetBillingAndDiscountsReport",view,"","Student","Admission Number","Class","Fee Type","Charge Date","Net Charge",view=="Discount Detail"?"Applied Discount":"Discount");
        Schema("MonthlyFeeReconciliation","Monthly Summary","","Billing Month","Group","Outstanding","Net Fees","Money Allocated");
        Schema("CertificateIssueRegister","","","Recorded On","Certificate Date","Recipient","Certificate Type","Certificate No.","Student / Staff No.");
    }
}
