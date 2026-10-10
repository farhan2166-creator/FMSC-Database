SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE   PROCEDURE dbo.GetAdmissionOutstandingBalance
    @UserId       INT,
    @AdmissionId  INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission @UserId, N'Reports.View';

    SELECT ISNULL(SUM(dbo.GetFeeChargeBalance(BC.BillingChargeId)), 0)
    FROM dbo.BillingCharges BC
    WHERE BC.AdmissionId = @AdmissionId
      AND BC.Status <> N'Cancelled';
END;

GO
