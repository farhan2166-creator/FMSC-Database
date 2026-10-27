using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

// Print-only correction: acknowledgements establish fee figures, not a payment visit.
internal static class FeeCarryForwardLabels
{
    internal const string Query="""
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        DECLARE @Before bigint=CASE WHEN @ReceiptNumber IS NULL THEN
          (SELECT COALESCE(MAX(VisitSequence),0)+1 FROM dbo.CollectionReceipts)
          ELSE (SELECT VisitSequence FROM dbo.CollectionReceipts WHERE ReceiptNumber=@ReceiptNumber) END;
        IF @Before IS NULL THROW 54931,'The saved receipt could not be found. Reload before printing.',1;
        SELECT C.ChargeId,CONVERT(bit,CASE WHEN P.PaymentMethod<>N'Fee changes' THEN 1
          WHEN LastPayment.BalanceAfter>0 THEN 1 ELSE 0 END) IsCarriedForward
        FROM OPENJSON(@Candidates) WITH(ChargeId bigint,PreviousReceiptNumber nvarchar(40)) C
        JOIN dbo.CollectionReceipts P ON P.ReceiptNumber=C.PreviousReceiptNumber
        OUTER APPLY(SELECT TOP(1) L.BalanceAfter FROM dbo.CollectionReceipts R
            CROSS APPLY OPENJSON(COALESCE(JSON_QUERY(R.SnapshotJson,'$.AccountLines'),JSON_QUERY(R.SnapshotJson,'$.Lines')))
              WITH(ChargeId bigint,BalanceAfter decimal(12,2)) L
            WHERE R.VisitSequence<@Before AND R.PaymentMethod<>N'Fee changes'
              AND L.ChargeId=C.ChargeId ORDER BY R.VisitSequence DESC) LastPayment
        WHERE P.VisitSequence<@Before;
        """;

    internal static async Task ApplyAsync(DataTable lines,string? receiptNumber=null,Func<SqlParameter[],Task<DataTable>>? read=null)
    {
        if(!lines.Columns.Contains("IsCarriedForward")||!lines.Columns.Contains("PreviousReceiptNumber")||!lines.Columns.Contains("OpeningBalance"))return;
        string key=lines.Columns.Contains("ChargeId")?"ChargeId":"BillingChargeId";
        var candidates=lines.AsEnumerable().Where(row=>!row.IsNull("IsCarriedForward")&&Convert.ToBoolean(row["IsCarriedForward"])
            &&!row.IsNull("OpeningBalance")&&Convert.ToDecimal(row["OpeningBalance"])>0
            &&!string.IsNullOrWhiteSpace(Convert.ToString(row["PreviousReceiptNumber"]))).ToArray();
        if(candidates.Length==0)return;
        string json=JsonSerializer.Serialize(candidates.Select(row=>new {ChargeId=Convert.ToInt64(row[key]),PreviousReceiptNumber=Convert.ToString(row["PreviousReceiptNumber"])}).Distinct());
        read??=args=>DatabaseHelper.QuerySqlAsync(Query,args);
        using var classification=await read(new[]{DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.P("@ReceiptNumber",receiptNumber),DatabaseHelper.P("@Candidates",json)});
        var byCharge=classification.AsEnumerable().ToDictionary(row=>Convert.ToInt64(row["ChargeId"]),row=>Convert.ToBoolean(row["IsCarriedForward"]));
        bool readOnly=lines.Columns["IsCarriedForward"]!.ReadOnly;
        try
        {
            lines.Columns["IsCarriedForward"]!.ReadOnly=false;
            foreach(var row in candidates)
                if(byCharge.TryGetValue(Convert.ToInt64(row[key]),out bool carried))row["IsCarriedForward"]=carried;
        }
        finally{lines.Columns["IsCarriedForward"]!.ReadOnly=readOnly;}
    }
}
