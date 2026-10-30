# Loaded only by the uniquely owned, schema-only TestAutomaticBilling fixture.
[void](Query @'
UPDATE dbo.Classes SET DisplayOrder=1 WHERE ClassId=1;
INSERT dbo.Classes(ClassCode,ClassName,AcademicProgramId,DisplayOrder) VALUES(N'NEXT',N'Next Class',1,2);
INSERT dbo.Campuses(CampusCode,CampusName,CampusType) VALUES(N'OTHER',N'Other Campus',N'School');
INSERT dbo.Sections(SectionCode,SectionName) VALUES(N'B',N'Section B');
INSERT dbo.CampusClassSections(CampusId,ClassId,SectionId) VALUES(1,1,2),(1,2,1),(2,1,1),(2,2,1);
UPDATE dbo.FeeTypes SET IsMandatory=1 WHERE FeeTypeId=1;
INSERT dbo.FeeConfigurations(CampusId,ClassId,FeeTypeId,Amount,Frequency)
VALUES(1,1,1,10000,N'Monthly'),(1,2,1,11000,N'Monthly'),(2,1,1,12000,N'Monthly'),(2,2,1,13000,N'Monthly');
UPDATE dbo.Students SET LegacySetupPending=0;
DECLARE @Start date=(SELECT StartDate FROM dbo.AcademicYears WHERE AcademicYearId=1);
INSERT dbo.AcademicYears(YearName,StartDate,EndDate,IsCurrent)
VALUES(N'Previous',DATEADD(year,-1,@Start),DATEADD(day,-1,@Start),0);
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,Status,LegacySetupPending)
 VALUES('1005',N'PLAN-5',DATEADD(year,-1,@Start),1,N'Promotion Example',N'Active',0),('1006',N'PLAN-6',DATEADD(year,-1,@Start),1,N'Repeat Example',N'Active',0);
INSERT dbo.Enrollments(AdmissionId,AcademicYearId,ClassSectionId,EnrollmentDate,BillingStartDate,Status,BillingReady,PlacementConfirmedAt)
 VALUES(5,2,1,DATEADD(year,-1,@Start),DATEADD(year,-1,@Start),N'Active',1,SYSDATETIME()),(6,2,1,DATEADD(year,-1,@Start),DATEADD(year,-1,@Start),N'Active',1,SYSDATETIME());
'@)
$script:taskHashes=@{}
function Run-Plan($json,$id,[int]$year,[bool]$preview=$false){
 if(-not $preview -and -not $script:taskHashes.ContainsKey($id.ToString())){[void](Run-Plan $json $id $year $true)}
 $result=Query 'EXEC dbo.ApplyStudentPlacementPlan @UserId=1,@RequestId=@Id,@Plan=@Json,@DestinationYearId=@Year,@EffectiveDate=@Date,@Reason=N''Fictional reviewed placement change'',@Preview=@Preview,@ExpectedReviewHash=@Hash;' @{
  '@Id'=$id;'@Json'=$json;'@Year'=$year;'@Date'=[datetime](Query 'SELECT StartDate FROM dbo.AcademicYears WHERE AcademicYearId=1;').Tables[0].Rows[0][0];'@Preview'=$preview;'@Hash'=$(if($script:taskHashes.ContainsKey($id.ToString())){$script:taskHashes[$id.ToString()]}else{[DBNull]::Value})}
 if($preview){$script:taskHashes[$id.ToString()]=($result.Tables[$result.Tables.Count-1].Rows[0].ResultJson|ConvertFrom-Json).ReviewHash}
 return $result
}
function Item([int]$enrollment,[string]$action,[int]$target,[int]$undo=0){
 $current=(Query 'SELECT ClassSectionId,CONVERT(varchar(33),UpdatedAt,126) Stamp FROM dbo.Enrollments WHERE EnrollmentId=@Id;' @{'@Id'=$enrollment}).Tables[0].Rows[0]
 return @{EnrollmentId=$enrollment;SourcePlacementId=[int]$current.ClassSectionId;Stamp=if($current.IsNull('Stamp')){$null}else{[string]$current.Stamp};Action=$action;DestinationPlacementId=$target;FeePolicy=$null;UndoItemId=$undo}
}
function Json($items){return ConvertTo-Json -InputObject @($items) -Compress}
function Refuses($script,[int]$number,$message){
 try{& $script | Out-Null;throw 'Expected refusal did not happen.'}catch{
  $error=$_.Exception;while($error.InnerException){$error=$error.InnerException}
  if($error -isnot [Data.SqlClient.SqlException] -or $error.Number -ne $number){throw}
  Check $true $message
 }
}
$targetSection=[int](Query 'SELECT ClassSectionId FROM dbo.CampusClassSections WHERE CampusId=1 AND ClassId=1 AND SectionId=2;').Tables[0].Rows[0][0]
$targetNext=[int](Query 'SELECT ClassSectionId FROM dbo.CampusClassSections WHERE CampusId=2 AND ClassId=2 AND SectionId=1;').Tables[0].Rows[0][0]
$move=Json @((Item 1 'MOVE' $targetSection));$moveId=[guid]::NewGuid()
[void](Run-Plan $move $moveId 1 $true)
Check ((Number 'SELECT ClassSectionId FROM dbo.Enrollments WHERE EnrollmentId=1;') -eq 1) 'Preview rolls back placement changes.'
Check ((Number 'SELECT COUNT(*) FROM dbo.StudentPlacementPlans;') -eq 0) 'Preview leaves no saved plan or recovery marker.'
[void](Run-Plan $move $moveId 1)
[void](Run-Plan $move $moveId 1)
Check ((Number "SELECT COUNT(*) FROM dbo.StudentPlacementPlanItems WHERE Action=N'MOVE';") -eq 1) 'Retry of an identical request returns its saved result without duplicate movement.'
Refuses {Run-Plan $move ([guid]::NewGuid()) 1} 59810 'A stale source snapshot is refused.'
$bad=Json @((Item 4 'PROMOTE' $targetNext),(Item 5 'PROMOTE' 999999))
Refuses {Run-Plan $bad ([guid]::NewGuid()) 1} 59814 'Invalid destination rolls back the entire mixed plan.'
Check ((Number "SELECT COUNT(*) FROM dbo.Enrollments WHERE AdmissionId=5 AND Status=N'Active' AND AcademicYearId=2;") -eq 1) 'An earlier successful row is restored after a later row fails.'
$promotion=Json @((Item 4 'PROMOTE' $targetNext),(Item 5 'HOLD' 1));$promotionId=[guid]::NewGuid()
[void](Run-Plan $promotion $promotionId 1)
Check ((Number "SELECT COUNT(*) FROM dbo.Enrollments E JOIN dbo.CampusClassSections P ON P.ClassSectionId=E.ClassSectionId WHERE E.AdmissionId=5 AND E.Status=N'Active' AND P.CampusId=2 AND P.ClassId=2 AND E.BillingReady=0;") -eq 1) 'Cross-campus promotion creates the next-class placement behind the return-admission billing gate.'
Check ((Number "SELECT COUNT(*) FROM dbo.Enrollments WHERE AdmissionId=6 AND Status=N'Active' AND AcademicYearId=1 AND ClassSectionId=1;") -eq 1) 'A repeater keeps the original placement in the new year.'
$saved=(Query "SELECT ItemId,DestinationEnrollmentId FROM dbo.StudentPlacementPlanItems WHERE Action=N'PROMOTE';").Tables[0].Rows[0]
$undo=Json @((Item ([int]$saved.DestinationEnrollmentId) 'UNDO' 0 ([int]$saved.ItemId)))
[void](Run-Plan $undo ([guid]::NewGuid()) 1)
Check ((Number "SELECT COUNT(*) FROM dbo.Enrollments WHERE AdmissionId=5 AND Status=N'Active' AND AcademicYearId=2;") -eq 1) 'Undo restores the original placement before return admission.'
Check ((Number 'SELECT COUNT(*) FROM dbo.Enrollments WHERE AdmissionId=5;') -eq 2) 'Undo retains both original and mistaken enrollment records.'
$held=(Query "SELECT ItemId,DestinationEnrollmentId FROM dbo.StudentPlacementPlanItems WHERE Action=N'HOLD';").Tables[0].Rows[0]
[void](Query 'UPDATE dbo.Enrollments SET BillingReady=1 WHERE EnrollmentId=@Id;' @{'@Id'=[int]$held.DestinationEnrollmentId})
$blockedUndo=Json @((Item ([int]$held.DestinationEnrollmentId) 'UNDO' 0 ([int]$held.ItemId)))
Refuses {Run-Plan $blockedUndo ([guid]::NewGuid()) 1} 59812 'Undo refuses a destination whose return admission has started billing.'
$staleId=[guid]::NewGuid();$stale=Json @((Item 1 'MOVE' 1))
[void](Run-Plan $stale $staleId 1 $true)
[void](Query 'UPDATE dbo.StudentFeeAssignments SET Amount=Amount+1 WHERE EnrollmentId=1 AND FeeTypeId=1;')
Refuses {Run-Plan $stale $staleId 1} 59820 'Financial changes after review invalidate the entire plan.'
$campusTarget=[int](Query 'SELECT ClassSectionId FROM dbo.CampusClassSections WHERE CampusId=2 AND ClassId=1 AND SectionId=1;').Tables[0].Rows[0][0]
$merge=Json @((Item 1 'MOVE' $campusTarget),(Item ([int]$held.DestinationEnrollmentId) 'MOVE' $campusTarget))
[void](Run-Plan $merge ([guid]::NewGuid()) 1)
Check ((Number "SELECT COUNT(*) FROM dbo.Enrollments WHERE EnrollmentId IN(1,$($held.DestinationEnrollmentId)) AND ClassSectionId=$campusTarget;") -eq 2) 'A whole group can move campuses and merge into one destination.'
$split=Json @((Item 1 'CORRECT' 1),(Item ([int]$held.DestinationEnrollmentId) 'CORRECT' $targetSection))
[void](Run-Plan $split ([guid]::NewGuid()) 1)
Check ((Number "SELECT COUNT(DISTINCT ClassSectionId) FROM dbo.Enrollments WHERE EnrollmentId IN(1,$($held.DestinationEnrollmentId));") -eq 2) 'Individual destinations can split a group and record corrective moves.'
$feeMove=Item 1 'MOVE' $campusTarget;$feeMove.FeePolicy='NEXT_MONTH'
[void](Run-Plan (Json @($feeMove)) ([guid]::NewGuid()) 1)
Check ((Number "SELECT COUNT(*) FROM dbo.StudentFeeAssignments WHERE EnrollmentId=1 AND Amount=12000;") -ge 1) 'Destination fee synchronization works inside a bulk plan without nested INSERT EXEC failures.'
