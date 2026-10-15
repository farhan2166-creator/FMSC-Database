-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Collect';
    IF @BillingMonth IS NULL
        THROW 54730, 'A billing month is required.', 1;
    DECLARE @Month AS DATE = DATEFROMPARTS(YEAR(@BillingMonth), MONTH(@BillingMonth), 1);
    DECLARE @AnyFeeType AS BIT = CASE WHEN EXISTS (SELECT 1
                                                   FROM   @FeeTypeIds) THEN 1 ELSE 0 END;
    SELECT   BC.BillingChargeId,
             E.AdmissionId,
             S.RegistrationNumber,
             S.StudentName,
             CAMP.CampusName,
             C.ClassName,
             SEC.SectionName,
             FT.FeeTypeName,
             BC.ChargeKind,
             BC.BillingMonth,
             BC.GrossAmount,
             BC.DiscountAmount,
             BC.NetAmount,
             ISNULL(PA.Collected, 0) AS CollectedAmount,
             BC.NetAmount - ISNULL(PA.Collected, 0) AS UncollectedAmount,
             CASE WHEN ISNULL(PA.Collected, 0) > 0 THEN 1 ELSE 0 END AS HasCollectedMoney,
             CASE WHEN ISNULL(PA.Collected, 0) > 0 THEN N'Collected - use Fee Collection Correction/Refund/Reversal' ELSE N'Uncollected - can be reversed here' END AS ReversalNote
    FROM     dbo.BillingCharges AS BC
             INNER JOIN
             dbo.Enrollments AS E
             ON E.EnrollmentId = BC.EnrollmentId
             INNER JOIN
             dbo.Students AS S
             ON S.AdmissionId = BC.AdmissionId
             INNER JOIN
             dbo.CampusClassSections AS CCS
             ON CCS.ClassSectionId = E.ClassSectionId
             INNER JOIN
             dbo.Campuses AS CAMP
             ON CAMP.CampusId = CCS.CampusId
             INNER JOIN
             dbo.Classes AS C
             ON C.ClassId = CCS.ClassId
             INNER JOIN
             dbo.Sections AS SEC
             ON SEC.SectionId = CCS.SectionId
             INNER JOIN
             dbo.FeeTypes AS FT
             ON FT.FeeTypeId = BC.FeeTypeId OUTER APPLY (SELECT SUM(A.Amount) AS Collected
                                                         FROM   dbo.FeePaymentAllocations AS A
                                                                INNER JOIN
                                                                dbo.FeePayments AS FP
                                                                ON FP.FeePaymentId = A.FeePaymentId
                                                                   AND FP.Status = N'Completed'
                                                         WHERE  A.BillingChargeId = BC.BillingChargeId) AS PA
    WHERE    BC.BillingMonth = @Month AND E.Status=N'Active' AND E.Status=N'Active'
             AND BC.Status <> N'Cancelled'
             AND (@CampusId = 0
                  OR CCS.CampusId = @CampusId)
             AND (@ClassId = 0
                  OR CCS.ClassId = @ClassId)
             AND (@SectionId = 0
                  OR CCS.SectionId = @SectionId)
             AND (@AnyFeeType = 0
                  OR EXISTS (SELECT 1
                             FROM   @FeeTypeIds AS F
                             WHERE  F.FeeTypeId = BC.FeeTypeId))
    ORDER BY CAMP.CampusName, C.ClassName, SEC.SectionName, S.StudentName, FT.DisplayOrder, FT.FeeTypeName;
    WITH   C
    AS     (SELECT BC.BillingChargeId,
                   BC.AdmissionId,
                   BC.NetAmount,
                   ISNULL(PA.Collected, 0) AS Collected
            FROM   dbo.BillingCharges AS BC
                   INNER JOIN
                   dbo.Enrollments AS E
                   ON E.EnrollmentId = BC.EnrollmentId
                   INNER JOIN
                   dbo.CampusClassSections AS CCS
                   ON CCS.ClassSectionId = E.ClassSectionId OUTER APPLY (SELECT SUM(A.Amount) AS Collected
                                                                         FROM   dbo.FeePaymentAllocations AS A
                                                                                INNER JOIN
                                                                                dbo.FeePayments AS FP
                                                                                ON FP.FeePaymentId = A.FeePaymentId
                                                                                   AND FP.Status = N'Completed'
                                                                         WHERE  A.BillingChargeId = BC.BillingChargeId) AS PA
            WHERE  BC.BillingMonth = @Month AND E.Status=N'Active' AND E.Status=N'Active'
                   AND BC.Status <> N'Cancelled'
                   AND (@CampusId = 0
                        OR CCS.CampusId = @CampusId)
                   AND (@ClassId = 0
                        OR CCS.ClassId = @ClassId)
                   AND (@SectionId = 0
                        OR CCS.SectionId = @SectionId)
                   AND (@AnyFeeType = 0
                        OR EXISTS (SELECT 1
                                   FROM   @FeeTypeIds AS F
                                   WHERE  F.FeeTypeId = BC.FeeTypeId)))
    SELECT @Month AS BillingMonth,
           COUNT(DISTINCT AdmissionId) AS StudentsAffected,
           COUNT(*) AS ChargesAffected,
           ISNULL(SUM(NetAmount), 0) AS TotalPosted,
           ISNULL(SUM(Collected), 0) AS TotalCollected,
           ISNULL(SUM(NetAmount - Collected), 0) AS TotalUncollected,
           SUM(CASE WHEN Collected = 0 THEN 1 ELSE 0 END) AS ReversibleCharges,
           SUM(CASE WHEN Collected > 0 THEN 1 ELSE 0 END) AS BlockedCharges,
           ISNULL(SUM(CASE WHEN Collected = 0 THEN NetAmount - Collected ELSE 0 END), 0) AS ReversibleAmount
    FROM   C;
END
