namespace FMSC_SMS.Core;

internal static class FeeReceiptPlacement
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
                SELECT P.AdmissionId,P.EnrollmentId,S.AdmissionNumber,P.ReceiptNumber,P.Status,P.TotalAmount,
                    ISNULL(A.Refunded,0) Refunded,ISNULL(A.Corrected,0) Corrected
                FROM dbo.FeePayments P
                JOIN dbo.Students S ON S.AdmissionId=P.AdmissionId
                OUTER APPLY (SELECT
                    -SUM(CASE WHEN X.PaymentMethod=N'Refund' THEN X.TotalAmount ELSE 0 END) Refunded,
                    -SUM(CASE WHEN X.PaymentMethod=N'Correction' THEN X.TotalAmount ELSE 0 END) Corrected
                    FROM dbo.FeePayments X WHERE X.RelatedFeePaymentId=P.FeePaymentId
                    AND X.Status=N'Completed' AND X.TotalAmount<0) A
                WHERE P.RelatedFeePaymentId IS NULL
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
            show &= net > 0m && CurrentUser.HasPermission("Fees.View");
            decimal balance = 0m;
            if (show)
            {
                int admissionId = latest!.Field<int>("AdmissionId");
                var enrollments = await Task.Run(() => DatabaseHelper.QuerySql("""
                    EXEC dbo.RequirePermission @UserId,N'Fees.View';
                    SELECT EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId;
                    """, DatabaseHelper.Int("@UserId",CurrentUser.UserId),
                    DatabaseHelper.Int("@AdmissionId",admissionId)));
                foreach (DataRow enrollment in enrollments.Rows)
                {
                    var charges = await DatabaseHelper.QueryUserAsync("GetOutstandingCharges",
                        DatabaseHelper.Int("@AdmissionId",admissionId),
                        DatabaseHelper.Int("@EnrollmentId",enrollment.Field<int>("EnrollmentId")),
                        DatabaseHelper.Date("@AsOfDate",DateTime.Today));
                    balance += charges.AsEnumerable().Sum(row => Convert.ToDecimal(row["Balance"]));
                }
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
        return Task.Run(() => DatabaseHelper.QuerySql("""
            EXEC dbo.RequirePermission @UserId,@ReadPermission;
            SELECT S.StudentName,S.FatherName,FP.AdmissionId,FP.EnrollmentId,
                C.ClassName,SEC.SectionName,CMP.CampusName,
                FT.FeeTypeName,BC.BillingMonth,FPA.Amount,FP.ReceiptNumber,FP.PaymentDate
            FROM dbo.FeePayments FP
            JOIN dbo.Students S ON S.AdmissionId=FP.AdmissionId
            JOIN dbo.Enrollments E ON E.EnrollmentId=FP.EnrollmentId
            JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
            JOIN dbo.Classes C ON C.ClassId=CCS.ClassId
            JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId
            JOIN dbo.Campuses CMP ON CMP.CampusId=CCS.CampusId
            JOIN dbo.FeePaymentAllocations FPA ON FPA.FeePaymentId=FP.FeePaymentId
            JOIN dbo.BillingCharges BC ON BC.BillingChargeId=FPA.BillingChargeId
            JOIN dbo.FeeTypes FT ON FT.FeeTypeId=BC.FeeTypeId
            WHERE FP.ReceiptNumber=@ReceiptNumber AND FP.RelatedFeePaymentId IS NULL AND FPA.Amount<>0
            ORDER BY S.StudentName,FP.EnrollmentId,
                CASE WHEN BC.BillingMonth IS NULL THEN 0 ELSE 1 END,
                BC.BillingMonth,FT.DisplayOrder,FT.FeeTypeName;
            """,parameters));
    }
}
