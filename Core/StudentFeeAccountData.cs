using FMSC_SMS.Pages.Finance.StudentAccounts;

namespace FMSC_SMS.Core;

// Composition only: balances, documents and eligibility remain owned by their existing readers.
internal sealed record StudentFeeAccountData(DataTable Receipts,DataTable Payments,decimal Outstanding,decimal Credit,DataTable? CreditEntries=null)
{
    internal static async Task<StudentFeeAccountData> ReadAsync(int admissionId)
    {
        var outstanding=await FeeReceiptQueries.QueryCurrentBalanceAsync(admissionId);
        using var credit=await DatabaseHelper.QueryUserSetAsync("GetStudentCredit",DatabaseHelper.Int("@AdmissionId",admissionId));
        using var receipts=await DatabaseHelper.QueryUserSetAsync("GetStudentCollectionReceipts",DatabaseHelper.Int("@AdmissionId",admissionId));
        var payments=await StudentFeeAdjustmentsPage.ReadPaymentHistoryAsync(CurrentUser.UserId,admissionId);
        DescribePayments(payments);
        return new(receipts.Tables[0].Copy(),payments,outstanding,Convert.ToDecimal(credit.Tables[0].Rows[0]["AvailableCredit"]),credit.Tables[1].Copy());
    }

    internal static PaymentAdjustmentState Eligibility(DataRow row)=>PaymentAdjustmentState.Create(
        Text(row,"Status"),Text(row,"PaymentMethod"),Money(row,"PaidAmount"),Number(row,"RelatedFeePaymentId")>0,
        Money(row,"Refunded"),Money(row,"Corrected"),row.Table.Columns.Contains("IsCreditEntry")&&row["IsCreditEntry"] is true,
        Money(row,"ReleasedToCredit"));

    internal static async Task<DataTable> ReadReceiptPaymentsAsync(string receipt)
    {
        using var links=await DatabaseHelper.QuerySqlAsync("""
            EXEC dbo.RequirePermission @UserId,N'Fees.View';
            SELECT P.FeePaymentId,P.AdmissionId FROM dbo.CollectionReceipts R
            JOIN dbo.CollectionReceiptPayments L ON L.CollectionReceiptId=R.CollectionReceiptId
            JOIN dbo.FeePayments P ON P.FeePaymentId=L.FeePaymentId WHERE R.ReceiptNumber=@Receipt;
            """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.P("@Receipt",receipt));
        var result=new DataTable();
        var ids=links.AsEnumerable().Select(row=>Number(row,"FeePaymentId")).ToHashSet();
        foreach(int admission in links.AsEnumerable().Select(row=>Number(row,"AdmissionId")).Distinct())
        {
            using var history=await StudentFeeAdjustmentsPage.ReadPaymentHistoryAsync(CurrentUser.UserId,admission);
            var matching=history.Clone();matching.Columns.Add("AccountAdmissionId",typeof(int));
            foreach(var row in history.AsEnumerable().Where(row=>ids.Contains(Number(row,"FeePaymentId"))||ids.Contains(Number(row,"RelatedFeePaymentId"))))
            {var added=matching.NewRow();foreach(DataColumn c in history.Columns)added[c.ColumnName]=row[c];added["AccountAdmissionId"]=admission;matching.Rows.Add(added);}
            result.Merge(matching,true,MissingSchemaAction.Add);matching.Dispose();
        }
        DescribePayments(result);
        return result;
    }
    internal static void DescribePayments(DataTable payments)
    {
        payments.Columns.Add("RemainingRecorded",typeof(decimal));payments.Columns.Add("AccountState");
        foreach(DataRow row in payments.Rows){var state=Eligibility(row);row["RemainingRecorded"]=state.Remaining;row["AccountState"]=state.Status;}
    }
    internal static string Text(DataRow row,string name)=>row.Table.Columns.Contains(name)&&!row.IsNull(name)?Convert.ToString(row[name])??"":"";
    internal static decimal Money(DataRow row,string name)=>row.Table.Columns.Contains(name)&&!row.IsNull(name)?Convert.ToDecimal(row[name]):0;
    internal static int Number(DataRow row,string name)=>(int)Money(row,name);
}
