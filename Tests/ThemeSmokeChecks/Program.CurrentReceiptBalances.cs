using System.Data;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckCurrentReceiptBalances()
    {
        using var header=new DataTable();
        header.Columns.Add("ReceiptNumber");header.Columns.Add("BalanceRemaining",typeof(decimal));header.Columns.Add("CashApplied",typeof(decimal));
        var saved=header.Rows.Add("FAMILY",9000m,1000m);
        using var students=new DataTable();
        students.Columns.Add("AdmissionId",typeof(int));students.Columns.Add("BalanceRemaining",typeof(decimal));
        students.Rows.Add(1,4000m);students.Rows.Add(2,5000m);
        var recorded=new Dictionary<int,decimal>{{1,4000m},{2,5000m}};
        bool latest=false;int reads=0;decimal due=700m;
        Task<FeeReceiptQueries.ReceiptBalanceState> State(string _) => Task.FromResult(new FeeReceiptQueries.ReceiptBalanceState(latest,recorded));
        Task<decimal> Balance(int id){reads++;return Task.FromResult(id==1?due:300m);}
        bool Apply()=>FeeReceiptDocuments.RefreshBalancesAsync(saved,students,Balance,State).GetAwaiter().GetResult();
        Check(!Apply() && reads==0 && saved.Field<decimal>("BalanceRemaining")==9000m
            && students.Rows[0].Field<decimal>("BalanceRemaining")==4000m && students.Rows[1].Field<decimal>("BalanceRemaining")==5000m,
            "An older family receipt keeps its original child balances and never reads today's dues");
        latest=true;
        Check(Apply() && saved.Field<decimal>("BalanceRemaining")==1000m
            && students.Rows[0].Field<decimal>("BalanceRemaining")==700m && students.Rows[1].Field<decimal>("BalanceRemaining")==300m,
            "The latest family receipt prints current child balances and their total");
        Check(saved.Field<decimal>("CashApplied")==1000m,"Choosing the balance basis preserves the original payment");
        due=200m;Apply();
        Check(saved.Field<decimal>("BalanceRemaining")==500m,"The latest receipt rereads live dues on each print");
        latest=false;Apply();
        Check(saved.Field<decimal>("BalanceRemaining")==9000m,
            "Once a newer receipt exists, reprinting restores the original outstanding instead of a previous live figure");
        students.Rows.RemoveAt(1);Apply();
        Check(saved.Field<decimal>("BalanceRemaining")==4000m && students.Rows[0].Field<decimal>("BalanceRemaining")==4000m,
            "An older individual receipt retains its own recorded outstanding");
        latest=true;due=0m;Apply();
        Check(saved.Field<decimal>("BalanceRemaining")==0m,"A fully settled latest receipt prints zero current outstanding");

        using var receipts=FeeDocumentData.Create();
        string[] numbers={"OLD","LATEST","REFUNDED","CANCELLED","CORRECTED"};
        foreach(string number in numbers)
        {
            var row=receipts.NewRow();row["ReceiptPageKey"]=receipts.Rows.Count+1;row["ReceiptNumber"]=number;
            row["AdmissionNumberText"]="1001";row["Amount"]=1000m;row["PaymentEnrollmentId"]=1;receipts.Rows.Add(row);
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
        Task<FeeReceiptQueries.ReceiptBalanceState> LegacyState(string number)=>Task.FromResult(
            new FeeReceiptQueries.ReceiptBalanceState(number=="LATEST",new Dictionary<int,decimal>{{1,9000m}}));
        reads=0;due=450m;
        Task<decimal> LegacyBalance(int id){reads++;return Task.FromResult(due);}
        void LegacyApply()=>FeeReceiptQueries.ApplyCurrentBalanceAsync(receipts,"Fees.Statement.View",Payments,LegacyBalance,LegacyState).GetAwaiter().GetResult();
        LegacyApply();
        Check(receipts.AsEnumerable().All(row=>row.Field<bool>("ShowBalance") && row.Field<decimal>("BalanceRemaining")==
            (row.Field<string>("ReceiptNumber")=="LATEST"?450m:9000m)),
            "Annual and legacy printouts use current outstanding only for the latest receipt and snapshots for older receipts");
        Check(reads==1,"Only the latest receipt reads the student's current balance");
        Check(receipts.Rows[2].Field<decimal>("NetPaid")==0m && receipts.Rows[2].Field<string>("ReceiptStatus")=="Fully refunded"
            && receipts.Rows[3].Field<string>("ReceiptStatus")=="Cancelled"
            && receipts.Rows[4].Field<decimal>("NetPaid")==800m && receipts.Rows[4].Field<string>("ReceiptStatus")=="Partially corrected",
            "Changing the outstanding basis preserves refund, reversal and correction history");
        due=0m;LegacyApply();
        Check(receipts.Rows[1].Field<decimal>("BalanceRemaining")==0m && receipts.Rows[0].Field<decimal>("BalanceRemaining")==9000m,
            "Paying off today's dues does not rewrite an old receipt's outstanding");
        bool failed=false;
        try{FeeReceiptDocuments.RefreshBalancesAsync(saved,students,_=>Task.FromException<decimal>(new InvalidOperationException("Balance unavailable")),State).GetAwaiter().GetResult();}
        catch(InvalidOperationException){failed=true;}
        Check(failed,"A failed live read stops the latest receipt instead of silently printing a snapshot");

        using var unknown=receipts.Clone();var old=unknown.NewRow();
        old["ReceiptPageKey"]=1;old["ReceiptNumber"]="OLD";old["AdmissionNumberText"]="1001";old["PaymentEnrollmentId"]=1;unknown.Rows.Add(old);
        Task<FeeReceiptQueries.ReceiptBalanceState> Unknown(string _)=>Task.FromResult(new FeeReceiptQueries.ReceiptBalanceState(false,new Dictionary<int,decimal>()));
        failed=false;
        try{FeeReceiptQueries.ApplyCurrentBalanceAsync(unknown,"Fees.Statement.View",Payments,LegacyBalance,Unknown).GetAwaiter().GetResult();}
        catch(InvalidOperationException){failed=true;}
        Check(failed,"An old receipt with no recorded balance cannot silently substitute today's dues or an invented zero");
        old["ShowBalance"]=true;old["BalanceRemaining"]=6300m;
        FeeReceiptQueries.ApplyCurrentBalanceAsync(unknown,"Fees.Statement.View",Payments,LegacyBalance,Unknown).GetAwaiter().GetResult();
        Check(old.Field<decimal>("BalanceRemaining")==6300m,"An existing legacy receipt's explicitly saved balance remains usable");
    }
}
