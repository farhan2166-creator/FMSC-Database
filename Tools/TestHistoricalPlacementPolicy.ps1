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
 [void](Query "INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes) VALUES(N'60',SYSDATETIME(),SUSER_SNAME(),N'Owned schema-only fixture');")
 [void](Query ([IO.File]::ReadAllText((Join-Path $root 'Validation/AutomaticBillingFixture.sql'))))
 [void](Query "INSERT dbo.AdmissionNumberCounter(CounterId,LastNumber) VALUES(1,1004);INSERT dbo.FeeConfigurations(CampusId,ClassId,FeeTypeId,Amount,Frequency) VALUES(1,1,1,10000,N'Monthly');")
 [void](Query "DECLARE @O uniqueidentifier=NEWID();INSERT dbo.AuditOperations(OperationId,UserId,OperationType) VALUES(@O,1,N'FICTIONAL_EXISTING_HISTORY');EXEC dbo.WriteAuditLog @O,1,N'Students',N'1',N'UPDATE',NULL,N'{""Status"":""Active""}';")
 Apply 61 $false
 Check ((Number 'SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC') -eq '60') 'Migration rollback preserves schema 60.'
 Apply 61 $true
 Check ((Number 'SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC') -eq '61') 'Migration advances to schema 61.'
 [void](Query @"
 INSERT dbo.Sections(SectionCode,SectionName) VALUES(N'B',N'Fictional B');
 INSERT dbo.Classes(ClassCode,ClassName,AcademicProgramId) VALUES(N'TEST2',N'Fictional Class 2',1);
 INSERT dbo.Campuses(CampusCode,CampusName,CampusType) VALUES(N'TEST2',N'Fictional Campus 2',N'School');
 INSERT dbo.CampusClassSections(CampusId,ClassId,SectionId) VALUES(1,1,2),(1,2,1),(2,1,1);
 INSERT dbo.AcademicYears(YearName,StartDate,EndDate,IsCurrent) VALUES(N'Fictional Past',DATEADD(year,-2,CONVERT(date,GETDATE())),DATEADD(year,-1,CONVERT(date,GETDATE())),0);
 INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,Status,LegacySetupPending)
 VALUES('8001',N'TEST-8001',DATEADD(year,-2,CONVERT(date,GETDATE())),1,N'Fictional Historical Child',N'Active',0);
 INSERT dbo.Enrollments(AdmissionId,AcademicYearId,ClassSectionId,EnrollmentDate,Status,BillingReady,PlacementConfirmedAt)
 VALUES(5,2,1,DATEADD(year,-2,CONVERT(date,GETDATE())),N'Active',0,SYSDATETIME());
"@)
 foreach($proc in @('TransferEnrollmentClassSection','TransferEnrollmentClass','TransferEnrollmentCampus')) {
  $target=switch($proc){'TransferEnrollmentClassSection'{2};'TransferEnrollmentClass'{3};default{4}}
  Check ((Refusal "DECLARE @D date=CONVERT(date,GETDATE());EXEC dbo.$proc @UserId=1,@EnrollmentId=4,@NewClassSectionId=$target,@TransferDate=@D,@Remarks=N'Fictional history';") -eq 59640) "$proc rejects a present-day transfer against an expired year."
  Check ((Refusal "DECLARE @D date=DATEADD(year,-1,CONVERT(date,GETDATE()));EXEC dbo.$proc @UserId=1,@EnrollmentId=4,@NewClassSectionId=$target,@TransferDate=@D,@Remarks=NULL;") -eq 59641) "$proc requires a reason for historical transfer."
  if($proc -ne 'TransferEnrollmentClassSection') {
   Check ((Refusal "DECLARE @D date=DATEADD(year,-1,CONVERT(date,GETDATE()));EXEC dbo.$proc @UserId=1,@EnrollmentId=4,@NewClassSectionId=$target,@TransferDate=@D,@Remarks=N'Fictional history',@FeePolicy=N'CURRENT_MONTH',@FeeSyncReason=N'Fictional fee change';") -eq 59642) "$proc refuses fee synchronization for a historical placement."
  }
  [void](Query "DECLARE @D date=DATEADD(year,-1,CONVERT(date,GETDATE()));EXEC dbo.$proc @UserId=1,@EnrollmentId=4,@NewClassSectionId=$target,@TransferDate=@D,@Remarks=N'Fictional history';UPDATE dbo.Enrollments SET ClassSectionId=1 WHERE EnrollmentId=4;")
 }
 Check ((Number 'SELECT COUNT(*) FROM dbo.EnrollmentHistory WHERE EnrollmentId=4') -eq 3) 'Valid historical transfers preserve operational history.'
 Check ((Number 'SELECT COUNT(*) FROM dbo.StudentFeeAssignments WHERE EnrollmentId=4') -eq 0) 'Historical transfers do not create fee plans.'
 Check ((Number 'SELECT COUNT(*) FROM dbo.BillingCharges WHERE EnrollmentId=4') -eq 0) 'Historical transfers do not create bills.'
 Check ((Refusal "EXEC dbo.SyncTransferStudentFees_Internal 1,4,N'CURRENT_MONTH',N'Fictional direct call';") -eq 59642) 'Internal fee synchronization independently rejects history.'
 Check ((Refusal "DECLARE @D date=CONVERT(date,GETDATE());EXEC dbo.CloseStudentAdmission @UserId=1,@AdmissionId=5,@LeavingDate=@D,@Reason=N'Fictional departure',@Preview=1;") -eq 59643) 'Departure after the historical academic year is refused.'
 $departure=Query "DECLARE @D date=DATEADD(year,-1,CONVERT(date,GETDATE()));EXEC dbo.CloseStudentAdmission @UserId=1,@AdmissionId=5,@LeavingDate=@D,@Reason=N'Fictional departure',@Preview=1;"
 Check ($departure.Tables[0].Rows.Count -eq 1) 'Historical departure within the year remains reviewable.'
 foreach($action in @('Hold','Resume')) {
  if($action -eq 'Resume') {[void](Query "INSERT dbo.StudentPostingHolds(AdmissionId,EnrollmentId,HoldDate,Reason,CreatedBy,MonthlyFeePolicy,PaidReductionAction,PreviousBillingReady) VALUES(5,4,DATEADD(year,-2,CONVERT(date,GETDATE())),N'Fictional historical hold',1,N'KeepIssued',N'Credit',0);")}
  $expected=if($action -eq 'Hold'){59644}else{59645}
  Check ((Refusal "DECLARE @D date=DATEADD(year,-1,CONVERT(date,GETDATE())),@R uniqueidentifier=NEWID();EXEC dbo.SaveStudentPostingDecision @UserId=1,@AdmissionId=5,@Action=N'$action',@EffectiveDate=@D,@Reason=N'Fictional decision',@RequestId=@R,@Preview=1;") -eq $expected) "$action requires a current-year placement even with a backdated effective date."
 }
 Check ((Query 'EXEC dbo.GetStudentDetails 1,5;').Tables[0].Columns.Contains('PlacementYearEnd')) 'Admission receives recorded academic-year boundaries.'
 Check ((Query 'EXEC dbo.GetStudentEnrollmentsForLifecycle 1,5;').Tables[0].Columns.Contains('PlacementYearEnd')) 'Transfer workflow receives recorded academic-year boundaries.'
 Check ((Number 'SELECT BillingReady FROM dbo.Enrollments WHERE EnrollmentId=4') -eq 0) 'Historical billing remains disabled.'
 [void](Query "DECLARE @D date=CONVERT(date,GETDATE());EXEC dbo.TransferEnrollmentClassSection 1,1,2,@D;EXEC dbo.TransferEnrollmentClass 1,1,3,@D;EXEC dbo.TransferEnrollmentCampus 1,1,4,@D,N'Fictional current transfer';")
 Check ((Number 'SELECT COUNT(*) FROM dbo.EnrollmentHistory WHERE EnrollmentId=1') -eq 3) 'Current placements retain all three transfer operations.'
 $currentHold=Query "DECLARE @D date=CONVERT(date,GETDATE()),@R uniqueidentifier=NEWID();EXEC dbo.SaveStudentPostingDecision @UserId=1,@AdmissionId=1,@Action=N'Hold',@EffectiveDate=@D,@Reason=N'Fictional current hold',@RequestId=@R,@Preview=1;"
 Check ($currentHold.Tables[0].Rows.Count -eq 1) 'Current placements retain reviewed hold operations.'
 [void](Query 'UPDATE dbo.Enrollments SET BillingReady=0 WHERE EnrollmentId=1;')
 Check ((Refusal "EXEC dbo.SyncTransferStudentFees_Internal 1,1,N'CURRENT_MONTH',N'Fictional nonbilling call';") -eq 59642) 'Current nonbilling placements cannot bypass fee-setup restrictions through a transfer.'
 Check ((Number "SELECT COUNT(*) FROM sys.procedures P JOIN sys.sql_modules M ON M.object_id=P.object_id WHERE P.name<>N'WriteAuditLog' AND M.definition LIKE N'%WriteAuditLog%'") -eq 0) 'Revised procedures retain audit retirement.'
 Write-Output "PASS: $script:checks historical placement SQL checks."
} finally {
 if($created){& SqlLocalDB stop $catalog; & SqlLocalDB delete $catalog}
}
