# Applies pending certificate migrations only to a uniquely owned database clone, then exercises the copy lifecycle.
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$owner=[guid]::NewGuid().ToString('N');$catalog='FMSC_CertificateWorkflow_'+$owner
$run=Join-Path $project ('artifacts/certificate-workflow-tests/'+$owner)
New-Item -ItemType Directory -Path $run -Force | Out-Null
& SqlLocalDB.exe start MSSQLLocalDB | Out-Null
$connection=[Data.SqlClient.SqlConnection]::new('Server=(localdb)\MSSQLLocalDB;Database=FMSC_DB;Integrated Security=True;TrustServerCertificate=True;Pooling=False;Connect Timeout=8')
$created=$false
function Query([string]$sql){$q=$connection.CreateCommand();$q.CommandText=$sql;$q.CommandTimeout=180;$data=[Data.DataSet]::new();$adapter=[Data.SqlClient.SqlDataAdapter]::new($q);try{[void]$adapter.Fill($data);if($data.Tables.Count){return ,$data.Tables[$data.Tables.Count-1]};return ,[Data.DataTable]::new()}finally{$adapter.Dispose();$q.Dispose()}}
try{
 $connection.Open()
 if([string](Query 'SELECT physical_name FROM sys.database_files WHERE file_id=1;').Rows[0][0] -ine 'C:\ProgramData\FMSC_Database\FMSC_DB.mdf'){throw 'Unexpected source database.'}
 $sourceVersion=[int](Query 'SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC;').Rows[0][0]
 if($sourceVersion -notin @(48,49,50,51,52)){throw 'This fixture requires schema 48 through 52.'}
 $backup=(Join-Path $run 'source.bak').Replace("'","''")
 [void](Query "BACKUP DATABASE FMSC_DB TO DISK=N'$backup' WITH COPY_ONLY,CHECKSUM,INIT; RESTORE VERIFYONLY FROM DISK=N'$backup' WITH CHECKSUM;")
 $moves=@();$index=0
 foreach($file in (Query "RESTORE FILELISTONLY FROM DISK=N'$backup';").Rows){$target=Join-Path $run ('fixture-'+$index+$(if($file.Type -eq 'L'){'.ldf'}else{'.mdf'}));$moves+="MOVE N'$(([string]$file.LogicalName).Replace("'","''"))' TO N'$($target.Replace("'","''"))'";$index++}
 $connection.ChangeDatabase('master');[void](Query "RESTORE DATABASE [$catalog] FROM DISK=N'$backup' WITH $($moves -join ','),CHECKSUM,RECOVERY;");$created=$true
 $connection.ChangeDatabase($catalog);[void](Query "EXEC sys.sp_addextendedproperty @name=N'FMSC_CertificateWorkflowOwner',@value=N'$owner';")
 [void](Query "EXEC sys.sp_addextendedproperty @name=N'FMSC_CertificateChecksOwner',@value=N'$owner';")
 foreach($migration in @(49,50,51,52)|Where-Object {$_ -gt $sourceVersion}){
  $transaction=$connection.BeginTransaction()
  try{foreach($batch in [regex]::Split([IO.File]::ReadAllText((Join-Path $project "Tests/LifecycleSqlChecks/Migrations/$migration.sql")),'(?im)^\s*GO\s*\r?$')){if([string]::IsNullOrWhiteSpace($batch)){continue};$q=$connection.CreateCommand();$q.Transaction=$transaction;$q.CommandTimeout=120;$q.CommandText=$batch;try{[void]$q.ExecuteNonQuery()}finally{$q.Dispose()}};$transaction.Commit()}catch{try{$transaction.Rollback()}catch{};throw}finally{$transaction.Dispose()}
 }
 $fixtureBuilder=[Data.SqlClient.SqlConnectionStringBuilder]::new($connection.ConnectionString);$fixtureBuilder['Initial Catalog']=$catalog
 $env:FMSC_CERTIFICATE_TEST_CONNECTION=$fixtureBuilder.ConnectionString;$env:FMSC_CERTIFICATE_TEST_OWNER=$owner
 try{& dotnet run --project (Join-Path $project 'Tests/CertificateReissueSqlChecks/CertificateReissueSqlChecks.csproj') --no-restore -- --contract-only;if($LASTEXITCODE-ne0){throw 'Schema 52 compatibility contract failed.'}}
 finally{Remove-Item Env:FMSC_CERTIFICATE_TEST_CONNECTION,Env:FMSC_CERTIFICATE_TEST_OWNER -ErrorAction SilentlyContinue}
 $index=0
 foreach($batch in [regex]::Split([IO.File]::ReadAllText((Join-Path $project 'Validation/CertificateWorkflowChecks.sql')),'(?im)^\s*GO\s*\r?$')){
  if([string]::IsNullOrWhiteSpace($batch)){continue};$index++
  try{$result=Query $batch;foreach($row in $result.Rows){Write-Output ([string]$row[0])}}catch{throw "Certificate scenario $index failed: $_"}
 }
 Write-Output 'PASS: migration and certificate lifecycle checks completed in the owned clone; FMSC_DB data was untouched.'
}finally{
 if($created -and $connection.State-eq'Open'){
  $connection.ChangeDatabase($catalog)
  if([string](Query "SELECT CONVERT(nvarchar(32),value) FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_CertificateWorkflowOwner';").Rows[0][0]-ne$owner){throw 'Wrong fixture owner.'}
  $expected=[IO.Path]::GetFullPath($run)+[IO.Path]::DirectorySeparatorChar
  foreach($row in (Query 'SELECT physical_name FROM sys.database_files;').Rows){if(![IO.Path]::GetFullPath([string]$row[0]).StartsWith($expected,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture files escaped the owned workspace.'}}
  $connection.ChangeDatabase('master');[void](Query "ALTER DATABASE [$catalog] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$catalog];")
 }
 $connection.Dispose()
}
