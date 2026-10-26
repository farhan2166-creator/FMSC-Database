# Integration checks in an owned COPY_ONLY clone. Never changes school records.
param([string]$Instance='(localdb)\MSSQLLocalDB')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$id=[guid]::NewGuid().ToString('N');$catalog='FMSC_ReceiptTrail_'+$id
$run=Join-Path $root ('artifacts/receipt-paper-trail/'+$id)
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
 if([int](Query 'SELECT TOP(1) VersionNumber FROM FMSC_DB.dbo.DatabaseVersion ORDER BY VersionId DESC;').Tables[0].Rows[0][0] -eq 56){
  $cn.Close();& (Join-Path $PSScriptRoot 'TestFeeVisits.ps1') -Instance $Instance;return
 }
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
 $before=[string](Query "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM CollectionReceipts ORDER BY CreatedAt,ReceiptNumber FOR JSON PATH)),2);").Tables[0].Rows[0][0]
 $version=[int](Query 'SELECT TOP(1) VersionNumber FROM DatabaseVersion ORDER BY VersionId DESC;').Tables[0].Rows[0][0]
 if($version -eq 54){
  $tx=$cn.BeginTransaction()
  try{
   foreach($batch in [regex]::Split([IO.File]::ReadAllText((Join-Path $root 'Migrations/55.sql')),'(?im)^\s*GO\s*\r?$')){
    if([string]::IsNullOrWhiteSpace($batch)){continue};$q=$cn.CreateCommand();$q.Transaction=$tx;$q.CommandTimeout=180;$q.CommandText=$batch
    try{[void]$q.ExecuteNonQuery()}finally{$q.Dispose()}
   };$tx.Commit()
  }catch{$tx.Rollback();throw}finally{$tx.Dispose()}
 }elseif($version -ne 55){throw 'This test requires schema 54 or 55.'}
 # Compare the original columns only; the new version defaults to 1.
 $after=[string](Query "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT CollectionReceiptId,ReceiptNumber,PrimaryAdmissionId,TransactionDate,CreatedAt,CreatedByUserId,CashReceived,CashApplied,CreditApplied,DiscountApplied,BalanceRemaining,CreditRemaining,PaymentMethod,Remarks,SnapshotJson FROM CollectionReceipts ORDER BY CreatedAt,ReceiptNumber FOR JSON PATH)),2);").Tables[0].Rows[0][0]
 if($version -eq 54 -and $before -cne $after){throw 'Migration changed an original receipt.'}
 Write-Output 'PASS: migration preserves every existing receipt and snapshot.'
 $result=Query ([IO.File]::ReadAllText((Join-Path $root 'Validation/ReceiptPaperTrailChecks.sql')))
 foreach($table in $result.Tables){foreach($row in $table.Rows){if(([string]$row[0]).StartsWith('PASS:')){Write-Output ([string]$row[0])}}}
 $read=Query @'
BEGIN TRAN;
DECLARE @Actor int=(SELECT TOP(1) U.UserId FROM Users U JOIN Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator');
DECLARE @Now datetime2(0)=SYSDATETIME();
EXEC dbo.IssueCollectionReceipt @Actor,17,@Now,N'Fee changes',N'Reader check',N'[17,18,19,3272]',N'[]',N'[]',0,0,0,1;
DECLARE @Number nvarchar(40)=(SELECT TOP(1) ReceiptNumber FROM CollectionReceipts WHERE SnapshotVersion=2 ORDER BY CreatedAt DESC,ReceiptNumber DESC);
EXEC dbo.GetCollectionReceipt @Actor,@Number;
SELECT DiscountApplied,PrintedDiscountAmount FROM CollectionReceipts WHERE ReceiptNumber=@Number;
ROLLBACK;
'@
 if($read.Tables.Count -ne 6 -or [int]$read.Tables[1].Rows[0].SnapshotVersion -ne 2 -or $read.Tables[3].Rows.Count -eq 0 -or $read.Tables[4].Rows.Count -eq 0){throw 'The complete receipt reader lost saved fee or agreement details.'}
 if([decimal]$read.Tables[5].Rows[0].DiscountApplied -ne 0 -or [decimal]$read.Tables[5].Rows[0].PrintedDiscountAmount -ne [decimal]$read.Tables[1].Rows[0].DiscountApplied){throw 'Printed cumulative concessions leaked into transaction discount totals.'}
 Write-Output 'PASS: receipt reader returns complete fees and agreements, while transaction discount totals remain separate.'
 Write-Output "PASS: disposable integration database $catalog; backup retained at $backup"
}finally{
 if($cn.State -eq 'Open' -and $created){$cn.ChangeDatabase('master');[void](Query "ALTER DATABASE [$catalog] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$catalog];")}
 $cn.Dispose()
}
