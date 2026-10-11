

CREATE   PROCEDURE dbo.GetAuditTableNames
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequireAnyPermission
        @UserId,
        N'System.Audit',
        N'System.Administration';

    SELECT DISTINCT
        TableName
    FROM dbo.AuditLogs
    WHERE NULLIF(LTRIM(RTRIM(TableName)), N'') IS NOT NULL
    ORDER BY TableName;
END;

