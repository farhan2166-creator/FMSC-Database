CREATE PROCEDURE dbo.GetStudentProfileOutstandingBalance
    @UserId INT,
    @AdmissionId INT
AS
BEGIN
    SET NOCOUNT ON;
    -- Business rule, V20 F-04: profile balance is visible to every Students.View user.
    -- Keep Reports.View on GetAdmissionOutstandingBalance and other report procedures.
    -- See Docs/AUDIT_DECISIONS.md; normal zero-role users still fail RequirePermission.
    EXEC dbo.RequirePermission @UserId, N'Students.View';

    SELECT ISNULL(SUM(dbo.GetFeeChargeBalance(BC.BillingChargeId)), 0)
    FROM dbo.BillingCharges BC
    WHERE BC.AdmissionId = @AdmissionId
      AND BC.Status <> N'Cancelled';
END;

