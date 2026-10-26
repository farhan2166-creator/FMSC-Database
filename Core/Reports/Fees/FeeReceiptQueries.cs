namespace FMSC_SMS.Core;

internal static class FeeReceiptQueries
{
    internal sealed record ReceiptBalanceState(bool IsLatest,IReadOnlyDictionary<int,decimal> RecordedBalances);

    // Issue order, rather than a backdated payment date or the current family,
    // decides whether a receipt still represents these students' latest collection.
    private const string BalanceStateSql = """
        EXEC dbo.RequirePermission @UserId,@ReadPermission;
        DECLARE @Id uniqueidentifier=(SELECT CollectionReceiptId FROM dbo.CollectionReceipts WHERE ReceiptNumber=@ReceiptNumber);
        IF @Id IS NULL SELECT TOP(1) @Id=L.CollectionReceiptId FROM dbo.CollectionReceiptPayments L
            JOIN dbo.FeePayments P ON P.FeePaymentId=L.FeePaymentId WHERE P.ReceiptNumber=@ReceiptNumber;
        DECLARE @IssuedAt datetime2(7),@LastPayment int,@Canonical nvarchar(40);
        SELECT @IssuedAt=CreatedAt,@Canonical=ReceiptNumber FROM dbo.CollectionReceipts WHERE CollectionReceiptId=@Id;
        DECLARE @Originals TABLE(FeePaymentId int,AdmissionId int,CreatedAt datetime2(7));
        INSERT @Originals
        SELECT P.FeePaymentId,P.AdmissionId,P.CreatedAt FROM dbo.FeePayments P
        WHERE P.RelatedFeePaymentId IS NULL AND
            ((@Id IS NULL AND P.ReceiptNumber=@ReceiptNumber) OR EXISTS(
                SELECT 1 FROM dbo.CollectionReceiptPayments L WHERE L.CollectionReceiptId=@Id AND L.FeePaymentId=P.FeePaymentId));
        SELECT @LastPayment=COALESCE(MAX(FeePaymentId),0),@IssuedAt=COALESCE(@IssuedAt,MAX(CreatedAt)) FROM @Originals;
        DECLARE @Members TABLE(AdmissionId int PRIMARY KEY,RecordedBalance decimal(12,2));
        INSERT @Members SELECT S.AdmissionId,S.BalanceRemaining FROM dbo.CollectionReceipts R
            CROSS APPLY OPENJSON(R.SnapshotJson,'$.Students') WITH(AdmissionId int,BalanceRemaining decimal(12,2)) S
            WHERE R.CollectionReceiptId=@Id;
        INSERT @Members SELECT DISTINCT P.AdmissionId,NULL FROM @Originals P
            WHERE NOT EXISTS(SELECT 1 FROM @Members M WHERE M.AdmissionId=P.AdmissionId);
        IF @IssuedAt IS NULL OR NOT EXISTS(SELECT 1 FROM @Members)
            THROW 54912,'The original receipt could not be identified. Reload before printing.',1;
        DECLARE @Latest bit=1;
        IF EXISTS(SELECT 1 FROM dbo.FeePayments P JOIN @Members M ON M.AdmissionId=P.AdmissionId
            WHERE P.RelatedFeePaymentId IS NULL AND NULLIF(P.ReceiptNumber,N'') IS NOT NULL
                AND NOT EXISTS(SELECT 1 FROM @Originals O WHERE O.FeePaymentId=P.FeePaymentId)
                AND (P.CreatedAt>@IssuedAt OR (P.CreatedAt=@IssuedAt AND P.FeePaymentId>@LastPayment)))
            SET @Latest=0;
        IF EXISTS(SELECT 1 FROM dbo.CollectionReceipts R
            CROSS APPLY OPENJSON(R.SnapshotJson,'$.Students') WITH(AdmissionId int) S
            JOIN @Members M ON M.AdmissionId=S.AdmissionId
            WHERE (@Id IS NULL OR R.CollectionReceiptId<>@Id) AND (R.CreatedAt>@IssuedAt OR
                (R.CreatedAt=@IssuedAt AND (EXISTS(SELECT 1 FROM dbo.CollectionReceiptPayments L
                    WHERE L.CollectionReceiptId=R.CollectionReceiptId AND L.FeePaymentId>@LastPayment)
                    OR (NOT EXISTS(SELECT 1 FROM dbo.CollectionReceiptPayments L WHERE L.CollectionReceiptId=R.CollectionReceiptId)
                        AND R.ReceiptNumber>COALESCE(@Canonical,@ReceiptNumber))))))
            SET @Latest=0;
        SELECT AdmissionId,RecordedBalance,@Latest IsLatest FROM @Members;
        """;

    internal static async Task<ReceiptBalanceState> QueryBalanceStateAsync(string receipt,string readPermission="Fees.View")
    {
        using var data=await DatabaseHelper.QuerySqlAsync(BalanceStateSql,DatabaseHelper.Int("@UserId",CurrentUser.UserId),
            DatabaseHelper.P("@ReceiptNumber",receipt),DatabaseHelper.P("@ReadPermission",readPermission));
        return new(Convert.ToBoolean(data.Rows[0]["IsLatest"]),data.AsEnumerable().Where(row=>!row.IsNull("RecordedBalance"))
            .ToDictionary(row=>Convert.ToInt32(row["AdmissionId"]),row=>Convert.ToDecimal(row["RecordedBalance"])));
    }
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
        Func<string,Task<DataTable>>? loadPayments = null, Func<int,Task<decimal>>? loadBalance = null,
        Func<string,Task<ReceiptBalanceState>>? loadState=null)
    {
        loadPayments ??= receipt => QueryPaymentsAsync(receipt, readPermission);
        loadBalance ??= admissionId => QueryCurrentBalanceAsync(admissionId, readPermission);
        loadState ??= receipt=>QueryBalanceStateAsync(receipt,readPermission);
        var balances = new Dictionary<int,decimal>();
        var states=new Dictionary<string,ReceiptBalanceState>(StringComparer.OrdinalIgnoreCase);
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
            if(!states.TryGetValue(receipt,out var state))states[receipt]=state=await loadState(receipt);
            decimal balance;
            if(state.IsLatest)
            {
                if (!balances.TryGetValue(admissionId, out balance))
                    balances[admissionId] = balance = await loadBalance(admissionId);
            }
            else if(!state.RecordedBalances.TryGetValue(admissionId,out balance))
            {
                if(first.Field<bool?>("ShowBalance")==true && !first.IsNull("BalanceRemaining"))balance=Convert.ToDecimal(first["BalanceRemaining"]);
                else throw new InvalidOperationException("This older receipt has no recorded outstanding balance. Its original figure cannot be confirmed; today's balance has not been substituted.");
            }
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
