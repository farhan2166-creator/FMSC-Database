namespace FMSC_SMS.Core;

/// <summary>Loads saved collections and routes them to the matching receipt report.</summary>
internal static class FeeReceiptDocuments
{
    internal static async Task<bool> TryShowAsync(Form? owner,InstitutionProfile.PrintIdentity institution,string receiptNumber)
    {
        using var result=await DatabaseHelper.QueryUserSetAsync("GetCollectionReceipt",DatabaseHelper.P("@ReceiptNumber",receiptNumber));
        if(result.Tables.Count<3 || result.Tables[0].Rows.Count==0)return false;
        await RefreshBalancesAsync(result.Tables[0].Rows[0],result.Tables[1]);
        await ReadChildCashReceivedAsync(receiptNumber,result.Tables[0].Rows[0],result.Tables[1]);
        using var data=BuildData(institution,result.Tables[0].Rows[0],result.Tables[1],result.Tables[2]);
        Show(owner,data);
        return true;
    }

    // The collection snapshot stores each child's fee allocation. The linked
    // original payments identify who actually received any retained advance.
    private static async Task ReadChildCashReceivedAsync(string receiptNumber,DataRow header,DataTable students)
    {
        using var payments=await DatabaseHelper.QuerySqlAsync("""
            EXEC dbo.RequirePermission @UserId,N'Fees.View';
            SELECT P.AdmissionId,SUM(P.TotalAmount) CashReceived
            FROM dbo.CollectionReceipts R
            JOIN dbo.CollectionReceiptPayments L ON L.CollectionReceiptId=R.CollectionReceiptId
            JOIN dbo.FeePayments P ON P.FeePaymentId=L.FeePaymentId
            WHERE R.ReceiptNumber=@ReceiptNumber AND P.IsCreditApplication=0
            GROUP BY P.AdmissionId;
            """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.P("@ReceiptNumber",receiptNumber));
        ApplyChildCashReceived(header,students,payments);
    }

    internal static void ApplyChildCashReceived(DataRow header,DataTable students,DataTable payments)
    {
        var received=payments.AsEnumerable().ToDictionary(row=>Convert.ToInt32(row["AdmissionId"]),row=>Convert.ToDecimal(row["CashReceived"]));
        var ids=students.AsEnumerable().Select(row=>Convert.ToInt32(row["AdmissionId"])).ToHashSet();
        if(received.Keys.Any(id=>!ids.Contains(id))||received.Values.Sum()!=Convert.ToDecimal(header["CashReceived"]))
            throw new InvalidOperationException("The receipt's original payments could not be matched to its children. Reload before printing.");
        if(!students.Columns.Contains("CashReceived"))students.Columns.Add("CashReceived",typeof(decimal));
        foreach(DataRow student in students.Rows)
        {
            decimal amount=received.GetValueOrDefault(Convert.ToInt32(student["AdmissionId"]));
            if(amount<Convert.ToDecimal(student["CashApplied"]))
                throw new InvalidOperationException("The child's received payment could not be confirmed. Reload before printing.");
            student["CashReceived"]=amount;
        }
    }

    // Refresh only the printable data; the original collection snapshot stays intact.
    internal static async Task RefreshBalancesAsync(DataRow header,DataTable students,Func<int,Task<decimal>>? loadBalance=null)
    {
        loadBalance ??= admissionId=>FeeReceiptQueries.QueryCurrentBalanceAsync(admissionId);
        var balances=new Dictionary<int,decimal>();
        foreach(DataRow student in students.Rows)
        {
            int admissionId=Convert.ToInt32(student["AdmissionId"]);
            if(!balances.TryGetValue(admissionId,out decimal balance))
                balances[admissionId]=balance=await loadBalance(admissionId);
            student["BalanceRemaining"]=balance;
        }
        header["BalanceRemaining"]=balances.Values.Sum();
    }

    internal static DataTable BuildData(InstitutionProfile.PrintIdentity institution,DataRow header,DataTable students,DataTable lines) =>
        students.Rows.Count>1?FamilyReceiptReport.BuildData(institution,header,students,lines)
            :StudentReceiptReport.BuildData(institution,header,students,lines);

    internal static void Show(Form? owner,DataTable data)
    {
        if(data.Columns.Contains("FamilyRowKind"))FamilyReceiptReport.Show(owner,data);
        else StudentReceiptReport.Show(owner,data);
    }
}
