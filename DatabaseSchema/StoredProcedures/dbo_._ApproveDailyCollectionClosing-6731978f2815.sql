-- Module body only. See companion XML for signatures and relationships.

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
        DECLARE @Operation AS UNIQUEIDENTIFIER = NEWID(), @Record AS NVARCHAR (200), @Values AS NVARCHAR (MAX);
        SET @Record = CONVERT (NVARCHAR (200), @ClosingId);
        SET @Values = (SELECT ClosingDate,
                              Revision,
                              ApprovedBy,
                              ApprovedAt
                       FROM   dbo.DailyCollectionClosings
                       WHERE  ClosingId = @ClosingId
                       FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER);
        INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CompletedAt, Status)
        VALUES                     (@Operation, @UserId, N'DAILY_CLOSING_APPROVE', SYSDATETIME(), N'Success');
        EXECUTE dbo.WriteAuditLog @Operation, @UserId, N'DailyCollectionClosings', @Record, N'APPROVE', NULL, @Values;
        DELETE dbo.AuditOperations WHERE OperationId=@Operation AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@Operation);
COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK;
        THROW;
    END CATCH
END
