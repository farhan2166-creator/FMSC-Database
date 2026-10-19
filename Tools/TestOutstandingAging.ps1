param(
    [Parameter(Mandatory=$true)][string]$Server,
    [string]$Database='FMSC_DB',
    [ValidateSet(25,26)][int]$MigrationVersion=26,
    [switch]$ApplyMigration
)
$ErrorActionPreference='Stop'
$connection=[Data.SqlClient.SqlConnection]::new("Server=$Server;Database=$Database;Integrated Security=True;TrustServerCertificate=True")
$connection.Open()
$transaction=$connection.BeginTransaction()
$script:checks=0
function Read-Data([string]$Sql) {
    $command=$connection.CreateCommand();$command.Transaction=$transaction;$command.CommandText=$Sql
    $adapter=[Data.SqlClient.SqlDataAdapter]::new($command);$data=[Data.DataSet]::new()
    try {$null=$adapter.Fill($data);return ,$data} finally {$adapter.Dispose();$command.Dispose()}
}
function Check([bool]$Condition,[string]$Message) {
    if(!$Condition){throw $Message};$script:checks++
}
function Fingerprint {
    $parts=foreach($table in @('Students','Enrollments','StudentFeeAssignments','BillingCharges','FeePayments','FeePaymentAllocations','StudentCreditEntries','CollectionReceipts','CollectionReceiptPayments','IssuedFeeBills')) {
        $result=Read-Data "SELECT (SELECT * FROM dbo.[$table] ORDER BY 1 FOR JSON PATH,INCLUDE_NULL_VALUES) Payload;"
        $sha=[Security.Cryptography.SHA256]::Create()
        try {$table+':'+[Convert]::ToHexString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([string]$result.Tables[0].Rows[0]['Payload'])))}
        finally {$sha.Dispose();$result.Dispose()}
    }
    return $parts -join '|'
}
try {
    $before=Fingerprint
    $migration=[IO.File]::ReadAllText((Join-Path $PSScriptRoot "../Migrations/$MigrationVersion.sql"))
    foreach($batch in [regex]::Split($migration,'(?im)^\s*GO\s*$')) {
        if([string]::IsNullOrWhiteSpace($batch)){continue}
        $command=$connection.CreateCommand();$command.Transaction=$transaction;$command.CommandText=$batch
        try {$null=$command.ExecuteNonQuery()} finally {$command.Dispose()}
    }
    $actor=Read-Data "SELECT TOP(1) U.UserId,CONVERT(varchar(10),GETDATE(),23) Today FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId;"
    $user=[int]$actor.Tables[0].Rows[0]['UserId'];$today=[datetime]$actor.Tables[0].Rows[0]['Today']
    $baseline=Read-Data @'
SELECT COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) Outstanding,
 COUNT(DISTINCT AdmissionId) Students FROM dbo.BillingCharges
WHERE Status<>N'Cancelled' AND dbo.GetFeeChargeBalance(BillingChargeId)>0;
'@
    foreach($offset in @(-1,0,1,31,61,91)) {
        $asOf=$today.AddDays($offset).ToString('yyyy-MM-dd')
        $students=Read-Data "EXEC dbo.GetOutstandingAndAgingReport @UserId=$user,@View=N'Students',@AsOfDate='$asOf';"
        $charges=Read-Data "EXEC dbo.GetOutstandingAndAgingReport @UserId=$user,@View=N'Charges',@AsOfDate='$asOf';"
        $aging=Read-Data "EXEC dbo.GetOutstandingAndAgingReport @UserId=$user,@View=N'Aging',@AsOfDate='$asOf';"
        foreach($result in @($students,$charges)) {
            Check ($result.Tables[0].Columns.Contains('Adm. / QR No.')) 'Student references must include admission/QR numbers'
            Check ($result.Tables[0].Columns.Contains('Class / Section')) 'Class and section must be combined'
            foreach($row in $result.Tables[0].Rows) {
                Check (![string]::IsNullOrWhiteSpace([string]$row['Adm. / QR No.'])) 'An owing student has a blank reference'
                Check ([decimal]$row['Outstanding'] -gt 0) 'Fully settled charges must be excluded'
            }
        }
        $sum=[decimal](($students.Tables[0].Rows | Measure-Object -Property Outstanding -Sum).Sum)
        Check ($sum -eq [decimal]$students.Tables[1].Rows[0]['Total Outstanding']) 'Student balances must match authoritative summary'
        Check ($sum -eq [decimal]$charges.Tables[1].Rows[0]['Total Outstanding']) 'Charge and student totals must reconcile'
        Check ($sum -eq [decimal]$aging.Tables[1].Rows[0]['Total Outstanding']) 'Aging and student totals must reconcile'
        Check ($sum -eq [decimal]$baseline.Tables[0].Rows[0]['Outstanding']) 'Report must match independent balances from all open charges'
        Check ($students.Tables[1].Rows[0]['Students Owing'] -eq $baseline.Tables[0].Rows[0]['Students']) 'Student count must match independent owing students'
        foreach($row in $students.Tables[0].Rows) {
            Check ([decimal]$row['Open Charges (Net)']-[decimal]$row['Settled on Open Charges'] -eq [decimal]$row['Outstanding']) 'Open-charge net less settlement must equal outstanding'
            $days=([datetime]$asOf-[datetime]$row['Oldest Due Date']).Days
            $expected=if($days-lt0){'Not Yet Due'}elseif($days-eq0){'Due Today'}elseif($days-le30){'1-30 days'}elseif($days-le60){'31-60 days'}elseif($days-le90){'61-90 days'}else{'90+ days'}
            Check ($row['Aging']-eq$expected) 'Student aging must distinguish future, today and overdue'
        }
        $students.Dispose();$charges.Dispose();$aging.Dispose()
    }
    $scopes=Read-Data @'
SELECT DISTINCT CCS.CampusId,E.AcademicYearId,CCS.ClassId,CCS.SectionId
FROM dbo.BillingCharges B JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId
JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
WHERE B.Status<>N'Cancelled' AND dbo.GetFeeChargeBalance(B.BillingChargeId)>0;
'@
    foreach($scope in $scopes.Tables[0].Rows) {
        $campus=[int]$scope['CampusId'];$year=[int]$scope['AcademicYearId'];$class=[int]$scope['ClassId'];$section=[int]$scope['SectionId']
        $filtered=Read-Data "EXEC dbo.GetOutstandingAndAgingReport @UserId=$user,@View=N'Students',@CampusId=$campus,@AcademicYearId=$year,@ClassId=$class,@SectionId=$section;"
        $expected=Read-Data "SELECT SUM(dbo.GetFeeChargeBalance(B.BillingChargeId)) Due FROM dbo.BillingCharges B JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId JOIN dbo.CampusClassSections C ON C.ClassSectionId=E.ClassSectionId WHERE B.Status<>N'Cancelled' AND dbo.GetFeeChargeBalance(B.BillingChargeId)>0 AND C.CampusId=$campus AND E.AcademicYearId=$year AND C.ClassId=$class AND C.SectionId=$section;"
        Check ([decimal]$filtered.Tables[1].Rows[0]['Total Outstanding']-eq[decimal]$expected.Tables[0].Rows[0]['Due']) 'Location/year filters must match independent scoped balances'
        $filtered.Dispose();$expected.Dispose()
    }
    if($MigrationVersion-eq26){. (Join-Path $PSScriptRoot 'CheckCollectionsReport.ps1')}
    Check ((Fingerprint)-eq$before) 'Migration/report queries changed business or financial records'
    $version=Read-Data 'SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC;'
    Check ($version.Tables[0].Rows[0][0]-eq[string]$MigrationVersion) 'Schema version did not advance to the migration version'
    if($ApplyMigration){$transaction.Commit();$outcome='committed'}else{$transaction.Rollback();$outcome='rolled back'}
    Write-Output "PASS: $script:checks finance report checks; migration $outcome; business and financial records unchanged."
} finally {
    if($transaction.Connection){$transaction.Rollback()}
    $transaction.Dispose();$connection.Dispose()
}
