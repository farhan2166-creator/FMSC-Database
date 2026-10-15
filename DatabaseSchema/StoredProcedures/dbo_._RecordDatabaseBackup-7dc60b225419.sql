-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Users
                   WHERE  UserId = @UserId)
        THROW 50001, 'The specified user does not exist.', 1;
    INSERT  INTO dbo.DatabaseBackups (Operation, FileName, FilePath, FileSizeBytes, BackupDate, Status, UserId, Remarks)
    VALUES                          (@Operation, @FileName, @FilePath, @FileSizeBytes, SYSUTCDATETIME(), @Status, @UserId, @Remarks);
END
