# Upgrade schema 56 with a verified backup and unchanged financial-record hashes.
param([switch]$Apply,[string]$Instance='(localdb)\MSSQLLocalDB')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$cn=[Data.SqlClient.SqlConnection]::new("Server=$Instance;Database=FMSC_DB;Integrated Security=True;TrustServerCertificate=True;Pooling=False;Connect Timeout=5")
$tx=$null
function Query([string]$sql){
 $q=$cn.CreateCommand();$q.CommandText=$sql;$q.CommandTimeout=180;if($tx){$q.Transaction=$tx}
 $data=[Data.DataTable]::new();$a=[Data.SqlClient.SqlDataAdapter]::new($q)
 try{[void]$a.Fill($data);return ,$data}finally{$a.Dispose();$q.Dispose()}
}
function Hash([string]$table,[string]$columns,[string]$order){
 return [string](Query "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT $columns FROM dbo.[$table] WITH(HOLDLOCK) ORDER BY $order FOR JSON PATH,INCLUDE_NULL_VALUES)),2);").Rows[0][0]
}
try{
 $cn.Open()
 if([string](Query 'SELECT physical_name FROM sys.database_files WHERE file_id=1;').Rows[0][0] -ine 'C:\ProgramData\FMSC_Database\FMSC_DB.mdf'){throw 'Unexpected database file; upgrade refused.'}
 $version=[int](Query 'SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC;').Rows[0][0]
 if($version -eq 57){Write-Output 'Schema 57 is already applied.';return}
 if($version -ne 56){throw 'This upgrade requires schema 56.'}
 if(!$Apply){Write-Output 'Read-only preview: schema 56 -> 57; existing financial records and original receipt snapshots will be preserved.';return}
 $run=Join-Path $root ('artifacts/historical-return-upgrade/'+[guid]::NewGuid().ToString('N'))
 [void](New-Item -ItemType Directory -Path $run -Force)
 $backup=Join-Path $run 'before-schema-57.bak';$sqlPath=$backup.Replace("'","''")
 [void](Query "BACKUP DATABASE FMSC_DB TO DISK=N'$sqlPath' WITH COPY_ONLY,CHECKSUM,INIT; RESTORE VERIFYONLY FROM DISK=N'$sqlPath' WITH CHECKSUM;")
 $tx=$cn.BeginTransaction([Data.IsolationLevel]::Serializable)
 $tables=Query @'
SELECT T.name TableName,
 (SELECT STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(C.name)),N',') WITHIN GROUP(ORDER BY C.column_id)
 FROM sys.columns C WHERE C.object_id=T.object_id AND C.system_type_id<>189) ColumnsSql,
 COALESCE((SELECT STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(C.name)),N',') WITHIN GROUP(ORDER BY IC.key_ordinal)
 FROM sys.indexes I JOIN sys.index_columns IC ON IC.object_id=I.object_id AND IC.index_id=I.index_id
 JOIN sys.columns C ON C.object_id=IC.object_id AND C.column_id=IC.column_id
 WHERE I.object_id=T.object_id AND I.is_primary_key=1),N'1') OrderSql
FROM sys.tables T JOIN sys.schemas S ON S.schema_id=T.schema_id
WHERE S.name=N'dbo' AND T.name<>N'DatabaseVersion' AND T.is_ms_shipped=0 ORDER BY T.name;
'@
 $before=@{};foreach($table in $tables.Rows){$before[[string]$table.TableName]=Hash $table.TableName $table.ColumnsSql $table.OrderSql}
 foreach($batch in [regex]::Split([IO.File]::ReadAllText((Join-Path $root 'Migrations/57.sql')),'(?im)^\s*GO\s*\r?$')){
  if(![string]::IsNullOrWhiteSpace($batch)){[void](Query $batch)}
 }
 if([int](Query 'SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC;').Rows[0][0] -ne 57){throw 'Upgrade did not reach schema 57.'}
 foreach($table in $tables.Rows){if((Hash $table.TableName $table.ColumnsSql $table.OrderSql) -cne $before[[string]$table.TableName]){throw "Existing records changed in $($table.TableName); upgrade refused."}}
 $tx.Commit();$tx.Dispose();$tx=$null
 [ordered]@{FromVersion=56;ToVersion=57;Backup=$backup;PreservedTableHashes=$before;CompletedUtc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $run 'upgrade-report.json')
 Write-Output "PASS: schema 57 applied; verified $($before.Count) existing tables unchanged. Verified backup: $backup"
}catch{if($tx){try{$tx.Rollback()}catch{}};throw}finally{if($tx){$tx.Dispose()};$cn.Dispose()}
