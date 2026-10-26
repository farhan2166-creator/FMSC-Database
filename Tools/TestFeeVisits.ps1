# Integration checks in an owned COPY_ONLY clone. Never changes school records.
param([string]$Instance='(localdb)\MSSQLLocalDB')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$id=[guid]::NewGuid().ToString('N');$catalog='FMSC_FeeVisits_'+$id
$run=Join-Path $root ('artifacts/fee-visits/'+$id)
[void](New-Item -ItemType Directory -Path $run -Force)
$backup=Join-Path $run 'fixture.bak';$created=$false
$cn=[Data.SqlClient.SqlConnection]::new("Server=$Instance;Database=master;Integrated Security=True;TrustServerCertificate=True;Pooling=False;Connect Timeout=5")
function Query([string]$sql){
 $q=$cn.CreateCommand();$q.CommandText=$sql;$q.CommandTimeout=180
 $data=[Data.DataSet]::new();$a=[Data.SqlClient.SqlDataAdapter]::new($q)
 try{[void]$a.Fill($data);return ,$data}finally{$a.Dispose();$q.Dispose()}
}
try{
 $cn.Open()
 [void](Query ("BACKUP DATABASE FMSC_DB TO DISK=N'"+$backup.Replace("'","''")+"' WITH COPY_ONLY,CHECKSUM,INIT;"))
 $list=Query ("RESTORE FILELISTONLY FROM DISK=N'"+$backup.Replace("'","''")+"';")
 $moves=@();$n=0
 foreach($file in $list.Tables[0].Rows){
  if($file.Type -notin @('D','L')){throw 'Unexpected backup file type.'}
  $path=Join-Path $run ('fixture-'+$n+$(if($file.Type -eq 'L'){'.ldf'}else{'.mdf'}))
  $moves+="MOVE N'$(([string]$file.LogicalName).Replace("'","''"))' TO N'$($path.Replace("'","''"))'";$n++
 }
 [void](Query ("RESTORE DATABASE [$catalog] FROM DISK=N'"+$backup.Replace("'","''")+"' WITH "+($moves -join ',')+",CHECKSUM,RECOVERY;"));$created=$true
 $cn.ChangeDatabase($catalog)
 $before=[string](Query "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT CollectionReceiptId,ReceiptNumber,SnapshotJson,SnapshotVersion,PrintedDiscountAmount FROM CollectionReceipts ORDER BY CreatedAt,ReceiptNumber FOR JSON PATH)),2);").Tables[0].Rows[0][0]
 $version=[int](Query 'SELECT TOP(1) VersionNumber FROM DatabaseVersion ORDER BY VersionId DESC;').Tables[0].Rows[0][0]
 if($version -eq 55){
  $tx=$cn.BeginTransaction()
  try{
   foreach($batch in [regex]::Split([IO.File]::ReadAllText((Join-Path $root 'Migrations/56.sql')),'(?im)^\s*GO\s*\r?$')){
    if([string]::IsNullOrWhiteSpace($batch)){continue};$q=$cn.CreateCommand();$q.Transaction=$tx;$q.CommandTimeout=180;$q.CommandText=$batch
    try{[void]$q.ExecuteNonQuery()}finally{$q.Dispose()}
   };$tx.Commit()
  }catch{$tx.Rollback();throw}finally{$tx.Dispose()}
 }elseif($version -ne 56){throw 'This test requires schema 55 or 56.'}
 $after=[string](Query "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT CollectionReceiptId,ReceiptNumber,SnapshotJson,SnapshotVersion,PrintedDiscountAmount FROM CollectionReceipts ORDER BY CreatedAt,ReceiptNumber FOR JSON PATH)),2);").Tables[0].Rows[0][0]
 if($before -cne $after){throw 'Migration changed an original receipt snapshot.'}
 Write-Output 'PASS: migration preserves every original receipt snapshot.'
 $result=Query ([IO.File]::ReadAllText((Join-Path $root 'Validation/FeeVisitChecks.sql')))
 foreach($table in $result.Tables){foreach($row in $table.Rows){if(([string]$row[0]).StartsWith('PASS:')){Write-Output ([string]$row[0])}}}
 for($visit=1;$visit -le 3;$visit++){
  $number='FR-2026-'+(11+$visit).ToString('00000')
  $saved=Query "DECLARE @Actor int=(SELECT TOP(1) U.UserId FROM Users U JOIN Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator'); EXEC dbo.GetCollectionReceipt @Actor,N'$number';"
  if($saved.Tables.Count -ne 4 -or [int]$saved.Tables[0].Rows[0].SnapshotVersion -ne 3){throw 'Visit receipt reader returned an unexpected shape.'}
  $saved.WriteXml((Join-Path $run "actual-visit-$visit.xml"),[Data.XmlWriteMode]::WriteSchema)
 }
 [IO.File]::WriteAllText((Join-Path $root 'artifacts/fee-visits/latest-fixture.txt'),$run)
 Write-Output 'PASS: actual reader data exported for C# report rendering checks.'
 Write-Output "PASS: visit integration checks used disposable database $catalog."
}finally{
 if($cn.State -eq 'Open' -and $created){$cn.ChangeDatabase('master');[void](Query "ALTER DATABASE [$catalog] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$catalog];")}
 $cn.Dispose()
}
