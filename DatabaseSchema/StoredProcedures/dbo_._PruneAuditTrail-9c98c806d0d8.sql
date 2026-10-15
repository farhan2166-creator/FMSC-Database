-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    /* Removing or gutting audit evidence is a management action, not a viewing one:
       the screen itself is gated on System.Audit. */
    EXEC dbo.RequirePermission @UserId, N'System.Administration';

    IF @ClearPayloadOlderThanMonths IS NULL OR @ClearPayloadOlderThanMonths < 0
        THROW 59930, 'The full-detail window cannot be negative.', 1;

    IF @DeleteOlderThanMonths IS NULL OR @DeleteOlderThanMonths < 0
        THROW 59931, 'The retention window cannot be negative.', 1;

    IF @MaxRows IS NULL OR @MaxRows < 0
        THROW 59932, 'The row cap cannot be negative.', 1;

    DECLARE @PayloadsCleared INT = 0;
    DECLARE @RowsDeleted INT = 0;
    DECLARE @RowsCapped INT = 0;
    DECLARE @OperationsRemoved INT = 0;
    DECLARE @Cutoff DATETIME2;

    BEGIN TRY
        BEGIN TRANSACTION;

        /* ---- stage 1: keep the record, drop the before/after images ---- */

        IF @ClearPayloadOlderThanMonths > 0
        BEGIN
            SET @Cutoff = DATEADD(month, -@ClearPayloadOlderThanMonths, SYSDATETIME());

            UPDATE dbo.AuditLogs
            SET OldValues = NULL,
                NewValues = NULL
            WHERE ActionDateTime < @Cutoff
              AND (OldValues IS NOT NULL OR NewValues IS NOT NULL);

            SET @PayloadsCleared = @@ROWCOUNT;
        END

        /* ---- stage 2: the rows themselves, only when a window is asked for ---- */

        IF @DeleteOlderThanMonths > 0
        BEGIN
            SET @Cutoff = DATEADD(month, -@DeleteOlderThanMonths, SYSDATETIME());

            DELETE FROM dbo.AuditLogs
            WHERE ActionDateTime < @Cutoff;

            SET @RowsDeleted = @@ROWCOUNT;

            /* The headers those rows belonged to. The NOT EXISTS guard is what makes
               this safe on a second run and leaves any operation that still has a
               line to explain it. */
            DELETE FROM dbo.AuditOperations
            WHERE StartedAt < @Cutoff
              AND NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.AuditLogs L
                      WHERE L.OperationId = dbo.AuditOperations.OperationId
                  );

            SET @OperationsRemoved = @@ROWCOUNT;
        END

        /* ---- stage 3: the hard cap - the newest @MaxRows lines, and no more ---- */

        IF @MaxRows > 0
        BEGIN
            /* AuditLogId is an identity, so its order IS the order things happened;
               OFFSET without FETCH is "skip the newest N, delete the rest". */
            DELETE FROM dbo.AuditLogs
            WHERE AuditLogId IN
            (
                SELECT AuditLogId
                FROM dbo.AuditLogs
                ORDER BY AuditLogId DESC
                OFFSET @MaxRows ROWS
            );

            SET @RowsCapped = @@ROWCOUNT;

            /* Headers left with nothing to explain. Bounded by the oldest surviving
               line, so a header that never had a line in the first place is not
               touched - and when the trail is empty the comparison is NULL and
               nothing goes. */
            DECLARE @OldestKept DATETIME2 =
                (SELECT MIN(ActionDateTime) FROM dbo.AuditLogs);

            DELETE FROM dbo.AuditOperations
            WHERE StartedAt < @OldestKept
              AND NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.AuditLogs L
                      WHERE L.OperationId = dbo.AuditOperations.OperationId
                  );

            SET @OperationsRemoved = @OperationsRemoved + @@ROWCOUNT;
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;

        THROW;
    END CATCH

    SELECT
        @PayloadsCleared      AS PayloadsCleared,
        @RowsDeleted          AS RowsDeleted,
        @RowsCapped           AS RowsCapped,
        @OperationsRemoved    AS OperationsRemoved,
        @ClearPayloadOlderThanMonths AS FullDetailMonths,
        @DeleteOlderThanMonths       AS RetentionMonths,
        @MaxRows                     AS MaxRows;
END;
