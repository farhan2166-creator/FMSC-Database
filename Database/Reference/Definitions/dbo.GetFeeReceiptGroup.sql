SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- GetFeeReceiptGroup
CREATE   PROCEDURE dbo.GetFeeReceiptGroup
@UserId INT, @ReceiptNumber NVARCHAR (40)
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.View';
    IF @ReceiptNumber IS NULL
       OR LTRIM(RTRIM(@ReceiptNumber)) = N''
        THROW 59910, 'A receipt number is required.', 1;
    DECLARE @Issued AS DECIMAL (12, 2), @Reversed AS DECIMAL (12, 2), @PayerCount AS INT;
    SELECT @Issued = ISNULL(SUM(FP.TotalAmount), 0),
           @PayerCount = COUNT(DISTINCT FP.AdmissionId)
    FROM   dbo.FeePayments AS FP
    WHERE  FP.ReceiptNumber = @ReceiptNumber
           AND FP.RelatedFeePaymentId IS NULL;
    SELECT @Reversed = ISNULL(SUM(-R.TotalAmount), 0)
    FROM   dbo.FeePayments AS R
           INNER JOIN
           dbo.FeePayments AS P
           ON P.FeePaymentId = R.RelatedFeePaymentId
    WHERE  P.ReceiptNumber = @ReceiptNumber
           AND R.TotalAmount <> 0;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.FeePayments AS FP
                   WHERE  FP.ReceiptNumber = @ReceiptNumber)
        THROW 59911, 'No payments were found for that receipt number.', 1;
    SELECT @ReceiptNumber AS ReceiptNumber,
           MIN(FP.PaymentDate) AS PaymentDate,
           MAX(FP.PaymentMethod) AS PaymentMethod,
           MAX(FP.ReferenceNumber) AS ReferenceNumber,
           @PayerCount AS StudentCount,
           @Issued AS IssuedAmount,
           @Reversed AS ReversedAmount,
           @Issued - @Reversed AS LiveAmount,
           CASE WHEN @Reversed <= 0 THEN N'Issued' WHEN @Issued - @Reversed <= 0 THEN N'Fully reversed' ELSE N'Partially reversed' END AS ReceiptStatus
    FROM   dbo.FeePayments AS FP
    WHERE  FP.ReceiptNumber = @ReceiptNumber
           AND FP.RelatedFeePaymentId IS NULL;
    SELECT   S.StudentName,
             FP.AdmissionId,
             FT.FeeTypeName,
             BC.BillingMonth,
             FPA.Amount,
             FP.ReceiptNumber,
             FP.PaymentDate
    FROM     dbo.FeePayments AS FP
             INNER JOIN
             dbo.Students AS S
             ON S.AdmissionId = FP.AdmissionId
             INNER JOIN
             dbo.FeePaymentAllocations AS FPA
             ON FPA.FeePaymentId = FP.FeePaymentId
             INNER JOIN
             dbo.BillingCharges AS BC
             ON BC.BillingChargeId = FPA.BillingChargeId
             INNER JOIN
             dbo.FeeTypes AS FT
             ON FT.FeeTypeId = BC.FeeTypeId
    WHERE    FP.ReceiptNumber = @ReceiptNumber
             AND FP.RelatedFeePaymentId IS NULL
             AND FPA.Amount <> 0
    ORDER BY S.StudentName, CASE WHEN BC.BillingMonth IS NULL THEN 0 ELSE 1 END, BC.BillingMonth, FT.DisplayOrder, FT.FeeTypeName;
END



GO
