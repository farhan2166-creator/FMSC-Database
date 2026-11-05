# Apply the reviewed migration with a verified backup and unchanged business rows.
param([switch]$Apply,[string]$Instance='(localdb)\MSSQLLocalDB')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$Apply){
 & sqlcmd -S $Instance -E -d FMSC_DB -b -l 10 -Q "SET NOCOUNT ON; SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC;"
 if($LASTEXITCODE -ne 0){throw 'Could not read database version.'}
 Write-Output 'Preview only: migration 64 adds guarded registration reopening and preserves existing rows. Pass -Apply to back up, verify and apply.'
 return
}
$run=Join-Path $root ('artifacts/registration-workflow-upgrade/'+[guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $run -Force)
$backup=Join-Path $run 'before-schema-64.bak'
$backupSql=$backup.Replace("'","''")
$header=@"
SET NOCOUNT ON; SET XACT_ABORT ON;
IF DB_NAME()<>N'FMSC_DB' OR NOT EXISTS(SELECT 1 FROM sys.database_files WHERE file_id=1 AND physical_name=N'C:\ProgramData\FMSC_Database\FMSC_DB.mdf')
 THROW 51000,'Unexpected school database file; upgrade refused.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>63
 THROW 51000,'This upgrade requires schema 63.',1;
BACKUP DATABASE FMSC_DB TO DISK=N'$backupSql' WITH COPY_ONLY,CHECKSUM,INIT;
RESTORE VERIFYONLY FROM DISK=N'$backupSql' WITH CHECKSUM;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRAN;
CREATE TABLE #Preserved(TableName sysname,ColumnsSql nvarchar(max),OrderSql nvarchar(max),BeforeHash varbinary(32));
INSERT #Preserved(TableName,ColumnsSql,OrderSql)
SELECT T.name,
 (SELECT STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(C.name)),N',') WITHIN GROUP(ORDER BY C.column_id)
  FROM sys.columns C WHERE C.object_id=T.object_id AND C.system_type_id<>189),
 COALESCE((SELECT STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(C.name)),N',') WITHIN GROUP(ORDER BY IC.key_ordinal)
  FROM sys.indexes I JOIN sys.index_columns IC ON IC.object_id=I.object_id AND IC.index_id=I.index_id
  JOIN sys.columns C ON C.object_id=IC.object_id AND C.column_id=IC.column_id
  WHERE I.object_id=T.object_id AND I.is_primary_key=1),N'(SELECT NULL)')
FROM sys.tables T JOIN sys.schemas S ON S.schema_id=T.schema_id
WHERE S.name=N'dbo' AND T.name<>N'DatabaseVersion' AND T.is_ms_shipped=0;
DECLARE @Table sysname,@Columns nvarchar(max),@Order nvarchar(max),@Sql nvarchar(max),@Hash varbinary(32);
DECLARE BeforeTables CURSOR LOCAL FAST_FORWARD FOR SELECT TableName,ColumnsSql,OrderSql FROM #Preserved ORDER BY TableName;
OPEN BeforeTables; FETCH NEXT FROM BeforeTables INTO @Table,@Columns,@Order;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @Sql=N'SELECT @H=HASHBYTES(''SHA2_256'',COALESCE((SELECT '+@Columns+N' FROM dbo.'+QUOTENAME(@Table)+N' WITH(HOLDLOCK) ORDER BY '+@Order+N' FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]''));';
 EXEC sp_executesql @Sql,N'@H varbinary(32) OUTPUT',@H=@Hash OUTPUT;
 UPDATE #Preserved SET BeforeHash=@Hash WHERE TableName=@Table;
 FETCH NEXT FROM BeforeTables INTO @Table,@Columns,@Order;
END;
CLOSE BeforeTables; DEALLOCATE BeforeTables;
GO
"@
$footer=@'
DECLARE @Table sysname,@Columns nvarchar(max),@Order nvarchar(max),@Sql nvarchar(max),@Hash varbinary(32),@Before varbinary(32);
DECLARE AfterTables CURSOR LOCAL FAST_FORWARD FOR SELECT TableName,ColumnsSql,OrderSql,BeforeHash FROM #Preserved ORDER BY TableName;
OPEN AfterTables; FETCH NEXT FROM AfterTables INTO @Table,@Columns,@Order,@Before;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @Sql=N'SELECT @H=HASHBYTES(''SHA2_256'',COALESCE((SELECT '+@Columns+N' FROM dbo.'+QUOTENAME(@Table)+N' WITH(HOLDLOCK) ORDER BY '+@Order+N' FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]''));';
 EXEC sp_executesql @Sql,N'@H varbinary(32) OUTPUT',@H=@Hash OUTPUT;
 IF @Hash IS NULL OR @Before IS NULL OR @Hash<>@Before
 BEGIN
  DECLARE @Failure nvarchar(2048)=N'Preservation check failed for '+QUOTENAME(@Table)+N'; migration refused.';
  THROW 51000,@Failure,1;
 END;
 FETCH NEXT FROM AfterTables INTO @Table,@Columns,@Order,@Before;
END;
CLOSE AfterTables; DEALLOCATE AfterTables;
IF (SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC)<>N'64'
 THROW 51000,'Migration did not reach schema 64.',1;
COMMIT;
SELECT COUNT(*) AS PreservedBusinessTables FROM #Preserved;
PRINT 'PASS: schema 64 applied; existing business rows preserved.';
'@
$script=Join-Path $run 'apply.sql'
[IO.File]::WriteAllText($script,$header+"`r`n"+[IO.File]::ReadAllText((Join-Path $root 'Tests/LifecycleSqlChecks/Migrations/64.sql'))+"`r`n"+$footer)
& sqlcmd -S $Instance -E -d FMSC_DB -b -l 10 -t 180 -i $script 2>&1 | Tee-Object -FilePath (Join-Path $run 'result.txt')
if($LASTEXITCODE -ne 0){throw 'Registration workflow migration failed; review result.txt. The transaction is rolled back on connection close.'}
Write-Output "Verified pre-migration backup: $backup"
