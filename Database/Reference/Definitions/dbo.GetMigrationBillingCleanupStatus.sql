SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO


CREATE PROCEDURE dbo.GetMigrationBillingCleanupStatus
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission @UserId, N'System.Setup';

    SELECT
        SP.MigrationBillingCleanupCompletedAt,
        SP.MigrationBillingCleanupEnrollmentCount,
        U.Username AS CompletedByUsername,
        (
            SELECT COUNT(*)
            FROM dbo.Enrollments E
            WHERE E.Status = N'Active'
              AND E.BillingStartDate IS NULL
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.StudentFeeAssignments SFA
                  INNER JOIN dbo.BillingCharges BC
                      ON BC.StudentFeeAssignmentId = SFA.StudentFeeAssignmentId
                  WHERE SFA.EnrollmentId = E.EnrollmentId
              )
        ) AS CandidateCount
    FROM dbo.SchoolProfile SP
    LEFT JOIN dbo.Users U
        ON U.UserId = SP.MigrationBillingCleanupCompletedByUserId
    WHERE SP.SchoolProfileId = 1;
END;

GO
