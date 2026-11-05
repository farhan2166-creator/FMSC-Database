using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports.Finance;
using System.Xml.Linq;

internal static partial class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static void Main(string[] args)
    {
        if(args.Contains("--deduction-signs"))
        {
            CheckDeductionSigns();Console.WriteLine($"PASS: {checks} report deduction sign and Excel reconciliation checks.");return;
        }
        if(args.Contains("--academic-periods"))
        {
            CheckAcademicPeriods();Console.WriteLine($"PASS: {checks} academic period boundary checks.");return;
        }
        CheckAcademicPeriods();
        if(args.Contains("--xlsx-export"))
        {
            CheckXlsxExport();
            Console.WriteLine($"PASS: {checks} Excel workbook type, formatting, data and atomic-save checks.");
            return;
        }
        CheckReceipts(); CheckOutstanding(); CheckCredit(); CheckConcessions(); CheckReceiptRdlc(); CheckQueryContracts(); CheckNewReports(); CheckPrintPresentation(); CheckSavedViews(); CheckReportOrdering();CheckSavedOrderAliases();
        CheckXlsxExport();CheckPdfContext();CheckDeductionSigns();
        if (args.Contains("--sql")) CheckSql();
        Console.WriteLine($"PASS: {checks} focused report data, grouping, totals, filtering and layout checks" + (args.Contains("--sql") ? " including disposable SQL queries." : ". SQL integration was not requested."));
    }
    private static DataTable ReceiptRows()
    {
        var data = new DataTable();
        foreach (string col in new[] { "Receipt / Reference", "Campus", "Class / Section" }) data.Columns.Add(col);
        foreach (string col in new[] { "Money Received", "Refunds", "Corrections", "Credit Applied", "Adjusted Collection" }) data.Columns.Add(col, typeof(decimal));
        for (int i = 0; i < 100; i++) data.Rows.Add("R-"+i, i < 70 ? "Boys" : "Girls", "Play Group – A",100m,0m,0m,5m,100m);
        data.Rows[99]["Money Received"] = 0m; data.Rows[99]["Corrections"] = 30m; data.Rows[99]["Adjusted Collection"] = -30m;
        return data;
    }
    private static void CheckReceipts()
    {
        foreach (string mode in new[] { "Whole School", "Campus", "Class", "Section" })
        {
            var data = ReceiptRows(); CollectionReceiptGrouping.Apply(data,mode);
            var details = CollectionReceiptGrouping.DetailsOnly(data);
            Check(details.Rows.Count == 100 && details.AsEnumerable().Sum(r=>r.Field<decimal>("Adjusted Collection"))==9870m,mode+": CSV has receipts only, with signed corrections");
            Check(CollectionReceiptGrouping.Layout(details) is null,mode+": data-only export cannot regain subtotal metadata");
            var first = CollectionReceiptGrouping.Page(data,0,50); var second = CollectionReceiptGrouping.Page(data,1,50);
            Check(first.TotalRows==100 && first.PageCount==2 && second.PageCount==2,mode+": pager counts receipt entries only");
            Check(CollectionReceiptGrouping.Page(data,999,50).Index==1,mode+": page index clamped");
            var seen = new List<string>();
            foreach (var page in new[] { first, second })
            {
                var info = CollectionReceiptGrouping.Layout(page.Rows)!;
                seen.AddRange(page.Rows.AsEnumerable().Where((r,i)=>info[i].Kind=="Detail").Select(r=>(string)r[0]));
                Check(info.Count(r=>r.Kind=="Detail")==50,mode+": full receipt page");
                for (int i=0;i<info.Length;i++) if(info[i].Kind=="Subtotal")
                    Check(i>0 && info[i-1].Kind=="Detail" && info[i-1].GroupKey==info[i].GroupKey,mode+": subtotal stays with last receipt");
            }
            Check(seen.Count==100 && seen.Distinct().Count()==100,mode+": pagination does not duplicate or lose receipts");
            Check(CollectionReceiptGrouping.Layout(first.Rows)!.All(r=>r.Kind!="GrandTotal") && CollectionReceiptGrouping.Layout(second.Rows)!.Last().Kind=="GrandTotal",mode+": grand total only on final page");
            if(mode!="Whole School") Check(CollectionReceiptGrouping.Layout(second.Rows)!.First().Kind=="Heading",mode+": continuation page repeats group heading");
        }
        var mixed=ReceiptRows().Clone(); mixed.Rows.Add("FR-1","Multiple","Multiple",100m,0m,0m,0m,100m);
        CollectionReceiptGrouping.Apply(mixed,"Section");
        Check(CollectionReceiptGrouping.DetailsOnly(mixed).Rows.Count==1,"Mixed-location family receipt remains intact");
        Check(mixed.AsEnumerable().Count(r=>Equals(r[0],"FR-1"))==1,"Mixed family is not counted under several sections");
        CheckReceiptLocationOrder();
    }
    private static void CheckOutstanding()
    {
        var data = new DataTable();data.Columns.Add("Outstanding",typeof(decimal));data.Columns.Add("Days Overdue",typeof(int));
        foreach (var (amount,days) in new[] {(100m,0),(200m,1),(300m,30),(400m,31),(500m,90)}) data.Rows.Add(amount,days);
        data.Rows.Add(600m,DBNull.Value);
        var filtered=new OutstandingReportFilter(true,200m,1,30).Apply(data);
        Check(filtered.Rows.Count==2 && filtered.AsEnumerable().Sum(r=>r.Field<decimal>("Outstanding"))==500m,"Overdue bounds are inclusive and exclude future/due-today/missing dates");
        Check(new OutstandingReportFilter(false,0,0,0).Apply(data).Rows.Count==1,"An upper bound of zero means due-today/not-yet-overdue only");
        var totals=OutstandingReportFilter.Totals(filtered);
        Check((decimal)totals.Rows[0]["Outstanding shown"]==500m && (int)totals.Rows[0]["Rows shown"]==2,"Filtered summary describes only displayed rows");
        Check(new OutstandingReportFilter(true,10000m,0,null).Apply(data).Rows.Count==0,"No matching overdue records stays empty");
        foreach (string field in new[] { "Overdue Amount", "Due Today", "Not Yet Due" }) data.Columns.Add(field,typeof(decimal));
        data.Rows.Clear(); data.Rows.Add(1200m,30,200m,0m,1000m);
        var debtor=new OutstandingReportFilter(true,0,0,null).Apply(data);
        var split=OutstandingReportFilter.Totals(debtor);
        Check((decimal)split.Rows[0]["Outstanding shown"]==1200m && (decimal)split.Rows[0]["Overdue Amount"]==200m
            && (decimal)split.Rows[0]["Not Yet Due"]==1000m,"Debtor filter preserves full balance and separately totals actual overdue/future money");
        Check(OutstandingReportFilter.Totals(data.Clone()).Rows[0].Field<decimal>("Overdue Amount")==0m,"Empty due-status totals remain zero");
        data.Columns.Add("Due Date Missing",typeof(decimal));
        data.Rows[0]["Due Date Missing"]=0m;
        data.Rows.Add(50m,DBNull.Value,0m,0m,0m,50m);
        Check((decimal)OutstandingReportFilter.Totals(data).Rows[0]["Due Date Missing"]==50m,
            "Legacy missing due dates remain visible as a separate amount, not future or overdue");
        data.Rows.Add(70m,0,0m,70m,0m,0m);
        Check(new OutstandingReportFilter(false,0,0,null){DueStatus="None"}.Apply(data).Rows.Count==3,"None applies no due-status restriction");
        foreach(var (status,amount) in new[]{("Overdue",1200m),("Due Today",70m),("Not Yet Due",1200m),("No Due Date",50m)})
        {
            using var chosen=new OutstandingReportFilter(status=="Overdue",0,0,null){DueStatus=status}.Apply(data);
            Check(chosen.Rows.Count==1&&chosen.Rows[0].Field<decimal>("Outstanding")==amount,status+" filters by saved category amounts and preserves the full student balance");
        }
        Check(ReportPrintSummary.Caption("Open Charges (Net)")=="Total Fees"&&ReportPrintSummary.Caption("Settled On Open Charges")=="Paid Towards Fees","Outstanding report captions use familiar fee wording");
    }
    private static DataTable CreditRows()
    {
        var data=new DataTable();data.Columns.Add("AdmissionId",typeof(int));data.Columns.Add("FamilyId",typeof(int));
        foreach(string key in new[]{"Adm. / QR No.","Student","Family","Campus","Father Name","Class / Section","Student Status"})data.Columns.Add(key);
        data.Columns.Add("Available Credit",typeof(decimal));data.Columns.Add("Last Activity",typeof(DateTime));
        data.Rows.Add(1,10,"1001","One","FAM-10","Boys","Parent","Class 1 – A","Active",700m,new DateTime(2026,10,20));
        data.Rows.Add(2,10,"1002","Two","FAM-10","Girls","Parent","Class 2 – A","Active",150m,new DateTime(2026,10,21));
        data.Rows.Add(3,DBNull.Value,"QR-3","Three","","Unassigned","Parent","","Pending",100m,new DateTime(2026,10,19));
        data.Rows.Add(4,DBNull.Value,"QR-4","Four","","Unassigned","Parent","","Pending",250m,new DateTime(2026,10,18));
        return data;
    }
    private static void CheckCredit()
    {
        var data=CreditRows();
        data.Columns.Add("AdmissionNumber");data.Columns.Add("__AdmissionNumberBase",typeof(long));
        var (students,totals)=CreditBalanceReportData.Create(data,false,0m);
        Check(students.Rows.Count==4 && students.Columns.Count==8 && !students.Columns.Contains("AdmissionId") && !students.Columns.Contains("FamilyId") && !students.Columns.Contains("Family"),"Internal identifiers, enrichment fields and the empty family-code column are never exported");
        Check((decimal)totals.Rows[0]["Credit held"]==1200m,"Credit register totals sum positive student balances once");
        var (families,_) = CreditBalanceReportData.Create(data,true,0m);
        Check(families.Rows.Count==3,"Unlinked students remain separate instead of forming a fake family");
        Check((decimal)families.Rows[0]["Available Credit"]==850m && families.Rows[0].Field<string>("Members")!.Contains("Boys · Class 1 – A\nAdvance: Rs. 700"),"Family total preserves each student's placement and advance");
        Check(families.Rows[0].Field<string>("Members")!.Contains("1001 - One child of Parent") && families.Rows[0].Field<string>("Members")!.Contains("1002 - Two child of Parent\nGirls · Class 2 – A\nAdvance: Rs. 150"),"Family details pair individual identities, campuses, classes and balances without guessing gender");
        Check(families.AsEnumerable().Sum(r=>r.Field<decimal>("Available Credit"))==1200m,"Family totals reconcile with student totals");
        var (minimum,minimumTotals)=CreditBalanceReportData.Create(data,true,200m);
        Check(minimum.Rows.Count==2 && (decimal)minimumTotals.Rows[0]["Credit held"]==950m,"Minimum credit is per student as labelled");
        Check(CreditBalanceReportData.Create(data.Clone(),false,0).Rows.Rows.Count==0,"Empty credit register");
    }
    private static void CheckConcessions()
    {
        var data=new DataTable();data.Columns.Add("AdmissionId",typeof(int));
        foreach(string c in new[]{"Campus","Class","Fee Type","Frequency","Concession"})data.Columns.Add(c);
        data.Columns.Add("Assigned Discount",typeof(decimal));
        data.Rows.Add(1,"Boys","Class 1","Tuition","Monthly","Sibling",100m);
        data.Rows.Add(2,"Boys","Class 1","Tuition","Monthly","Sibling",50m);
        data.Rows.Add(1,"Boys","Class 1","Tuition","Yearly","Sibling",1000m);
        var (rows,totals)=ConcessionSummaryData.Create(data);
        Check(rows.Rows.Count==2,"Monthly and yearly discount cycles never combine");
        Check((decimal)rows.Rows[0]["Assigned Discount / Cycle (Rs.)"]==150m && (int)rows.Rows[0]["Students"]==2,"Concession group counts students and rate correctly");
        Check((int)totals.Rows[0]["Students"]==2 && (int)totals.Rows[0]["Assignments"]==3,"Summary deduplicates students across assignments");
        Check(totals.Columns.Count==2,"No misleading grand sum of monthly and yearly discounts");
        foreach(string key in new[]{"CampusId","ClassId","FeeTypeId"}) data.Columns.Add(key,typeof(int));
        foreach(DataRow row in data.Rows) { row["CampusId"]=1;row["ClassId"]=1;row["FeeTypeId"]=1; }
        var other=data.NewRow(); other.ItemArray=data.Rows[0].ItemArray; other["CampusId"]=2; data.Rows.Add(other);
        Check(ConcessionSummaryData.Create(data).Rows.Rows.Count==3,"Same display names in different campus IDs stay separate");
    }
    private static void CheckReceiptRdlc()
    {
        XNamespace ns="http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";
        XElement E(string name,params object[] content)=>new(ns+name,content);
        XElement Box(string name)=>E("Textbox",new XAttribute("Name",name),E("Paragraphs",E("Paragraph",E("TextRuns",E("TextRun",E("Value","Value"),E("Style",E("FontWeight","Normal")))),E("Style",E("TextAlign","Left")))));
        var table=E("Tablix",E("TablixBody",E("TablixRows",
            E("TablixRow",E("Height",".2in"),E("TablixCells",E("TablixCell",E("CellContents",Box("Heading"))))),
            E("TablixRow",E("Height",".2in"),E("TablixCells",E("TablixCell",E("CellContents",Box("Detail"))))))),
            E("TablixRowHierarchy",E("TablixMembers",E("TablixMember"),E("TablixMember",E("Group",new XAttribute("Name","Details"))))));
        CollectionReceiptRdlc.Apply(table,new[]{0,1},new[]{false,true},0);
        Check(table.Descendants(ns+"TablixRow").Count()==5,"Grouped print has headings, details, subtotal and grand total rows");
        Check(table.Descendants(ns+"RepeatOnNewPage").Any(e=>e.Value=="true"),"Group heading repeats on printed continuation pages");
        Check(table.Descendants(ns+"KeepWithGroup").Count(e=>e.Value=="Before")==2,"Subtotal and grand total keep with previous detail group");
        Check(table.Descendants(ns+"Value").Any(e=>e.Value.Contains("Sum(Fields!N1.Value, \"ReceiptGroup0\")")),"Print subtotals sum original numeric fields within group");
        Check(table.Descendants(ns+"Value").Any(e=>e.Value=="=Format(Sum(Fields!N1.Value), \"#,##0.##\")"),"Grand total sums raw receipt data once");
    }
}
