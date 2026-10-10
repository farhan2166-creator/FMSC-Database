SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- RecordDatabaseBackup
CREATE   PROCEDURE dbo.RecordDatabaseBackup
@Operation NVARCHAR (20), @FileName NVARCHAR (260), @FilePath NVARCHAR (1000), @FileSizeBytes BIGINT=NULL, @Status NVARCHAR (20), @UserId INT, @Remarks NVARCHAR (500)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Users
                   WHERE  UserId = @UserId)
        THROW 50001, 'The specified user does not exist.', 1;
    INSERT  INTO dbo.DatabaseBackups (Operation, FileName, FilePath, FileSizeBytes, BackupDate, Status, UserId, Remarks)
    VALUES                          (@Operation, @FileName, @FilePath, @FileSizeBytes, SYSUTCDATETIME(), @Status, @UserId, @Remarks);
END



GO
