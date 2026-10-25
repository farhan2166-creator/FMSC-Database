# Explicit local data cleanup, never part of numbered release migrations.
param([switch]$Apply)
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targets=@('CharacterCertificateTypeReviews','CharacterCertificateIssueSnapshots','DateOfBirthCertificateIssueSnapshots','CharacterCertificateIssues','DateOfBirthCertificateIssues','RegularStudentCertificateIssues','ProvisionalCertificateSubjects','CharacterCertificates','DateOfBirthCertificates','RegularStudentCertificates','SportsCertificates','NoObjectionCertificates','ProvisionalCertificates','RegularLecturerCertificates','BankAccountOpeningCertificates')
$protected=@('LeaveCertificates','LeaveCertificateIssues','LeaveCertificateIssueSnapshots','Students','Enrollments','BillingCharges','FeePayments','FeePaymentAllocations','StudentCreditEntries','CertificateCopyRequests','DatabaseVersion')
$connection=[Data.SqlClient.SqlConnection]::new('Server=(localdb)\MSSQLLocalDB;Database=FMSC_DB;Integrated Security=True;TrustServerCertificate=True;Pooling=False;Connect Timeout=8')
$transaction=$null
function Query([string]$sql){$q=$connection.CreateCommand();$q.CommandText=$sql;$q.CommandTimeout=180;if($transaction){$q.Transaction=$transaction};$table=[Data.DataTable]::new();$adapter=[Data.SqlClient.SqlDataAdapter]::new($q);try{[void]$adapter.Fill($table);return ,$table}finally{$adapter.Dispose();$q.Dispose()}}
function Fingerprint([string]$table,[string]$columns='*'){
 $data=Query "SELECT (SELECT $columns FROM dbo.[$table] WITH(HOLDLOCK) ORDER BY 1 FOR JSON PATH,INCLUDE_NULL_VALUES) Json;"
 return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes([string]$data.Rows[0][0])))
}
try{
 & SqlLocalDB.exe start MSSQLLocalDB | Out-Null
 $connection.Open()
 if([string](Query 'SELECT physical_name FROM sys.database_files WHERE file_id=1;').Rows[0][0] -ine 'C:\ProgramData\FMSC_Database\FMSC_DB.mdf'){throw 'Unexpected database file; cleanup refused.'}
 if([string](Query 'SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC;').Rows[0][0] -ne '51'){throw 'This cleanup was reviewed for schema 51.'}
 $before=@{};foreach($table in $targets){$before[$table]=[long](Query "SELECT COUNT_BIG(*) FROM dbo.[$table];").Rows[0][0]}
 $before.GetEnumerator()|Sort-Object Name|Format-Table Name,Value -AutoSize
 if(!$Apply){Write-Output 'Read-only preview. Use -Apply only for an explicitly authorized cleanup.';return}
 $run=Join-Path $project ('artifacts/certificate-cleanup/'+(Get-Date -Format yyyyMMdd-HHmmss)+'-'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path $run -Force | Out-Null
 $backup=(Join-Path $run 'before-non-leaving-cleanup.bak').Replace("'","''")
 [void](Query "BACKUP DATABASE FMSC_DB TO DISK=N'$backup' WITH COPY_ONLY,CHECKSUM,INIT; RESTORE VERIFYONLY FROM DISK=N'$backup' WITH CHECKSUM;")
 $transaction=$connection.BeginTransaction([Data.IsolationLevel]::Serializable)
 try{
  $fingerprints=@{};foreach($table in $protected){$fingerprints[$table]=Fingerprint $table}
  # Keep request/recovery markers and payloads. Only their nullable bank FK must be detached.
  $markerColumns=[string](Query "SELECT STRING_AGG(QUOTENAME(name),N',') WITHIN GROUP(ORDER BY column_id) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.MutationRequests') AND name<>N'BankCertificateId';").Rows[0][0]
  $markerFingerprint=Fingerprint 'MutationRequests' $markerColumns
  foreach($table in $targets){$before[$table]=[long](Query "SELECT COUNT_BIG(*) FROM dbo.[$table] WITH(TABLOCKX,HOLDLOCK);").Rows[0][0]}
  $quotedTables=($targets|ForEach-Object {"N'$_'"}) -join ','
  $triggers=Query "SELECT OBJECT_NAME(parent_id) TableName,name TriggerName FROM sys.triggers WHERE parent_class=1 AND is_disabled=0 AND OBJECT_NAME(parent_id) IN($quotedTables);"
  foreach($trigger in $triggers.Rows){$triggerName=([string]$trigger.TriggerName).Replace(']',']]');[void](Query "DISABLE TRIGGER [$triggerName] ON dbo.[$($trigger.TableName)];")}
  [void](Query 'UPDATE dbo.MutationRequests SET BankCertificateId=NULL WHERE BankCertificateId IN(SELECT BankAccountOpeningCertificateId FROM dbo.BankAccountOpeningCertificates);')
  foreach($table in $targets){[void](Query "DELETE FROM dbo.[$table];")}
  foreach($trigger in $triggers.Rows){$triggerName=([string]$trigger.TriggerName).Replace(']',']]');[void](Query "ENABLE TRIGGER [$triggerName] ON dbo.[$($trigger.TableName)];")}
  foreach($table in $targets){if([long](Query "SELECT COUNT_BIG(*) FROM dbo.[$table];").Rows[0][0]-ne0){throw "Records remain in $table."}}
  foreach($table in $protected){if((Fingerprint $table)-cne$fingerprints[$table]){throw "Protected records changed in $table; cleanup rolled back."}}
  if((Fingerprint 'MutationRequests' $markerColumns)-cne$markerFingerprint){throw 'Request/recovery marker contents changed; cleanup rolled back.'}
  foreach($trigger in $triggers.Rows){$triggerName=([string]$trigger.TriggerName).Replace("'","''");if([int](Query "SELECT is_disabled FROM sys.triggers WHERE name=N'$triggerName';").Rows[0][0]-ne0){throw 'A certificate guard was not restored.'}}
  $transaction.Commit();$transaction.Dispose();$transaction=$null
  $report=[ordered]@{DeletedRows=$before;ProtectedTableHashes=$fingerprints;Backup=$backup;CompletedUtc=[DateTime]::UtcNow.ToString('O');IdentityReseeded=$false;RequestMarkersPreserved=$true}
  $report|ConvertTo-Json -Depth 5|Set-Content (Join-Path $run 'cleanup-report.json')
  Write-Output 'Deleted all non-leaving certificate records and child histories.'
  Write-Output ('School/College leaving certificates retained: '+[string](Query 'SELECT COUNT_BIG(*) FROM dbo.LeaveCertificates;').Rows[0][0])
  Write-Output 'Verified leaving records, students, enrollments, financial records and recovery markers were preserved. Certificate guards are restored; identities were not reset.'
  Write-Output ('Verified backup: '+$backup)
 }catch{try{$transaction.Rollback()}catch{};throw}
}finally{if($transaction){$transaction.Dispose()};$connection.Dispose()}
