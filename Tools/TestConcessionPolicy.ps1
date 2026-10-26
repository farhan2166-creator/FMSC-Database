# Financial integration checks use an owned COPY_ONLY clone. School records are untouched.
param([string]$Instance='(localdb)\MSSQLLocalDB')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$owner=[guid]::NewGuid().ToString('N');$catalog='FMSC_ConcessionChecks_'+$owner
$run=Join-Path $root ('artifacts/concession-policy/'+$owner)
[void](New-Item -ItemType Directory -Path $run -Force)
$backup=Join-Path $run 'fixture.bak';$files=@($backup);$created=$false;$cn=$null
function Query([string]$sql){
    $command=$cn.CreateCommand();$command.CommandText=$sql;$command.CommandTimeout=120
    $data=New-Object System.Data.DataSet;$adapter=New-Object System.Data.SqlClient.SqlDataAdapter $command
    try{[void]$adapter.Fill($data);return ,$data}finally{$adapter.Dispose();$command.Dispose()}
}
function Check([bool]$condition,[string]$message){if(-not $condition){throw $message};Write-Output ('PASS: '+$message)}
function Concede([bool]$full,[bool]$agreement,[object]$bill,[decimal]$discount){
    $command=$cn.CreateCommand();$command.CommandType='StoredProcedure';$command.CommandText='dbo.SaveStudentFeeDiscount';$command.CommandTimeout=120
    $values=@{ '@UserId'=$script:actor;'@StudentFeeAssignmentId'=$script:assignment;'@DiscountAmount'=$discount;
        '@EffectiveFrom'=$script:from;'@EffectiveThrough'=$(if($null -eq $bill){$script:through}else{$script:from});
        '@ConcessionLabel'=$(if($full){'Free'}else{[DBNull]::Value});'@ApplyToBills'=$true;'@UpdateAgreement'=$agreement;
        '@FullFeeWaiver'=$full;'@BillingChargeId'=$(if($null -eq $bill){[DBNull]::Value}else{$bill});
        '@ApplyAvailableCredit'=$false;'@PaidReductionAction'='Credit';'@Reason'='Concession integration fixture';'@Preview'=$true }
    foreach($key in $values.Keys){[void]$command.Parameters.AddWithValue($key,$values[$key])}
    $preview=New-Object System.Data.DataSet;$adapter=New-Object System.Data.SqlClient.SqlDataAdapter $command
    try{
        [void]$adapter.Fill($preview)
        Check ([decimal]$preview.Tables[0].Rows[0]['ReleasedPaidAmount'] -eq 0) 'Preview releases no previously paid money.'
        $command.Parameters['@Preview'].Value=$false
        [void]$command.Parameters.AddWithValue('@ExpectedSnapshot',[string]$preview.Tables[0].Rows[0]['Snapshot'])
        $saved=New-Object System.Data.DataSet;[void]$adapter.Fill($saved)
        Check ([decimal]$saved.Tables[0].Rows[0]['ReleasedPaidAmount'] -eq 0) 'Saving releases no paid money to refund or advance.'
    }finally{$adapter.Dispose();$command.Dispose()}
}
try{
    $cn=New-Object System.Data.SqlClient.SqlConnection ('Data Source='+$Instance+';Initial Catalog=master;Integrated Security=True;TrustServerCertificate=True;Pooling=False')
    $cn.Open()
    [void](Query ("BACKUP DATABASE FMSC_DB TO DISK=N'"+$backup.Replace("'","''")+"' WITH COPY_ONLY,CHECKSUM,INIT;"))
    $fileList=Query ("RESTORE FILELISTONLY FROM DISK=N'"+$backup.Replace("'","''")+"';")
    $moves=@();$index=0
    foreach($file in $fileList.Tables[0].Rows){
        if($file.Type -notin @('D','L')){throw 'Unexpected backup file type.'}
        $path=Join-Path $run ('database-'+$index+$(if($file.Type -eq 'L'){'.ldf'}else{'.mdf'}));$files+=$path
        $moves+="MOVE N'$(([string]$file.LogicalName).Replace("'","''"))' TO N'$($path.Replace("'","''"))'";$index++
    }
    $created=$true
    [void](Query ("RESTORE DATABASE [$catalog] FROM DISK=N'"+$backup.Replace("'","''")+"' WITH "+($moves -join ',')+",CHECKSUM,RECOVERY;"))
    $cn.ChangeDatabase($catalog)
    $version=Query 'SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC;'
    if([int]$version.Tables[0].Rows[0][0] -eq 53){
        $tx=$cn.BeginTransaction()
        try{
            foreach($batch in [regex]::Split((Get-Content (Join-Path $root 'Migrations/54.sql') -Raw),'(?im)^GO\s*$')){
                if([string]::IsNullOrWhiteSpace($batch)){continue}
                $command=$cn.CreateCommand();$command.Transaction=$tx;$command.CommandText=$batch;$command.CommandTimeout=120
                try{[void]$command.ExecuteNonQuery()}finally{$command.Dispose()}
            };$tx.Commit()
        }catch{$tx.Rollback();throw}
    }elseif([int]$version.Tables[0].Rows[0][0] -notin @(54,55)){throw 'The fixture requires schema 53, 54 or 55.'}
    $setup=Query @'
DECLARE @Enrollment int,@Admission int,@From date,@Through date,@Actor int;
SELECT TOP(1) @Enrollment=E.EnrollmentId,@Admission=E.AdmissionId,
 @From=DATEADD(month,1,DATEFROMPARTS(YEAR(CASE WHEN E.BillingStartDate>E.EnrollmentDate THEN E.BillingStartDate ELSE E.EnrollmentDate END),MONTH(CASE WHEN E.BillingStartDate>E.EnrollmentDate THEN E.BillingStartDate ELSE E.EnrollmentDate END),1)),
 @Through=DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1)
FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId WHERE E.Status=N'Active' AND E.BillingStartDate IS NOT NULL AND E.BillingReady=1 AND E.PlacementConfirmedAt IS NOT NULL AND DATEDIFF(month,E.BillingStartDate,Y.EndDate)>=4 ORDER BY E.EnrollmentId DESC;
SELECT TOP(1) @Actor=U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator';
IF @Enrollment IS NULL OR @Actor IS NULL THROW 51000,'The clone has no eligible fixture enrollment or administrator.',1;
INSERT dbo.FeeTypes(FeeTypeCode,FeeTypeName,IsMandatory,IsTransportLinked) VALUES(CONCAT(N'CAP-',LEFT(CONVERT(nvarchar(36),NEWID()),8)),N'Concession fixture',0,0);
DECLARE @Fee int=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.StudentFeeAssignments(AdmissionId,EnrollmentId,FeeTypeId,Amount,DiscountAmount,Frequency,EffectiveFrom)
 VALUES(@Admission,@Enrollment,@Fee,500,0,N'Monthly',@From);
DECLARE @Assignment int=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
 SELECT @Assignment,@Enrollment,@Admission,@Fee,DATEADD(month,N,@From),DATEADD(month,N,@From),DATEADD(month,N,@From),500,0,N'Monthly' FROM (VALUES(0),(1),(2)) X(N);
DECLARE @Partial bigint=(SELECT BillingChargeId FROM dbo.BillingCharges WHERE StudentFeeAssignmentId=@Assignment AND BillingMonth=@From),
 @Paid bigint=(SELECT BillingChargeId FROM dbo.BillingCharges WHERE StudentFeeAssignmentId=@Assignment AND BillingMonth=DATEADD(month,2,@From));
INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Status,CreatedByUserId)
 VALUES(@Admission,@Enrollment,CONVERT(date,GETDATE()),CONCAT(N'CAP-',NEWID()),300,N'Cash',N'Completed',@Actor);
INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(CONVERT(int,SCOPE_IDENTITY()),@Admission,@Partial,300);
INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Status,CreatedByUserId)
 VALUES(@Admission,@Enrollment,CONVERT(date,GETDATE()),CONCAT(N'CAP-',NEWID()),500,N'Cash',N'Completed',@Actor);
INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(CONVERT(int,SCOPE_IDENTITY()),@Admission,@Paid,500);
SELECT @Assignment AssignmentId,@Admission AdmissionId,@From FromMonth,@Through ThroughMonth,@Actor ActorId,@Partial PartialCharge,
 (SELECT COUNT(*) FROM dbo.FeePayments) PaymentCount,(SELECT COUNT(*) FROM dbo.StudentCreditEntries) CreditCount;
'@
    $row=$setup.Tables[0].Rows[0];$script:assignment=[int]$row.AssignmentId;$script:from=[datetime]$row.FromMonth;$script:through=[datetime]$row.ThroughMonth;$script:actor=[int]$row.ActorId
    Concede $true $false ([long]$row.PartialCharge) 0
    $check=Query "SELECT Amount,DiscountAmount FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=$assignment; SELECT GrossAmount,DiscountAmount,NetAmount,dbo.GetFeeChargeBalance(BillingChargeId) Due,Status FROM dbo.BillingCharges WHERE BillingChargeId=$($row.PartialCharge);"
    Check ([decimal]$check.Tables[0].Rows[0].DiscountAmount -eq 0 -and [decimal]$check.Tables[1].Rows[0].DiscountAmount -eq 200 -and [decimal]$check.Tables[1].Rows[0].Due -eq 0 -and $check.Tables[1].Rows[0].Status -ne 'Cancelled') 'Waive once discounts the remaining Rs. 200, keeps the charge and its paid Rs. 300, and leaves the plan unchanged.'
    Concede $false $true $null 300
    $bills=Query "SELECT GrossAmount,DiscountAmount,NetAmount,dbo.GetFeeChargeBalance(BillingChargeId) Due FROM dbo.BillingCharges WHERE StudentFeeAssignmentId=$assignment ORDER BY BillingMonth; SELECT Amount,DiscountAmount FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=$assignment;"
    Check ([decimal]$bills.Tables[0].Rows[0].DiscountAmount -eq 200 -and [decimal]$bills.Tables[0].Rows[1].DiscountAmount -eq 300 -and [decimal]$bills.Tables[0].Rows[1].Due -eq 200 -and [decimal]$bills.Tables[0].Rows[2].DiscountAmount -eq 0 -and [decimal]$bills.Tables[1].Rows[0].DiscountAmount -eq 300) 'The requested Rs. 300 discount is capped at old unpaid dues, applied to other months, and saved for upcoming months; a fully paid month is unchanged.'
    Concede $true $true $null 0
    $final=Query "SELECT Amount,DiscountAmount,ConcessionLabel FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=$assignment; SELECT DiscountAmount,dbo.GetFeeChargeBalance(BillingChargeId) Due FROM dbo.BillingCharges WHERE StudentFeeAssignmentId=$assignment ORDER BY BillingMonth; SELECT (SELECT COUNT(*) FROM dbo.FeePayments) PaymentCount,(SELECT COUNT(*) FROM dbo.StudentCreditEntries) CreditCount;"
    Check ([decimal]$final.Tables[0].Rows[0].Amount -eq 500 -and [decimal]$final.Tables[0].Rows[0].DiscountAmount -eq 500 -and $final.Tables[0].Rows[0].ConcessionLabel -eq 'Free' -and [decimal]$final.Tables[1].Rows[0].DiscountAmount -eq 200 -and [decimal]$final.Tables[1].Rows[1].DiscountAmount -eq 500 -and [decimal]$final.Tables[1].Rows[2].DiscountAmount -eq 0) 'Sibling concession fully discounts the plan and unpaid month while preserving paid portions and the original Rs. 500 fee.'
    Check ([int]$final.Tables[2].Rows[0].PaymentCount -eq [int]$row.PaymentCount -and [int]$final.Tables[2].Rows[0].CreditCount -eq [int]$row.CreditCount) 'No refund, advance credit, replacement payment or extra receipt was created.'
    $source=Get-Content (Join-Path $root 'Core/Reports/Fees/FeeBillPreview.cs') -Raw
    $billQuery=[regex]::Match($source,'(?s)private const string ChargesSql = """\s*(.*?)\s*""";').Groups[1].Value
    if(-not $billQuery){throw 'Production bill query was not found.'}
    $command=$cn.CreateCommand();$command.CommandText=$billQuery
    [void]$command.Parameters.AddWithValue('@UserId',$script:actor)
    [void]$command.Parameters.AddWithValue('@Admission',[int]$row.AdmissionId)
    [void]$command.Parameters.AddWithValue('@Ids',('['+$row.AdmissionId+']'))
    [void]$command.Parameters.AddWithValue('@WholeFamily',$false)
    $bill=New-Object System.Data.DataTable;$adapter=New-Object System.Data.SqlClient.SqlDataAdapter $command
    try{[void]$adapter.Fill($bill)}finally{$adapter.Dispose();$command.Dispose()}
    $fixtureLines=@($bill.Rows | Where-Object {([string]$_['FeeTypeName']).StartsWith('Concession fixture') -and [long]$_['BillingChargeId'] -gt 0})
    Check ($fixtureLines.Count -eq 2 -and @($fixtureLines|Where-Object {[decimal]$_['GrossAmount'] -ne 500 -or [decimal]$_['Due'] -ne 0}).Count -eq 0) 'The normal bill query retains the waived charges with full fee, discount and zero due; a fully paid charge is omitted.'
}finally{
    if($cn){
        if($created -and $catalog -eq ('FMSC_ConcessionChecks_'+$owner) -and $owner -match '^[0-9a-f]{32}$'){
            try{$cn.ChangeDatabase('master');[void](Query "IF DB_ID(N'$catalog') IS NOT NULL BEGIN ALTER DATABASE [$catalog] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$catalog]; END;")}catch{Write-Warning 'Owned fixture cleanup needs review.'}
        };$cn.Dispose()
    }
    foreach($file in $files){
        $absolute=[IO.Path]::GetFullPath($file)
        if(-not $absolute.StartsWith([IO.Path]::GetFullPath($run)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Refusing cleanup outside the owned fixture directory.'}
        Remove-Item -LiteralPath $absolute -Force -ErrorAction SilentlyContinue
    }
}
