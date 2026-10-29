namespace FMSC_SMS.Core;

internal static class FeeReceiptChildCopy
{
    internal static DataSet Extract(DataSet saved,int admissionId)
    {
        if(saved.Tables.Count<3||saved.Tables[0].Rows.Count!=1)throw new InvalidOperationException("The saved receipt is incomplete.");
        var member=saved.Tables[1].AsEnumerable().SingleOrDefault(r=>Convert.ToInt32(r["AdmissionId"])==admissionId)
            ??throw new InvalidOperationException("This child is not on the original receipt.");
        var result=saved.Clone();result.Tables[0].ImportRow(saved.Tables[0].Rows[0]);result.Tables[1].ImportRow(member);
        for(int i=2;i<saved.Tables.Count;i++)
            foreach(var row in saved.Tables[i].AsEnumerable().Where(r=>Convert.ToInt32(r["AdmissionId"])==admissionId))result.Tables[i].ImportRow(row);
        var header=result.Tables[0].Rows[0];
        foreach(string field in new[]{"CashReceived","CashApplied","CreditApplied","DiscountApplied","BalanceRemaining","CreditRemaining"})
        {
            if(!member.Table.Columns.Contains(field)||member.IsNull(field)){result.Dispose();throw new InvalidOperationException("The child's saved amounts cannot be confirmed. Reprint the original family receipt instead.");}
            header[field]=member[field];
        }
        header["ReceiptStatus"]=(Convert.ToString(header["ReceiptStatus"])+" Child copy — this student's share of the original family receipt only.").Trim();
        return result;
    }
}
