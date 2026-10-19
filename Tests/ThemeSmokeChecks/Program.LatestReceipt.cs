using System.Data;
using System.Reflection;
using FMSC_SMS.Pages.Finance.Collection;

internal static partial class Program
{
    private static void CheckLatestReceipt()
    {
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
}
