
-- fn_ExpectedRecurringChargeMonths
CREATE   FUNCTION dbo.fn_ExpectedRecurringChargeMonths
(@EnrollmentId INT, @AsOfDate DATE)
RETURNS TABLE 
AS
RETURN 
    WITH   EnrollmentWindow
    AS     (SELECT DATEFROMPARTS(YEAR(ISNULL(E.BillingStartDate, E.EnrollmentDate)), MONTH(ISNULL(E.BillingStartDate, E.EnrollmentDate)), 1) AS StartMonth,
                   DATEFROMPARTS(YEAR(CASE WHEN E.LeavingDate IS NULL
                                                OR E.LeavingDate > @AsOfDate THEN @AsOfDate ELSE E.LeavingDate END), MONTH(CASE WHEN E.LeavingDate IS NULL
                                                                                                                                     OR E.LeavingDate > @AsOfDate THEN @AsOfDate ELSE E.LeavingDate END), 1) AS EndMonth
            FROM   dbo.Enrollments AS E
            WHERE  E.EnrollmentId = @EnrollmentId
                   AND E.Status IN (N'Active', N'Promoted', N'Completed', N'Left')
                   AND E.BillingReady = 1),
           Months
    AS     (SELECT EW.StartMonth AS BillingMonth,
                   EW.StartMonth AS AnchorMonth,
                   EW.EndMonth
            FROM   EnrollmentWindow AS EW
            WHERE  EW.StartMonth <= EW.EndMonth
            UNION ALL
            SELECT DATEADD(MONTH, 1, M.BillingMonth),
                   M.AnchorMonth,
                   M.EndMonth
            FROM   Months AS M
            WHERE  M.BillingMonth < M.EndMonth)
    SELECT SFA.StudentFeeAssignmentId,
           SFA.FeeTypeId,
           M.BillingMonth
    FROM   dbo.StudentFeeAssignments AS SFA CROSS JOIN Months AS M
    WHERE  SFA.EnrollmentId = @EnrollmentId
           AND SFA.CancelledAt IS NULL
           AND SFA.Frequency = N'Monthly'
           AND (SFA.EffectiveFrom IS NULL
                OR M.BillingMonth >= SFA.EffectiveFrom)
           AND (SFA.EffectiveTo IS NULL
                OR M.BillingMonth <= SFA.EffectiveTo)




