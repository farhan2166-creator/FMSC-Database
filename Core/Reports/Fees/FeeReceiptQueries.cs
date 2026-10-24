namespace FMSC_SMS.Core;

internal static class FeeReceiptQueries
{
    internal static async Task<decimal> QueryCurrentBalanceAsync(int admissionId, string readPermission = "Fees.View")
    {
        using var balances = await Task.Run(() => DatabaseHelper.QuerySql("""
            EXEC dbo.RequirePermission @UserId,@ReadPermission;
            SELECT COALESCE(SUM(dbo.GetFeeChargeBalance(B.BillingChargeId)),0) Balance
            FROM dbo.BillingCharges B
            WHERE B.AdmissionId=@AdmissionId AND B.Status<>N'Cancelled'
                AND dbo.GetFeeChargeBalance(B.BillingChargeId)>0;
            """, DatabaseHelper.Int("@UserId",CurrentUser.UserId),
            DatabaseHelper.Int("@AdmissionId",admissionId),DatabaseHelper.P("@ReadPermission",readPermission)));
        return Convert.ToDecimal(balances.Rows[0]["Balance"]);
    }

    internal static async Task ApplyCurrentBalanceAsync(DataTable reportData, string readPermission = "Fees.View",
        Func<string,Task<DataTable>>? loadPayments = null, Func<int,Task<decimal>>? loadBalance = null)
    {
        loadPayments ??= receipt => QueryPaymentsAsync(receipt, readPermission);
        loadBalance ??= admissionId => QueryCurrentBalanceAsync(admissionId, readPermission);
        var balances = new Dictionary<int,decimal>();
        foreach (var page in reportData.AsEnumerable().GroupBy(row => row.Field<int>("ReceiptPageKey")))
        {
            var first = page.First();
            string receipt = first.Field<string>("ReceiptNumber") ?? "";
            string admissionNumber = first.Field<string>("AdmissionNumberText") ?? "";
            using var payments = await loadPayments(receipt);
            var originals = payments.AsEnumerable().Where(row =>
                row.Field<string>("AdmissionNumber") == admissionNumber && row.Field<string>("ReceiptNumber") == receipt
                && (first.Field<int>("PaymentEnrollmentId") == 0
                    || row.Field<int>("EnrollmentId") == first.Field<int>("PaymentEnrollmentId"))).ToList();
            if (originals.Count == 0)
                throw new InvalidOperationException("The original payment could not be read. Reload the student before printing.");
            decimal original = originals.Sum(row => Convert.ToDecimal(row["TotalAmount"]));
            decimal refunded = originals.Sum(row => Convert.ToDecimal(row["Refunded"]));
            decimal corrected = originals.Sum(row => Convert.ToDecimal(row["Corrected"]));
            decimal reversed = originals.Where(row => row.Field<string>("Status") == "Reversed")
                .Sum(row => Convert.ToDecimal(row["TotalAmount"]));
            decimal net = Math.Max(0m,original-refunded-corrected-reversed);
            bool adjusted = refunded > 0m || corrected > 0m || reversed > 0m;
            string status = reversed > 0m ? "Cancelled" : refunded > 0m && corrected > 0m
                ? "Refunded / corrected" : refunded > 0m ? (net > 0m ? "Partially refunded" : "Fully refunded")
                : corrected > 0m ? (net > 0m ? "Partially corrected" : "Fully corrected") : "";
            int admissionId = originals[0].Field<int>("AdmissionId");
            if (!balances.TryGetValue(admissionId, out decimal balance))
                balances[admissionId] = balance = await loadBalance(admissionId);
            foreach (var row in page)
            {
                row["ShowBalance"] = true;
                row["BalanceRemaining"] = balance;
                row["OriginalPaid"] = original;
                row["RefundAmount"] = refunded;
                row["CorrectionAmount"] = corrected;
                row["ReversedAmount"] = reversed;
                row["NetPaid"] = net;
                row["ShowAdjustments"] = adjusted;
                row["ReceiptStatus"] = status;
            }
        }
    }

    private static Task<DataTable> QueryPaymentsAsync(string receipt, string readPermission)
    {
        return Task.Run(() => DatabaseHelper.QuerySql("""
                EXEC dbo.RequirePermission @UserId,@ReadPermission;
                SELECT P.AdmissionId,P.EnrollmentId,COALESCE(S.AdmissionNumber,S.QuickReference,N'') AdmissionNumber,P.ReceiptNumber,P.Status,P.TotalAmount,
                    ISNULL(A.Refunded,0) Refunded,ISNULL(A.Corrected,0) Corrected
                FROM dbo.FeePayments P
                JOIN dbo.Students S ON S.AdmissionId=P.AdmissionId
                OUTER APPLY (SELECT
                    -SUM(CASE WHEN X.PaymentMethod=N'Refund' THEN X.TotalAmount ELSE 0 END) Refunded,
                    -SUM(CASE WHEN X.PaymentMethod=N'Correction' THEN X.TotalAmount ELSE 0 END) Corrected
                    FROM dbo.FeePayments X WHERE X.RelatedFeePaymentId=P.FeePaymentId
                    AND X.Status=N'Completed' AND X.TotalAmount<0 AND X.IsCreditApplication=0) A
                WHERE P.RelatedFeePaymentId IS NULL AND P.IsCreditApplication=0
                    AND P.ReceiptNumber=@ReceiptNumber;
                """, DatabaseHelper.Int("@UserId",CurrentUser.UserId),
                DatabaseHelper.P("@ReceiptNumber",receipt),DatabaseHelper.P("@ReadPermission",readPermission)));
    }

    internal static Task<DataTable> QueryStudentAsync(int admissionId)
    {
        var parameters = new[] { DatabaseHelper.Int("@UserId",CurrentUser.UserId),
            DatabaseHelper.Int("@AdmissionId",admissionId) };
        return Task.Run(() => DatabaseHelper.QuerySql("""
            EXEC dbo.RequirePermission @UserId,N'Fees.View';
            SELECT StudentName,FatherName FROM dbo.Students WHERE AdmissionId=@AdmissionId;
            """,parameters));
    }
    internal static Task<DataTable> QueryLinesAsync(string receiptNumber, string readPermission = "Fees.View")
    {
        var parameters = new[] {
            DatabaseHelper.Int("@UserId", CurrentUser.UserId),
            DatabaseHelper.P("@ReceiptNumber", receiptNumber),
            DatabaseHelper.P("@ReadPermission", readPermission)
        };
        return DatabaseHelper.QueryUserAsync("GetFeeReceiptLines", parameters);
    }
}
