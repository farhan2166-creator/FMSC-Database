-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.GetFeeReceiptGroup
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
           AND R.TotalAmount <> 0 AND R.IsCreditApplication=0 AND R.Status=N'Completed';
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.FeePayments AS FP
                   WHERE  FP.ReceiptNumber = @ReceiptNumber)
        THROW 59911, 'No payments were found for that receipt number.', 1;
    DECLARE @ConcessionJson nvarchar(max)=(SELECT TOP(1) ConcessionLinesJson FROM dbo.FeePayments WHERE ReceiptNumber=@ReceiptNumber ORDER BY FeePaymentId);
    SET @PayerCount=@PayerCount+(SELECT COUNT(DISTINCT J.AdmissionId) FROM OPENJSON(@ConcessionJson) WITH(AdmissionId int) J
      WHERE NOT EXISTS(SELECT 1 FROM dbo.FeePayments P WHERE P.ReceiptNumber=@ReceiptNumber AND P.AdmissionId=J.AdmissionId));
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
    EXEC dbo.GetFeeReceiptLines @UserId,@ReceiptNumber;
END;
