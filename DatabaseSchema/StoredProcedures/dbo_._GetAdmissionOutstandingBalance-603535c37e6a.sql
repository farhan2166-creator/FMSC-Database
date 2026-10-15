-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission @UserId, N'Reports.View';

    SELECT ISNULL(SUM(dbo.GetFeeChargeBalance(BC.BillingChargeId)), 0)
    FROM dbo.BillingCharges BC
    WHERE BC.AdmissionId = @AdmissionId
      AND BC.Status <> N'Cancelled';
END;
