

/* ------------------------------------------------------------
   2. Authoritative current charge balance.
      No adjustment table is consulted.
      Completed allocations are signed:
          + collection
          - refund
      Reversed payments contribute zero.
   ------------------------------------------------------------ */

CREATE   FUNCTION dbo.GetFeeChargeBalance
(
    @BillingChargeId BIGINT
)
RETURNS DECIMAL(12,2)
AS
BEGIN
    DECLARE
        @Net DECIMAL(12,2),
        @Paid DECIMAL(12,2);

    SELECT
        @Net = NetAmount
    FROM dbo.BillingCharges
    WHERE BillingChargeId = @BillingChargeId;

    SELECT
        @Paid = ISNULL(SUM(FPA.Amount), 0)
    FROM dbo.FeePaymentAllocations FPA
    INNER JOIN dbo.FeePayments FP
        ON FP.FeePaymentId = FPA.FeePaymentId
       AND FP.Status = N'Completed'
    WHERE FPA.BillingChargeId = @BillingChargeId;

    RETURN
        CASE
            WHEN ISNULL(@Net, 0) - ISNULL(@Paid, 0) < 0
                THEN 0
            ELSE ISNULL(@Net, 0) - ISNULL(@Paid, 0)
        END;
END;

