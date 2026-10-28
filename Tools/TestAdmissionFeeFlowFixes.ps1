# Schema-only fixture: never accepts a school database connection or backup.
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$owner=[guid]::NewGuid().ToString('N')
$catalog='FMSC_BillingChecks_'+$owner
$instance='(localdb)\'+$catalog
$run=Join-Path $root ('artifacts/admission-fee-flow-fixes/'+$owner)
New-Item -ItemType Directory -Path $run -Force | Out-Null
$script:checks=0
function Check([bool]$ok,[string]$message){if(-not $ok){throw "FAIL: $message"};$script:checks++;Write-Output "PASS: $message"}
function Connect {
 $c=[Data.SqlClient.SqlConnection]::new("Server=$instance;Database=$catalog;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15")
 $c.Open();$q=$c.CreateCommand();$q.CommandText="SELECT CONVERT(nvarchar(32),value) FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_BillingChecksOwner'"
 if([string]$q.ExecuteScalar() -ne $owner){$c.Dispose();throw 'Fixture ownership mismatch.'};return $c
}
function Query([string]$sql){$c=Connect;try{$q=$c.CreateCommand();$q.CommandText=$sql;$q.CommandTimeout=90;$t=[Data.DataSet]::new();$a=[Data.SqlClient.SqlDataAdapter]::new($q);[void]$a.Fill($t);return ,$t}finally{$c.Dispose()}}
function Number([string]$sql){return (Query $sql).Tables[0].Rows[0][0]}
function Apply([int]$version,[bool]$commit){
 $c=Connect;$tx=$c.BeginTransaction()
 try{foreach($batch in [regex]::Split([IO.File]::ReadAllText((Join-Path $root ('Migrations/'+$version+'.sql'))),'(?im)^\s*GO\s*\r?$')){
  if([string]::IsNullOrWhiteSpace($batch)){continue};$q=$c.CreateCommand();$q.Transaction=$tx;$q.CommandText=$batch;$q.CommandTimeout=90;[void]$q.ExecuteNonQuery()
 };if($commit){$tx.Commit()}else{$tx.Rollback()}}catch{if($tx.Connection){$tx.Rollback()};throw}finally{$c.Dispose()}
}
function Refusal([string]$sql){try{[void](Query $sql);return 0}catch{$e=$_.Exception;while($e.InnerException){$e=$e.InnerException};if($e -is [Data.SqlClient.SqlException]){return $e.Number};throw}}
$created=$false
try {
 & SqlLocalDB create $catalog -s;if($LASTEXITCODE -ne 0){throw 'Cannot create private instance'};$created=$true
 $safe=$run.Replace("'","''")
 & sqlcmd -S $instance -d master -E -b -Q "CREATE DATABASE [$catalog] ON PRIMARY(NAME=N'$catalog',FILENAME=N'$safe\fixture.mdf') LOG ON(NAME=N'${catalog}_log',FILENAME=N'$safe\fixture_log.ldf'); EXEC [$catalog].sys.sp_addextendedproperty @name=N'FMSC_BillingChecksOwner',@value=N'$owner';"
 if($LASTEXITCODE -ne 0){throw 'Cannot create owned catalog'}
 $package=Join-Path $env:USERPROFILE '.nuget/packages/microsoft.sqlpackage/170.5.96/tools/net8.0/any/sqlpackage.dll'
 & dotnet $package '/Action:Publish' "/SourceFile:$root/fmsc.dacpac" "/TargetConnectionString:Server=$instance;Database=$catalog;Integrated Security=True;TrustServerCertificate=True" '/p:DropObjectsNotInSource=False' '/p:DropExtendedPropertiesNotInSource=False' '/p:ScriptDatabaseOptions=False' '/Quiet:True'
 if($LASTEXITCODE -ne 0){throw 'Cannot publish record-free schema'}
 [void](Query "DECLARE @Scope uniqueidentifier=NEWID();EXEC sys.sp_updateextendedproperty @name=N'FMSC_QuickRegistrationScope',@value=@Scope;")
 [void](Query "INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes) VALUES(N'58',SYSDATETIME(),SUSER_SNAME(),N'Owned schema-only fixture');")
 [void](Query ([IO.File]::ReadAllText((Join-Path $root 'Validation/AutomaticBillingFixture.sql'))))
 [void](Query "INSERT dbo.AdmissionNumberCounter(CounterId,LastNumber) VALUES(1,1004);INSERT dbo.FeeConfigurations(CampusId,ClassId,FeeTypeId,Amount,Frequency) VALUES(1,1,1,10000,N'Monthly');")
 [void](Query "DECLARE @O uniqueidentifier=NEWID();INSERT dbo.AuditOperations(OperationId,UserId,OperationType) VALUES(@O,1,N'FICTIONAL_EXISTING_HISTORY');EXEC dbo.WriteAuditLog @O,1,N'Students',N'1',N'UPDATE',NULL,N'{""Status"":""Active""}';")
 $originalLogs=Number 'SELECT COUNT(*) FROM dbo.AuditLogs'
 $originalOperations=Number 'SELECT COUNT(*) FROM dbo.AuditOperations'
 Check ($originalLogs -gt 0 -and $originalOperations -gt 0) 'Fixture contains historical generic audit records before migration.'
 Apply 59 $true
 $fingerprint="DECLARE @sql nvarchar(max)=N''; SELECT @sql+=N'SELECT N'''+S.name+'.'+T.name+N''' TableName,CONVERT(varchar(64),HASHBYTES(''SHA2_256'',(SELECT '+(SELECT STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(C.name)),N',') WITHIN GROUP(ORDER BY C.column_id) FROM sys.columns C WHERE C.object_id=T.object_id AND NOT(T.name=N'BillingCharges' AND C.name IN(N'CancellationReason',N'CancelledAt',N'CancelledByUserId')))+N' FROM '+QUOTENAME(S.name)+'.'+QUOTENAME(T.name)+N' FOR JSON PATH,INCLUDE_NULL_VALUES)),2) Fingerprint;'+CHAR(10) FROM sys.tables T JOIN sys.schemas S ON S.schema_id=T.schema_id WHERE T.name<>N'DatabaseVersion' ORDER BY S.name,T.name;EXEC sys.sp_executesql @sql;"
 $before=(Query $fingerprint).GetXml()
 Apply 60 $false
 Check ((Number 'SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC') -eq '59') 'Rollback restores version 59.'
 Check ([DBNull]::Value -eq (Number "SELECT COL_LENGTH(N'dbo.BillingCharges',N'CancellationReason')")) 'Rollback removes newly added reason fields.'
 Check ((Query $fingerprint).GetXml() -ceq $before) 'Rollback retains every existing field value.'
 Apply 60 $true
 Check ((Number 'SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC') -eq '60') 'Migration reaches version 60.'
 Check ((Query $fingerprint).GetXml() -ceq $before) 'Migration preserves all existing business and historical values.'
 [void](Query "INSERT dbo.Roles(RoleName) VALUES(N'Fictional Cashier'),(N'Fictional Adjuster');INSERT dbo.Users(Username,RoleId) VALUES(N'fictional_cashier',2),(N'fictional_adjuster',3);INSERT dbo.Permissions(PermissionCode,PermissionName) VALUES(N'Fees.Collect',N'Collect'),(N'Fees.Adjust',N'Adjust');INSERT dbo.UserRoles(UserId,RoleId) VALUES(2,2),(3,3);INSERT dbo.RolePermissions(RoleId,PermissionId) SELECT 2,PermissionId FROM dbo.Permissions WHERE PermissionCode=N'Fees.Collect';INSERT dbo.RolePermissions(RoleId,PermissionId) SELECT 3,PermissionId FROM dbo.Permissions WHERE PermissionCode=N'Fees.Adjust';")
 [void](Query "EXEC dbo.RequirePermission 2,N'Fees.Collect';EXEC dbo.RequirePermission 3,N'Fees.Adjust';")
 Check ($true) 'Limited fixture actors have their intended individual grants.'
 $bill=Number "INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind,Remarks) VALUES(1,1,1,1,CONVERT(date,GETDATE()),CONVERT(date,GETDATE()),CONVERT(date,GETDATE()),100,0,N'Manual',REPLICATE(N'o',500));SELECT CONVERT(bigint,SCOPE_IDENTITY());"
 Check ((Refusal "EXEC dbo.ReverseBillingCharge 2,$bill,N'Not authorized';") -ne 0) 'Collector-only SQL cannot cancel a charge.'
 Check ((Refusal "EXEC dbo.ReverseBillingCharge 3,$bill,N'Not authorized';") -ne 0) 'Adjuster-only SQL cannot cancel without collection permission.'
 Check ((Number "SELECT COUNT(*) FROM dbo.BillingCharges WHERE BillingChargeId=$bill AND Status=N'Open'") -eq 1) 'Rejected cancellation keeps the bill open.'
 [void](Query "DECLARE @Reason nvarchar(500)=REPLICATE(N'r',500);EXEC dbo.ReverseBillingCharge 1,$bill,@Reason;")
 Check ((Number "SELECT COUNT(*) FROM dbo.BillingCharges WHERE BillingChargeId=$bill AND Status=N'Cancelled' AND CancellationReason=REPLICATE(N'r',500) AND Remarks=REPLICATE(N'o',500) AND CancelledByUserId=1 AND CancelledAt IS NOT NULL") -eq 1) 'Full reason and old note survive cancellation with actor and time.'
 Check ((Refusal "EXEC dbo.ReverseBillingCharge 1,$bill,N'Repeat';") -eq 53202) 'An already cancelled charge cannot be cancelled twice.'
 [void](Query "DECLARE @R uniqueidentifier=NEWID();EXEC dbo.ProcessStudentCredit @UserId=2,@AdmissionId=1,@Action=N'Deposit',@Amount=50,@RequestId=@R,@Reason=N'Fictional deposit by cashier';")
 Check ((Refusal 'BEGIN TRAN; EXEC dbo.ApplyStudentCredit_Internal 1; COMMIT;') -eq 54902) 'Credit application refuses to borrow a previous actor.'
 [void](Query 'BEGIN TRAN;EXEC dbo.PostEnrollmentOpeningCharges @EnrollmentId=1,@UserId=1;COMMIT;')
 Check ((Number "SELECT COUNT(*) FROM dbo.StudentCreditEntries WHERE AdmissionId=1 AND EntryKind=N'Application' AND CreatedByUserId=1") -gt 0) 'Opening settlement records the authenticated posting actor.'
 Check ((Number "SELECT COUNT(*) FROM dbo.StudentCreditEntries WHERE AdmissionId=1 AND EntryKind=N'Deposit' AND CreatedByUserId=2") -eq 1) 'Original deposit keeps its own operator.'
 Check ((Number 'SELECT COUNT(*) FROM dbo.FeePayments WHERE IsCreditApplication=1 AND CreatedByUserId=1') -gt 0) 'Credit payment attribution matches its movement.'
 $admit=@'
DECLARE @F dbo.FeeTypeIdList,@D dbo.AdmissionFeeDiscountType,@Today date=CONVERT(date,GETDATE());
EXEC dbo.AdmitStudent @UserId=1,@AdmissionDate=@Today,@AdmissionCampusId=1,@AcademicYearId=1,@ClassSectionId=1,
 @StudentName=N'NAME_VALUE',@FatherName=N'Fictional Parent',@DateOfBirth=NULL,@PlaceOfBirth=NULL,@Gender=NULL,@BFormNumber=NULL,
 @Religion=NULL,@BloodGroup=NULL,@ContactNumber=NULL,@AlternateContactNumber=NULL,@Address=NULL,@PreviousSchool=NULL,
 @FatherOccupation=NULL,@Disability=N'No',@UsesTransport=0,@SelectedFeeTypeIds=@F,@AdmissionDiscounts=@D,
 @AdmissionNumber='7001',@UseSelectedFeeTypes=1,@FamilyStudentId=FAMILY_VALUE;
'@
 $studentCount=Number 'SELECT COUNT(*) FROM dbo.Students'
 foreach($attempt in @($admit.Replace('NAME_VALUE','').Replace('FAMILY_VALUE','NULL'),$admit.Replace('NAME_VALUE','Fictional Refused Child').Replace('FAMILY_VALUE','999999'))){
  $state=0;$number=0;try{[void](Query $attempt)}catch{$e=$_.Exception;while($e.InnerException){$e=$e.InnerException};if($e -is [Data.SqlClient.SqlException]){$state=$e.State;$number=$e.Number}else{throw}}
  Check ($number -in @(51011,56642)) 'Admission refusal occurs at the intended pre/post-write validation.'
  Check ($state -eq 60) 'Admission signals verified rollback with state 60.'
  Check ((Number 'SELECT COUNT(*) FROM dbo.Students') -eq $studentCount) 'Rejected admission leaves no student behind.'
 }
 Check ((Number 'SELECT LastNumber FROM dbo.AdmissionNumberCounter WHERE CounterId=1') -eq 1004) 'Rolled-back admission restores official-number allocation.'
 $outer=Connect;$outerTransaction=$outer.BeginTransaction()
 try{
  $command=$outer.CreateCommand();$command.Transaction=$outerTransaction;$command.CommandText=$admit.Replace('NAME_VALUE','').Replace('FAMILY_VALUE','NULL')
  try{[void]$command.ExecuteNonQuery();throw 'Expected rejection'}catch{if($_.Exception.InnerException -isnot [Data.SqlClient.SqlException]){throw}}
  $outerTransaction.Rollback();Check ($true) 'Enclosing application transaction retains safe rollback ownership.'
 }finally{$outer.Dispose()}
 [void](Query ($admit.Replace('NAME_VALUE','Fictional Committed Child').Replace('FAMILY_VALUE','NULL')))
 Check ((Number "SELECT COUNT(*) FROM dbo.Students WHERE AdmissionNumber='7001' AND StudentName=N'Fictional Committed Child'") -eq 1) 'Valid admission still commits normally after rejected submissions.'
 Check ((Number "SELECT COUNT(*) FROM sys.procedures P JOIN sys.sql_modules M ON M.object_id=P.object_id WHERE P.name<>N'WriteAuditLog' AND M.definition LIKE N'%WriteAuditLog%'") -eq 0) 'Revised procedures retain audit retirement.'
 Check ((Number 'SELECT COUNT(*) FROM dbo.AuditLogs') -eq $originalLogs) 'Existing audit history is unchanged.'
 $env:FMSC_FLOW_FIX_TEST_CONNECTION="Server=$instance;Database=$catalog;Integrated Security=True;TrustServerCertificate=True"
 $env:FMSC_FLOW_FIX_TEST_OWNER=$owner
 & dotnet (Join-Path $root 'Tests/ThemeSmokeChecks/bin/Debug/net8.0-windows/ThemeSmokeChecks.dll') '--admission-fee-flow-sql-compat'
 if($LASTEXITCODE -ne 0){throw 'Application compatibility validation failed'}
 Write-Output "PASS: $script:checks admission/fee-flow SQL checks plus application compatibility."
} finally {
 if($created){& SqlLocalDB stop $catalog; & SqlLocalDB delete $catalog}
}
