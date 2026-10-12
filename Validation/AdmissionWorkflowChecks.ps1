param([string]$Server)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
if(-not $Server){
 & 'C:/Program Files/Microsoft SQL Server/150/Tools/Binn/SqlLocalDB.exe' start MSSQLLocalDB | Out-Null
 $taskInfo=& 'C:/Program Files/Microsoft SQL Server/150/Tools/Binn/SqlLocalDB.exe' info MSSQLLocalDB
 $Server=($taskInfo|Select-String 'np:').ToString().Replace('Instance pipe name:','').Trim()
}
$taskPipe=$Server
$taskName='FMSC_AdmissionChecks_'+[Guid]::NewGuid().ToString('N').Substring(0,10)
$taskDir=Join-Path $taskRoot ('Tests/'+$taskName)
New-Item -ItemType Directory -Path $taskDir -Force | Out-Null
function Connect([string]$catalog){$c=New-Object System.Data.SqlClient.SqlConnection ('Server='+$taskPipe+';Database='+$catalog+';Integrated Security=true');$c.Open();return $c}
function Query([string]$sql,[hashtable]$parameters=@{}){
 $q=$script:taskConnection.CreateCommand();$q.CommandText=$sql;$q.CommandTimeout=180
 foreach($k in $parameters.Keys){[void]$q.Parameters.AddWithValue('@'+$k,$parameters[$k])}
 $d=New-Object System.Data.DataTable;$r=$null
 try{$r=$q.ExecuteReader();$d.Load($r);return ,$d}finally{if($r){$r.Dispose()};$q.Dispose()}
}
function Assert($condition,$label){if(-not $condition){throw $label};'PASS: '+$label}
function Refused([string]$sql,[int]$number,$label){try{[void](Query $sql);throw 'Expected refusal'}catch{$e=$_.Exception;while($e.InnerException){$e=$e.InnerException};if($e -isnot [System.Data.SqlClient.SqlException] -or $e.Number -ne $number){throw};'PASS: '+$label}}
$script:taskConnection=Connect 'master'
$taskCreated=$false
try{
 $taskBackup=Join-Path $taskDir 'source.bak'
 [void](Query 'BACKUP DATABASE FMSC_DB TO DISK=@Path WITH COPY_ONLY,INIT' @{Path=$taskBackup})
 $taskFiles=Query 'RESTORE FILELISTONLY FROM DISK=@Path' @{Path=$taskBackup}
 $taskDataName=($taskFiles.Select("Type='D'")|Select-Object -First 1).LogicalName.Replace("'","''")
 $taskLogName=($taskFiles.Select("Type='L'")|Select-Object -First 1).LogicalName.Replace("'","''")
 $taskDataPath=(Join-Path $taskDir 'checks.mdf').Replace("'","''")
 $taskLogPath=(Join-Path $taskDir 'checks_log.ldf').Replace("'","''")
 [void](Query "RESTORE DATABASE [$taskName] FROM DISK=@Path WITH MOVE N'$taskDataName' TO N'$taskDataPath',MOVE N'$taskLogName' TO N'$taskLogPath'" @{Path=$taskBackup})
 $taskCreated=$true;$script:taskConnection.Dispose();$script:taskConnection=Connect $taskName
 foreach($batch in [regex]::Split([IO.File]::ReadAllText((Join-Path $taskRoot 'DatabaseScripts/AdmissionWorkflow.sql')),'(?im)^GO\s*$')){
  if(-not [string]::IsNullOrWhiteSpace($batch)){[void](Query $batch)}
 }
 $taskContext=Query "SELECT TOP(1) Y.AcademicYearId,CS.ClassSectionId,CS.CampusId FROM dbo.AcademicYears Y CROSS JOIN dbo.CampusClassSections CS WHERE CONVERT(date,GETDATE()) BETWEEN Y.StartDate AND Y.EndDate ORDER BY CS.ClassSectionId;"
 $taskYear=[int]$taskContext.Rows[0].AcademicYearId;$taskPlacement=[int]$taskContext.Rows[0].ClassSectionId;$taskCampus=[int]$taskContext.Rows[0].CampusId
 [void](Query "INSERT dbo.FeeConfigurations(CampusId,ClassId,FeeTypeId,Amount,Frequency) SELECT CS.CampusId,CS.ClassId,F.FeeTypeId,2000,CASE WHEN F.FeeTypeCode=N'TUITION' THEN N'Monthly' ELSE N'Yearly' END FROM dbo.CampusClassSections CS CROSS JOIN dbo.FeeTypes F WHERE CS.ClassSectionId=$taskPlacement AND F.IsMandatory=1 AND NOT EXISTS(SELECT 1 FROM dbo.FeeConfigurations C WHERE C.CampusId=CS.CampusId AND C.ClassId=CS.ClassId AND C.FeeTypeId=F.FeeTypeId);")
 function Admit([bool]$quick,[int]$family=0,[string]$parentName='Test Parent'){
  $taskBit=[int]$quick;$taskFamily=if($family -gt 0){[string]$family}else{'NULL'}
  return Query "DECLARE @Fees dbo.FeeTypeIdList,@Discounts dbo.AdmissionFeeDiscountType,@D date=CONVERT(date,GETDATE()); EXEC dbo.AdmitStudent @UserId=1,@AdmissionDate=@D,@AdmissionCampusId=$taskCampus,@AcademicYearId=$taskYear,@ClassSectionId=$taskPlacement,@StudentName=N'Isolated admission check',@FatherName=@ParentName,@DateOfBirth='20150101',@PlaceOfBirth=NULL,@Gender=N'Male',@BFormNumber=NULL,@Religion=NULL,@BloodGroup=NULL,@ContactNumber=N'03001234567',@AlternateContactNumber=NULL,@Address=NULL,@PreviousSchool=NULL,@FatherOccupation=NULL,@Disability=N'No',@UsesTransport=0,@SelectedFeeTypeIds=@Fees,@AdmissionDiscounts=@Discounts,@QuickRegistration=$taskBit,@FamilyStudentId=$taskFamily;" @{ParentName=$parentName}
 }
 # EXEC parameter expressions must be assigned before invocation on SQL Server.
 $taskFirst=Admit $false;$taskDirect=[int]$taskFirst.Rows[0].AdmissionId;$taskDirectE=[int]$taskFirst.Rows[0].EnrollmentId
 $taskDirectState=Query "SELECT S.Status,S.AdmissionNumber,E.BillingReady,E.PlacementConfirmedAt,(SELECT COUNT(*) FROM dbo.BillingCharges WHERE EnrollmentId=E.EnrollmentId) Charges FROM dbo.Students S JOIN dbo.Enrollments E ON E.AdmissionId=S.AdmissionId WHERE S.AdmissionId=$taskDirect;"
 Assert ($taskDirectState.Rows[0].Status -eq 'Active' -and $taskDirectState.Rows[0].BillingReady -and $taskDirectState.Rows[0].PlacementConfirmedAt -is [DateTime] -and $taskDirectState.Rows[0].Charges -gt 0) 'Direct admission saves active placement and opening fees together'
 $taskQ1=Admit $true;$taskChild1=[int]$taskQ1.Rows[0].AdmissionId;$taskE1=[int]$taskQ1.Rows[0].EnrollmentId
 $taskQ2=Admit $true $taskChild1 'Edited Parent';$taskChild2=[int]$taskQ2.Rows[0].AdmissionId
 $taskPending=Query "SELECT S.Status,S.AdmissionNumber,S.QuickReference,E.Status PlacementStatus,(SELECT COUNT(*) FROM dbo.BillingCharges WHERE EnrollmentId=E.EnrollmentId) Charges FROM dbo.Students S JOIN dbo.Enrollments E ON E.AdmissionId=S.AdmissionId WHERE S.AdmissionId=$taskChild1;"
 Assert ($taskPending.Rows[0].Status -eq 'Pending' -and $taskPending.Rows[0].AdmissionNumber -is [DBNull] -and $taskPending.Rows[0].QuickReference -like 'QR-*' -and $taskPending.Rows[0].Charges -gt 0) 'Quick registration has a bill and reference, without an official admission number'
 Assert ((Query "SELECT RegistrationNumber FROM dbo.Students WHERE AdmissionId=$taskChild1").Rows[0].RegistrationNumber -is [DBNull]) 'Temporary registration has no official registration number'
 Refused "UPDATE dbo.Enrollments SET Status=N'Active' WHERE EnrollmentId=$taskE1;" 59752 'A provisional placement cannot bypass admission completion'
 Refused "INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind) SELECT TOP(1) StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,DATEADD(MONTH,1,BillingMonth),DATEADD(MONTH,1,ChargeDate),DATEADD(MONTH,1,DueDate),GrossAmount,DiscountAmount,N'Monthly' FROM dbo.BillingCharges WHERE EnrollmentId=$taskE1 AND ChargeKind=N'Monthly';" 59633 'Pending registrations do not receive subsequent monthly postings'
 Assert ((Query "SELECT COUNT(*) Total FROM dbo.FamilyMembers WHERE AdmissionId IN($taskChild1,$taskChild2)").Rows[0].Total -eq 2) 'Children share one family'
 Assert ((Query "SELECT FatherName FROM dbo.Students WHERE AdmissionId=$taskChild1").Rows[0].FatherName -eq 'Test Parent' -and (Query "SELECT FatherName FROM dbo.Students WHERE AdmissionId=$taskChild2").Rows[0].FatherName -eq 'Edited Parent') 'Editing copied parent details affects only the new child and retains the family link'
 [void](Query "UPDATE dbo.Students SET DateOfBirth=NULL,Gender=NULL WHERE AdmissionId=$taskChild1;")
 Refused "EXEC dbo.CompleteQuickAdmission @UserId=1,@AdmissionId=$taskChild1" 59702 'Incomplete basic registration cannot become official admission'
 [void](Query "DECLARE @Discounts dbo.StudentFeeDiscountType;EXEC dbo.SaveStudentDetailsWithFirstEnrollment @UserId=1,@AdmissionId=$taskChild1,@StudentName=N'Isolated admission check',@FatherName=N'Test Parent',@DateOfBirth='20150101',@Gender=N'Male',@ContactNumber=N'03001234567',@Address=N'Completed address',@PlaceOfBirth=N'Test town',@Discounts=@Discounts;")
 Assert ((Query "SELECT AdmissionNumber,DateOfBirth FROM dbo.Students WHERE AdmissionId=$taskChild1").Rows[0].AdmissionNumber -is [DBNull]) 'Saving pending details preserves registration stage and does not allocate an admission number'
 [void](Query "DECLARE @Discounts dbo.StudentFeeDiscountType;INSERT @Discounts SELECT StudentFeeAssignmentId,100 FROM dbo.StudentFeeAssignments WHERE EnrollmentId=$taskE1;EXEC dbo.SaveStudentDetailsWithFirstEnrollment @UserId=1,@AdmissionId=$taskChild1,@StudentName=N'Isolated admission check',@FatherName=N'Test Parent',@DateOfBirth='20150101',@Gender=N'Male',@ContactNumber=N'03001234567',@Address=N'Updated registration address',@PlaceOfBirth=N'Test town',@Discounts=@Discounts;")
 Assert ((Query "SELECT COUNT(*) Total FROM dbo.BillingCharges WHERE EnrollmentId=$taskE1 AND DiscountAmount<>100").Rows[0].Total -eq 0) 'Saving pending registration discounts updates its unpaid opening bills'
 Assert ((Query "SELECT Address,Status FROM dbo.Students WHERE AdmissionId=$taskChild1").Rows[0].Address -eq 'Updated registration address') 'Pending registration basic edits save with its discounts'
 $taskCount=[int]$taskPending.Rows[0].Charges
 $taskCharge=(Query "SELECT TOP(1) BillingChargeId,NetAmount FROM dbo.BillingCharges WHERE EnrollmentId=$taskE1 AND Status=N'Open' AND NetAmount>0 ORDER BY BillingChargeId;").Rows[0]
 $taskChargeId=[long]$taskCharge.BillingChargeId;$taskAmount=[decimal]$taskCharge.NetAmount
 [void](Query "DECLARE @D date=CONVERT(date,GETDATE()),@A dbo.FeePaymentAllocationType;INSERT @A VALUES($taskChargeId,$taskAmount);EXEC dbo.ProcessFeeTransaction @AdmissionId=$taskChild1,@EnrollmentId=$taskE1,@TransactionType=N'Collection',@Amount=$taskAmount,@TransactionDate=@D,@PaymentMethod=N'Cash',@ReferenceNumber=NULL,@Remarks=N'Isolated check',@FeePaymentId=NULL,@BillingChargeId=NULL,@Allocations=@A,@UserId=1;")
 Refused "DECLARE @D date=CONVERT(date,GETDATE());EXEC dbo.CloseStudentAdmission @UserId=1,@AdmissionId=$taskChild1,@LeavingDate=@D,@Reason=N'Cancel paid registration',@Cancel=1" 59716 'Paid registration cannot bypass fee corrections'
 [void](Query "EXEC dbo.CompleteQuickAdmission @UserId=1,@AdmissionId=$taskChild1;")
 $taskCompleted=Query "SELECT S.Status,S.AdmissionNumber,(SELECT COUNT(*) FROM dbo.BillingCharges WHERE EnrollmentId=$taskE1) Charges,(SELECT COUNT(*) FROM dbo.FeePaymentAllocations WHERE AdmissionId=S.AdmissionId) Allocations FROM dbo.Students S WHERE AdmissionId=$taskChild1;"
 Assert ($taskCompleted.Rows[0].Status -eq 'Active' -and $taskCompleted.Rows[0].AdmissionNumber -is [string] -and $taskCompleted.Rows[0].Charges -eq $taskCount -and $taskCompleted.Rows[0].Allocations -gt 0) 'Completion keeps the same placement, paid charges and payment allocations without duplicate bills'
 Assert (-not [string]::IsNullOrWhiteSpace((Query "SELECT RegistrationNumber FROM dbo.Students WHERE AdmissionId=$taskChild1").Rows[0].RegistrationNumber)) 'Confirmation assigns the official registration number to the same student'
 Refused "EXEC dbo.CompleteQuickAdmission @UserId=1,@AdmissionId=$taskChild1" 59701 'A registration cannot be completed twice'
 $taskLeave=(Query "DECLARE @D date=CONVERT(date,GETDATE());EXEC dbo.CloseStudentAdmission @UserId=1,@AdmissionId=$taskDirect,@LeavingDate=@D,@Reason=N'Left next day',@Preview=1;").Rows[0].Snapshot
 [void](Query "DECLARE @D date=CONVERT(date,GETDATE());EXEC dbo.CloseStudentAdmission @UserId=1,@AdmissionId=$taskDirect,@LeavingDate=@D,@Reason=N'Left next day',@Preview=0,@ExpectedSnapshot='$taskLeave';")
 Assert ((Query "SELECT Status FROM dbo.Students WHERE AdmissionId=$taskDirect").Rows[0].Status -eq 'Left') 'An admitted student may leave without payment or certificate'
 $taskCancel=(Query "DECLARE @D date=CONVERT(date,GETDATE());EXEC dbo.CloseStudentAdmission @UserId=1,@AdmissionId=$taskChild2,@LeavingDate=@D,@Reason=N'Parent withdrew registration',@Cancel=1;").Rows[0].Snapshot
 [void](Query "DECLARE @D date=CONVERT(date,GETDATE());EXEC dbo.CloseStudentAdmission @UserId=1,@AdmissionId=$taskChild2,@LeavingDate=@D,@Reason=N'Parent withdrew registration',@Cancel=1,@Preview=0,@ExpectedSnapshot='$taskCancel';")
 Assert ((Query "SELECT Status FROM dbo.Students WHERE AdmissionId=$taskChild2").Rows[0].Status -eq 'Cancelled') 'Cancellation closes only the selected child'
 Assert ((Query "SELECT COUNT(*) Total FROM dbo.BillingCharges WHERE AdmissionId=$taskChild2 AND Status<>N'Cancelled'").Rows[0].Total -eq 0) 'Cancellation reverses unpaid registration charges'
 Refused "DECLARE @D date=CONVERT(date,GETDATE());EXEC dbo.CloseStudentAdmission @UserId=1,@AdmissionId=$taskChild1,@LeavingDate=@D,@Reason=N'Cancel completed registration',@Cancel=1" 59720 'Completed registration cannot be cancelled once officially admitted'
 $taskFuture=Admit $false;$taskFutureStudent=[int]$taskFuture.Rows[0].AdmissionId;$taskFutureE=[int]$taskFuture.Rows[0].EnrollmentId
 Refused "DECLARE @D date=CONVERT(date,GETDATE());EXEC dbo.CloseStudentAdmission @UserId=1,@AdmissionId=$taskFutureStudent,@LeavingDate=@D,@Reason=N'Cancel direct admission',@Cancel=1" 59720 'Unpaid direct admission cannot be cancelled once its official number is assigned'
 [void](Query "UPDATE dbo.Enrollments SET BillingStartDate=DATEADD(MONTH,1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)) WHERE EnrollmentId=$taskFutureE;INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind) SELECT TOP(1) StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,DATEADD(MONTH,1,BillingMonth),DATEADD(MONTH,1,ChargeDate),DATEADD(MONTH,1,DueDate),GrossAmount,DiscountAmount,N'Monthly' FROM dbo.BillingCharges WHERE EnrollmentId=$taskFutureE AND ChargeKind=N'Monthly';")
 $taskFuturePlan=Query "DECLARE @D date=CONVERT(date,GETDATE());EXEC dbo.CloseStudentAdmission @UserId=1,@AdmissionId=$taskFutureStudent,@LeavingDate=@D,@Reason=N'Left before next month';"
 Assert ($taskFuturePlan.Rows[0].BlockedCount -eq 1 -and $taskFuturePlan.Rows[0].FutureCount -eq 1) 'Departure requires an explicit decision about unpaid future fees'
 $taskFuturePlan=Query "DECLARE @D date=CONVERT(date,GETDATE());EXEC dbo.CloseStudentAdmission @UserId=1,@AdmissionId=$taskFutureStudent,@LeavingDate=@D,@Reason=N'Left before next month',@ReverseFutureUnpaid=1;"
 $taskFutureSnapshot=[string]$taskFuturePlan.Rows[0].Snapshot
 [void](Query "DECLARE @D date=CONVERT(date,GETDATE());EXEC dbo.CloseStudentAdmission @UserId=1,@AdmissionId=$taskFutureStudent,@LeavingDate=@D,@Reason=N'Left before next month',@ReverseFutureUnpaid=1,@Preview=0,@ExpectedSnapshot='$taskFutureSnapshot';")
 Assert ((Query "SELECT COUNT(*) Total FROM dbo.BillingCharges WHERE EnrollmentId=$taskFutureE AND Status<>N'Cancelled' AND BillingMonth>DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)").Rows[0].Total -eq 0) 'Reviewed departure reverses future unpaid fees atomically'
 Assert ((Query "SELECT COUNT(*) Total FROM dbo.BillingCharges WHERE EnrollmentId=$taskFutureE AND Status<>N'Cancelled'").Rows[0].Total -gt 0) 'Valid existing dues remain collectible after departure'
 $taskPreview=Query 'DECLARE @D date=CONVERT(date,GETDATE()),@M date=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1);EXEC dbo.FinishLegacyAdmissionSetup @UserId=1,@GoLiveDate=@D,@BillingStart=@M;'
 Assert ($taskPreview.Rows[0].BlockedCount -gt 0) 'Go Live blocks unverified legacy records'
 # Only the disposable copy is narrowed to one active legacy fixture for activation tests.
 [void](Query "UPDATE dbo.Students SET LegacySetupPending=0;UPDATE dbo.Students SET LegacySetupPending=1 WHERE AdmissionId=$taskChild1;")
 [void](Query "EXEC dbo.VerifyLegacyStudent @UserId=1,@AdmissionId=$taskChild1;")
 Assert ((Query "SELECT LegacyVerifiedAt FROM dbo.Students WHERE AdmissionId=$taskChild1").Rows[0].LegacyVerifiedAt -is [DateTime]) 'Legacy record can be explicitly verified'
 [void](Query "UPDATE dbo.Students SET FatherName=N'Corrected parent' WHERE AdmissionId=$taskChild1;")
 Assert ((Query "SELECT LegacyVerifiedAt FROM dbo.Students WHERE AdmissionId=$taskChild1").Rows[0].LegacyVerifiedAt -is [DBNull]) 'Editing a verified legacy record clears verification'
 [void](Query "EXEC dbo.VerifyLegacyStudent @UserId=1,@AdmissionId=$taskChild1;")
 [void](Query "UPDATE dbo.Enrollments SET Remarks=N'Placement reviewed' WHERE EnrollmentId=$taskE1;")
 Assert ((Query "SELECT LegacyVerifiedAt FROM dbo.Students WHERE AdmissionId=$taskChild1").Rows[0].LegacyVerifiedAt -is [DBNull]) 'Placement changes also clear legacy verification'
 [void](Query "EXEC dbo.VerifyLegacyStudent @UserId=1,@AdmissionId=$taskChild1;")
 $taskLivePlan=Query 'DECLARE @D date=CONVERT(date,GETDATE()),@M date=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1);EXEC dbo.FinishLegacyAdmissionSetup @UserId=1,@GoLiveDate=@D,@BillingStart=@M;'
 Assert ($taskLivePlan.Rows[0].BlockedCount -eq 0 -and $taskLivePlan.Rows[0].ActiveStudents -eq 1) 'Reviewed active legacy fixture is ready for activation'
 $taskSnapshot=[string]$taskLivePlan.Rows[0].Snapshot
 [void](Query "UPDATE dbo.Students SET Address=N'Changed after preview' WHERE AdmissionId=$taskChild1;EXEC dbo.VerifyLegacyStudent @UserId=1,@AdmissionId=$taskChild1;")
 Refused "DECLARE @D date=CONVERT(date,GETDATE()),@M date=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1);EXEC dbo.FinishLegacyAdmissionSetup @UserId=1,@GoLiveDate=@D,@BillingStart=@M,@Preview=0,@ExpectedSnapshot='$taskSnapshot';" 59743 'Go Live refuses stale review snapshots'
 $taskLivePlan=Query 'DECLARE @D date=CONVERT(date,GETDATE()),@M date=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1);EXEC dbo.FinishLegacyAdmissionSetup @UserId=1,@GoLiveDate=@D,@BillingStart=@M;'
 $taskSnapshot=[string]$taskLivePlan.Rows[0].Snapshot
 [void](Query "DECLARE @D date=CONVERT(date,GETDATE()),@M date=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1);EXEC dbo.FinishLegacyAdmissionSetup @UserId=1,@GoLiveDate=@D,@BillingStart=@M,@Preview=0,@ExpectedSnapshot='$taskSnapshot';")
 Assert ((Query "SELECT LegacySetupPending FROM dbo.Students WHERE AdmissionId=$taskChild1").Rows[0].LegacySetupPending -eq 0) 'Go Live completes legacy setup'
 Assert ((Query "SELECT COUNT(*) Total FROM dbo.BillingCharges WHERE EnrollmentId=$taskE1").Rows[0].Total -eq $taskCount) 'Go Live preserves existing paid bills without duplicate opening fees'
 Refused "DECLARE @D date=CONVERT(date,GETDATE()),@M date=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1);EXEC dbo.FinishLegacyAdmissionSetup @UserId=1,@GoLiveDate=@D,@BillingStart=@M;" 59741 'Go Live cannot run twice'
 Refused "UPDATE dbo.Enrollments SET AcademicYearId=(SELECT MIN(AcademicYearId) FROM dbo.AcademicYears WHERE AcademicYearId<>$taskYear) WHERE EnrollmentId=$taskE1;" 59640 'Operational placement locks academic year after Go Live'
 'All admission workflow checks passed. Live student records were not changed.'
}finally{
 if($script:taskConnection){$script:taskConnection.Dispose()}
 if($taskCreated){$script:taskConnection=Connect 'master';[void](Query "ALTER DATABASE [$taskName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;DROP DATABASE [$taskName];");$script:taskConnection.Dispose()}
 $taskResolved=[IO.Path]::GetFullPath($taskDir)
 if(-not $taskResolved.StartsWith([IO.Path]::GetFullPath((Join-Path $taskRoot 'Tests'))+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe cleanup path'}
 Remove-Item -LiteralPath $taskResolved -Recurse -Force
}
