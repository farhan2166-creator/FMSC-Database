# Dot-sourced only by the owned, schema-only audit-retirement fixture.
if($catalog -notmatch '^FMSC_BillingChecks_[0-9a-f]{32}$' -or $instance -ne ('(localdb)\'+$catalog)){throw 'Owned disposable fixture required.'}

$stop="DECLARE @D date=CONVERT(date,GETDATE()); EXEC dbo.DiscontinueStudentFeeAssignment @UserId=1,@StudentFeeAssignmentId=2,@EffectiveTo=@D,@Reason=N'Reviewed stop';"
Refused ($stop.Replace('@UserId=1','@UserId=2')) 50003 'Stopping a fee retains permission checks.'
Refused ($stop.Replace("@Reason=N'Reviewed stop'","@Reason=N' '")) 53130 'Stopping a fee requires a reason.'
Refused ($stop.Replace('@EffectiveTo=@D','@EffectiveTo=NULL')) 53136 'Stopping a fee requires an end date.'
Refused ($stop.Replace('@StudentFeeAssignmentId=2','@StudentFeeAssignmentId=999999')) 53131 'Stopping a fee rejects an unknown assignment.'
Refused ($stop.Replace('@StudentFeeAssignmentId=2','@StudentFeeAssignmentId=1')) 53132 'Stopping a fee rejects a cancelled assignment.'
Refused ($stop.Replace('CONVERT(date,GETDATE())','CONVERT(date,DATEADD(month,-1,GETDATE()))')) 53134 'Stopping a fee cannot precede its last billed month.'
$paidBefore=Number 'SELECT SUM(Amount) FROM dbo.FeePaymentAllocations;'
$result=Query $stop
Check ($result.Tables[0].Rows[0].StudentFeeAssignmentId -eq 2 -and $result.Tables[0].Rows[0].Amount -eq 2000 -and ([datetime]$result.Tables[0].Rows[0].EffectiveTo).Day -eq 1) 'Stopping a fee returns its terms and normalizes the end date to its month.'
Check ((Number 'SELECT SUM(Amount) FROM dbo.FeePaymentAllocations;') -eq $paidBefore -and (Number "SELECT COUNT(*) FROM dbo.BillingCharges WHERE StudentFeeAssignmentId=2 AND Status<>N'Cancelled';") -eq 1) 'Stopping a paid fee preserves the issued charge and payment allocation.'
Refused $stop 53133 'A stopped fee cannot be stopped twice.'

[void](Query "INSERT dbo.FeeTypes(FeeTypeCode,FeeTypeName,DefaultFrequency) VALUES(N'APPLY_TEST',N'Fictional configured fee',N'Monthly'); DECLARE @F int=SCOPE_IDENTITY(),@M date=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1); INSERT dbo.FeeConfigurations(CampusId,ClassId,FeeTypeId,Amount,Frequency) VALUES(1,1,@F,200,N'Monthly'); INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,EffectiveFrom) VALUES(1,1,@F,100,20,N'Monthly',@M);")
$typeId=[int](Number "SELECT FeeTypeId FROM dbo.FeeTypes WHERE FeeTypeCode=N'APPLY_TEST';")
$assignmentId=[int](Number "SELECT StudentFeeAssignmentId FROM dbo.StudentFeeAssignments WHERE EnrollmentId=1 AND FeeTypeId=$typeId;")
$apply="DECLARE @D date=DATEADD(month,1,CONVERT(date,GETDATE())); EXEC dbo.ApplyFeeConfigurationToExistingAssignments @UserId=1,@CampusId=1,@ClassId=1,@FeeTypeId=$typeId,@EffectiveFrom=@D,@Reason=N'Reviewed configured rate';"
Refused ($apply.Replace('@UserId=1','@UserId=2')) 50003 'Applying fee rates retains permission checks.'
Refused ($apply.Replace("@Reason=N'Reviewed configured rate'","@Reason=NULL")) 53151 'Applying fee rates requires a reason.'
Refused ($apply.Replace('@EffectiveFrom=@D','@EffectiveFrom=NULL')) 53152 'Applying fee rates requires an effective date.'
Refused ($apply.Replace("@FeeTypeId=$typeId",'@FeeTypeId=999999')) 53153 'Applying fee rates requires a matching configuration.'
$result=Query $apply
$summary=@($result.Tables|Where-Object {$_.Columns.Contains('TotalAssignments')})
Check ($summary.Count -eq 1 -and $summary[0].Rows[0].TotalAssignments -eq 1 -and $summary[0].Rows[0].Succeeded -eq 1 -and $summary[0].Rows[0].Failed -eq 0 -and ([datetime]$summary[0].Rows[0].EffectiveFrom).Day -eq 1) 'Applying a rate reports successful candidates and normalized effective month.'
Check ((Number "SELECT COUNT(*) FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=$assignmentId AND Amount=100 AND EffectiveTo=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1);") -eq 1 -and (Number "SELECT COUNT(*) FROM dbo.StudentFeeAssignments WHERE EnrollmentId=1 AND FeeTypeId=$typeId AND Amount=200 AND DiscountAmount=20 AND EffectiveTo IS NULL AND EffectiveFrom=DATEADD(month,1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1));") -eq 1) 'Applying a configured rate preserves the old version and creates the new version with its discount.'
$result=Query $apply;$summary=@($result.Tables|Where-Object {$_.Columns.Contains('TotalAssignments')})
Check ($summary[0].Rows[0].TotalAssignments -eq 0 -and $summary[0].Rows[0].Succeeded -eq 0 -and $summary[0].Rows[0].Failed -eq 0) 'Reapplying an already current rate creates no extra versions.'
$newAssignment=[int](Number "SELECT StudentFeeAssignmentId FROM dbo.StudentFeeAssignments WHERE EnrollmentId=1 AND FeeTypeId=$typeId AND EffectiveTo IS NULL;")
Refused ($stop.Replace('@StudentFeeAssignmentId=2',"@StudentFeeAssignmentId=$newAssignment")) 53137 'A scheduled fee cannot stop before its starting month.'

# Use the fictional cash payment recorded by the preceding batch.
$closingPrefix='DECLARE @D date=CONVERT(date,GETDATE()),@Snapshot nvarchar(max),@Hash varchar(64); EXEC dbo.DailyClosingSnapshot @D,@Snapshot OUTPUT,@Hash OUTPUT; '
$closingSave=$closingPrefix+"EXEC dbo.SaveDailyCollectionClosing @UserId=1,@ClosingDate=@D,@ExpectedClosingId=0,@SnapshotHash=@Hash,@ActualAmounts=N'[{"+'"Method":"Cash","Amount":100'+"}]',@Notes=N'';"
Refused ($closingSave.Replace('@UserId=1','@UserId=2')) 50003 'Closing submission retains permission checks.'
Refused ($closingSave.Replace('@ClosingDate=@D','@ClosingDate=NULL')) 59921 'Closing submission requires a date.'
Refused ($closingSave.Replace('@ExpectedClosingId=0','@ExpectedClosingId=NULL')) 59922 'Closing submission requires the reviewed version.'
Refused ($closingSave.Replace('@SnapshotHash=@Hash',"@SnapshotHash='stale'")) 59925 'Closing submission rejects a stale payment snapshot.'
Refused ($closingSave.Replace('@ExpectedClosingId=0','@ExpectedClosingId=999999')) 59924 'Closing submission rejects a stale revision.'
Refused ($closingSave.Replace('"Amount":100','"Amount":90')) 59928 'Closing differences still require explanatory notes.'
Refused ($closingSave.Replace('"Amount":100','"Amount":-1')) 59927 'Closing actual collections cannot be negative.'
Refused ($closingSave.Replace('"Method":"Cash"','"Method":"Bank"')) 59926 'Closing actual amounts must match the listed methods.'
[void](Query $closingSave)
$closingId=[long](Number 'SELECT MAX(ClosingId) FROM dbo.DailyCollectionClosings;')
Check ((Number "SELECT COUNT(*) FROM dbo.DailyCollectionClosings WHERE ClosingId=$closingId AND Revision=1 AND SubmittedBy=1 AND SubmittedAt IS NOT NULL AND ISJSON(PaymentSnapshot)=1 AND ISJSON(ActualAmounts)=1 AND ApprovedAt IS NULL;") -eq 1) 'Closing submission retains the snapshot, actual amounts, revision and submitter.'
Refused $closingSave 59924 'An outdated closing screen cannot submit a second initial revision.'
$approve=$closingPrefix+"EXEC dbo.ApproveDailyCollectionClosing @UserId=1,@ClosingDate=@D,@ClosingId=$closingId,@SnapshotHash=@Hash;"
Refused ($approve.Replace('@ClosingId='+$closingId,'@ClosingId=NULL')) 59931 'Approval requires a reviewed closing.'
Refused ($approve.Replace('@SnapshotHash=@Hash',"@SnapshotHash='stale'")) 59925 'Approval rejects a stale payment snapshot.'
Refused ($approve.Replace('@ClosingId='+$closingId,'@ClosingId=999999')) 59931 'Approval rejects an unknown or stale closing.'
[void](Query "IF NOT EXISTS(SELECT 1 FROM dbo.Permissions WHERE PermissionCode=N'Fees.Collect') INSERT dbo.Permissions(PermissionCode,PermissionName,Description) VALUES(N'Fees.Collect',N'Collect fees',N'Fictional collection permission'); INSERT dbo.UserRoles(UserId,RoleId) VALUES(2,2); INSERT dbo.RolePermissions(RoleId,PermissionId) SELECT 2,PermissionId FROM dbo.Permissions WHERE PermissionCode=N'Fees.Collect';")
Refused ($approve.Replace('@UserId=1','@UserId=2')) 59930 'Collection permission alone does not allow closing approval.'
[void](Query $approve)
Check ((Number "SELECT COUNT(*) FROM dbo.DailyCollectionClosings WHERE ClosingId=$closingId AND ApprovedBy=1 AND ApprovedAt IS NOT NULL AND PaymentSnapshot IS NOT NULL AND ActualAmounts IS NOT NULL;") -eq 1) 'Administrator approval retains the closing evidence and approval metadata.'
Refused $approve 59931 'The same closing cannot be approved twice.'
[void](Query ($closingSave.Replace('@ExpectedClosingId=0',"@ExpectedClosingId=$closingId").Replace('"Amount":100','"Amount":95').Replace("@Notes=N''","@Notes=N'Reviewed cash difference'")))
Check ((Number "SELECT COUNT(*) FROM dbo.DailyCollectionClosings WHERE ClosingDate=CONVERT(date,GETDATE()) AND Revision=2 AND Notes=N'Reviewed cash difference' AND ApprovedAt IS NULL;") -eq 1 -and (Number "SELECT COUNT(*) FROM dbo.DailyCollectionClosings WHERE ClosingId=$closingId AND ApprovedBy=1;") -eq 1) 'A new closing revision preserves the older approved record.'
Refused $approve 59931 'An older closing cannot be approved after a newer revision exists.'
Check ((Number 'SELECT SUM(Amount) FROM dbo.FeePaymentAllocations;') -eq $paidBefore) 'Daily closing never rewrites payment allocations.'
