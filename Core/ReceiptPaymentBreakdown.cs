namespace FMSC_SMS.Core;

internal static class ReceiptPaymentBreakdown
{
    // Read the original participants from the saved receipt, never today's family.
    // NetSettled uses the same allocation/adjustment calculation as
    // GetStudentCollectionReceipts, once for each participant instead of one child.
    internal const string Sql = """
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        SELECT R.ReceiptNumber,R.TransactionDate,R.CreatedAt,R.PaymentMethod,S.AdmissionId,S.AdmissionNumber,S.StudentName,
            S.CashApplied,S.CreditApplied,S.BalanceRemaining,R.CashApplied+R.CreditApplied OriginalTotal,
            R.CashReceived ReceiptCashReceived,R.CreditApplied ReceiptCreditApplied,
            S.CashApplied+S.CreditApplied-COALESCE(N.NetSettled,0) Adjusted,
            COALESCE(N.NetSettled,0) NetSettled,
            CASE WHEN N.PaymentCount>0 AND N.ReversedCount=N.PaymentCount THEN N'Reversed'
                 WHEN N.ReversedCount>0 THEN N'Partially reversed'
                 WHEN S.CashApplied+S.CreditApplied>COALESCE(N.NetSettled,0) THEN N'Adjusted'
                 WHEN S.CashApplied+S.CreditApplied>0 THEN N'Paid' ELSE N'No payment' END PaymentStatus
        FROM dbo.CollectionReceipts R
        CROSS APPLY OPENJSON(R.SnapshotJson,'$.Students') WITH
            (AdmissionId int,AdmissionNumber nvarchar(50),StudentName nvarchar(200),CashApplied decimal(12,2),CreditApplied decimal(12,2),BalanceRemaining decimal(12,2)) S
        OUTER APPLY(SELECT COUNT(*) PaymentCount,
            SUM(CASE WHEN P.Status=N'Reversed' THEN 1 ELSE 0 END) ReversedCount,
            SUM(CASE WHEN P.Status<>N'Completed' THEN 0 ELSE
                CASE WHEN COALESCE(A.Applied,0)+COALESCE(X.Adjusted,0)<0 THEN 0
                     ELSE COALESCE(A.Applied,0)+COALESCE(X.Adjusted,0) END END) NetSettled
            FROM dbo.CollectionReceiptPayments L JOIN dbo.FeePayments P ON P.FeePaymentId=L.FeePaymentId
            OUTER APPLY(SELECT SUM(Amount) Applied FROM dbo.FeePaymentAllocations WHERE FeePaymentId=P.FeePaymentId) A
            OUTER APPLY(SELECT SUM(FA.Amount) Adjusted FROM dbo.FeePayments FP
                JOIN dbo.FeePaymentAllocations FA ON FA.FeePaymentId=FP.FeePaymentId
                WHERE FP.RelatedFeePaymentId=P.FeePaymentId AND FP.Status=N'Completed') X
            WHERE L.CollectionReceiptId=R.CollectionReceiptId AND P.AdmissionId=S.AdmissionId) N
        WHERE R.PaymentMethod<>N'Fee changes'
            AND EXISTS(SELECT 1 FROM OPENJSON(R.SnapshotJson,'$.Students') WITH(AdmissionId int) M WHERE M.AdmissionId=@AdmissionId)
        ORDER BY R.CreatedAt DESC,S.AdmissionId;
        """;

    internal static Task<DataTable> ReadAsync(int admissionId)=>DatabaseHelper.QuerySqlAsync(Sql,
        DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@AdmissionId",admissionId));

    internal static Task<DataTable> ReadForStudentsAsync(int[] admissions)=>DatabaseHelper.QuerySqlAsync(
        Sql.Replace("M.AdmissionId=@AdmissionId","M.AdmissionId IN (SELECT TRY_CONVERT(int,value) FROM OPENJSON(@Admissions))",StringComparison.Ordinal),
        DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.P("@Admissions",System.Text.Json.JsonSerializer.Serialize(admissions)));
}
