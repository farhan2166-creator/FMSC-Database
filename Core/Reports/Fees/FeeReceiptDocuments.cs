namespace FMSC_SMS.Core;

/// <summary>Loads saved collections and routes them to the matching receipt report.</summary>
internal static class FeeReceiptDocuments
{
    internal static async Task<bool> TryShowAsync(Form? owner,InstitutionProfile.PrintIdentity institution,string receiptNumber)
    {
        using var result=await DatabaseHelper.QueryUserSetAsync("GetCollectionReceipt",DatabaseHelper.P("@ReceiptNumber",receiptNumber));
        if(result.Tables.Count<3 || result.Tables[0].Rows.Count==0)return false;
        using var data=BuildData(institution,result.Tables[0].Rows[0],result.Tables[1],result.Tables[2]);
        Show(owner,data);
        return true;
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
