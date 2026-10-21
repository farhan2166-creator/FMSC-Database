# Owns a unique schema-only LocalDB instance. Accepts no live database/connection parameter.
param([switch]$KeepFixture)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$owner=[guid]::NewGuid().ToString('N')
$catalog='FMSC_BillingChecks_'+$owner
$instance='(localdb)\'+$catalog
$run=Join-Path $projectRoot ('artifacts/holds/'+$owner)
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
function Preview {return Query 'EXEC dbo.RunAutomaticMonthlyBilling @UserId=1;'}
function Post($review,[bool]$missed=$false,[bool]$reviewMissed=$false) {
 return Query 'EXEC dbo.RunAutomaticMonthlyBilling @UserId=1,@Preview=0,@ExpectedSnapshot=@Token,@IncludeMissed=@Missed,@ReviewMissed=@Review;' @{
  '@Token'=[string]$review.Tables[0].Rows[0].Snapshot;'@Missed'=$missed;'@Review'=$reviewMissed}
}
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

 function Decision([string]$action,[datetime]$effective,[hashtable]$options=@{},[bool]$preview=$true,[string]$snapshot=$null,[guid]$request=[guid]::NewGuid(),[int]$student=1) {
  $values=@{'@Student'=$student;'@Action'=$action;'@Date'=$effective;'@Request'=$request;'@Preview'=$preview;'@Snapshot'=$(if($snapshot){$snapshot}else{[DBNull]::Value})}
  $arguments='@UserId=1,@AdmissionId=@Student,@Action=@Action,@EffectiveDate=@Date,@Reason=N''Fictional reviewed decision'',@RequestId=@Request,@Preview=@Preview,@ExpectedSnapshot=@Snapshot'
  foreach($key in $options.Keys){$values[$key]=$options[$key];$arguments+=",$key=$key"}
  return Query ('EXEC dbo.SaveStudentPostingDecision '+$arguments) $values
 }
 function SaveDecision([string]$action,[datetime]$effective,[hashtable]$options=@{},[guid]$request=[guid]::NewGuid(),[int]$student=1) {
  $reviewed=Decision $action $effective $options -student $student
  return Decision $action $effective $options $false ([string]$reviewed.Tables[0].Rows[0].Snapshot) $request $student
 }
 function Refused([scriptblock]$operation,[string]$message) {
  $failed=$false;try{[void](& $operation)}catch{$failed=$true}
  Check $failed $message
 }
 $today=[datetime]::Today;$month=[datetime]::new($today.Year,$today.Month,1);$prior=$month.AddMonths(-1)
 [void](Query "INSERT dbo.SchoolProfile(SchoolProfileId,SchoolName,CreatedByUserId,UpdatedByUserId,FeeProrationCutoffDay) VALUES(1,N'Fictional School',1,1,16);")
 [void](Post (Preview))
 $originalStart=Query 'SELECT EnrollmentDate FROM dbo.Enrollments WHERE EnrollmentId=1;'
 $originalStart=[datetime]$originalStart.Tables[0].Rows[0][0]
 $keep=@{'@MonthlyFeePolicy'='KeepIssued'}
 $request=[guid]::NewGuid()
 $plan=Decision 'Hold' $prior $keep
 Check ($plan.Tables[0].Rows[0].Outstanding -eq 12000 -and $plan.Tables[0].Rows[0].Waived -eq 0) 'Hold review retains existing dues when keeping issued bills.'
 [void](Decision 'Hold' $prior $keep $false $plan.Tables[0].Rows[0].Snapshot $request)
 Check ((Number 'SELECT COUNT(*) FROM dbo.StudentPostingHolds WHERE ResumeDate IS NULL;') -eq 1) 'One active hold is recorded.'
 Check ((Number "SELECT COUNT(*) FROM dbo.Students WHERE AdmissionId=1 AND Status=N'Active';") -eq 1) 'Hold does not mark the student Left.'
 $pending=Query 'EXEC dbo.GetPendingReturnAdmissions @UserId=1;'
 Check (@($pending.Tables[0].Rows | Where-Object { $_.AdmissionId -eq 1 }).Count -eq 0) 'A held placement is not offered as a promoted return.'
 # A stale return screen must be refused even when its choices waive all opening
 # fees and the posting trigger would have no new charge to inspect.
 [void](Query 'UPDATE dbo.FeeTypes SET IsMandatory=1 WHERE FeeTypeId=1;')
 $blockedReturn=Query @'
DECLARE @Choices dbo.ReturnFeeChoiceType,@Today date=CONVERT(date,GETDATE());
INSERT @Choices VALUES(1,0,0);
BEGIN TRY
 EXEC dbo.ConfirmPromotedAdmission @UserId=1,@EnrollmentId=1,@ReturnDate=@Today,@UsesTransport=0,@FeeChoices=@Choices;
 SELECT 0 ErrorNumber,@@TRANCOUNT Transactions;
END TRY
BEGIN CATCH SELECT ERROR_NUMBER() ErrorNumber,@@TRANCOUNT Transactions; END CATCH;
'@
 Check ($blockedReturn.Tables[0].Rows[0].ErrorNumber -eq 53017 -and $blockedReturn.Tables[0].Rows[0].Transactions -eq 0) 'Stale all-zero return confirmation is clearly refused without leaking a transaction.'
 Check ((Number 'SELECT COUNT(*) FROM dbo.Enrollments WHERE EnrollmentId=1 AND BillingReady=0 AND ReturnConfirmedAt IS NULL;') -eq 1 -and (Number 'SELECT SUM(Amount) FROM dbo.StudentFeeAssignments WHERE EnrollmentId=1;') -eq 12000 -and (Number "SELECT COUNT(*) FROM dbo.EnrollmentHistory WHERE EnrollmentId=1 AND ActionType=N'ReturnConfirmed';") -eq 0) 'Refused return changes no fee assignments, placement readiness or return history.'
 [void](Query 'UPDATE dbo.FeeTypes SET IsMandatory=0 WHERE FeeTypeId=1;')
 Check ((Number 'SELECT COUNT(*) FROM dbo.fn_MonthlyBillingCandidates(DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1),1) WHERE EnrollmentId=1 AND BlockReason IS NULL;') -eq 0) 'Held students are not eligible for monthly posting.'
 Check ((Preview).Tables[0].Rows[0].CurrentCharges -eq 0) 'Automatic billing has no held-student charges.'
 $extra=Query 'EXEC dbo.GetAdditionalFeeCandidates @UserId=1,@AcademicYearId=1;'
 Check (@($extra.Tables[0].Rows | Where-Object { $_.EnrollmentId -eq 1 }).Count -eq 0) 'Held students are absent from additional-fee selection.'
 Refused { Query "INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind) VALUES(1,1,1,1,DATEADD(month,-1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)),GETDATE(),GETDATE(),10000,0,N'Monthly');" } 'Direct posting cannot bypass a hold.'
 [void](Query "UPDATE dbo.Enrollments SET BillingReady=1 WHERE EnrollmentId=1;")
 Check ((Number 'SELECT COUNT(*) FROM dbo.fn_MonthlyBillingCandidates(DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1),1) WHERE EnrollmentId=1 AND BlockReason IS NULL;') -eq 0) 'Changing billing readiness cannot bypass the independently recorded hold.'
 [void](Decision 'Hold' $prior $keep $false $plan.Tables[0].Rows[0].Snapshot $request)
 Check ((Number 'SELECT COUNT(*) FROM dbo.StudentPostingHolds;') -eq 1) 'Exact hold resubmission returns the saved result without another episode.'
 Refused { Decision 'Hold' $today $keep $false $plan.Tables[0].Rows[0].Snapshot $request } 'A request cannot be reused for changed decision details.'
 $outcome=Query 'EXEC dbo.GetStudentPostingDecisionOutcome @UserId=1,@AdmissionId=1,@RequestId=@Id;' @{'@Id'=$request}
 Check ($outcome.Tables[0].Rows.Count -eq 1) 'Saved outcome is recoverable by request after a lost response.'
 [void](Query "DECLARE @Bill bigint=(SELECT BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=1 AND FeeTypeId=1),@Today date=CONVERT(date,GETDATE()),@A dbo.FeePaymentAllocationType; INSERT @A VALUES(@Bill,2000); EXEC dbo.ProcessFeeTransaction @AdmissionId=1,@EnrollmentId=1,@TransactionType=N'Collection',@Amount=2000,@TransactionDate=@Today,@PaymentMethod=N'Cash',@ReferenceNumber=NULL,@Remarks=N'Fictional collection during hold',@FeePaymentId=NULL,@BillingChargeId=NULL,@Allocations=@A,@UserId=1;")
 Check ((Number 'SELECT SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges;') -eq 10000) 'Retained dues remain collectible during a hold.'
 $resume=@{'@BillingFromMonth'=$month;'@TargetEnrollmentId'=1}
 [void](SaveDecision 'Resume' $today $resume)
 Check ((Number 'SELECT COUNT(*) FROM dbo.StudentPostingHolds WHERE ResumeDate IS NULL;') -eq 0) 'Reviewed resume lifts the hold.'
 Check ((Number 'SELECT COUNT(*) FROM dbo.fn_MonthlyBillingCandidates(DATEADD(month,-1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)),1) WHERE EnrollmentId=1 AND BlockReason IS NULL;') -eq 0) 'Skipped absence months do not reappear as missed automatic fees.'
 Refused { Query "INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind) VALUES(1,1,1,1,DATEADD(month,-1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)),GETDATE(),GETDATE(),10000,0,N'Monthly');" } 'Direct posting also respects a saved no-fee absence period.'
 Check ((Number 'SELECT COUNT(*) FROM dbo.Enrollments WHERE AdmissionId=1;') -eq 1) 'Same-placement resume creates no readmission or new placement.'
 $start=Query 'SELECT EnrollmentDate FROM dbo.Enrollments WHERE EnrollmentId=1;'
 Check ([datetime]$start.Tables[0].Rows[0][0] -eq $originalStart) 'Hold and resume preserve the original class placement start.'
 # Second hold: reducing a partially paid bill releases only the paid portion.
 $waive=@{'@MonthlyFeePolicy'='FromMonth';'@WaiveFromMonth'=$month;'@PaidReductionAction'='Credit'}
 $plan=Decision 'Hold' $today $waive
 Check ($plan.Tables[0].Rows[0].Waived -eq 10000 -and $plan.Tables[0].Rows[0].ReleasedPaid -eq 2000) 'Partially paid waiver separates unpaid dues from paid money released.'
 [void](Decision 'Hold' $today $waive $false $plan.Tables[0].Rows[0].Snapshot)
 Check ((Number 'SELECT SUM(Amount) FROM dbo.StudentCreditEntries WHERE AdmissionId=1;') -eq 2000) 'Paid reduction creates exactly the reviewed advance.'
 Check ((Number 'SELECT SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges;') -eq 0) 'Waiving the monthly bills clears their unpaid dues.'
 $waived=Query 'EXEC dbo.GetStudentPostingHoldBills @UserId=1,@AdmissionId=1;'
 Check ($waived.Tables[0].Rows.Count -eq 2) 'Resume offers the separately recorded waived fees.'
 $tuition=[long](Number 'SELECT BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=1 AND FeeTypeId=1 AND CorrectionOfBillingChargeId IS NULL;')
 $restore=@{'@BillingFromMonth'=$month;'@TargetEnrollmentId'=1;'@RestoreBills'="[$tuition]";'@ApplyAvailableCredit'=$false}
 $plan=Decision 'Resume' $today $restore
 Check ($plan.Tables[0].Rows[0].Restored -eq 10000 -and $plan.Tables[0].Rows[0].CreditToApply -eq 0 -and $plan.Tables[0].Rows[0].ResultingOutstanding -eq 10000) 'Selective restoration leaves unselected fees waived and advance unapplied.'
 $resumeRequest=[guid]::NewGuid()
 [void](Decision 'Resume' $today $restore $false $plan.Tables[0].Rows[0].Snapshot $resumeRequest)
 [void](Decision 'Resume' $today $restore $false $plan.Tables[0].Rows[0].Snapshot $resumeRequest)
 Check ((Number 'SELECT SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges;') -eq 10000) 'A repeated resume does not duplicate a restored charge.'
 Check ((Number 'SELECT SUM(Amount) FROM dbo.StudentCreditEntries WHERE AdmissionId=1;') -eq 2000) 'Declining advance application preserves the advance.'
 Check ((Number 'SELECT COUNT(*) FROM dbo.StudentPostingHolds;') -eq 2) 'Repeated hold and resume episodes retain their separate history.'
 # A changed payment invalidates an old preview.
 $plan=Decision 'Hold' $today $keep
 [void](Query "DECLARE @R uniqueidentifier=NEWID(); EXEC dbo.ProcessStudentCredit @UserId=1,@AdmissionId=1,@Action=N'Deposit',@Amount=100,@RequestId=@R,@Reason=N'Fictional change after review';")
 Refused { Decision 'Hold' $today $keep $false $plan.Tables[0].Rows[0].Snapshot } 'Changed account credit invalidates a stale decision before saving.'
 [void](SaveDecision 'Hold' $today $waive)
 $withCredit=@{'@BillingFromMonth'=$month;'@TargetEnrollmentId'=1;'@RestoreBills'="[$tuition]";'@ApplyAvailableCredit'=$true}
 $plan=Decision 'Resume' $today $withCredit
 Check ($plan.Tables[0].Rows[0].CreditToApply -eq 2100 -and $plan.Tables[0].Rows[0].ResultingOutstanding -eq 7900) 'Resume preview includes only the chosen available advance.'
 [void](Decision 'Resume' $today $withCredit $false $plan.Tables[0].Rows[0].Snapshot)
 Check ((Number 'SELECT SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges;') -eq 7900) 'Applying advance matches the reviewed resulting outstanding.'
 # Refunds remain refunds when the original fee is restored.
 $refund=@{'@MonthlyFeePolicy'='FromMonth';'@WaiveFromMonth'=$month;'@PaidReductionAction'='Refund'}
 [void](SaveDecision 'Hold' $today $refund)
 $refundTotal=Number "SELECT -SUM(TotalAmount) FROM dbo.FeePayments WHERE PaymentMethod=N'Refund';"
 Check ($refundTotal -eq 2100) 'The paid portion can be recorded as an explicit refund.'
 [void](SaveDecision 'Resume' $today $restore)
 Check ((Number "SELECT -SUM(TotalAmount) FROM dbo.FeePayments WHERE PaymentMethod=N'Refund';") -eq $refundTotal) 'Restoration preserves recorded refunds.'
 Check ((Number 'SELECT SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges;') -eq 10000) 'Restoration creates dues without falsely restoring the refunded payment.'

 # Certificate possession and a temporary posting hold are separate decisions.
 [void](SaveDecision 'Hold' $today $keep)
 [void](Query "DECLARE @D date=CONVERT(date,GETDATE()); EXEC dbo.CreateLeaveCertificate @UserId=1,@AdmissionId=1,@LeaveDate=@D,@ClassPromotedTo=N'Fictional destination',@AcademicRecord=N'Fictional academic record',@AttendanceRecord=N'Fictional attendance',@Remarks=N'Fictional certificate while held',@KeepLaterCharges=1;")
 Check ((Number 'SELECT COUNT(*) FROM dbo.LeaveCertificates WHERE AdmissionId=1;') -eq 1) 'A held student can obtain a leaving certificate through the existing permission-controlled path.'
 Check ((Number 'SELECT COUNT(*) FROM dbo.LeaveCertificateIssues;') -eq 1) 'Original certificate issuance evidence is preserved.'
 $oldTransport=[long](Number 'SELECT BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=1 AND FeeTypeId=2 AND CorrectionOfBillingChargeId IS NULL;')
 $restoreEarlier=@{'@BillingFromMonth'=$month;'@TargetEnrollmentId'=1;'@RestoreBills'="[$oldTransport]"}
 $earlierPlan=Decision 'Resume' $today $restoreEarlier
 Check ($earlierPlan.Tables[0].Rows[0].Restored -eq 2000) 'An eligible waiver from an earlier hold episode remains explicitly restorable.'
 [void](Decision 'Resume' $today $restoreEarlier $false $earlierPlan.Tables[0].Rows[0].Snapshot)
 Check ((Number "SELECT COUNT(*) FROM dbo.StudentPostingHoldBills WHERE BillingChargeId=$oldTransport AND RestoredRequestId IS NOT NULL;") -eq 1) 'Restoration retains the link to the earlier hold waiver.'
 Check ((Number "SELECT COUNT(*) FROM dbo.Students WHERE AdmissionId=1 AND Status=N'Active';") -eq 1) 'Certificate presence does not prohibit a reviewed resume.'
 Check ((Number 'SELECT COUNT(*) FROM dbo.LeaveCertificates WHERE AdmissionId=1;') -eq 1) 'Resuming does not delete or rewrite the issued certificate.'
 # A historical Left record needs explicit operator-reviewed adoption.
 [void](Query "UPDATE dbo.Students SET Status=N'Left',LegacySetupPending=0,WithdrawalDate=CONVERT(date,GETDATE()) WHERE AdmissionId=2; UPDATE dbo.Enrollments SET Status=N'Left',LeavingDate=CONVERT(date,GETDATE()),BillingReady=0 WHERE EnrollmentId=2;")
 Check ((Number 'SELECT COUNT(*) FROM dbo.StudentPostingHolds WHERE AdmissionId=2;') -eq 0) 'Existing Left records have not been silently relabelled as holds.'
 Refused { Decision 'Hold' $prior $keep -student 2 } 'Left records cannot enter the ordinary active-student hold path.'
 $adopt=@{'@MonthlyFeePolicy'='KeepIssued';'@AdoptLeft'=$true}
 [void](SaveDecision 'Hold' $prior $adopt -student 2)
 Check ((Number 'SELECT COUNT(*) FROM dbo.StudentPostingHolds WHERE AdmissionId=2 AND AdoptedLeft=1 AND ResumeDate IS NULL;') -eq 1) 'Reviewed adoption records the historical-Left origin explicitly.'
 $returnLegacy=@{'@BillingFromMonth'=$prior;'@TargetEnrollmentId'=2}
 $legacyPlan=Decision 'Resume' $today $returnLegacy -student 2
 Check ($legacyPlan.Tables[0].Rows[0].NewCharges -eq 20000) 'Billing from the selected absence month previews both missing monthly fees.'
 [void](Decision 'Resume' $today $returnLegacy $false $legacyPlan.Tables[0].Rows[0].Snapshot -student 2)
 Check ((Number 'SELECT SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges WHERE AdmissionId=2;') -eq 20000) 'Absence-period fees match the reviewed billing commencement month.'
 Check ((Number 'SELECT COUNT(*) FROM dbo.Enrollments WHERE AdmissionId=2;') -eq 1) 'An adopted Left student resumes under the same identity and placement.'

 # A collection-time concession must survive a later hold/restoration.
 [void](Query "DECLARE @Bill bigint=(SELECT BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=2 AND BillingMonth=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)),@D date=CONVERT(date,GETDATE()),@A dbo.FeePaymentAllocationType; DECLARE @Correction uniqueidentifier=NEWID(); EXEC dbo.AdjustBillingChargeRate @UserId=1,@BillingChargeId=@Bill,@NewAmount=10000,@PaidReductionAction=N'Credit',@Reason=N'Fictional previous reviewed terms',@RequestId=@Correction,@NewDiscount=0,@ApplyAvailableCredit=0; INSERT @A VALUES(@Bill,-500); EXEC dbo.ProcessFeeTransaction @AdmissionId=2,@EnrollmentId=2,@TransactionType=N'Collection',@Amount=0,@TransactionDate=@D,@PaymentMethod=N'Cash',@ReferenceNumber=NULL,@Remarks=N'Fictional collection-time concession',@FeePaymentId=NULL,@BillingChargeId=NULL,@Allocations=@A,@UserId=1;")
 $secondWaive=@{'@MonthlyFeePolicy'='FromMonth';'@WaiveFromMonth'=$month}
 [void](SaveDecision 'Hold' $today $secondWaive -student 2)
 $discountBill=[long](Number 'SELECT BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=2 AND BillingMonth=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1) AND CorrectionOfBillingChargeId IS NULL;')
 $discountRestore=@{'@BillingFromMonth'=$month;'@TargetEnrollmentId'=2;'@RestoreBills'="[$discountBill]"}
 $discountPlan=Decision 'Resume' $today $discountRestore -student 2
 Check ($discountPlan.Tables[0].Rows[0].Restored -eq 9500) 'Restoring a hold preserves the complete pre-hold concession rather than the original full rate.'
 [void](Decision 'Resume' $today $discountRestore $false $discountPlan.Tables[0].Rows[0].Snapshot -student 2)
 Check ((Number 'SELECT SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges WHERE AdmissionId=2;') -eq 19500) 'Collection-time concessions survive a completed hold and resume.'

 # Both sides of the configured whole-month cutoff, on a separate student.
 [void](Query "UPDATE dbo.Enrollments SET BillingStartDate=EnrollmentDate WHERE EnrollmentId=3; DECLARE @D date=DATEADD(month,-1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)),@S dbo.PostingSelectionType; INSERT @S VALUES(3,1,@D); EXEC dbo.PostSelectedMonthlyFees @UserId=1,@BillingMonth=@D,@Selection=@S,@AcademicYearId=1;")
 $early=$prior.AddDays(13);$late=$prior.AddDays(15)
 $earlyPlan=Decision 'Hold' $early @{} -student 3
 Check ($earlyPlan.Tables[0].Rows[0].Waived -eq 10000) 'Before cutoff day 16, school policy includes the hold month in the waiver.'
 $latePlan=Decision 'Hold' $late @{} -student 3
 Check ($latePlan.Tables[0].Rows[0].Waived -eq 0) 'On cutoff day 16, the already-issued hold-month fee remains due.'
 [void](Decision 'Hold' $late @{} $false $latePlan.Tables[0].Rows[0].Snapshot -student 3)
 # A promotion prepares the next placement while the independently held identity
 # stays excluded from billing until the reviewed resume.
 [void](Query "DECLARE @M date=CONVERT(date,GETDATE()); UPDATE dbo.AcademicYears SET IsCurrent=0,EndDate=DATEADD(day,-1,@M) WHERE AcademicYearId=1; INSERT dbo.AcademicYears(YearName,StartDate,EndDate,IsCurrent) VALUES(N'Fixture return year',@M,DATEADD(day,-1,DATEADD(year,1,@M)),1); DECLARE @New int=SCOPE_IDENTITY(); UPDATE dbo.FeeTypes SET IsMandatory=1 WHERE FeeTypeId=1; INSERT dbo.FeeTypes(FeeTypeCode,FeeTypeName,DefaultFrequency,IsMandatory) VALUES(N'ANNUAL',N'Annual fee',N'Yearly',1); INSERT dbo.FeeConfigurations(CampusId,ClassId,FeeTypeId,Amount,Frequency) SELECT 1,1,FeeTypeId,1500,N'Yearly' FROM dbo.FeeTypes WHERE FeeTypeCode=N'ANNUAL'; INSERT dbo.FeeConfigurations(CampusId,ClassId,FeeTypeId,Amount,Frequency) VALUES(1,1,1,12000,N'Monthly'); DECLARE @Decisions dbo.BulkPromotionDecisionType; INSERT @Decisions VALUES(3,N'PROMOTE'),(2,N'PROMOTE'); EXEC dbo.BulkPromoteStudents @UserId=1,@SourceAcademicYearId=1,@DestinationAcademicYearId=@New,@SourceCampusId=1,@SourceClassId=1,@SourceSectionId=1,@DestinationClassSectionId=1,@EffectiveDate=@M,@Remarks=N'Fictional held student promotion',@Decisions=@Decisions;")
 $newPlacement=[int](Number "SELECT EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=3 AND Status=N'Active';")
 Check ((Number 'SELECT COUNT(*) FROM dbo.fn_MonthlyBillingCandidates(DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1),0) WHERE AdmissionId=3 AND BlockReason IS NULL;') -eq 0) 'A new academic-year placement does not bypass the student-level hold.'
 $pending=Query 'EXEC dbo.GetPendingReturnAdmissions @UserId=1;'
 Check (@($pending.Tables[0].Rows | Where-Object { $_.AdmissionId -eq 3 }).Count -eq 0 -and @($pending.Tables[0].Rows | Where-Object { $_.AdmissionId -eq 2 }).Count -eq 1) 'Only the non-held promoted sibling is offered for ordinary return confirmation.'
 [void](Query "DECLARE @E int=(SELECT EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=2 AND Status=N'Active'),@Today date=CONVERT(date,GETDATE()),@Choices dbo.ReturnFeeChoiceType; INSERT @Choices SELECT A.FeeTypeId,A.Amount,A.DiscountAmount FROM dbo.StudentFeeAssignments A JOIN dbo.FeeTypes F ON F.FeeTypeId=A.FeeTypeId WHERE A.EnrollmentId=@E AND A.CancelledAt IS NULL AND F.IsMandatory=1; EXEC dbo.ConfirmPromotedAdmission @UserId=1,@EnrollmentId=@E,@ReturnDate=@Today,@UsesTransport=0,@FeeChoices=@Choices;")
 Check ((Number "SELECT COUNT(*) FROM dbo.Enrollments WHERE AdmissionId=2 AND Status=N'Active' AND BillingReady=1 AND ReturnConfirmedAt IS NOT NULL;") -eq 1 -and (Number "SELECT COUNT(*) FROM dbo.EnrollmentHistory WHERE AdmissionId=2 AND ActionType=N'ReturnConfirmed';") -eq 1) 'Normal promoted return still confirms placement and preserves its return history.'
 $newReturn=@{'@TargetEnrollmentId'=$newPlacement;'@BillingFromMonth'=$month}
 $nextPlan=Decision 'Resume' $today $newReturn -student 3
 Check ($nextPlan.Tables[0].Rows[0].NewCharges -eq 12000 -and $nextPlan.Tables[0].Rows[0].Outstanding -eq 10000) 'Cross-year resume reviews the new placement fee and retains old-year arrears.'
 [void](Decision 'Resume' $today $newReturn $false $nextPlan.Tables[0].Rows[0].Snapshot -student 3)
 Check ((Number 'SELECT COUNT(*) FROM dbo.Enrollments WHERE AdmissionId=3;') -eq 2) 'Cross-year resume retains both placements under one student identity.'
 Check ((Number 'SELECT SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges WHERE AdmissionId=3;') -eq 22000) 'Old and new placement dues reconcile after cross-year resume.'

 $annualReturn=@{'@TargetEnrollmentId'=$newPlacement;'@BillingFromMonth'=$month;'@ChargeAnnualFees'=$true}
 [void](SaveDecision 'Hold' $today $keep -student 3)
 $annualPlan=Decision 'Resume' $today $annualReturn -student 3
 Check ($annualPlan.Tables[0].Rows[0].NewCharges -eq 1500) 'Annual fees appear only when explicitly selected for the prepared placement.'
 [void](Decision 'Resume' $today $annualReturn $false $annualPlan.Tables[0].Rows[0].Snapshot -student 3)
 Check ((Number "SELECT COUNT(*) FROM dbo.BillingCharges WHERE EnrollmentId=$newPlacement AND ChargeKind=N'Annual';") -eq 1) 'The selected annual fee is issued once.'
 [void](SaveDecision 'Hold' $today $secondWaive -student 3)
 Check ((Number "SELECT SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges WHERE EnrollmentId=$newPlacement AND ChargeKind=N'Annual';") -eq 1500) 'A monthly waiver retains the annual charge unchanged.'
 $annualAgain=Decision 'Resume' $today $annualReturn -student 3
 Check ($annualAgain.Tables[0].Rows[0].NewCharges -eq 0) 'Later resume does not duplicate an existing annual bill.'
 [void](Decision 'Resume' $today $annualReturn $false $annualAgain.Tables[0].Rows[0].Snapshot -student 3)

 # Permission boundary, authoritative negative outcome and compatibility gate.
 [void](Query "INSERT dbo.Roles(RoleName) VALUES(N'Fictional denied role'); DECLARE @Role int=SCOPE_IDENTITY(); INSERT dbo.Users(Username,RoleId) VALUES(N'fictional_denied_user',@Role);")
 Refused { Query "DECLARE @R uniqueidentifier=NEWID(),@D date=CONVERT(date,GETDATE()),@U int=(SELECT UserId FROM dbo.Users WHERE Username=N'fictional_denied_user'); EXEC dbo.SaveStudentPostingDecision @UserId=@U,@AdmissionId=1,@Action=N'Hold',@EffectiveDate=@D,@Reason=N'Not authorized',@RequestId=@R;" } 'An operator without student-administration permission cannot review a hold.'
 $notSaved=Query 'DECLARE @R uniqueidentifier=NEWID(); EXEC dbo.GetStudentPostingDecisionOutcome @UserId=1,@AdmissionId=1,@RequestId=@R;'
 Check ($notSaved.Tables[0].Rows.Count -eq 0) 'Outcome checking can authoritatively establish that a request was not committed.'
 $oldConnection=$env:FMSC_BILLING_CHECK_CONNECTION;$oldOwner=$env:FMSC_BILLING_CHECK_OWNER
 try {
  $env:FMSC_BILLING_CHECK_CONNECTION="Server=$instance;Database=$catalog;Integrated Security=True;TrustServerCertificate=True"
  $env:FMSC_BILLING_CHECK_OWNER=$owner
  & dotnet run -c Release --project (Join-Path $projectRoot 'Tests/QuickRegistrationSqlChecks/QuickRegistrationSqlChecks.csproj') -- --billing-compatibility
  Native 'Production hold/resume schema compatibility'
 } finally {$env:FMSC_BILLING_CHECK_CONNECTION=$oldConnection;$env:FMSC_BILLING_CHECK_OWNER=$oldOwner}

 Write-Output "PASS: $checks hold/resume SQL checks."

} finally {
 if($created -and -not $KeepFixture){
  & SqlLocalDB stop $catalog;Native 'Stop disposable instance'
  & SqlLocalDB delete $catalog;Native 'Delete disposable instance'
  $resolved=[IO.Path]::GetFullPath($run)
  $allowed=[IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts/holds'))+[IO.Path]::DirectorySeparatorChar
  if(-not $resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -ne $owner){throw 'Unsafe fixture cleanup path.'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
 } elseif($created){Write-Output "Retained disposable fixture: $catalog ($run)"}
}
$global:LASTEXITCODE=0
