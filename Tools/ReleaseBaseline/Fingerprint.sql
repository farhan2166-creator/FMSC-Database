-- Read-only preservation proof. No school rows or values leave SQL: only hashes.
SET NOCOUNT ON;
CREATE TABLE #Proof(ObjectName nvarchar(300),[RowCount] bigint,DataHash varchar(64));
DECLARE @Schema sysname,@Table sysname,@Order nvarchar(max),@Query nvarchar(max);
DECLARE Tables CURSOR LOCAL FAST_FORWARD FOR
 SELECT s.name,t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
 WHERE t.is_ms_shipped=0 AND NOT(s.name=N'dbo' AND t.name=N'DatabaseVersion') ORDER BY s.name,t.name;
OPEN Tables;FETCH NEXT FROM Tables INTO @Schema,@Table;
WHILE @@FETCH_STATUS=0
BEGIN
 SELECT @Order=STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(c.name)),N',') WITHIN GROUP(ORDER BY ic.key_ordinal)
 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
 JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
 WHERE i.object_id=OBJECT_ID(QUOTENAME(@Schema)+N'.'+QUOTENAME(@Table)) AND i.is_primary_key=1;
 SET @Query=N'INSERT #Proof SELECT N'''+REPLACE(@Schema+N'.'+@Table,N'''',N'''''')+N''',
 (SELECT COUNT_BIG(*) FROM '+QUOTENAME(@Schema)+N'.'+QUOTENAME(@Table)+N'),
 CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT * FROM '+QUOTENAME(@Schema)+N'.'+QUOTENAME(@Table)+
 CASE WHEN @Order IS NULL THEN N'' ELSE N' ORDER BY '+@Order END+N' FOR JSON PATH,INCLUDE_NULL_VALUES))),2);';
 EXEC sys.sp_executesql @Query;
 FETCH NEXT FROM Tables INTO @Schema,@Table;
END;
CLOSE Tables;DEALLOCATE Tables;
SELECT CONCAT(ObjectName,N'|',[RowCount],N'|',DataHash) FROM #Proof ORDER BY ObjectName;
SELECT CONCAT(N'SCHEMA|',CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),(
 SELECT s.name SchemaName,o.name ObjectName,o.type,OBJECT_DEFINITION(o.object_id) Definition,
 (SELECT c.name,TYPE_NAME(c.user_type_id) TypeName,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_computed
  FROM sys.columns c WHERE c.object_id=o.object_id ORDER BY c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES) Columns,
 (SELECT i.name,i.is_unique,i.type,i.filter_definition FROM sys.indexes i WHERE i.object_id=o.object_id ORDER BY i.index_id FOR JSON PATH,INCLUDE_NULL_VALUES) Indexes
 FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
 WHERE o.is_ms_shipped=0 ORDER BY s.name,o.name,o.type FOR JSON PATH,INCLUDE_NULL_VALUES))),2));
SELECT CONCAT(N'DATABASE_GUID|',CONVERT(nvarchar(36),database_guid)) FROM sys.database_recovery_status WHERE database_id=DB_ID();
