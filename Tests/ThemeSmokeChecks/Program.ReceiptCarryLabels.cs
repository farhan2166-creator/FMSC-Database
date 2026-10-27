using System.Data;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckReceiptCarryLabels()
    {
        using var lines=new DataTable();lines.Columns.Add("ChargeId",typeof(long));lines.Columns.Add("OpeningBalance",typeof(decimal));
        lines.Columns.Add("IsCarriedForward",typeof(bool));lines.Columns.Add("PreviousReceiptNumber");lines.Columns.Add("AmountBefore",typeof(decimal));
        lines.Rows.Add(1L,5700m,true,"FEE-CHANGES",5700m);
        lines.Rows.Add(2L,2800m,true,"EARLIER-PAYMENT",2800m);
        lines.Rows.Add(3L,0m,false,"",400m);
        lines.Rows.Add(4L,100m,true,"FEE-CHANGES-AFTER-PAYMENT",100m);
        int reads=0;
        Task<DataTable> Read(Microsoft.Data.SqlClient.SqlParameter[] args)
        {
            reads++;
            Check(Convert.ToString(args.Single(arg=>arg.ParameterName=="@ReceiptNumber").Value)=="CURRENT-PAYMENT","Label lookup is limited to the saved receipt's earlier visits");
            string json=Convert.ToString(args.Single(arg=>arg.ParameterName=="@Candidates").Value)!;
            using var parsed=System.Text.Json.JsonDocument.Parse(json);
            Check(parsed.RootElement.GetArrayLength()==3,"Only carried-looking fee lines require historical label checks");
            var result=new DataTable();result.Columns.Add("ChargeId",typeof(long));result.Columns.Add("IsCarriedForward",typeof(bool));
            result.Rows.Add(1L,false);result.Rows.Add(2L,true);result.Rows.Add(4L,true);return Task.FromResult(result);
        }
        FeeCarryForwardLabels.ApplyAsync(lines,"CURRENT-PAYMENT",Read).GetAwaiter().GetResult();
        Check(!lines.Rows[0].Field<bool>("IsCarriedForward"),"A fee-change-only note does not turn the first payment into carried dues");
        Check(lines.Rows[1].Field<bool>("IsCarriedForward"),"An earlier partial payment retains the Due distinction for the remaining balance");
        Check(!lines.Rows[2].Field<bool>("IsCarriedForward"),"A newly billed fee remains a first-time fee");
        Check(lines.Rows[3].Field<bool>("IsCarriedForward"),"An acknowledgement after a real collection does not erase an existing carried balance");
        Check(lines.AsEnumerable().Sum(row=>row.Field<decimal>("AmountBefore"))==9000m&&reads==1,"Label correction never changes amounts or needs one lookup per fee");
        using var legacy=new DataTable();legacy.Columns.Add("AmountBefore",typeof(decimal));legacy.Rows.Add(500m);
        FeeCarryForwardLabels.ApplyAsync(legacy,read:_=>throw new Exception("Legacy receipts must not query visit metadata")).GetAwaiter().GetResult();
        Check(legacy.Rows[0].Field<decimal>("AmountBefore")==500m,"Older receipts without carried-balance metadata retain their saved presentation");
    }
}
