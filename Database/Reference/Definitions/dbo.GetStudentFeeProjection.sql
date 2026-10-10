SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* ---------------------------------------------------------------------
   GetStudentFeeProjection - read-only. Gated by Fees.View, matching the
   exact permission GetStudentFeeAssignments and GetOutstandingCharges use
   for this same per-student read access (confirmed by reading both
   procedures' real bodies in the dacpac before writing this, per Rule 10)
   - not Fees.Reports.View, which is for the school-wide Fee Reports page,
   not a single student's account.
   --------------------------------------------------------------------- */

CREATE PROCEDURE dbo.GetStudentFeeProjection
    @UserId INT,
    @EnrollmentId INT,
    @AsOfDate DATE = NULL,
    @MonthsAhead INT = 6
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission @UserId, N'Fees.View';

    IF NOT EXISTS (SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId = @EnrollmentId)
        THROW 53120, 'The selected enrollment does not exist.', 1;

    IF @AsOfDate IS NULL
        SET @AsOfDate = CAST(GETDATE() AS DATE);

    IF @MonthsAhead IS NULL OR @MonthsAhead < 1
        SET @MonthsAhead = 6;

    -- Sane ceiling: also keeps the horizon date well inside
    -- fn_ExpectedRecurringChargeMonths's own 100-month recursion cap
    -- (Phase 3), since that cap is counted from the enrollment's billing
    -- start, not from @AsOfDate.
    IF @MonthsAhead > 24
        SET @MonthsAhead = 24;

    DECLARE @CurrentMonth DATE = DATEFROMPARTS(YEAR(@AsOfDate), MONTH(@AsOfDate), 1);
    DECLARE @HorizonDate DATE = DATEADD(MONTH, @MonthsAhead, @AsOfDate);

    SELECT
        X.BillingMonth AS ExpectedBillingMonth,
        FT.FeeTypeId,
        FT.FeeTypeName,
        SFA.Frequency,
        SFA.Amount AS ExpectedAmount,
        SFA.DiscountAmount AS ExpectedDiscountAmount,
        SFA.Amount - SFA.DiscountAmount AS ExpectedNetAmount,
        SFA.StudentFeeAssignmentId
    FROM dbo.fn_ExpectedRecurringChargeMonths(@EnrollmentId, @HorizonDate) X
    INNER JOIN dbo.StudentFeeAssignments SFA ON SFA.StudentFeeAssignmentId = X.StudentFeeAssignmentId
    INNER JOIN dbo.FeeTypes FT ON FT.FeeTypeId = X.FeeTypeId
    WHERE X.BillingMonth > @CurrentMonth
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.BillingCharges BC
          WHERE BC.EnrollmentId = SFA.EnrollmentId
            AND BC.FeeTypeId = SFA.FeeTypeId
            AND BC.BillingMonth = X.BillingMonth
            AND BC.ChargeKind = N'Monthly'
            AND BC.Status <> N'Cancelled'
      )
    ORDER BY X.BillingMonth, FT.FeeTypeName;
END;


GO
