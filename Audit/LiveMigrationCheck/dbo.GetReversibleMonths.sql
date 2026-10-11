CREATE   PROCEDURE dbo.GetReversibleMonths

(

      @UserId      INT

    , @CampusId    INT = 0

    , @ClassId     INT = 0

    , @SectionId   INT = 0

    , @FeeTypeIds  dbo.FeeTypeIdList READONLY

)

AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission @UserId, N'Fees.Collect';

    DECLARE @AnyFeeType BIT =
        CASE WHEN EXISTS (SELECT 1 FROM @FeeTypeIds) THEN 1 ELSE 0 END;

    /* The same live-charge test dbo.PreviewPostingReversal uses: a charge that
       is not Cancelled, in a billing month, matching the scope. A charge with no
       billing month, or one not dated the first of its month, can never be
       reached by a month-based reversal, so it is not offered as a month. */
    SELECT
        BC.BillingMonth,
        COUNT(*)                                                   AS ChargesAffected,
        SUM(CASE WHEN ISNULL(PA.Collected, 0) = 0 THEN 1 ELSE 0 END) AS ReversibleCharges,
        SUM(BC.NetAmount)                                          AS TotalPosted,
        SUM(ISNULL(PA.Collected, 0))                               AS TotalCollected
    FROM dbo.BillingCharges BC
    INNER JOIN dbo.Enrollments E
        ON E.EnrollmentId = BC.EnrollmentId
    INNER JOIN dbo.CampusClassSections CCS
        ON CCS.ClassSectionId = E.ClassSectionId
    OUTER APPLY
    (
        SELECT SUM(A.Amount) AS Collected
        FROM dbo.FeePaymentAllocations A
        INNER JOIN dbo.FeePayments FP ON FP.FeePaymentId = A.FeePaymentId AND FP.Status = N'Completed'
        WHERE A.BillingChargeId = BC.BillingChargeId
    ) PA
    WHERE E.Status=N'Active' AND BC.Status <> N'Cancelled'
      AND BC.BillingMonth IS NOT NULL
      AND BC.BillingMonth = DATEFROMPARTS(YEAR(BC.BillingMonth), MONTH(BC.BillingMonth), 1)
      AND (@CampusId  = 0 OR CCS.CampusId  = @CampusId)
      AND (@ClassId   = 0 OR CCS.ClassId   = @ClassId)
      AND (@SectionId = 0 OR CCS.SectionId = @SectionId)
      AND (@AnyFeeType = 0 OR EXISTS (SELECT 1 FROM @FeeTypeIds F WHERE F.FeeTypeId = BC.FeeTypeId))
    GROUP BY BC.BillingMonth
    ORDER BY BC.BillingMonth DESC;
END;

