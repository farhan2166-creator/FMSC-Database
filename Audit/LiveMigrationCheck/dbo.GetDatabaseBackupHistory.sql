
-- ----------------------------------------------------------------
-- dbo.GetDatabaseBackupHistory
-- ----------------------------------------------------------------

CREATE   PROCEDURE dbo.GetDatabaseBackupHistory
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Backup.View';

    SELECT
        db.BackupId,
        db.Operation,
        db.FileName,
        db.FilePath,
        db.FileSizeBytes,
        CASE
            WHEN db.FileSizeBytes IS NULL THEN N'-'
            WHEN db.FileSizeBytes < 1024 THEN CAST(db.FileSizeBytes AS NVARCHAR(20)) + N' B'
            WHEN db.FileSizeBytes < 1024 * 1024 THEN CAST(CAST(db.FileSizeBytes / 1024.0 AS DECIMAL(10, 1)) AS NVARCHAR(20)) + N' KB'
            WHEN db.FileSizeBytes < 1024 * 1024 * 1024 THEN CAST(CAST(db.FileSizeBytes / 1024.0 / 1024.0 AS DECIMAL(10, 1)) AS NVARCHAR(20)) + N' MB'
            ELSE CAST(CAST(db.FileSizeBytes / 1024.0 / 1024.0 / 1024.0 AS DECIMAL(10, 1)) AS NVARCHAR(20)) + N' GB'
        END AS FileSize,
        db.BackupDate,
        db.Status,
        db.Remarks,
        u.Username
    FROM dbo.DatabaseBackups db
    INNER JOIN dbo.Users u
        ON u.UserId = db.UserId
    ORDER BY
        db.BackupDate DESC,
        db.BackupId DESC;
END

