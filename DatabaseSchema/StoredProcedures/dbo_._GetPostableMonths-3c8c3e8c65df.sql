-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Collect';
    IF @AcademicYearId IS NULL
       OR @AcademicYearId <= 0
        THROW 54740, 'An academic year is required.', 1;
    DECLARE @First AS DATE, @Last AS DATE;
    SELECT @First = DATEFROMPARTS(YEAR(AY.StartDate), MONTH(AY.StartDate), 1),
           @Last = DATEFROMPARTS(YEAR(AY.EndDate), MONTH(AY.EndDate), 1)
    FROM   dbo.AcademicYears AS AY
    WHERE  AY.AcademicYearId = @AcademicYearId;
    IF @First IS NULL
        THROW 54741, 'The academic year was not found.', 1;
    IF @Last < @First
        THROW 54742, 'The academic year ends before it starts.', 1;
    IF DATEDIFF(MONTH, @First, @Last) > 23
        SET @Last = DATEADD(MONTH, 23, @First);
    DECLARE @AnyFeeType AS BIT = CASE WHEN EXISTS (SELECT 1
                                                   FROM   @FeeTypeIds) THEN 1 ELSE 0 END;
    WITH     Months
    AS       (SELECT @First AS BillingMonth
              UNION ALL
              SELECT DATEADD(MONTH, 1, BillingMonth)
              FROM   Months
              WHERE  BillingMonth < @Last)
    SELECT   M.BillingMonth,
             COUNT(*) AS ChargesToPost,
             COUNT(DISTINCT E.AdmissionId) AS EligibleStudents,
             SUM(SFA.Amount - SFA.DiscountAmount) AS TotalNet
    FROM     Months AS M CROSS JOIN dbo.StudentFeeAssignments AS SFA
             INNER JOIN
             dbo.Enrollments AS E
             ON E.EnrollmentId = SFA.EnrollmentId
             INNER JOIN
             dbo.CampusClassSections AS CCS
             ON CCS.ClassSectionId = E.ClassSectionId
    WHERE    E.AcademicYearId = @AcademicYearId AND E.Status = N'Active'
             AND E.BillingReady = 1
             AND SFA.CancelledAt IS NULL
             AND SFA.Frequency = N'Monthly'
             AND M.BillingMonth >= DATEFROMPARTS(YEAR(ISNULL(E.BillingStartDate, E.EnrollmentDate)), MONTH(ISNULL(E.BillingStartDate, E.EnrollmentDate)), 1)
             AND (@CampusId = 0
                  OR CCS.CampusId = @CampusId)
             AND (@ClassId = 0
                  OR CCS.ClassId = @ClassId)
             AND (@SectionId = 0
                  OR CCS.SectionId = @SectionId)
             AND (@AnyFeeType = 0
                  OR EXISTS (SELECT 1
                             FROM   @FeeTypeIds AS F
                             WHERE  F.FeeTypeId = SFA.FeeTypeId))
             AND (SFA.EffectiveFrom IS NULL
                  OR M.BillingMonth >= SFA.EffectiveFrom)
             AND (SFA.EffectiveTo IS NULL
                  OR M.BillingMonth <= SFA.EffectiveTo)
             AND (E.LeavingDate IS NULL
                  OR DATEDIFF(MONTH, E.LeavingDate, M.BillingMonth) <= 0)
             AND NOT (E.Status = N'Promoted'
                      AND EXISTS (SELECT 1
                                  FROM   dbo.Enrollments AS NX
                                  WHERE  NX.AdmissionId = E.AdmissionId
                                         AND NX.EnrollmentId > E.EnrollmentId
                                         AND NX.BillingReady = 1
                                         AND NX.Status IN (N'Active', N'Promoted', N'Left')
                                         AND M.BillingMonth >= DATEFROMPARTS(YEAR(ISNULL(NX.BillingStartDate, NX.EnrollmentDate)), MONTH(ISNULL(NX.BillingStartDate, NX.EnrollmentDate)), 1)))
             AND NOT EXISTS (SELECT 1
                             FROM   dbo.BillingCharges AS BC
                             WHERE  BC.EnrollmentId = SFA.EnrollmentId
                                    AND BC.FeeTypeId = SFA.FeeTypeId
                                    AND BC.BillingMonth = M.BillingMonth
                                    AND BC.ChargeKind = N'Monthly'
                                    AND BC.Status <> N'Cancelled')
    GROUP BY M.BillingMonth
    HAVING   COUNT(*) > 0
    ORDER BY M.BillingMonth
    OPTION (MAXRECURSION 100);
END
