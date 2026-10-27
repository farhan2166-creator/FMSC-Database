using System.Data;
using System.Reflection;
using FMSC_SMS.Pages.Finance.Collection;

internal static partial class Program
{
    private static void CheckLatestReceipt()
    {
        CheckRecentReceiptActivity();
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var select = typeof(FeeCollectionPage).GetMethod("LatestPrintableReceipt", flags)!;
        using var statement = new DataTable();
        statement.Columns.Add("FeePaymentId", typeof(int));
        statement.Columns.Add("ReceiptNumber");
        statement.Columns.Add("TransactionDate", typeof(DateTime));
        statement.Columns.Add("TransactionType");
        statement.Columns.Add("PaymentMethod");
        statement.Columns.Add("EnrollmentId", typeof(int));
        using var issued = new DataTable();
        issued.Columns.Add("CollectionReceiptId", typeof(int));
        issued.Columns.Add("ReceiptNumber");
        issued.Columns.Add("TransactionDate", typeof(DateTime));
        issued.Columns.Add("PaymentMethod");
        using var payments = new DataTable();
        payments.Columns.Add("FeePaymentId", typeof(int));
        payments.Columns.Add("Status");
        payments.Columns.Add("CreditKind");
        string? Latest() => (string?)select.Invoke(null, new object[] { statement, issued, payments });
        var day = new DateTime(2026, 10, 19);
        void Payment(int id, string receipt, DateTime date, string status="Completed", string kind="",
            string transaction="PAYMENT", string method="Cash", int enrollment=1)
        {
            statement.Rows.Add(id, receipt, date, transaction, method, enrollment);
            payments.Rows.Add(id, status, kind);
        }
        Check(Latest() is null, "No payment history leaves Print Receipt unavailable");
        Payment(1,"FMSC-2026-0001",day);
        Check(Latest()=="FMSC-2026-0001", "Legacy individual receipts are restored without a new payment");
        issued.Rows.Add(1,"FMSC-2026-0002",day.AddMinutes(1));
        Check(Latest()=="FMSC-2026-0002", "A newer issued family receipt takes precedence");
        Payment(3,"FMSC-2026-0003",day.AddMinutes(2),enrollment:99);
        Check(Latest()=="FMSC-2026-0003", "Latest receipt is independent of the currently selected placement");
        Payment(4,"FMSC-2026-0004",day.AddMinutes(3),status:"Reversed");
        Payment(5,"REFUND",day.AddMinutes(4),transaction:"REFUND",method:"Refund");
        Check(Latest()=="FMSC-2026-0003", "Reversed payments and refunds do not replace the last printable receipt");
        Payment(6,"FMSC-CR-2026-0006",day.AddMinutes(5),kind:"Application",transaction:"CREDIT",method:"Account Credit");
        Check(Latest()=="FMSC-CR-2026-0006", "Legacy credit-only settlement receipts can be restored");
        issued.Rows.Add(2,"FMSC-2026-0007",day.AddMinutes(6));
        Payment(7,"FMSC-CR-2026-0007",day.AddMinutes(6),kind:"Application",method:"Account Credit");
        Check(Latest()=="FMSC-2026-0007", "Unified receipt wins over its linked payment reference at the same time");
        issued.Rows.Add(3,"FEE-CHANGE-NOTE",day.AddMinutes(7),"Fee changes");
        Check(Latest()=="FMSC-2026-0007","A newer fee-change note never replaces Print Receipt's latest payment");
        var visible=(IEnumerable<DataRow>)typeof(FeeCollectionPage).GetMethod("PaymentReceiptRows",flags)!.Invoke(null,new object[]{issued})!;
        Check(visible.All(row=>Convert.ToString(row["ReceiptNumber"])!="FEE-CHANGE-NOTE")&&issued.Rows.Count==3,
            "Recent Payments excludes fee-change notes while preserving their saved records");

        using var page = new FeeCollectionPage(false);
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(FeeCollectionPage).GetField("lastGroupReceiptNumber",instance)!.SetValue(page,"PREVIOUS-STUDENT");
        var button = (Button)typeof(FeeCollectionPage).GetField("printReceiptButton",instance)!.GetValue(page)!;
        button.Enabled=true;
        var clear = (Task)typeof(FeeCollectionPage).GetMethod("SelectStudentAsync",instance)!.Invoke(page,new object[] { 0 })!;
        clear.GetAwaiter().GetResult();
        Check(!button.Enabled && typeof(FeeCollectionPage).GetField("lastGroupReceiptNumber",instance)!.GetValue(page) is null,
            "Changing the student clears the previous receipt before loading anything else");
    }

    private static void CheckRecentReceiptActivity()
    {
        var activity=typeof(FeeCollectionPage).GetMethod("SavedReceiptActivity",BindingFlags.NonPublic|BindingFlags.Static)!;
        using var receipts=new DataTable();
        receipts.Columns.Add("ReceiptNumber");receipts.Columns.Add("StudentCount",typeof(int));
        receipts.Columns.Add("CashApplied",typeof(decimal));receipts.Columns.Add("CreditApplied",typeof(decimal));
        var family=receipts.Rows.Add("FR-2026-00007",3,7600m,0m);
        var single=receipts.Rows.Add("R-2026-00001",1,2800m,0m);
        var credit=receipts.Rows.Add("R-CREDIT",1,0m,500m);
        using var payments=new DataTable();payments.Columns.Add("ReceiptNumber");payments.Columns.Add("Status");
        string Label(DataRow row)=>(string)activity.Invoke(null,new object[]{row,payments})!;
        var selected=payments.Rows.Add("FR-2026-00007","Completed");
        payments.Rows.Add("R-2026-00001","Reversed");
        Check(Label(family)=="Family payment","An untouched student share stays a family payment despite an unrelated reversed receipt");
        selected["Status"]="Reversed";
        Check(Label(family)=="Reversed","A fully reversed student share is labelled Reversed even though siblings still have completed payments");
        Check(Label(single)=="Reversed","A saved individual receipt also displays its reversal");
        var remaining=payments.Rows.Add("FR-2026-00007","Completed");
        Check(Label(family)=="Partially reversed","Multiple payment parts for this student distinguish a partial reversal from a full reversal");
        remaining["Status"]="Reversed";
        Check(Label(family)=="Reversed","All reversed payment parts show a full reversal");
        selected["Status"]="Completed";remaining["Status"]="Completed";
        Check(Label(family)=="Family payment","Another sibling's current view remains a family payment when their own shares are completed");
        payments.Rows[1]["Status"]="Completed";
        Check(Label(single)=="Payment"&&Label(credit)=="Credit settlement","Unchanged individual and credit settlement labels remain intact");
        Check((decimal)family["CashApplied"]==7600m&&(int)family["StudentCount"]==3,"Activity labels preserve saved receipt amounts and family scope");
    }
}
