SET NOCOUNT ON;
SELECT (SELECT DB_NAME() name,physical_name FROM sys.database_files FOR JSON PATH) identity_json,
(SELECT s.name schema_name,o.name,o.type,m.definition,m.uses_ansi_nulls,m.uses_quoted_identifier FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id JOIN sys.sql_modules m ON m.object_id=o.object_id FOR JSON PATH) modules_json,
(SELECT s.name schema_name,t.name table_name,c.name column_name,TYPE_NAME(c.user_type_id) type_name,c.max_length,c.is_nullable FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id FOR JSON PATH) columns_json,
(SELECT VersionId,VersionNumber,AppliedOn,Notes FROM dbo.DatabaseVersion ORDER BY VersionId FOR JSON PATH) versions_json
FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
