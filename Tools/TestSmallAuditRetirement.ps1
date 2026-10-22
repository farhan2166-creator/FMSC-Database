# Owns a unique schema-only LocalDB instance. Accepts no live database/connection parameter.
param([switch]$KeepFixture)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$owner=[guid]::NewGuid().ToString('N')
$catalog='FMSC_BillingChecks_'+$owner
$instance='(localdb)\'+$catalog
$run=Join-Path $projectRoot ('artifacts/small-audit-retirement/'+$owner)
$databaseRoot=Join-Path $run 'database'
New-Item -ItemType Directory -Path $databaseRoot -Force | Out-Null
$created=$false
$checks=0
function Native($operation) {if($LASTEXITCODE -ne 0){throw "$operation failed ($LASTEXITCODE)."}}
function Query([string]$sql,[hashtable]$parameters=@{}) {
 if($catalog -notmatch '^FMSC_BillingChecks_[0-9a-f]{32}$' -or $instance -ne ('(localdb)\'+$catalog)){throw 'Unsafe fixture identity.'}
 $connection=[Data.SqlClient.SqlConnection]::new("Server=$instance;Database=$catalog;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15")
 try {
  $connection.Open()
  $guard=$connection.CreateCommand()
  $guard.CommandText="SELECT CONVERT(nvarchar(32),value) FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_BillingChecksOwner';"
  if([string]$guard.ExecuteScalar() -ne $owner){throw 'Disposable database ownership mismatch.'}
  $command=$connection.CreateCommand();$command.CommandTimeout=90;$command.CommandText=$sql
  foreach($key in $parameters.Keys){[void]$command.Parameters.AddWithValue($key,$parameters[$key])}
  $data=[Data.DataSet]::new();$adapter=[Data.SqlClient.SqlDataAdapter]::new($command);[void]$adapter.Fill($data)
  return ,$data
 } finally {$connection.Dispose()}
}
function Check([bool]$condition,[string]$message) {
 if(-not $condition){throw "FAIL: $message"};$script:checks++;Write-Output "PASS: $message"
}
function Number([string]$sql) { $result=Query $sql;return [decimal]$result.Tables[0].Rows[0][0] }
try {
 & SqlLocalDB create $catalog -s;Native 'Create isolated instance';$created=$true
 $safePath=$databaseRoot.Replace("'","''")
 & sqlcmd -S $instance -d master -E -b -Q "CREATE DATABASE [$catalog] ON PRIMARY(NAME=N'$catalog',FILENAME=N'$safePath\fixture.mdf') LOG ON(NAME=N'${catalog}_log',FILENAME=N'$safePath\fixture_log.ldf');"
 Native 'Create disposable catalog'
 & sqlcmd -S $instance -d $catalog -E -b -Q "EXEC sys.sp_addextendedproperty @name=N'FMSC_BillingChecksOwner',@value=N'$owner';"
 Native 'Mark fixture owner'
 $sqlPackage=Join-Path $env:USERPROFILE '.nuget/packages/microsoft.sqlpackage/170.5.96/tools/net8.0/any/sqlpackage.dll'
 if(-not(Test-Path -LiteralPath $sqlPackage)){throw 'Restore the pinned SQLPackage tool in Tests/LifecycleSqlChecks first.'}
 $fixture=Join-Path $projectRoot 'Tests/LifecycleSqlChecks/Baseline/schema.dacpac'
 & dotnet $sqlPackage '/Action:Publish' "/SourceFile:$fixture" "/TargetConnectionString:Server=$instance;Database=$catalog;Integrated Security=True;TrustServerCertificate=True" '/p:DropObjectsNotInSource=False' '/p:DropExtendedPropertiesNotInSource=False' '/p:ScriptDatabaseOptions=False' '/Quiet:True'
 Native 'Publish data-free schema'
 # DacFx round-trips this recovery identity as varchar. Repair only this owned schema-only fixture.
 [void](Query "DECLARE @Scope uniqueidentifier=NEWID(); EXEC sys.sp_updateextendedproperty @name=N'FMSC_QuickRegistrationScope',@value=@Scope;")
 # Migration changes run under the same transaction boundary used by deployment.
 foreach($file in @(Get-ChildItem (Join-Path $projectRoot 'Tests/LifecycleSqlChecks/Migrations') -Filter '*.sql'; Get-ChildItem (Join-Path $projectRoot 'Migrations') -Filter '*.sql') | Sort-Object {[int]$_.BaseName}) {
  $content=Get-Content -LiteralPath $file.FullName -Raw
  $connection=[Data.SqlClient.SqlConnection]::new("Server=$instance;Database=$catalog;Integrated Security=True;TrustServerCertificate=True")
  $connection.Open();$transaction=$connection.BeginTransaction()
  try {
   $guard=$connection.CreateCommand();$guard.Transaction=$transaction
   $guard.CommandText="IF CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_BillingChecksOwner'))<>N'$owner' THROW 51000,'Wrong fixture owner',1;"
   [void]$guard.ExecuteNonQuery()
   foreach($batch in [regex]::Split($content,'(?im)^\s*GO\s*\r?$')) {
    if([string]::IsNullOrWhiteSpace($batch)){continue}
    $command=$connection.CreateCommand();$command.Transaction=$transaction;$command.CommandTimeout=90;$command.CommandText=$batch
    [void]$command.ExecuteNonQuery()
   }
   $transaction.Commit()
  } catch {if($transaction.Connection){$transaction.Rollback()};throw} finally {$connection.Dispose()}
 }
 $seed=Get-Content -LiteralPath (Join-Path $projectRoot 'Validation/AutomaticBillingFixture.sql') -Raw
 [void](Query $seed)

 function Refused([string]$sql,[int]$expected,[string]$message){
  $number=0
  try{[void](Query $sql)}catch{$errorItem=$_.Exception;while($errorItem.InnerException){$errorItem=$errorItem.InnerException};if($errorItem -is [Data.SqlClient.SqlException]){$number=$errorItem.Number}else{throw}}
  Check ($number -eq $expected) $message
 }
 $baselineLogs=Number 'SELECT COUNT(*) FROM dbo.AuditLogs;'
 $baselineOperations=Number 'SELECT COUNT(*) FROM dbo.AuditOperations;'
 Check ((Number 'SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC;') -eq 40) 'Sequential migration chain reaches schema 40.'
 [void](Query "INSERT dbo.Roles(RoleName) VALUES(N'Fictional No Grants'); INSERT dbo.Users(Username,RoleId) VALUES(N'fictional_second_user',2); UPDATE dbo.Users SET MustChangePassword=1 WHERE UserId=1;")
 Refused "EXEC dbo.ChangeOwnPassword @UserId=1,@PasswordHash=0x01,@PasswordSalt=0x02;" 57001 'Invalid password hash remains rejected.'
 Refused "DECLARE @H varbinary(64)=CONVERT(varbinary(64),REPLICATE('h',64)); EXEC dbo.ChangeOwnPassword @UserId=1,@PasswordHash=@H,@PasswordSalt=0x01;" 57002 'Invalid password salt remains rejected.'
 [void](Query "DECLARE @H varbinary(64)=CONVERT(varbinary(64),REPLICATE('h',64)),@S varbinary(32)=CONVERT(varbinary(32),REPLICATE('s',32)); EXEC dbo.ChangeOwnPassword @UserId=1,@PasswordHash=@H,@PasswordSalt=@S;")
 Check ((Number "SELECT COUNT(*) FROM dbo.Users WHERE UserId=1 AND PasswordHash=CONVERT(varbinary(64),REPLICATE('h',64)) AND PasswordSalt=CONVERT(varbinary(32),REPLICATE('s',32)) AND MustChangePassword=0 AND UpdatedAt IS NOT NULL;") -eq 1) 'Password change saves both supplied values and clears mandatory reset.'
 Refused "EXEC dbo.RenameOwnUsername @UserId=1,@NewUsername=N'   ';" 57301 'Blank username remains rejected.'
 Refused "EXEC dbo.RenameOwnUsername @UserId=1,@NewUsername=N'fictional_second_user';" 57303 'Another account username remains protected.'
 [void](Query "EXEC dbo.RenameOwnUsername @UserId=1,@NewUsername=N'  fictional_renamed_admin  ';")
 Check ((Number "SELECT COUNT(*) FROM dbo.Users WHERE UserId=1 AND Username=N'fictional_renamed_admin' AND UpdatedAt IS NOT NULL;") -eq 1) 'Own username change still trims and saves the supplied name.'
 Refused "EXEC dbo.RenameOwnUsername @UserId=0,@NewUsername=N'forbidden';" 50003 'Unauthenticated username change remains rejected.'
 Refused "DECLARE @D date=CONVERT(date,GETDATE()); EXEC dbo.CreateNoObjectionCertificate @UserId=2,@AdmissionId=1,@CertificateDate=@D;" 50003 'Certificate creation still requires certificate permission.'
 Refused "DECLARE @D date=CONVERT(date,GETDATE()); EXEC dbo.CreateNoObjectionCertificate @UserId=1,@AdmissionId=0,@CertificateDate=@D;" 58600 'Invalid student remains rejected.'
 Refused "EXEC dbo.CreateNoObjectionCertificate @UserId=1,@AdmissionId=1,@CertificateDate=NULL;" 58601 'Missing certificate date remains rejected.'
 Refused "DECLARE @D date=DATEADD(day,1,GETDATE()); EXEC dbo.CreateNoObjectionCertificate @UserId=1,@AdmissionId=1,@CertificateDate=@D;" 58602 'Future certificate date remains rejected.'
 Refused "DECLARE @D date=CONVERT(date,GETDATE()); EXEC dbo.CreateNoObjectionCertificate @UserId=1,@AdmissionId=999999,@CertificateDate=@D;" 58603 'Unknown student remains rejected.'
 $createdCertificate=Query "DECLARE @D date=CONVERT(date,GETDATE()); EXEC dbo.CreateNoObjectionCertificate @UserId=1,@AdmissionId=1,@CertificateDate=@D;"
 $certificateId=[int]$createdCertificate.Tables[0].Rows[0].NoObjectionCertificateId
 Check ($certificateId -gt 0 -and $createdCertificate.Tables[0].Rows[0].AdmissionId -eq 1) 'Certificate creation returns the certificate and admission IDs.'
 Check ((Number "SELECT COUNT(*) FROM dbo.NoObjectionCertificates WHERE NoObjectionCertificateId=$certificateId AND AdmissionId=1 AND StudentName=N'Ready Example' AND CreatedByUserId=1 AND CertificateDate=CONVERT(date,GETDATE());") -eq 1) 'Certificate retains the student snapshot, creator and requested date.'
 Refused "DECLARE @D date=CONVERT(date,GETDATE()); EXEC dbo.UpdateNoObjectionCertificate @UserId=2,@NoObjectionCertificateId=$certificateId,@CertificateDate=@D;" 50003 'Certificate update still requires certificate permission.'
 Refused "DECLARE @D date=CONVERT(date,GETDATE()); EXEC dbo.UpdateNoObjectionCertificate @UserId=1,@NoObjectionCertificateId=0,@CertificateDate=@D;" 58610 'Invalid certificate ID remains rejected.'
 Refused "EXEC dbo.UpdateNoObjectionCertificate @UserId=1,@NoObjectionCertificateId=$certificateId,@CertificateDate=NULL;" 58611 'Update still rejects a missing date.'
 Refused "DECLARE @D date=DATEADD(day,1,GETDATE()); EXEC dbo.UpdateNoObjectionCertificate @UserId=1,@NoObjectionCertificateId=$certificateId,@CertificateDate=@D;" 58612 'Update still rejects a future date.'
 Refused "DECLARE @D date=CONVERT(date,GETDATE()); EXEC dbo.UpdateNoObjectionCertificate @UserId=1,@NoObjectionCertificateId=999999,@CertificateDate=@D;" 58613 'Update still rejects an unknown certificate.'
 $updatedCertificate=Query "DECLARE @D date=DATEADD(day,-1,GETDATE()); EXEC dbo.UpdateNoObjectionCertificate @UserId=1,@NoObjectionCertificateId=$certificateId,@CertificateDate=@D;"
 Check ($updatedCertificate.Tables[0].Rows[0].NoObjectionCertificateId -eq $certificateId -and $updatedCertificate.Tables[0].Rows[0].AdmissionId -eq 1) 'Certificate update preserves its returned identifiers.'
 Check ((Number "SELECT COUNT(*) FROM dbo.NoObjectionCertificates WHERE NoObjectionCertificateId=$certificateId AND StudentName=N'Ready Example' AND CreatedByUserId=1 AND UpdatedByUserId=1 AND UpdatedAt IS NOT NULL AND CertificateDate=CONVERT(date,DATEADD(day,-1,GETDATE()));") -eq 1) 'Certificate update saves date and updater while retaining the issued student snapshot.'
 # Certificate edits must retain the original student snapshot even if the student later changes.
 foreach($kind in @('RegularStudent','Sports')){
  $create='dbo.Create'+$kind+'Certificate';$update='dbo.Update'+$kind+'Certificate'
  $table='dbo.'+$kind+'Certificates';$id=$kind+'CertificateId'
  $regular=$kind -eq 'RegularStudent';$base=$(if($regular){58500}else{58400})
  $fields=$(if($regular){",@ClassName=N'  Fictional Class  '"}else{",@SportName=N'  Cricket  ',@Performance=N'  Winner  '"})
  $date='DECLARE @D date=CONVERT(date,GETDATE()); '
  Refused ($date+"EXEC $create @UserId=2,@AdmissionId=1,@CertificateDate=@D"+$fields+';') 50003 "$kind creation requires permission."
  Refused ($date+"EXEC $create @UserId=1,@AdmissionId=0,@CertificateDate=@D"+$fields+';') $base "$kind creation rejects an invalid student."
  Refused ("EXEC $create @UserId=1,@AdmissionId=1,@CertificateDate=NULL"+$fields+';') ($base+1) "$kind creation requires a date."
  Refused ("DECLARE @D date=DATEADD(day,1,GETDATE()); EXEC $create @UserId=1,@AdmissionId=1,@CertificateDate=@D"+$fields+';') ($base+2) "$kind creation rejects a future date."
  $blankFields=$(if($regular){",@ClassName=N' '"}else{",@SportName=N' ',@Performance=N'Winner'"})
  Refused ($date+"EXEC $create @UserId=1,@AdmissionId=1,@CertificateDate=@D"+$blankFields+';') ($base+3) "$kind creation rejects a blank class/sport."
  if(-not $regular){Refused ($date+"EXEC $create @UserId=1,@AdmissionId=1,@CertificateDate=@D,@SportName=N'Cricket',@Performance=N' ';" ) 58404 'Sports creation requires performance.'}
  Refused ($date+"EXEC $create @UserId=1,@AdmissionId=999999,@CertificateDate=@D"+$fields+';') ($base+$(if($regular){4}else{5})) "$kind creation rejects an unknown student."
  if($regular){
   Refused ($date+"EXEC $create @UserId=1,@AdmissionId=1,@CertificateDate=@D"+$fields+';') 58505 'Regular student certificate requires a saved date of birth.'
  }
  [void](Query "UPDATE dbo.Students SET StudentName=N'Ready Example',FatherName=N'Fictional Father',DateOfBirth='20180102' WHERE AdmissionId=1;")
  $result=Query ($date+"EXEC $create @UserId=1,@AdmissionId=1,@CertificateDate=@D"+$fields+';')
  $savedId=[int]$result.Tables[0].Rows[0][$id]
  Check ($savedId -gt 0 -and $result.Tables[0].Rows[0].AdmissionId -eq 1) "$kind creation returns certificate and student IDs."
  $savedFields=$(if($regular){"ClassName=N'Fictional Class' AND DateOfBirth='20180102'"}else{"SportName=N'Cricket' AND Performance=N'Winner'"})
  Check ((Number "SELECT COUNT(*) FROM $table WHERE $id=$savedId AND AdmissionId=1 AND StudentName=N'Ready Example' AND FatherName=N'Fictional Father' AND CreatedByUserId=1 AND CertificateDate=CONVERT(date,GETDATE()) AND $savedFields;") -eq 1) "$kind creation preserves student details and trims supplied fields."
  Refused ($date+"EXEC $update @UserId=2,@$id=$savedId,@CertificateDate=@D"+$fields+';') 50003 "$kind update requires permission."
  Refused ($date+"EXEC $update @UserId=1,@$id=0,@CertificateDate=@D"+$fields+';') ($base+10) "$kind update rejects an invalid ID."
  Refused ("EXEC $update @UserId=1,@$id=$savedId,@CertificateDate=NULL"+$fields+';') ($base+11) "$kind update requires a date."
  Refused ("DECLARE @D date=DATEADD(day,1,GETDATE()); EXEC $update @UserId=1,@$id=$savedId,@CertificateDate=@D"+$fields+';') ($base+12) "$kind update rejects a future date."
  Refused ($date+"EXEC $update @UserId=1,@$id=$savedId,@CertificateDate=@D"+$blankFields+';') ($base+13) "$kind update rejects a blank class/sport."
  if(-not $regular){Refused ($date+"EXEC $update @UserId=1,@$id=$savedId,@CertificateDate=@D,@SportName=N'Cricket',@Performance=NULL;") 58414 'Sports update requires performance.'}
  Refused ($date+"EXEC $update @UserId=1,@$id=999999,@CertificateDate=@D"+$fields+';') ($base+$(if($regular){14}else{15})) "$kind update rejects an unknown certificate."
  [void](Query "UPDATE dbo.Students SET StudentName=N'Changed Student',FatherName=N'Changed Father',DateOfBirth='20190103' WHERE AdmissionId=1;")
  $changedFields=$(if($regular){",@ClassName=N'  New Class  '"}else{",@SportName=N'  Football  ',@Performance=N'  Runner-up  '"})
  $result=Query ("DECLARE @D date=DATEADD(day,-1,GETDATE()); EXEC $update @UserId=1,@$id=$savedId,@CertificateDate=@D"+$changedFields+';')
  Check ($result.Tables[0].Rows[0][$id] -eq $savedId -and $result.Tables[0].Rows[0].AdmissionId -eq 1) "$kind update preserves returned IDs."
  $savedFields=$(if($regular){"ClassName=N'New Class' AND DateOfBirth='20180102'"}else{"SportName=N'Football' AND Performance=N'Runner-up'"})
  Check ((Number "SELECT COUNT(*) FROM $table WHERE $id=$savedId AND StudentName=N'Ready Example' AND FatherName=N'Fictional Father' AND CreatedByUserId=1 AND UpdatedByUserId=1 AND UpdatedAt IS NOT NULL AND CertificateDate=CONVERT(date,DATEADD(day,-1,GETDATE())) AND $savedFields;") -eq 1) "$kind update saves requested changes and retains the issued student snapshot."
 }
 [void](Query "INSERT dbo.Staff(StaffNumber,StaffName,FatherName,Gender,CNIC,Specialization,Status) VALUES(N'TEST-STAFF',N'Fictional Teacher',N'Fictional Father',N'Male',N'00000-0000000-0',N'Mathematics',N'Active');")
 foreach($kind in @('RegularLecturer','BankAccountOpening')){
  $lecturer=$kind -eq 'RegularLecturer';$table='dbo.'+$kind+'Certificates';$id=$kind+'CertificateId'
  $fields=$(if($lecturer){",@RoleTitle=N'  Teacher  ',@SubjectTaught=N'  Mathematics  '"}else{",@Designation=N'  Teacher  ',@BankName=N'  Fictional Bank  ',@BranchName=N'  Fictional Branch  ',@MonthlyIncome=12345,@Purpose=N'  Salary  '"})
  [void](Query "UPDATE dbo.Staff SET StaffName=N'Fictional Teacher',FatherName=N'Fictional Father',Gender=N'Male',CNIC=N'00000-0000000-0' WHERE StaffId=1;")
  foreach($action in @('Create','Update')){
   if($action -eq 'Update'){
    $result=Query ("DECLARE @D date=CONVERT(date,GETDATE()); EXEC dbo.Create${kind}Certificate @UserId=1,@StaffId=1,@CertificateDate=@D"+$fields+';')
    $savedId=[int]$result.Tables[0].Rows[0][$id]
    Check ($savedId -gt 0 -and $result.Tables[0].Rows[0].StaffId -eq 1) "$kind creation returns staff and certificate IDs."
    $savedFields=$(if($lecturer){"RoleTitle=N'Teacher' AND SubjectTaught=N'Mathematics'"}else{"Designation=N'Teacher' AND BankName=N'Fictional Bank' AND BranchName=N'Fictional Branch' AND MonthlyIncome=12345 AND Purpose=N'Salary' AND CNIC=N'00000-0000000-0'"})
    Check ((Number "SELECT COUNT(*) FROM $table WHERE $id=$savedId AND StaffId=1 AND StaffName=N'Fictional Teacher' AND FatherName=N'Fictional Father' AND Gender=N'Male' AND CreatedByUserId=1 AND CertificateDate=CONVERT(date,GETDATE()) AND $savedFields;") -eq 1) "$kind creation saves trimmed fields, original staff details and optional income accurately."
   }
   $proc='dbo.'+$action+$kind+'Certificate';$key=$(if($action -eq 'Create'){'@StaffId=1'}else{"@$id=$savedId"})
   $base=$(if($lecturer){58700}else{58800})+$(if($action -eq 'Update'){10}else{0})
   $sql="DECLARE @D date=CONVERT(date,GETDATE()); EXEC $proc @UserId=1,$key,@CertificateDate=@D"+$fields+';'
   Refused ($sql.Replace('@UserId=1','@UserId=2')) 50003 "$kind $action still requires certificate permission."
   Refused ($sql.Replace($key,$key.Split('=')[0]+'=0')) $base "$kind $action rejects invalid identity."
   Refused ($sql.Replace($key,$key.Split('=')[0]+'=999999')) ($base+$(if($lecturer){5}else{8})) "$kind $action rejects an unknown record."
   $missingDate=$(if($lecturer){3}else{6});$futureDate=$missingDate+1
   Refused ($sql.Replace('@CertificateDate=@D','@CertificateDate=NULL')) ($base+$missingDate) "$kind $action requires a date."
   Refused ($sql.Replace('CONVERT(date,GETDATE())','CONVERT(date,DATEADD(day,1,GETDATE()))')) ($base+$futureDate) "$kind $action rejects a future date."
   $requiredFields=$(if($lecturer){@('RoleTitle','SubjectTaught')}else{@('Designation','BankName','BranchName','Purpose')})
   $fieldIndex=0
   foreach($field in $requiredFields){
    $fieldIndex++;$invalid=[regex]::Replace($sql,'@'+$field+"=N'[^']*'",'@'+$field+"=N' '")
    Refused $invalid ($base+$fieldIndex) "$kind $action requires $field."
   }
   if(-not $lecturer){Refused ($sql.Replace('@MonthlyIncome=12345','@MonthlyIncome=-1')) ($base+5) "Bank certificate $action rejects negative income.";Refused ($sql.Replace('@MonthlyIncome=12345','@MonthlyIncome=12345.67')) 547 "Bank certificate $action retains the whole-rupee income rule."}
  }
  [void](Query "UPDATE dbo.Staff SET StaffName=N'Changed Teacher',FatherName=N'Changed Father',Gender=N'Female',CNIC=N'11111-1111111-1' WHERE StaffId=1;")
  $newFields=$(if($lecturer){",@RoleTitle=N'  Lecturer  ',@SubjectTaught=N'  Physics  '"}else{",@Designation=N'  Lecturer  ',@BankName=N'  New Bank  ',@BranchName=N'  New Branch  ',@MonthlyIncome=0,@Purpose=N'  Account opening  '"})
  $result=Query ("DECLARE @D date=DATEADD(day,-1,GETDATE()); EXEC dbo.Update${kind}Certificate @UserId=1,@$id=$savedId,@CertificateDate=@D"+$newFields+';')
  Check ($result.Tables[0].Rows[0][$id] -eq $savedId -and $result.Tables[0].Rows[0].StaffId -eq 1) "$kind update preserves returned IDs."
  $savedFields=$(if($lecturer){"RoleTitle=N'Lecturer' AND SubjectTaught=N'Physics'"}else{"Designation=N'Lecturer' AND BankName=N'New Bank' AND BranchName=N'New Branch' AND MonthlyIncome IS NULL AND Purpose=N'Account opening' AND CNIC=N'00000-0000000-0'"})
  Check ((Number "SELECT COUNT(*) FROM $table WHERE $id=$savedId AND StaffName=N'Fictional Teacher' AND FatherName=N'Fictional Father' AND Gender=N'Male' AND CreatedByUserId=1 AND UpdatedByUserId=1 AND UpdatedAt IS NOT NULL AND CertificateDate=CONVERT(date,DATEADD(day,-1,GETDATE())) AND $savedFields;") -eq 1) "$kind update saves reviewed fields while preserving original staff details."
  if(-not $lecturer){
   [void](Query ("DECLARE @D date=CONVERT(date,GETDATE()); EXEC dbo.Create${kind}Certificate @UserId=1,@StaffId=1,@CertificateDate=@D"+$fields.Replace('@MonthlyIncome=12345','@MonthlyIncome=NULL')+';'))
   Check ((Number 'SELECT COUNT(*) FROM dbo.BankAccountOpeningCertificates WHERE MonthlyIncome IS NULL;') -eq 2) 'Bank certificates retain optional NULL income and convert zero income to NULL.'
  }
 }
 [void](Query "UPDATE dbo.Students SET StudentName=N'Original Student',FatherName=N'Original Father',Gender=N'Male',DateOfBirth='20180102' WHERE AdmissionId=1;")
 foreach($kind in @('Character','DateOfBirth')){
  $character=$kind -eq 'Character';$proc='dbo.Create'+$kind+'Certificate'
  $fields=$(if($character){",@RollNumber=N'  TEST-ROLL  ',@Remarks=N'  Good conduct  '"}else{''})
  $sql="DECLARE @D date=CONVERT(date,GETDATE()); EXEC $proc @UserId=1,@AdmissionId=1,@CertificateDate=@D"+$fields+';'
  $base=$(if($character){58000}else{58500})
  Refused ($sql.Replace('@UserId=1','@UserId=2')) 50003 "$kind certificate still requires permission."
  Refused ($sql.Replace('@AdmissionId=1','@AdmissionId=0')) $base "$kind certificate rejects an invalid student."
  Refused ($sql.Replace('@CertificateDate=@D','@CertificateDate=NULL')) ($base+1) "$kind certificate requires a date."
  Refused ($sql.Replace('CONVERT(date,GETDATE())','CONVERT(date,DATEADD(day,1,GETDATE()))')) ($base+2) "$kind certificate rejects a future date."
  Refused ($sql.Replace('@AdmissionId=1','@AdmissionId=999999')) ($base+$(if($character){6}else{4})) "$kind certificate rejects an unknown student."
  if($character){
   Refused ($sql.Replace("@RollNumber=N'  TEST-ROLL  '","@RollNumber=N' '")) 58003 'Character certificate requires a roll number.'
   Refused ($sql.Replace("@Remarks=N'  Good conduct  '","@Remarks=NULL")) 58004 'Character certificate requires remarks.'
   Refused ($sql.Replace('@AdmissionId=1','@AdmissionId=4')) 58007 'Character certificate requires an enrollment to determine examination year.'
  }else{
   [void](Query 'UPDATE dbo.Students SET DateOfBirth=NULL WHERE AdmissionId=1;')
   Refused $sql 58505 'DOB certificate requires the saved date of birth.'
   [void](Query "UPDATE dbo.Students SET DateOfBirth='20180102' WHERE AdmissionId=1;")
  }
  $result=Query $sql;$recordId=[int]$result.Tables[0].Rows[0].CertificateRecordId
  $issueColumn=$kind+'CertificateIssueId';$issueId=[int]$result.Tables[0].Rows[0][$issueColumn]
  Check ($recordId -gt 0 -and $issueId -gt 0 -and $result.Tables[0].Rows[0].AdmissionId -eq 1) "$kind certificate returns certificate, issue and student IDs."
  $table='dbo.'+$kind+'Certificates';$issues='dbo.'+$kind+'CertificateIssues'
  $savedFields=$(if($character){"RollNumber=N'TEST-ROLL' AND Remarks=N'Good conduct' AND LastExamYear=(SELECT YEAR(EndDate) FROM dbo.AcademicYears WHERE AcademicYearId=1)"}else{"DateOfBirth='20180102'"})
  Check ((Number "SELECT COUNT(*) FROM $table WHERE CertificateRecordId=$recordId AND AdmissionId=1 AND StudentName=N'Original Student' AND FatherName=N'Original Father' AND Gender=N'Male' AND CreatedByUserId=1 AND CertificateDate=CONVERT(date,GETDATE()) AND $savedFields;") -eq 1) "$kind certificate saves the original student snapshot and certificate fields."
  Check ((Number "SELECT COUNT(*) FROM $issues WHERE CertificateRecordId=$recordId AND $issueColumn=$issueId AND IssuedByUserId=1 AND IssuedAt IS NOT NULL;") -eq 1) "$kind certificate retains its operational issuance record."
  Refused $sql ($base+$(if($character){5}else{3})) "$kind certificate retains duplicate protection."
  Check ((Number "SELECT COUNT(*) FROM $issues WHERE CertificateRecordId=$recordId;") -eq 1) "$kind duplicate rejection creates no extra issuance record."
  [void](Query "UPDATE dbo.Students SET StudentName=N'Changed Student',FatherName=N'Changed Father',DateOfBirth='20190103' WHERE AdmissionId=1;")
  Check ((Number "SELECT COUNT(*) FROM $table WHERE CertificateRecordId=$recordId AND StudentName=N'Original Student' AND FatherName=N'Original Father' AND $savedFields;") -eq 1) "$kind issued certificate retains its original student details after later edits."
  [void](Query "UPDATE dbo.Students SET StudentName=N'Original Student',FatherName=N'Original Father',DateOfBirth='20180102' WHERE AdmissionId=1;")
 }
 [void](Query "INSERT dbo.Subjects(SubjectCode,SubjectName) VALUES(N'TEST-Z',N'Zoology'),(N'TEST-A',N'Algebra'),(N'TEST-B',N'Biology');")
 $provisionalFields=",@ExamType=N'SSC',@ExamYear=YEAR(GETDATE()),@BoardName=N'  Fictional Board  ',@RollNumber=N'  TEST-ROLL  ',@MarksObtained=800,@MarksOutOf=1100,@Grade=N'  A  ',@OtherSubjects=N'  Optional subject  ',@CertificateDate=@D,@SubjectIds=N'1,2'"
 # EXEC arguments require scalar variables, not function expressions.
 $provisionalFields=$provisionalFields.Replace('@ExamYear=YEAR(GETDATE())','@ExamYear=@Y')
 $provisionalPrefix='DECLARE @D date=CONVERT(date,GETDATE()),@Y int=YEAR(GETDATE()); '
 $createSql=$provisionalPrefix+'EXEC dbo.CreateProvisionalCertificate @UserId=1,@AdmissionId=1'+$provisionalFields+';'
 foreach($action in @('Create','Update')){
  if($action -eq 'Update'){
   $result=Query $createSql;$provisionalId=[int]$result.Tables[0].Rows[0].ProvisionalCertificateId
   Check ($provisionalId -gt 0 -and $result.Tables[0].Rows[0].AdmissionId -eq 1) 'Provisional creation returns certificate and student IDs.'
   Check ((Number "SELECT COUNT(*) FROM dbo.ProvisionalCertificates WHERE ProvisionalCertificateId=$provisionalId AND StudentName=N'Original Student' AND FatherName=N'Original Father' AND Gender=N'Male' AND DateOfBirth='20180102' AND ExamType=N'SSC' AND ExamYear=YEAR(GETDATE()) AND BoardName=N'Fictional Board' AND RollNumber=N'TEST-ROLL' AND MarksObtained=800 AND MarksOutOf=1100 AND Grade=N'A' AND OtherSubjects=N'Optional subject' AND CreatedByUserId=1 AND CertificateDate=CONVERT(date,GETDATE());") -eq 1) 'Provisional creation retains the student snapshot and trimmed exam details.'
   Check ((Number "SELECT COUNT(*) FROM dbo.ProvisionalCertificateSubjects WHERE ProvisionalCertificateId=$provisionalId AND ((SubjectId=2 AND SubjectName=N'Algebra' AND DisplayOrder=1) OR (SubjectId=1 AND SubjectName=N'Zoology' AND DisplayOrder=2));") -eq 2) 'Provisional creation stores subject-name snapshots in the established initial order.'
  }
  $key=$(if($action -eq 'Create'){'@AdmissionId=1'}else{"@ProvisionalCertificateId=$provisionalId"})
  $sql=$provisionalPrefix+"EXEC dbo.${action}ProvisionalCertificate @UserId=1,$key"+$provisionalFields+';'
  $base=$(if($action -eq 'Create'){58800}else{58820})
  Refused ($sql.Replace('@UserId=1','@UserId=2')) 50003 "Provisional $action requires certificate permission."
  Refused ($sql.Replace($key,$key.Split('=')[0]+'=0')) $base "Provisional $action rejects an invalid identity."
  Refused ($sql.Replace($key,$key.Split('=')[0]+'=999999')) ($base+10) "Provisional $action rejects an unknown record."
  Refused ($sql.Replace("@ExamType=N'SSC'","@ExamType=N'INVALID'")) ($base+1) "Provisional $action rejects an invalid exam type."
  Refused ($sql.Replace('@ExamYear=@Y','@ExamYear=1989')) ($base+2) "Provisional $action rejects an invalid examination year."
  Refused ($sql.Replace("@BoardName=N'  Fictional Board  '","@BoardName=N' '")) ($base+3) "Provisional $action requires a board."
  Refused ($sql.Replace("@RollNumber=N'  TEST-ROLL  '","@RollNumber=NULL")) ($base+4) "Provisional $action requires a roll number."
  Refused ($sql.Replace('@MarksObtained=800','@MarksObtained=-1')) ($base+5) "Provisional $action rejects negative marks."
  Refused ($sql.Replace('@MarksOutOf=1100','@MarksOutOf=0')) ($base+6) "Provisional $action requires a positive marks total."
  Refused ($sql.Replace('@MarksObtained=800','@MarksObtained=1200')) ($base+7) "Provisional $action rejects marks above the total."
  Refused ($sql.Replace('@CertificateDate=@D','@CertificateDate=NULL')) ($base+8) "Provisional $action requires a date."
  Refused ($sql.Replace('CONVERT(date,GETDATE())','CONVERT(date,DATEADD(day,1,GETDATE()))')) ($base+9) "Provisional $action rejects a future date."
 }
 [void](Query "UPDATE dbo.ProvisionalCertificateSubjects SET DisplayOrder=CASE SubjectId WHEN 1 THEN 10 ELSE 20 END WHERE ProvisionalCertificateId=$provisionalId; UPDATE dbo.Students SET StudentName=N'Later Student',FatherName=N'Later Father',DateOfBirth='20190103' WHERE AdmissionId=1;")
 $updateSql=$provisionalPrefix+"EXEC dbo.UpdateProvisionalCertificate @UserId=1,@ProvisionalCertificateId=$provisionalId"+$provisionalFields+';'
 $updateSql=$updateSql.Replace("@ExamType=N'SSC'","@ExamType=N'HSSC1'").Replace('@MarksObtained=800','@MarksObtained=900').Replace("@Grade=N'  A  '","@Grade=N' '").Replace("@OtherSubjects=N'  Optional subject  '","@OtherSubjects=NULL").Replace("@SubjectIds=N'1,2'","@SubjectIds=N'2,3,1'")
 $result=Query $updateSql
 Check ($result.Tables[0].Rows[0].ProvisionalCertificateId -eq $provisionalId -and $result.Tables[0].Rows[0].AdmissionId -eq 1) 'Provisional update preserves returned IDs.'
 Check ((Number "SELECT COUNT(*) FROM dbo.ProvisionalCertificates WHERE ProvisionalCertificateId=$provisionalId AND StudentName=N'Original Student' AND FatherName=N'Original Father' AND DateOfBirth='20180102' AND ExamType=N'HSSC1' AND MarksObtained=900 AND Grade IS NULL AND OtherSubjects IS NULL AND CreatedByUserId=1 AND UpdatedByUserId=1 AND UpdatedAt IS NOT NULL;") -eq 1) 'Provisional update saves reviewed exam fields and retains the original student snapshot.'
 Check ((Number "SELECT COUNT(*) FROM dbo.ProvisionalCertificateSubjects WHERE ProvisionalCertificateId=$provisionalId AND ((SubjectId=1 AND DisplayOrder=1) OR (SubjectId=2 AND DisplayOrder=2) OR (SubjectId=3 AND DisplayOrder=3));") -eq 3) 'Provisional update retains custom order for existing subjects and appends new subjects.'
 [void](Query ($updateSql.Replace("@SubjectIds=N'2,3,1'","@SubjectIds=N'3'")))
 Check ((Number "SELECT COUNT(*) FROM dbo.ProvisionalCertificateSubjects WHERE ProvisionalCertificateId=$provisionalId;") -eq 1 -and (Number "SELECT COUNT(*) FROM dbo.ProvisionalCertificateSubjects WHERE ProvisionalCertificateId=$provisionalId AND SubjectId=3 AND SubjectName=N'Biology';") -eq 1) 'Provisional update removes deselected subjects and saves the remaining subject snapshot.'
 [void](Query ($updateSql.Replace("@SubjectIds=N'2,3,1'","@SubjectIds=NULL")))
 Check ((Number "SELECT COUNT(*) FROM dbo.ProvisionalCertificateSubjects WHERE ProvisionalCertificateId=$provisionalId;") -eq 0) 'Provisional update can clear the subject list.'
 [void](Query ($createSql.Replace("@SubjectIds=N'1,2'","@SubjectIds=NULL").Replace("@Grade=N'  A  '","@Grade=N' '").Replace("@OtherSubjects=N'  Optional subject  '","@OtherSubjects=N' '")))
 Check ((Number 'SELECT COUNT(*) FROM dbo.ProvisionalCertificates WHERE Grade IS NULL AND OtherSubjects IS NULL;') -eq 2) 'Provisional creation accepts an empty subject list and normalizes blank optional text.'
 . (Join-Path $projectRoot 'Validation/LargeSmallAuditRetirementChecks.ps1')
 . (Join-Path $projectRoot 'Validation/ClosingFeeAuditRetirementChecks.ps1')
 Check ((Number 'SELECT COUNT(*) FROM dbo.AuditLogs;') -eq $baselineLogs) 'All seven batches create no audit-log entries.'
 Check ((Number 'SELECT COUNT(*) FROM dbo.AuditOperations;') -eq $baselineOperations) 'All seven batches leave no audit-operation entries.'
 Write-Output "PASS: $checks small audit-retirement SQL checks."
} finally {
 if($created -and -not $KeepFixture){
  & SqlLocalDB stop $catalog;Native 'Stop disposable instance'
  & SqlLocalDB delete $catalog;Native 'Delete disposable instance'
  $resolved=[IO.Path]::GetFullPath($run)
  $allowed=[IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts/small-audit-retirement'))+[IO.Path]::DirectorySeparatorChar
  if(-not $resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -ne $owner){throw 'Unsafe fixture cleanup path.'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
 } elseif($created){Write-Output "Retained disposable fixture: $catalog ($run)"}
}
$global:LASTEXITCODE=0
