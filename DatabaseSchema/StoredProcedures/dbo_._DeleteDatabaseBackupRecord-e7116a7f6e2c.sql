-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    /* Mutating the history is the same kind of action as relocating a record, which
       dbo.UpdateDatabaseBackupPath gates on Backup.Create. */
    EXEC dbo.RequirePermission @UserId, N'Backup.Create';

    IF @BackupId IS NULL OR @BackupId <= 0
        THROW 59920, 'A backup record id is required.', 1;

    DELETE FROM dbo.DatabaseBackups
    WHERE BackupId = @BackupId;

    /* No row count check on purpose: see the idempotence note above. */
END;
