using System.Data;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckCurrentReceiptBalances()
    {
        using var header=new DataTable();
        header.Columns.Add("BalanceRemaining",typeof(decimal));
        header.Columns.Add("CashApplied",typeof(decimal));
        var saved=header.Rows.Add(9000m,1000m);
        using var students=new DataTable();
        students.Columns.Add("AdmissionId",typeof(int));
        students.Columns.Add("BalanceRemaining",typeof(decimal));
        students.Rows.Add(1,4000m);students.Rows.Add(2,5000m);
        decimal due=700m;
        Task<decimal> Balance(int id)=>Task.FromResult(id==1?due:300m);
        FeeReceiptDocuments.RefreshBalancesAsync(saved,students,Balance).GetAwaiter().GetResult();
        Check(saved.Field<decimal>("BalanceRemaining")==1000m
            && students.Rows[0].Field<decimal>("BalanceRemaining")==700m
            && students.Rows[1].Field<decimal>("BalanceRemaining")==300m,
            "An old family receipt replaces snapshot balances with current child balances and their total");
        Check(saved.Field<decimal>("CashApplied")==1000m,"Refreshing outstanding preserves the original payment");
        due=200m;
        FeeReceiptDocuments.RefreshBalancesAsync(saved,students,Balance).GetAwaiter().GetResult();
        Check(saved.Field<decimal>("BalanceRemaining")==500m,"Reopening the same receipt reads balances again after a later payment");
        students.Rows.RemoveAt(1);due=0m;
        FeeReceiptDocuments.RefreshBalancesAsync(saved,students,Balance).GetAwaiter().GetResult();
        Check(saved.Field<decimal>("BalanceRemaining")==0m && students.Rows[0].Field<decimal>("BalanceRemaining")==0m,
            "A fully settled student prints zero outstanding on an old receipt");

        using var receipts=FeeDocumentData.Create();
        string[] numbers={"OLD","LATEST","REFUNDED","CANCELLED","CORRECTED"};
        foreach(string number in numbers)
        {
            var row=receipts.NewRow();
            row["ReceiptPageKey"]=receipts.Rows.Count+1;row["ReceiptNumber"]=number;
            row["AdmissionNumberText"]="1001";row["Amount"]=1000m;row["PaymentEnrollmentId"]=1;
            receipts.Rows.Add(row);
        }
        Task<DataTable> Payments(string number)
        {
            var data=new DataTable();
            data.Columns.Add("AdmissionId",typeof(int));data.Columns.Add("EnrollmentId",typeof(int));
            foreach(string name in new[]{"AdmissionNumber","ReceiptNumber","Status"})data.Columns.Add(name);
            foreach(string name in new[]{"TotalAmount","Refunded","Corrected"})data.Columns.Add(name,typeof(decimal));
            data.Rows.Add(1,1,"1001",number,number=="CANCELLED"?"Reversed":"Completed",1000m,number=="REFUNDED"?1000m:0m,number=="CORRECTED"?200m:0m);
            return Task.FromResult(data);
        }
        int reads=0;due=450m;
        Task<decimal> LegacyBalance(int id){reads++;return Task.FromResult(due);}
        FeeReceiptQueries.ApplyCurrentBalanceAsync(receipts,"Fees.Statement.View",Payments,LegacyBalance).GetAwaiter().GetResult();
        Check(receipts.AsEnumerable().All(row=>row.Field<bool>("ShowBalance") && row.Field<decimal>("BalanceRemaining")==450m),
            "Every legacy/annual receipt shows the same current balance, including old, refunded and cancelled receipts");
        Check(reads==1,"Annual receipts read one current balance per student for consistent page totals");
        Check(receipts.Rows[2].Field<decimal>("NetPaid")==0m && receipts.Rows[2].Field<string>("ReceiptStatus")=="Fully refunded"
            && receipts.Rows[3].Field<string>("ReceiptStatus")=="Cancelled"
            && receipts.Rows[4].Field<decimal>("NetPaid")==800m && receipts.Rows[4].Field<string>("ReceiptStatus")=="Partially corrected",
            "Current outstanding does not hide refund or cancellation history");
        due=0m;
        FeeReceiptQueries.ApplyCurrentBalanceAsync(receipts,"Fees.Statement.View",Payments,LegacyBalance).GetAwaiter().GetResult();
        Check(receipts.AsEnumerable().All(row=>row.Field<bool>("ShowBalance") && row.Field<decimal>("BalanceRemaining")==0m),
            "Zero outstanding stays visible on every legacy/annual receipt");
        bool failed=false;
        try
        {
            FeeReceiptDocuments.RefreshBalancesAsync(saved,students,_=>Task.FromException<decimal>(new InvalidOperationException("Balance unavailable")))
                .GetAwaiter().GetResult();
        }
        catch(InvalidOperationException){failed=true;}
        Check(failed,"A failed current balance read stops printing instead of using a stale snapshot");
    }
}
