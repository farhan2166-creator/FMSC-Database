# Creates and owns a uniquely named restored database. Never runs tests against FMSC_DB.
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$owner=[guid]::NewGuid().ToString('N');$catalog='FMSC_CertReissueChecks_'+$owner
$run=Join-Path $project ('artifacts/certificate-reissue-sql/'+$owner)
New-Item -ItemType Directory -Path $run -Force | Out-Null
& SqlLocalDB.exe start MSSQLLocalDB | Out-Null
$log=Join-Path $env:LOCALAPPDATA 'Microsoft/Microsoft SQL Server Local DB/Instances/MSSQLLocalDB/error.log'
$stream=[IO.File]::Open($log,'Open','Read','ReadWrite');$reader=[IO.StreamReader]::new($stream)
try{$text=$reader.ReadToEnd()}finally{$reader.Dispose()}
$pipes=[regex]::Matches($text,'ready to accept connection on \[ (.*?) \]')
if(!$pipes.Count){throw 'No running application database endpoint found.'}
$endpoint='np:'+$pipes[$pipes.Count-1].Groups[1].Value
$connection=[Data.SqlClient.SqlConnection]::new("Server=$endpoint;Database=FMSC_DB;Integrated Security=True;TrustServerCertificate=True;Pooling=False;Connect Timeout=8")
$created=$false
function Query([string]$sql){$q=$connection.CreateCommand();$q.CommandText=$sql;$q.CommandTimeout=120;$t=[Data.DataTable]::new();$a=[Data.SqlClient.SqlDataAdapter]::new($q);try{[void]$a.Fill($t);return ,$t}finally{$a.Dispose();$q.Dispose()}}
function Fingerprint([string]$table){$data=Query "SELECT (SELECT * FROM dbo.[$table] ORDER BY 1 FOR JSON PATH,INCLUDE_NULL_VALUES) Json;";return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes([string]$data.Rows[0][0])))}
try{
 $connection.Open()
 if([string](Query 'SELECT physical_name FROM sys.database_files WHERE file_id=1;').Rows[0][0] -ine 'C:\ProgramData\FMSC_Database\FMSC_DB.mdf'){throw 'Unexpected source database.'}
 if([int](Query 'SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC;').Rows[0][0] -ne 45){throw 'This fixture requires the original schema-45 database.'}
 $backup=Join-Path $run 'before46.bak';$quotedBackup=$backup.Replace("'","''")
 [void](Query "BACKUP DATABASE FMSC_DB TO DISK=N'$quotedBackup' WITH COPY_ONLY,CHECKSUM,INIT; RESTORE VERIFYONLY FROM DISK=N'$quotedBackup' WITH CHECKSUM;")
 $files=Query "RESTORE FILELISTONLY FROM DISK=N'$quotedBackup';"
 $moves=@();$index=0
 foreach($file in $files.Rows){if($file.Type -notin @('D','L')){throw 'Unexpected backup file type.'};$target=Join-Path $run ('fixture-'+$index+$(if($file.Type -eq 'L'){'.ldf'}else{'.mdf'}));$moves+="MOVE N'$(([string]$file.LogicalName).Replace("'","''"))' TO N'$($target.Replace("'","''"))'";$index++}
 $connection.ChangeDatabase('master')
 [void](Query "RESTORE DATABASE [$catalog] FROM DISK=N'$quotedBackup' WITH $($moves -join ','),CHECKSUM,RECOVERY;");$created=$true
 $connection.ChangeDatabase($catalog)
 [void](Query "EXEC sys.sp_addextendedproperty @name=N'FMSC_CertificateChecksOwner',@value=N'$owner';")
 $tables=@('Students','Enrollments','StudentFeeAssignments','BillingCharges','FeePayments','FeePaymentAllocations','StudentCreditEntries','RegularStudentCertificates','DateOfBirthCertificates','DateOfBirthCertificateIssues')
 $before=@{};foreach($table in $tables){$before[$table]=Fingerprint $table}
 $transaction=$connection.BeginTransaction()
 try{
  foreach($batch in [regex]::Split([IO.File]::ReadAllText((Join-Path $project 'Migrations/46.sql')),'(?im)^\s*GO\s*\r?$')){
   if([string]::IsNullOrWhiteSpace($batch)){continue};$q=$connection.CreateCommand();$q.Transaction=$transaction;$q.CommandTimeout=90;$q.CommandText=$batch;try{[void]$q.ExecuteNonQuery()}finally{$q.Dispose()}
  }
  $transaction.Commit()
 }catch{try{$transaction.Rollback()}catch{};throw}finally{$transaction.Dispose()}
 foreach($table in $tables){if((Fingerprint $table)-cne$before[$table]){throw "Migration changed saved $table records."}}
 Write-Output 'PASS: migration 46 preserves all existing student, financial and certificate records in the isolated clone.'
 $env:FMSC_CERTIFICATE_TEST_CONNECTION="Server=$endpoint;Database=$catalog;Integrated Security=True;TrustServerCertificate=True;Pooling=False;Connect Timeout=8"
 $env:FMSC_CERTIFICATE_TEST_OWNER=$owner
 & dotnet run --project (Join-Path $project 'Tests/CertificateReissueSqlChecks/CertificateReissueSqlChecks.csproj') --no-restore
 if($LASTEXITCODE-ne0){throw 'Isolated certificate reissue checks failed.'}
}finally{
 Remove-Item Env:FMSC_CERTIFICATE_TEST_CONNECTION,Env:FMSC_CERTIFICATE_TEST_OWNER -ErrorAction SilentlyContinue
 if($created -and $connection.State-eq'Open'){
  $connection.ChangeDatabase($catalog)
  if([string](Query "SELECT CONVERT(nvarchar(32),value) FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_CertificateChecksOwner';").Rows[0][0] -ne $owner){throw 'Refusing to remove a fixture with the wrong owner.'}
  $expected=[IO.Path]::GetFullPath($run)+[IO.Path]::DirectorySeparatorChar
  foreach($row in (Query 'SELECT physical_name FROM sys.database_files;').Rows){if(![IO.Path]::GetFullPath([string]$row[0]).StartsWith($expected,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture files escaped the owned workspace.'}}
  $connection.ChangeDatabase('master')
  [void](Query "ALTER DATABASE [$catalog] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$catalog];")
 }
 $connection.Dispose()
}
