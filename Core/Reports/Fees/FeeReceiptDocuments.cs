namespace FMSC_SMS.Core;

/// <summary>Loads saved collections and routes them to the matching receipt report.</summary>
internal static class FeeReceiptDocuments
{
    internal static async Task<bool> TryShowAsync(Form? owner,InstitutionProfile.PrintIdentity institution,string receiptNumber)
    {
        using var result=await DatabaseHelper.QueryUserSetAsync("GetCollectionReceipt",DatabaseHelper.P("@ReceiptNumber",receiptNumber));
        if(result.Tables.Count<3 || result.Tables[0].Rows.Count==0)return false;
        await RefreshBalancesAsync(result.Tables[0].Rows[0],result.Tables[1]);
        using var data=BuildData(institution,result.Tables[0].Rows[0],result.Tables[1],result.Tables[2]);
        Show(owner,data);
        return true;
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
