-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Collect';
    IF @BillingMonth IS NULL
        THROW 54710, 'A billing month is required.', 1;
    IF @AcademicYearId IS NULL OR @AcademicYearId<=0 OR NOT EXISTS(SELECT 1 FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId)
        THROW 54743, 'Select a valid academic year before reviewing or posting fees.', 1;
    DECLARE @Month AS DATE = DATEFROMPARTS(YEAR(@BillingMonth), MONTH(@BillingMonth), 1);
    DECLARE @AnyFeeType AS BIT = CASE WHEN EXISTS (SELECT 1
                                                   FROM   @FeeTypeIds) THEN 1 ELSE 0 END;
    DECLARE @Sample TABLE (
        AdmissionId INT);
    IF EXISTS (SELECT 1
               FROM   dbo.Users
               WHERE  UserId = @UserId)
        INSERT  @Sample (AdmissionId)
        VALUES         (NULL);
    SELECT   E.AdmissionId,
             E.AcademicYearId,
             S.RegistrationNumber,
             S.StudentName,
             CAMP.CampusName,
             C.ClassName,
             SEC.SectionName,
             FT.FeeTypeId,
             FT.FeeTypeName,
             SFA.StudentFeeAssignmentId,
             SFA.EnrollmentId,
             @Month AS BillingMonth,
             @Month AS ChargeDate,
             @Month AS DueDate,
             SFA.Amount AS GrossAmount,
             SFA.DiscountAmount,
             SFA.Amount - SFA.DiscountAmount AS NetAmount,
             CASE WHEN BC.BillingChargeId IS NOT NULL THEN 1 ELSE 0 END AS AlreadyPosted,
             CASE WHEN BC.BillingChargeId IS NOT NULL THEN N'Already posted for this month' WHEN EXISTS(SELECT 1 FROM dbo.BillingCharges R WHERE R.EnrollmentId=SFA.EnrollmentId AND R.FeeTypeId=SFA.FeeTypeId AND R.BillingMonth=@Month AND R.ChargeKind=N'Monthly' AND R.Status=N'Cancelled') THEN N'Previously reversed â€” selecting this creates a new charge' ELSE N'Eligible' END AS EligibilityNote
    FROM     dbo.StudentFeeAssignments AS SFA
             INNER JOIN
             dbo.Enrollments AS E
             ON E.EnrollmentId = SFA.EnrollmentId
             INNER JOIN
             dbo.Students AS S
             ON S.AdmissionId = E.AdmissionId
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
             ON FT.FeeTypeId = SFA.FeeTypeId
             LEFT OUTER JOIN
             @Sample AS SM
             ON SM.AdmissionId = E.AdmissionId OUTER APPLY (SELECT   TOP (1) BC.BillingChargeId
                                                            FROM     dbo.BillingCharges AS BC
                                                            WHERE    BC.EnrollmentId = SFA.EnrollmentId
                                                                     AND BC.FeeTypeId = SFA.FeeTypeId
                                                                     AND BC.BillingMonth = @Month
                                                                     AND BC.ChargeKind = N'Monthly'
                                                                     AND BC.Status <> N'Cancelled'
                                                            ORDER BY BC.BillingChargeId) AS BC
    WHERE    E.AcademicYearId = @AcademicYearId AND E.Status = N'Active'
             AND E.BillingReady = 1
             AND SFA.CancelledAt IS NULL
             AND SFA.Frequency = N'Monthly'
             AND @Month >= DATEFROMPARTS(YEAR(ISNULL(E.BillingStartDate, E.EnrollmentDate)), MONTH(ISNULL(E.BillingStartDate, E.EnrollmentDate)), 1)
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
                  OR @Month >= SFA.EffectiveFrom)
             AND (SFA.EffectiveTo IS NULL
                  OR @Month <= SFA.EffectiveTo)
             AND (E.LeavingDate IS NULL
                  OR DATEDIFF(MONTH, E.LeavingDate, @Month) <= 0)
             AND NOT (E.Status = N'Promoted'
                      AND EXISTS (SELECT 1
                                  FROM   dbo.Enrollments AS NX
                                  WHERE  NX.AdmissionId = E.AdmissionId
                                         AND NX.EnrollmentId > E.EnrollmentId
                                         AND NX.BillingReady = 1
                                         AND NX.Status IN (N'Active', N'Promoted', N'Left')
                                         AND @Month >= DATEFROMPARTS(YEAR(ISNULL(NX.BillingStartDate, NX.EnrollmentDate)), MONTH(ISNULL(NX.BillingStartDate, NX.EnrollmentDate)), 1)))
             AND (@ExcludeAlreadyPosted = 0
                  OR BC.BillingChargeId IS NULL)
    ORDER BY CAMP.CampusName, C.ClassName, SEC.SectionName, S.StudentName, FT.DisplayOrder, FT.FeeTypeName;
    WITH   Eligible
    AS     (SELECT E.AdmissionId,
                   SFA.StudentFeeAssignmentId,
                   SFA.Amount,
                   SFA.DiscountAmount,
                   CASE WHEN BC.BillingChargeId IS NOT NULL THEN 1 ELSE 0 END AS AlreadyPosted
            FROM   dbo.StudentFeeAssignments AS SFA
                   INNER JOIN
                   dbo.Enrollments AS E
                   ON E.EnrollmentId = SFA.EnrollmentId
                   INNER JOIN
                   dbo.CampusClassSections AS CCS
                   ON CCS.ClassSectionId = E.ClassSectionId
                   LEFT OUTER JOIN
                   @Sample AS SM
                   ON SM.AdmissionId = E.AdmissionId OUTER APPLY (SELECT   TOP (1) BC.BillingChargeId
                                                                  FROM     dbo.BillingCharges AS BC
                                                                  WHERE    BC.EnrollmentId = SFA.EnrollmentId
                                                                           AND BC.FeeTypeId = SFA.FeeTypeId
                                                                           AND BC.BillingMonth = @Month
                                                                           AND BC.ChargeKind = N'Monthly'
                                                                           AND BC.Status <> N'Cancelled'
                                                                  ORDER BY BC.BillingChargeId) AS BC
            WHERE  E.AcademicYearId = @AcademicYearId AND E.Status = N'Active'
                   AND E.BillingReady = 1
                   AND SFA.CancelledAt IS NULL
                   AND SFA.Frequency = N'Monthly'
                   AND @Month >= DATEFROMPARTS(YEAR(ISNULL(E.BillingStartDate, E.EnrollmentDate)), MONTH(ISNULL(E.BillingStartDate, E.EnrollmentDate)), 1)
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
                        OR @Month >= SFA.EffectiveFrom)
                   AND (SFA.EffectiveTo IS NULL
                        OR @Month <= SFA.EffectiveTo)
                   AND (E.LeavingDate IS NULL
                        OR DATEDIFF(MONTH, E.LeavingDate, @Month) <= 0)
                   AND NOT (E.Status = N'Promoted'
                            AND EXISTS (SELECT 1
                                        FROM   dbo.Enrollments AS NX
                                        WHERE  NX.AdmissionId = E.AdmissionId
                                               AND NX.EnrollmentId > E.EnrollmentId
                                               AND NX.BillingReady = 1
                                               AND NX.Status IN (N'Active', N'Promoted', N'Left')
                                               AND @Month >= DATEFROMPARTS(YEAR(ISNULL(NX.BillingStartDate, NX.EnrollmentDate)), MONTH(ISNULL(NX.BillingStartDate, NX.EnrollmentDate)), 1))))
    SELECT @Month AS BillingMonth,
           COUNT(DISTINCT CASE WHEN AlreadyPosted = 0 THEN AdmissionId END) AS EligibleStudents,
           SUM(CASE WHEN AlreadyPosted = 0 THEN 1 ELSE 0 END) AS ChargesToPost,
           SUM(CASE WHEN AlreadyPosted = 0 THEN Amount ELSE 0 END) AS TotalGross,
           SUM(CASE WHEN AlreadyPosted = 0 THEN DiscountAmount ELSE 0 END) AS TotalDiscount,
           SUM(CASE WHEN AlreadyPosted = 0 THEN Amount - DiscountAmount ELSE 0 END) AS TotalNet,
           COUNT(DISTINCT CASE WHEN AlreadyPosted = 1 THEN AdmissionId END) AS AlreadyPostedStudents,
           SUM(CASE WHEN AlreadyPosted = 1 THEN 1 ELSE 0 END) AS AlreadyPostedCharges
    FROM   Eligible;
END
