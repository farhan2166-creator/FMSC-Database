SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- RunMigrationBillingCleanup
CREATE   PROCEDURE dbo.RunMigrationBillingCleanup
@UserId INT, @GoLiveDate DATE, @Reason NVARCHAR (500)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'System.Setup';
    IF @GoLiveDate IS NULL
        THROW 58101, 'A go-live date is required.', 1;
    IF NULLIF (LTRIM(RTRIM(@Reason)), N'') IS NULL
        THROW 58102, 'A reason is required.', 1;
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1
               FROM   dbo.SchoolProfile WITH (UPDLOCK, HOLDLOCK)
               WHERE  SchoolProfileId = 1
                      AND MigrationBillingCleanupCompletedAt IS NOT NULL)
        BEGIN
            ROLLBACK;
            THROW 58103, 'This one-time migration billing cleanup has already been run and cannot be run again. Use "Set Billing Start" on the Student Lifecycle screen for any individual correction needed from here on.', 1;
        END
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.SchoolProfile
                   WHERE  SchoolProfileId = 1)
        BEGIN
            ROLLBACK;
            THROW 58104, 'School profile has not been set up yet. Complete School Information first.', 1;
        END
    DECLARE @Candidates TABLE (
        EnrollmentId INT PRIMARY KEY);
    INSERT INTO @Candidates (EnrollmentId)
    SELECT E.EnrollmentId
    FROM   dbo.Enrollments AS E
    WHERE  E.Status = N'Active'
           AND E.BillingStartDate IS NULL
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.StudentFeeAssignments AS SFA
                                  INNER JOIN
                                  dbo.BillingCharges AS BC
                                  ON BC.StudentFeeAssignmentId = SFA.StudentFeeAssignmentId
                           WHERE  SFA.EnrollmentId = E.EnrollmentId);
    DECLARE @EnrollmentId AS INT, @ProcessedCount AS INT = 0;
    DECLARE cleanup_cursor CURSOR LOCAL FAST_FORWARD
        FOR SELECT EnrollmentId
            FROM   @Candidates;
    OPEN cleanup_cursor;
    FETCH NEXT FROM cleanup_cursor INTO @EnrollmentId;
    WHILE @@FETCH_STATUS = 0
        BEGIN
            EXECUTE dbo.SetEnrollmentBillingStartDate @UserId = @UserId, @EnrollmentId = @EnrollmentId, @BillingStartDate = @GoLiveDate, @Reason = @Reason;
            SET @ProcessedCount += 1;
            FETCH NEXT FROM cleanup_cursor INTO @EnrollmentId;
        END
    CLOSE cleanup_cursor;
    DEALLOCATE cleanup_cursor;
    UPDATE dbo.SchoolProfile
    SET    MigrationBillingCleanupCompletedAt       = SYSDATETIME(),
           MigrationBillingCleanupCompletedByUserId = @UserId,
           MigrationBillingCleanupEnrollmentCount   = @ProcessedCount,
           UpdatedAt                                = SYSDATETIME(),
           UpdatedByUserId                          = @UserId
    WHERE  SchoolProfileId = 1;
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @NewValues AS NVARCHAR (MAX) = (SELECT @GoLiveDate AS GoLiveDate,
                                                   @Reason AS Reason,
                                                   @ProcessedCount AS EnrollmentsProcessed
                                            FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    COMMIT TRANSACTION;
    SELECT @ProcessedCount AS EnrollmentsProcessed,
           @GoLiveDate AS GoLiveDate;
END



GO
