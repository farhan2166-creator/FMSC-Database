-- Audit retirement: daily closing, applying rates and stopping fee assignments.
-- Preserve permissions, validation, transactions, operational history and returned results.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 38 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>37
 THROW 51000,'Migration 38 requires schema 37.',1;
GO







-- SaveDailyCollectionClosing
CREATE OR ALTER PROCEDURE dbo.SaveDailyCollectionClosing
@UserId INT, @ClosingDate DATE, @ExpectedClosingId BIGINT, @SnapshotHash VARCHAR (64), @ActualAmounts NVARCHAR (MAX), @Notes NVARCHAR (2000)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Collect';
    IF @ClosingDate IS NULL
       OR @ClosingDate > CONVERT (DATE, GETDATE())
        THROW 59921, 'Choose today or an earlier date.', 1;
    IF @ExpectedClosingId IS NULL
       OR @SnapshotHash IS NULL
       OR ISJSON(@ActualAmounts) <> 1
       OR LEFT(LTRIM(@ActualAmounts), 1) <> N'['
        THROW 59922, 'Actual amounts or closing version are invalid.', 1;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @Resource AS NVARCHAR (255) = CONCAT('DailyClosing:', CONVERT (CHAR (10), @ClosingDate, 23)), @Lock AS INT;
        EXECUTE @Lock = sys.sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @Lock < 0
            THROW 59923, 'This day is being closed by another operator. Reload and try again.', 1;
        DECLARE @Id AS BIGINT, @Revision AS INT, @Snapshot AS NVARCHAR (MAX), @Hash AS VARCHAR (64);
        SELECT   TOP (1) @Id = ClosingId,
                         @Revision = Revision
        FROM     dbo.DailyCollectionClosings
        WHERE    ClosingDate = @ClosingDate
        ORDER BY Revision DESC;
        IF COALESCE (@Id, 0) <> @ExpectedClosingId
            THROW 59924, 'The closing changed. Reload before submitting.', 1;
        EXECUTE dbo.DailyClosingSnapshot @ClosingDate, @Snapshot OUTPUT, @Hash OUTPUT;
        IF @Hash <> @SnapshotHash
            THROW 59925, 'Payments changed. Reload and check the amounts again.', 1;
        DECLARE @Actual TABLE (
            Method NVARCHAR (30)  ,
            Amount DECIMAL (18, 2));
        INSERT @Actual
        SELECT Method,
               Amount
        FROM   OPENJSON (@ActualAmounts) WITH (Method NVARCHAR (30), Amount DECIMAL (18, 2));
        IF EXISTS (SELECT   Method
                   FROM     @Actual
                   GROUP BY Method
                   HAVING   COUNT(*) <> 1)
           OR EXISTS (SELECT 1
                      FROM   @Actual
                      WHERE  Method IS NULL
                             OR Amount IS NULL
                             OR ABS(Amount) > 9999999999999999.99)
            THROW 59926, 'Enter one valid actual amount for every payment method.', 1;
        DECLARE @Recorded TABLE (
            Method NVARCHAR (30)  ,
            Amount DECIMAL (18, 2));
        INSERT @Recorded
        SELECT   Method,
                 SUM(Amount)
        FROM     OPENJSON (@Snapshot) WITH (Method NVARCHAR (30) '$.PaymentMethod', Amount DECIMAL (18, 2) '$.TotalAmount')
        WHERE    Method NOT IN (N'Refund', N'Correction')
        GROUP BY Method;
        IF EXISTS (SELECT Method
                   FROM   @Actual
                   EXCEPT
                   SELECT Method
                   FROM   @Recorded)
           OR EXISTS (SELECT Method
                      FROM   @Recorded
                      EXCEPT
                      SELECT Method
                      FROM   @Actual)
            THROW 59926, 'Enter actual amounts for exactly the listed payment methods.', 1;
        IF EXISTS (SELECT 1
                   FROM   @Actual
                   WHERE  Amount < 0
                          AND Method NOT IN (N'Refund', N'Correction'))
            THROW 59927, 'Cash, bank and other collection amounts cannot be negative.', 1;
        IF EXISTS (SELECT 1
                   FROM   @Recorded AS R
                          INNER JOIN
                          @Actual AS A
                          ON A.Method = R.Method
                   WHERE  A.Amount <> R.Amount)
           AND NULLIF (LTRIM(RTRIM(@Notes)), N'') IS NULL
            THROW 59928, 'Explain the difference in the closing notes.', 1;
        IF EXISTS (SELECT 1
                   FROM   OPENJSON (@Snapshot) WITH (Method NVARCHAR (30) '$.PaymentMethod')
                   WHERE  Method IN (N'Refund', N'Correction'))
           AND NULLIF (LTRIM(RTRIM(@Notes)), N'') IS NULL
            THROW 59929, 'Describe the refunds or corrections reviewed, including their cash/bank impact.', 1;
        INSERT  dbo.DailyCollectionClosings (ClosingDate, Revision, SnapshotHash, PaymentSnapshot, ActualAmounts, Notes, SubmittedBy)
        VALUES                             (@ClosingDate, COALESCE (@Revision, 0) + 1, @Hash, @Snapshot, @ActualAmounts, COALESCE (@Notes, N''), @UserId);
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK;
        THROW;
    END CATCH
END
GO

-- ApproveDailyCollectionClosing
CREATE OR ALTER PROCEDURE dbo.ApproveDailyCollectionClosing
@UserId INT, @ClosingDate DATE, @ClosingId BIGINT, @SnapshotHash VARCHAR (64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Collect';
    IF @ClosingDate IS NULL
       OR @ClosingId IS NULL
       OR @SnapshotHash IS NULL
        THROW 59931, 'Load a submitted closing before approval.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Users AS U
                          INNER JOIN
                          dbo.Roles AS R
                          ON R.RoleId = U.RoleId
                   WHERE  U.UserId = @UserId
                          AND R.RoleName = N'Administrator')
       AND NOT EXISTS (SELECT 1
                       FROM   dbo.UserRoles AS UR
                              INNER JOIN
                              dbo.Roles AS R
                              ON R.RoleId = UR.RoleId
                       WHERE  UR.UserId = @UserId
                              AND R.RoleName = N'Administrator')
        THROW 59930, 'Only an administrator can approve a daily closing.', 1;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @Resource AS NVARCHAR (255) = CONCAT('DailyClosing:', CONVERT (CHAR (10), @ClosingDate, 23)), @Lock AS INT;
        EXECUTE @Lock = sys.sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @Lock < 0
            THROW 59923, 'This day is being closed by another operator. Reload and try again.', 1;
        DECLARE @Snapshot AS NVARCHAR (MAX), @Hash AS VARCHAR (64);
        EXECUTE dbo.DailyClosingSnapshot @ClosingDate, @Snapshot OUTPUT, @Hash OUTPUT;
        IF @Hash <> @SnapshotHash
            THROW 59925, 'Payments changed. Reload and review this day.', 1;
        UPDATE dbo.DailyCollectionClosings
        SET    ApprovedBy = @UserId,
               ApprovedAt = SYSDATETIME()
        WHERE  ClosingId = @ClosingId
               AND ClosingDate = @ClosingDate
               AND SnapshotHash = @Hash
               AND ApprovedAt IS NULL
               AND ClosingId = (SELECT MAX(ClosingId)
                                FROM   dbo.DailyCollectionClosings
                                WHERE  ClosingDate = @ClosingDate);
        IF @@ROWCOUNT <> 1
            THROW 59931, 'This closing is stale or already approved. Reload it.', 1;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK;
        THROW;
    END CATCH
END
GO

-- ApplyFeeConfigurationToExistingAssignments
CREATE OR ALTER PROCEDURE dbo.ApplyFeeConfigurationToExistingAssignments
@UserId INT, @CampusId INT, @ClassId INT, @FeeTypeId INT, @EffectiveFrom DATE, @Reason NVARCHAR (500)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT OFF;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Configuration.Manage';
    IF @Reason IS NULL
       OR LTRIM(RTRIM(@Reason)) = N''
        THROW 53151, 'A reason is required to apply a fee configuration change to existing students.', 1;
    IF @EffectiveFrom IS NULL
        THROW 53152, 'An effective-from date is required.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.FeeConfigurations
                   WHERE  CampusId = @CampusId
                          AND ClassId = @ClassId
                          AND FeeTypeId = @FeeTypeId)
        THROW 53153, 'No fee configuration exists for the selected campus, class and fee type.', 1;
    DECLARE @NormalizedEffectiveFrom AS DATE = DATEFROMPARTS(YEAR(@EffectiveFrom), MONTH(@EffectiveFrom), 1);
    DECLARE @Candidates TABLE (
        RowNum                 INT             IDENTITY (1, 1),
        StudentFeeAssignmentId INT            ,
        NewAmount              DECIMAL (12, 2));
    INSERT INTO @Candidates (StudentFeeAssignmentId, NewAmount)
    SELECT StudentFeeAssignmentId,
           NewAmount
    FROM   dbo.fn_StaleFeeAssignmentsForConfiguration(@CampusId, @ClassId, @FeeTypeId);
    DECLARE @Total AS INT = (SELECT COUNT(*)
                             FROM   @Candidates);
    DECLARE @Succeeded AS INT = 0;
    DECLARE @Failed AS INT = 0;
    DECLARE @i AS INT = 1;
    DECLARE @CurrentAssignmentId AS INT, @CurrentNewAmount AS DECIMAL (12, 2);
    DECLARE @Failures TABLE (
        StudentFeeAssignmentId INT            ,
        ErrorMessage           NVARCHAR (2000));
    WHILE @i <= @Total
        BEGIN
            SELECT @CurrentAssignmentId = StudentFeeAssignmentId,
                   @CurrentNewAmount = NewAmount
            FROM   @Candidates
            WHERE  RowNum = @i;
            BEGIN TRY
                EXECUTE dbo.ChangeStudentFeeAssignmentAmount_Internal @UserId = @UserId, @StudentFeeAssignmentId = @CurrentAssignmentId, @NewAmount = @CurrentNewAmount, @EffectiveFrom = @NormalizedEffectiveFrom, @Reason = @Reason;
                SET @Succeeded = @Succeeded + 1;
            END TRY
            BEGIN CATCH
                SET @Failed = @Failed + 1;
                INSERT  INTO @Failures (StudentFeeAssignmentId, ErrorMessage)
                VALUES                (@CurrentAssignmentId, ERROR_MESSAGE());
            END CATCH
            SET @i = @i + 1;
        END
    SELECT @Total AS TotalAssignments,
           @Succeeded AS Succeeded,
           @Failed AS Failed,
           @NormalizedEffectiveFrom AS EffectiveFrom;
    SELECT StudentFeeAssignmentId,
           ErrorMessage
    FROM   @Failures;
END
GO

-- DiscontinueStudentFeeAssignment
CREATE OR ALTER PROCEDURE dbo.DiscontinueStudentFeeAssignment
@UserId INT, @StudentFeeAssignmentId INT, @EffectiveTo DATE, @Reason NVARCHAR (500)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
    IF @Reason IS NULL
       OR LTRIM(RTRIM(@Reason)) = N''
        THROW 53130, 'A reason is required to discontinue a fee assignment.', 1;
    IF @EffectiveTo IS NULL
        THROW 53136, 'An effective-to date is required.', 1;
    DECLARE @EnrollmentId AS INT, @ExistingEffectiveFrom AS DATE, @ExistingEffectiveTo AS DATE, @CancelledAt AS DATETIME2;
    SELECT @EnrollmentId = EnrollmentId,
           @ExistingEffectiveFrom = EffectiveFrom,
           @ExistingEffectiveTo = EffectiveTo,
           @CancelledAt = CancelledAt
    FROM   dbo.StudentFeeAssignments
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId;
    IF @EnrollmentId IS NULL
        THROW 53131, 'The selected student fee assignment does not exist.', 1;
    IF @CancelledAt IS NOT NULL
        THROW 53132, 'This fee assignment has been cancelled.', 1;
    IF @ExistingEffectiveTo IS NOT NULL
        THROW 53133, 'This fee assignment has already been discontinued.', 1;
    DECLARE @NormalizedEffectiveTo AS DATE = DATEFROMPARTS(YEAR(@EffectiveTo), MONTH(@EffectiveTo), 1);
    IF @ExistingEffectiveFrom IS NOT NULL
       AND @NormalizedEffectiveTo < @ExistingEffectiveFrom
        THROW 53137, 'Effective-to month cannot be earlier than this version''s own starting month.', 1;
    DECLARE @LastBilledMonth AS DATE = (SELECT MAX(BillingMonth)
                                        FROM   dbo.BillingCharges
                                        WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId);
    IF @LastBilledMonth IS NOT NULL
       AND @NormalizedEffectiveTo < @LastBilledMonth
        THROW 53134, 'Effective-to month cannot be earlier than the last month already billed for this fee.', 1;
    BEGIN TRANSACTION;
    UPDATE dbo.StudentFeeAssignments
    SET    EffectiveTo = @NormalizedEffectiveTo,
           UpdatedAt   = SYSDATETIME()
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId
           AND EffectiveTo IS NULL;
    IF @@ROWCOUNT = 0
        BEGIN
            ROLLBACK;
            THROW 53135, 'The fee assignment could not be discontinued (it may have just been changed by another operation).', 1;
        END
    COMMIT TRANSACTION;
    SELECT StudentFeeAssignmentId,
           EnrollmentId,
           AdmissionId,
           FeeTypeId,
           Amount,
           DiscountAmount,
           Frequency,
           EffectiveFrom,
           EffectiveTo
    FROM   dbo.StudentFeeAssignments
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId;
END
GO

IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>37',@Definition)=0 THROW 51000,'Unexpected communication callback definition.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>37',N'FROM dbo.DatabaseVersion)<>38');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition)); EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'38',SYSDATETIME(),SUSER_SNAME(),N'Retire audit-only work in closing save/approve, applying configured rates and stopping fees');
GO
