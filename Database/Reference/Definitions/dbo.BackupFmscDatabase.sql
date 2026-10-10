SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- ----------------------------------------------------------------
-- dbo.BackupFmscDatabase
-- ----------------------------------------------------------------

CREATE   PROCEDURE dbo.BackupFmscDatabase
    @UserId INT,
    @BackupPath NVARCHAR(1000)
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Backup.Create';

    BACKUP DATABASE [FMSC_DB]
    TO DISK = @BackupPath
    WITH INIT, FORMAT, CHECKSUM, STATS = 10;
END

GO
