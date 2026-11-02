using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckPrintPresentation()
    {
        var totals=new DataTable();totals.Columns.Add("Unusual future metric",typeof(decimal));totals.Columns.Add("Debtors",typeof(int));totals.Rows.Add(1234567.89m,2);
        var metrics=ReportPrintSummary.FromTotals(totals);
        Check(metrics[0].Value=="Rs. "+1234567.89m.ToString("#,##0.##"),"Authoritative money summary retains separators and historical precision");
        Check(metrics[1].Value=="2","Count summaries do not acquire a currency prefix");
        var parsed=ReportPrintSummary.Split("Campus: Boys   Report generated: 02-11-2026 12:00   Unusual future metric: wrong text   Debtors: 99",metrics);
        Check(parsed.Scope.Contains("Campus: Boys") && !parsed.Scope.Contains("wrong text") && !parsed.Scope.Contains("Debtors"),"Totals are removed from scope text when structured metrics are supplied");
        Check(parsed.Metrics[1].Value=="2","Structured totals beat a stale or rounded subtitle value");
        var fallback=ReportPrintSummary.Split("View: Students   Overdue Amount: Rs. 200   Due Today: Rs. 100   Not Yet Due: Rs. 700   Due Date Missing: Rs. 0");
        Check(fallback.Metrics.Count==4 && fallback.Scope=="View: Students","Legacy callers recognize every due-status metric");
        var rows=new DataTable();rows.Columns.Add("Student");rows.Columns.Add("Amount",typeof(decimal));rows.Rows.Add("Example",100m);rows.Rows.Add("TOTAL",100m);
        Check(ReportPrintSummary.DetailCount(rows)==1 && ReportPrintSummary.IsTotalRow(rows.Rows[1]),"TOTAL is emphasized without inflating detail count");
        rows.Rows[0]["Student"]="Netali";
        Check(!ReportPrintSummary.IsTotalRow(rows.Rows[0]),"Ordinary names starting Net do not become financial totals");
        Check(ReportPrintSummary.FromTotals(totals.Clone()).Count==0,"Empty totals do not invent summary values");
        var mixed=ReceiptPrintAmounts.Create(100m,900m,200m,true);
        Check(mixed.Discount==100m && mixed.FeesPayable==900m && mixed.ShowCredit && mixed.ShowTotal && mixed.SettlementCaption=="Total settled",
            "Mixed payment keeps price reduction separate from advance-credit settlement");
        Check(mixed.FeesPayable-200m-400m==300m,"1000 fee less 100 discount, 200 credit and 400 money leaves 300 due");
        var legacy=ReceiptPrintAmounts.Create(0m,0m,200m,false);
        Check(!legacy.ShowCredit && legacy.ShowTotal && legacy.SettlementCaption=="Net settled","Legacy adjustments preserve their independent net-settlement meaning");
        Check(!ReceiptPrintAmounts.Create(0m,900m,0m,true).ShowTotal,"Ordinary money-only receipt avoids a duplicate payment total");
    }
}
