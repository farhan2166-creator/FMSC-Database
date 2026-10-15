-- Module body only. See companion XML for signatures and relationships.

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
        DECLARE @NewId AS BIGINT = SCOPE_IDENTITY(), @Operation AS UNIQUEIDENTIFIER = NEWID(), @Record AS NVARCHAR (200), @Values AS NVARCHAR (MAX);
        SET @Record = CONVERT (NVARCHAR (200), @NewId);
        SET @Values = (SELECT ClosingDate,
                              Revision,
                              ActualAmounts,
                              Notes,
                              SubmittedBy,
                              SubmittedAt
                       FROM   dbo.DailyCollectionClosings
                       WHERE  ClosingId = @NewId
                       FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER);
        INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CompletedAt, Status)
        VALUES                     (@Operation, @UserId, N'DAILY_CLOSING_SUBMIT', SYSDATETIME(), N'Success');
        EXECUTE dbo.WriteAuditLog @Operation, @UserId, N'DailyCollectionClosings', @Record, N'INSERT', NULL, @Values;
        DELETE dbo.AuditOperations WHERE OperationId=@Operation AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@Operation);
COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK;
        THROW;
    END CATCH
END
