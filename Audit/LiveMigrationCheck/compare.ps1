$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$project=Split-Path -Parent $root
$live=((Get-Content "$PSScriptRoot/live.json" -Raw) -replace "[\r\n]", "") | ConvertFrom-Json
$modules=@{}; foreach($m in $live.modules_json){$modules["$($m.schema_name).$($m.name)"]=$m.definition}
function Normalize([string]$sql){
 $sql=[regex]::Replace($sql,'(?is)\A\s*(CREATE\s+OR\s+ALTER|CREATE|ALTER)\s+(PROCEDURE|PROC|TRIGGER|FUNCTION|VIEW)', 'CREATE $2')
 $tokens=[regex]::Matches($sql,"(?s)'(?:''|[^'])*'|--[^\r\n]*|/\*.*?\*/|\[[^\]]*\]|[\p{L}\p{N}_@#]+|[^\s]")
 return (($tokens | Where-Object {$_.Value -notmatch '^(--|/\*)'} | ForEach-Object {$_.Value}) -join '|')
}
$expected=@{}; $source=@{}
foreach($file in Get-ChildItem "$project/Migrations/*.sql" | Sort-Object {[int]$_.BaseName}){
 $sql=Get-Content $file.FullName -Raw
 foreach($match in [regex]::Matches($sql,"(?is)EXEC\s+sys\.sp_executesql\s+N'((?:''|[^'])*)'")){
  $def=$match.Groups[1].Value.Replace("''","'")
  if($def -match '(?is)^\s*(?:CREATE\s+OR\s+ALTER|ALTER|CREATE)\s+PROCEDURE\s+(dbo\.\w+)'){$expected[$Matches[1]]=$def;$source[$Matches[1]]=$file.Name}
 }
 foreach($batch in [regex]::Split($sql,'(?im)^\s*GO\s*$')){
  $match=[regex]::Match($batch,'(?is)(?:CREATE\s+OR\s+ALTER|ALTER|CREATE)\s+(?:PROCEDURE|TRIGGER|FUNCTION|VIEW)\s+(dbo\.\w+)')
  if($match.Success -and $batch.Substring(0,$match.Index) -notmatch "(?is)EXEC\s+sys\.sp_executesql\s+N'|OBJECT_DEFINITION"){
   $key=$match.Groups[1].Value;$expected[$key]=$batch.Substring($match.Index).Trim();$source[$key]=$file.Name
  }
 }
}
# Migration 8 amends the migration 6 procedure rather than replacing it.
$key='dbo.CreateReviewedLeaveCertificate'
$expected[$key]=$expected[$key].Replace('AND (BillingMonth>@Month OR (BillingMonth IS NULL AND ChargeDate>@LeaveDate));','AND (dbo.GetFeeChargeBalance(BillingChargeId)>0 OR BillingMonth>@Month OR (BillingMonth IS NULL AND ChargeDate>@LeaveDate));').Replace('Only reviewed, wholly uncollected later charges can be reversed.','Only reviewed, wholly uncollected charges on the current enrollment can be reversed.')
$results=@(foreach($key in $expected.Keys | Sort-Object){
 $status=if(!$modules.ContainsKey($key)){'MISSING'}elseif((Normalize $expected[$key]) -ceq (Normalize $modules[$key])){'MATCH'}else{'DIFFERENT'}
 if($status -eq 'DIFFERENT'){Set-Content "$PSScriptRoot/$key.expected.sql" $expected[$key];Set-Content "$PSScriptRoot/$key.live.sql" $modules[$key]}
 [pscustomobject]@{Object=$key;Migration=$source[$key];Status=$status}
})
$results | Export-Csv "$PSScriptRoot/module-comparison.csv" -NoTypeInformation
$results | Format-Table -AutoSize
$live.versions_json | Select-Object -Last 4 | Format-Table VersionId,VersionNumber,Notes -Wrap

$checks=[System.Collections.Generic.List[object]]::new()
function CheckFragment($migration,$name,$label,$fragment){
 $ok=$modules.ContainsKey($name) -and (Normalize $modules[$name]).Contains((Normalize $fragment))
 $checks.Add([pscustomobject]@{Migration=$migration;Object=$name;Check=$label;Status=$(if($ok){'PASS'}else{'REVIEW'})})
}
foreach($spec in @(@(4,'dbo.ChangeStudentFeeAssignmentAmount_Internal','branch'),@(5,'dbo.SyncTransferStudentFees_Internal','guard'),@(5,'dbo.SyncTransferStudentFees_Internal','replaceSchedule'))){
 $sql=Get-Content "$project/Migrations/$($spec[0]).sql" -Raw
 $pat="(?is)DECLARE\s+@"+$spec[2]+"\s+nvarchar\(max\)\s*=\s*N'((?:''|[^'])*)'"
 $m=[regex]::Match($sql,$pat)
 CheckFragment $spec[0] $spec[1] $spec[2] $m.Groups[1].Value.Replace("''","'")
}
CheckFragment 4 'dbo.GetStudentsAffectedByFeeConfiguration' 'Enrollment-scoped last billed month' 'SELECT MAX(BC.BillingMonth) FROM dbo.BillingCharges BC WHERE BC.EnrollmentId = X.EnrollmentId AND BC.FeeTypeId = @FeeTypeId'
foreach($name in 'dbo.TransferEnrollmentClass','dbo.TransferEnrollmentCampus'){
 CheckFragment 5 $name 'Reviewed fee parameters' '@FeeEffectiveMonth DATE=NULL, @ReplaceScheduledFees BIT=0'
 CheckFragment 5 $name 'Forward fee decisions' '@Reason = @FeeSyncReason, @EffectiveDate = @TransferDate, @FeeEffectiveMonth=@FeeEffectiveMonth, @ReplaceScheduledFees=@ReplaceScheduledFees;'
}
CheckFragment 6 'dbo.CreateLeaveCertificate' 'Keep later charges parameter' '@KeepLaterCharges BIT=0'
CheckFragment 6 'dbo.CreateLeaveCertificate' 'Conditional later-bill protection' 'AND @KeepLaterCharges=0 AND DATEFROMPARTS(YEAR(@LeaveDate), MONTH(@LeaveDate), 1) < @MaxBilledMonth'
CheckFragment 16 'dbo.GetStudentEnrollmentsForLifecycle' 'Saved billing start' 'E.LeavingDate, E.BillingStartDate,'
foreach($pattern in '@MonthlyStartMonth','@ChargeYearlyFees','@ReviewedRates','THROW 57100','THROW 57101','THROW 57107','THROW 57108'){
 CheckFragment 8 'dbo.ReAdmitStudent' $pattern $pattern
}
$opening=$modules['dbo.PostEnrollmentOpeningCharges']
$checks.Add([pscustomobject]@{Migration=8;Object='dbo.PostEnrollmentOpeningCharges';Check='Cancelled bills retained in duplicate prevention';Status=$(if($opening -and $opening -notmatch "AND BC.Status <> N'Cancelled'"){'PASS'}else{'REVIEW'})})
foreach($spec in @(@('Sections','Description','nvarchar',500,$true),@('CampusClassSections','IsActive','bit',1,$false))){
 $c=$live.columns_json | Where-Object {$_.schema_name -eq 'dbo' -and $_.table_name -eq $spec[0] -and $_.column_name -eq $spec[1]}
 $checks.Add([pscustomobject]@{Migration=$(if($spec[0] -eq 'Sections'){2}else{3});Object="dbo.$($spec[0]).$($spec[1])";Check='Column type, size and nullability';Status=$(if($c.type_name -eq $spec[2] -and $c.max_length -eq $spec[3] -and $c.is_nullable -eq $spec[4]){'PASS'}else{'REVIEW'})})
}
$checks | Export-Csv "$PSScriptRoot/patch-comparison.csv" -NoTypeInformation
$checks | Format-Table -AutoSize
foreach($spec in @(@(5,@('dbo.SyncTransferStudentFees_Internal','dbo.TransferEnrollmentClass','dbo.TransferEnrollmentCampus')),@(8,@('dbo.ReAdmitStudent','dbo.PostEnrollmentOpeningCharges','dbo.CreateReviewedLeaveCertificate','dbo.PreviewMonthlyFees','dbo.PostSelectedMonthlyFees','dbo.GetPostableMonths','dbo.PreviewPostingReversal','dbo.ReverseMonthlyFees')))){
 $sql=Get-Content "$project/Migrations/$($spec[0]).sql" -Raw
 $combined=($spec[1] | ForEach-Object {Normalize $modules[$_]}) -join '|'
 $matches=[regex]::Matches($sql,"(?is)SET\s+@sql\s*=\s*REPLACE\(@sql,\s*N'((?:''|[^'])*)',\s*N'((?:''|[^'])*)'\)")
 foreach($m in $matches){
  $replacement=$m.Groups[2].Value.Replace("''","'")
  if($replacement -match '^ALTER PROCEDURE$|^$'){continue}
  $ok=$combined.Contains((Normalize $replacement))
  if(!$ok){Write-Output "REVIEW replacement migration $($spec[0]): $replacement"}
 }
}
$live.modules_json | ForEach-Object {Set-Content -LiteralPath "$PSScriptRoot/$($_.schema_name).$($_.name).sql" -Value $_.definition -Encoding utf8}
