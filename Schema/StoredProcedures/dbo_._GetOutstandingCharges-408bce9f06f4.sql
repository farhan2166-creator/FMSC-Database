-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.GetOutstandingCharges
@UserId INT, @AdmissionId INT, @EnrollmentId INT, @AsOfDate DATE
AS
BEGIN
    EXECUTE dbo.RequirePermission @UserId, N'Fees.View';
    SET NOCOUNT ON;
    IF @AsOfDate IS NULL
        SET @AsOfDate = CAST (GETDATE() AS DATE);
    SELECT   BC.BillingChargeId,
             BC.StudentFeeAssignmentId,
             BC.EnrollmentId,
             BC.AdmissionId,
             FT.FeeTypeId,
             FT.FeeTypeCode,
             FT.FeeTypeName,
             BC.BillingMonth,
             BC.ChargeKind,
             BC.Remarks,
             BC.ChargeDate,
             BC.DueDate,
             BC.GrossAmount,
             BC.DiscountAmount,
             BC.NetAmount,
             BC.NetAmount - dbo.GetFeeChargeBalance(BC.BillingChargeId) AS PaidAmount,
             dbo.GetFeeChargeBalance(BC.BillingChargeId) AS Balance,
             BC.Status
    FROM     dbo.BillingCharges AS BC
             INNER JOIN
             dbo.FeeTypes AS FT
             ON FT.FeeTypeId = BC.FeeTypeId
    WHERE    BC.AdmissionId = @AdmissionId
             AND (@EnrollmentId=0 OR BC.EnrollmentId = @EnrollmentId)
             AND BC.Status <> N'Cancelled'
             AND dbo.GetFeeChargeBalance(BC.BillingChargeId) > 0
    ORDER BY CASE WHEN FT.FeeTypeCode=N'TUITION' THEN 0 ELSE 1 END, COALESCE(BC.BillingMonth,BC.ChargeDate), BC.BillingChargeId;
END
