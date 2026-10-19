namespace FMSC_SMS.Core;

internal static class FeeReceiptQueries
{
    internal static async Task ApplyCurrentBalanceAsync(DataTable reportData, string readPermission = "Fees.View")
    {
        foreach (var page in reportData.AsEnumerable().GroupBy(row => row.Field<int>("ReceiptPageKey")))
        {
            var first = page.First();
            string receipt = first.Field<string>("ReceiptNumber") ?? "";
            string admissionNumber = first.Field<string>("AdmissionNumberText") ?? "";
            var payments = await Task.Run(() => DatabaseHelper.QuerySql("""
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
                    AND P.AdmissionId IN (SELECT AdmissionId FROM dbo.FeePayments WHERE ReceiptNumber=@ReceiptNumber)
                ORDER BY P.PaymentDate DESC,P.FeePaymentId DESC;
                """, DatabaseHelper.Int("@UserId",CurrentUser.UserId),
                DatabaseHelper.P("@ReceiptNumber",receipt),DatabaseHelper.P("@ReadPermission",readPermission)));
            var latest = payments.AsEnumerable().FirstOrDefault(row =>
                row.Field<string>("AdmissionNumber") == admissionNumber);
            bool show = latest is not null && latest.Field<string>("ReceiptNumber") == receipt
                && latest.Field<string>("Status") == "Completed";
            var originals = payments.AsEnumerable().Where(row =>
                row.Field<string>("AdmissionNumber") == admissionNumber && row.Field<string>("ReceiptNumber") == receipt
                && (first.Field<int>("PaymentEnrollmentId") == 0
                    || row.Field<int>("EnrollmentId") == first.Field<int>("PaymentEnrollmentId"))).ToList();
            if (originals.Count == 0 && page.Any(row => Convert.ToDecimal(row["Amount"]) != 0m))
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
            show &= net > 0m && CurrentUser.HasPermission("Fees.View");
            decimal balance = 0m;
            if (show)
            {
                int admissionId = latest!.Field<int>("AdmissionId");
                var charges = await DatabaseHelper.QueryUserAsync("GetOutstandingCharges",
                    DatabaseHelper.Int("@AdmissionId", admissionId),
                    DatabaseHelper.Int("@EnrollmentId", 0),
                    DatabaseHelper.Date("@AsOfDate", DateTime.Today));
                balance = charges.AsEnumerable().Sum(row => Convert.ToDecimal(row["Balance"]));
            }
            foreach (var row in page)
            {
                row["ShowBalance"] = show;
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
