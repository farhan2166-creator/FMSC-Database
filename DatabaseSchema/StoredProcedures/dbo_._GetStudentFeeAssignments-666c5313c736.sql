-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.View';
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Enrollments
                   WHERE  EnrollmentId = @EnrollmentId)
        THROW 53100, 'The selected enrollment does not exist.', 1;
    SELECT   SFA.StudentFeeAssignmentId,
             SFA.EnrollmentId,
             SFA.AdmissionId,
             SFA.FeeTypeId,
             FT.FeeTypeCode,
             FT.FeeTypeName,
             SFA.Amount AS StandardAmount,
             SFA.DiscountAmount,
             SFA.Amount - SFA.DiscountAmount AS NetAmount,
             SFA.Frequency,
             SFA.Remarks,
             SFA.EffectiveFrom,
             SFA.EffectiveTo,
             SFA.CancelledAt,
             FT.IsMandatory,
             FT.IsTransportLinked,
             CAST (CASE WHEN EXISTS (SELECT 1
                                     FROM   dbo.BillingCharges AS BC
                                     WHERE  BC.StudentFeeAssignmentId = SFA.StudentFeeAssignmentId) THEN 1 ELSE 0 END AS BIT) AS HasPayments,
             (SELECT MAX(BC.BillingMonth)
              FROM   dbo.BillingCharges AS BC
              WHERE  BC.StudentFeeAssignmentId = SFA.StudentFeeAssignmentId) AS LastBilledMonth
    FROM     dbo.StudentFeeAssignments AS SFA
             INNER JOIN
             dbo.FeeTypes AS FT
             ON FT.FeeTypeId = SFA.FeeTypeId
    WHERE    SFA.EnrollmentId = @EnrollmentId
             AND (@IncludeCancelled=1 OR SFA.CancelledAt IS NULL)
    ORDER BY FT.DisplayOrder, FT.FeeTypeName, SFA.EffectiveFrom;
    IF @IncludeAvailableFees = 1
        BEGIN
            SELECT   FC.FeeConfigurationId,
                     FC.CampusId,
                     FC.ClassId,
                     FC.FeeTypeId,
                     FT.FeeTypeCode,
                     FT.FeeTypeName,
                     FC.Amount,
                     FC.Frequency,
                     FC.Remarks,
                     FT.IsMandatory,
                     FT.IsTransportLinked
            FROM     dbo.Enrollments AS E
                     INNER JOIN
                     dbo.CampusClassSections AS CCS
                     ON CCS.ClassSectionId = E.ClassSectionId
                     INNER JOIN
                     dbo.FeeConfigurations AS FC
                     ON FC.CampusId = CCS.CampusId
                        AND FC.ClassId = CCS.ClassId
                     INNER JOIN
                     dbo.FeeTypes AS FT
                     ON FT.FeeTypeId = FC.FeeTypeId
            WHERE    E.EnrollmentId = @EnrollmentId
            ORDER BY FT.DisplayOrder, FT.FeeTypeName;
        END
END
