namespace FMSC_SMS.Core;

// Collection needs unpaid charges and zero-due concessions that have not yet
// appeared on a saved receipt/acknowledgement. The visit baseline already tracks
// that distinction; the database's Paid status alone cannot.
internal static class CollectionChargeReview
{
    internal const string Query="""
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        DECLARE @Ids nvarchar(max)=CONCAT(N'[',@AdmissionId,N']'),
          @Before bigint=(SELECT COALESCE(MAX(VisitSequence),0)+1 FROM dbo.CollectionReceipts);
        DECLARE @Accounts nvarchar(max)=(SELECT * FROM dbo.FeeDocumentAccounts(@Ids,N'[]',N'[]') FOR JSON PATH);
        SELECT B.BillingChargeId,B.StudentFeeAssignmentId,B.EnrollmentId,B.AdmissionId,
          F.FeeTypeId,F.FeeTypeCode,F.FeeTypeName,B.BillingMonth,B.ChargeKind,B.Remarks,B.ChargeDate,B.DueDate,
          B.GrossAmount,B.DiscountAmount,B.NetAmount,
          B.NetAmount-Q.Balance PaidAmount,Q.Balance,B.Status,
          CONVERT(bit,CASE WHEN Q.Balance=0 AND V.Discount>0 THEN 1 ELSE 0 END) PendingWaiver
        FROM dbo.BillingCharges B JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
        CROSS APPLY(SELECT dbo.GetFeeChargeBalance(B.BillingChargeId) Balance) Q
        LEFT JOIN dbo.FeeVisitLines(@Accounts,@Before) V ON V.ChargeId=B.BillingChargeId
        WHERE B.AdmissionId=@AdmissionId AND B.Status<>N'Cancelled'
          AND (Q.Balance>0 OR (Q.Balance=0 AND V.Discount>0))
        ORDER BY CASE WHEN F.FeeTypeCode=N'TUITION' THEN 0 ELSE 1 END,
          COALESCE(B.BillingMonth,B.ChargeDate),B.BillingChargeId;
        """;

    internal static bool IsPendingWaiver(DataRow row)=>row.Table.Columns.Contains("PendingWaiver")
        && !row.IsNull("PendingWaiver") && Convert.ToBoolean(row["PendingWaiver"]);
}
