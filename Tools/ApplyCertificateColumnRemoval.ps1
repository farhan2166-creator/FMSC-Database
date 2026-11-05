# Upgrade the owner's restored clean snapshot, with a verified backup and record preservation checks.
param([switch]$Apply)
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$connection=[Data.SqlClient.SqlConnection]::new('Server=(localdb)\MSSQLLocalDB;Database=FMSC_DB;Integrated Security=True;TrustServerCertificate=True;Pooling=False;Connect Timeout=8')
$transaction=$null
function Query([string]$sql){
 $q=$connection.CreateCommand();$q.CommandText=$sql;$q.CommandTimeout=180
 if($transaction){$q.Transaction=$transaction}
 $data=[Data.DataTable]::new();$adapter=[Data.SqlClient.SqlDataAdapter]::new($q)
 try{[void]$adapter.Fill($data);return ,$data}finally{$adapter.Dispose();$q.Dispose()}
}
function Fingerprint([string]$table,[string]$columns,[string]$order){
 $quoted=$table.Replace(']',']]')
 return [string](Query "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT $columns FROM dbo.[$quoted] WITH(HOLDLOCK) ORDER BY $order FOR JSON PATH,INCLUDE_NULL_VALUES)),2);").Rows[0][0]
}
try{
 $connection.Open()
 if([string](Query 'SELECT physical_name FROM sys.database_files WHERE file_id=1;').Rows[0][0] -ine 'C:\ProgramData\FMSC_Database\FMSC_DB.mdf'){throw 'Unexpected database file; upgrade refused.'}
 $version=[int](Query 'SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC;').Rows[0][0]
 if($version -lt 48 -or $version -gt 52){throw 'This upgrade is reviewed for schema 48 through 52.'}
 if($version -eq 52){Write-Output 'Schema 52 is already applied.';return}
 if(!$Apply){Write-Output "Read-only preview: schema $version will upgrade to 52; student and certificate records will be verified unchanged.";return}
 $run=Join-Path $project ('artifacts/certificate-column-removal/'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path $run -Force | Out-Null
 $backup=Join-Path $run 'before-column-removal.bak';$sqlBackup=$backup.Replace("'","''")
 [void](Query "BACKUP DATABASE FMSC_DB TO DISK=N'$sqlBackup' WITH COPY_ONLY,CHECKSUM,INIT; RESTORE VERIFYONLY FROM DISK=N'$sqlBackup' WITH CHECKSUM;")
 $transaction=$connection.BeginTransaction([Data.IsolationLevel]::Serializable)
 try{
  if([int](Query 'SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC;').Rows[0][0] -ne $version){throw 'Database version changed during preparation.'}
  # Compare every existing business column, excluding the explicitly retired fields and
  # rowversion values, which necessarily change when migration 50 fills new snapshot fields.
  $tables=Query @'
SELECT T.name TableName,
 (SELECT STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(C.name)),N',') WITHIN GROUP(ORDER BY C.column_id)
  FROM sys.columns C WHERE C.object_id=T.object_id AND C.system_type_id<>189 AND C.name NOT IN(N'VoidedAt',N'VoidReason')) ColumnsSql,
 COALESCE((SELECT STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(C.name)),N',') WITHIN GROUP(ORDER BY IC.key_ordinal)
  FROM sys.indexes I JOIN sys.index_columns IC ON IC.object_id=I.object_id AND IC.index_id=I.index_id
  JOIN sys.columns C ON C.object_id=IC.object_id AND C.column_id=IC.column_id
  WHERE I.object_id=T.object_id AND I.is_primary_key=1),N'1') OrderSql
FROM sys.tables T JOIN sys.schemas S ON S.schema_id=T.schema_id
WHERE S.name=N'dbo' AND T.name<>N'DatabaseVersion' AND T.is_ms_shipped=0 ORDER BY T.name;
'@
  $before=@{}
  foreach($table in $tables.Rows){$before[[string]$table.TableName]=Fingerprint $table.TableName $table.ColumnsSql $table.OrderSql}
  foreach($migration in 49..52 | Where-Object {$_ -gt $version}){
   $path=Join-Path $project "Tests/LifecycleSqlChecks/Migrations/$migration.sql"
   foreach($batch in [regex]::Split([IO.File]::ReadAllText($path),'(?im)^\s*GO\s*\r?$')){
    if(![string]::IsNullOrWhiteSpace($batch)){[void](Query $batch)}
   }
  }
  if([int](Query 'SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC;').Rows[0][0] -ne 52){throw 'Upgrade did not reach schema 52.'}
  if([int](Query "SELECT COUNT(*) FROM sys.columns WHERE name IN(N'VoidedAt',N'VoidReason');").Rows[0][0] -ne 0){throw 'Retired columns remain.'}
  if([int](Query "SELECT COUNT(*) FROM sys.sql_modules WHERE definition LIKE N'%VoidedAt%' OR definition LIKE N'%VoidReason%';").Rows[0][0] -ne 0){throw 'SQL still references retired columns.'}
  foreach($table in $tables.Rows){if((Fingerprint $table.TableName $table.ColumnsSql $table.OrderSql) -cne $before[[string]$table.TableName]){throw "Existing records changed in $($table.TableName); upgrade rolled back."}}
  $counts=Query 'SELECT N''Students'' TableName,COUNT_BIG(*) Rows FROM dbo.Students UNION ALL SELECT N''LeaveCertificates'',COUNT_BIG(*) FROM dbo.LeaveCertificates UNION ALL SELECT N''LeaveCertificateIssues'',COUNT_BIG(*) FROM dbo.LeaveCertificateIssues UNION ALL SELECT N''LeaveCertificateIssueSnapshots'',COUNT_BIG(*) FROM dbo.LeaveCertificateIssueSnapshots;'
  $transaction.Commit();$transaction.Dispose();$transaction=$null
  [ordered]@{FromVersion=$version;ToVersion=52;Backup=$backup;PreservedTableHashes=$before;CompletedUtc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $run 'upgrade-report.json')
  Write-Output "PASS: upgraded schema $version to 52; verified $($before.Count) existing tables unchanged."
  $counts|Format-Table -AutoSize
  Write-Output "Verified backup: $backup"
 }catch{if($transaction){try{$transaction.Rollback()}catch{}};throw}
}finally{if($transaction){$transaction.Dispose()};$connection.Dispose()}
