-- Retire the generic dbo audit trail after removing its application readers.
-- Run through the migration installer so the existing database is backed up.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF ISNULL((SELECT TOP (1) TRY_CONVERT(int, VersionNumber)
           FROM dbo.DatabaseVersion ORDER BY VersionId DESC), 0) <> 1
    THROW 51000, 'Audit retirement requires schema 1.', 1;

IF EXISTS (
    SELECT 1
    FROM sys.sql_modules AS m
    JOIN sys.objects AS o ON o.object_id = m.object_id
    WHERE o.is_ms_shipped = 0
      AND o.object_id NOT IN (
          ISNULL(OBJECT_ID(N'dbo.AuditValueLeaves'), 0),
          ISNULL(OBJECT_ID(N'dbo.GetAuditLogs'), 0),
          ISNULL(OBJECT_ID(N'dbo.GetAuditTableNames'), 0),
          ISNULL(OBJECT_ID(N'dbo.PruneAuditTrail'), 0),
          ISNULL(OBJECT_ID(N'dbo.WriteAuditLog'), 0))
      AND (m.definition LIKE N'%AuditLogs%'
        OR m.definition LIKE N'%AuditOperations%'
        OR m.definition LIKE N'%AuditValueLeaves%'
        OR m.definition LIKE N'%WriteAuditLog%'
        OR m.definition LIKE N'%GetAuditTableNames%'
        OR m.definition LIKE N'%PruneAuditTrail%'))
    THROW 51000, 'A database module still references the retired generic audit system.', 1;

DROP PROCEDURE IF EXISTS dbo.GetAuditLogs, dbo.GetAuditTableNames, dbo.PruneAuditTrail, dbo.WriteAuditLog;
DROP FUNCTION IF EXISTS dbo.AuditValueLeaves;
DROP TABLE IF EXISTS dbo.AuditLogs;
DROP TABLE IF EXISTS dbo.AuditOperations;

INSERT dbo.DatabaseVersion(VersionNumber, AppliedOn, AppliedBy, Notes)
VALUES (N'2', SYSDATETIME(), SUSER_SNAME(), N'Retire the generic audit trail');
