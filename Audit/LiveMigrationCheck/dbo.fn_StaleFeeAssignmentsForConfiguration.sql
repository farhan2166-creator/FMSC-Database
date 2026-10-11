
-- fn_StaleFeeAssignmentsForConfiguration
CREATE   FUNCTION dbo.fn_StaleFeeAssignmentsForConfiguration
(@CampusId INT, @ClassId INT, @FeeTypeId INT)
RETURNS TABLE 
AS
RETURN 
    (SELECT SFA.StudentFeeAssignmentId,
            SFA.EnrollmentId,
            SFA.AdmissionId,
            SFA.Amount AS CurrentAmount,
            FC.Amount AS NewAmount,
            SFA.Frequency AS CurrentFrequency,
            FC.Frequency AS ConfiguredFrequency,
            SFA.EffectiveFrom AS AssignmentEffectiveFrom
     FROM   dbo.StudentFeeAssignments AS SFA
            INNER JOIN
            dbo.Enrollments AS E
            ON E.EnrollmentId = SFA.EnrollmentId
            INNER JOIN
            dbo.CampusClassSections AS CCS
            ON CCS.ClassSectionId = E.ClassSectionId
            INNER JOIN
            dbo.FeeConfigurations AS FC
            ON FC.CampusId = @CampusId
               AND FC.ClassId = @ClassId
               AND FC.FeeTypeId = @FeeTypeId
     WHERE  SFA.FeeTypeId = @FeeTypeId
            AND SFA.CancelledAt IS NULL
            AND SFA.Frequency IN (N'Monthly', N'Yearly')
            AND FC.Frequency IN (N'Monthly', N'Yearly')
            AND SFA.EffectiveTo IS NULL
            AND CCS.CampusId = @CampusId
            AND CCS.ClassId = @ClassId
            AND E.Status IN (N'Active', N'Promoted')
            AND SFA.Amount <> FC.Amount)




