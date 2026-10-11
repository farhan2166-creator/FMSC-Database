CREATE   PROCEDURE dbo.DailyClosingSnapshot
    @ClosingDate date, @Snapshot nvarchar(max) OUTPUT, @Hash varchar(64) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @Snapshot=(SELECT FeePaymentId,PaymentMethod,TotalAmount,ReceiptNumber,
        RelatedFeePaymentId,ReferenceNumber,CreatedByUserId
        FROM dbo.DailyClosingPayments WHERE PaymentDate=@ClosingDate
        ORDER BY FeePaymentId FOR JSON PATH, INCLUDE_NULL_VALUES);
    SET @Hash=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Snapshot),2);
END;

