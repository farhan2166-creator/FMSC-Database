-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Backup.Create';

    BACKUP DATABASE [FMSC_DB]
    TO DISK = @BackupPath
    WITH INIT, FORMAT, CHECKSUM, STATS = 10;
END
