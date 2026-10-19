param(
    [Parameter(Mandatory=$true)][string]$TestDatabase,
    [string]$SnapshotPath=(Join-Path $PSScriptRoot '../artifacts/fee-documents/bill-snapshot.xml')
)
$ErrorActionPreference='Stop'
if($TestDatabase -notmatch '^FMSC_Documents_Test_[A-Za-z0-9_]+$'){throw 'Use a disposable finance-document database.'}
function Read-BillTable([string]$Text) {
    $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null
    $reader=[Xml.XmlReader]::Create([IO.StringReader]::new($Text),$settings)
    try{$table=[Data.DataTable]::new();$null=$table.ReadXml($reader);return ,$table}finally{$reader.Dispose()}
}
$data=Read-BillTable ([IO.File]::ReadAllText((Resolve-Path -LiteralPath $SnapshotPath)))
$amount=[decimal]$data.Rows[0]['TotalPaid']
$connection=[Data.SqlClient.SqlConnection]::new("Server=(localdb)\MSSQLLocalDB;Database=$TestDatabase;Integrated Security=True;TrustServerCertificate=True")
$connection.Open();$transaction=$connection.BeginTransaction()
try {
    $fixture=$connection.CreateCommand();$fixture.Transaction=$transaction
    $fixture.CommandText=@'
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
DECLARE @New TABLE(ChargeId bigint,AdmissionId int);
INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,GrossAmount,DiscountAmount,Status,ChargeKind,ManualRequestId,Remarks)
 OUTPUT inserted.BillingChargeId,inserted.AdmissionId INTO @New
 SELECT TOP(1) A.StudentFeeAssignmentId,A.EnrollmentId,A.AdmissionId,A.FeeTypeId,NULL,CONVERT(date,GETDATE()),@Amount,0,N'Open',N'Manual',NEWID(),N'Disposable XML snapshot fixture'
 FROM dbo.StudentFeeAssignments A JOIN dbo.Enrollments E ON E.EnrollmentId=A.EnrollmentId
 WHERE E.Status=N'Active' AND E.BillingReady=1 AND A.CancelledAt IS NULL AND A.Frequency=N'Monthly'
 ORDER BY A.StudentFeeAssignmentId;
SELECT ChargeId,AdmissionId,(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator') UserId FROM @New;
'@
    $null=$fixture.Parameters.AddWithValue('@Amount',$amount)
    $adapter=[Data.SqlClient.SqlDataAdapter]::new($fixture);$keys=[Data.DataTable]::new();$null=$adapter.Fill($keys)
    if($keys.Rows.Count-ne1){throw 'No valid disposable placement was available.'}
    foreach($row in $data.Rows){$row['BillSourceKey']=[string]$keys.Rows[0]['ChargeId']}
    $writer=[IO.StringWriter]::new([Globalization.CultureInfo]::InvariantCulture)
    $data.WriteXml($writer,[Data.XmlWriteMode]::WriteSchema)
    $issue=$connection.CreateCommand();$issue.Transaction=$transaction;$issue.CommandType=[Data.CommandType]::StoredProcedure;$issue.CommandText='dbo.IssueFeeBill'
    $null=$issue.Parameters.AddWithValue('@UserId',[int]$keys.Rows[0]['UserId'])
    $null=$issue.Parameters.AddWithValue('@PrimaryAdmissionId',[int]$keys.Rows[0]['AdmissionId'])
    $null=$issue.Parameters.AddWithValue('@StudentIdsJson',('['+$keys.Rows[0]['AdmissionId']+']'))
    $xml=$issue.Parameters.Add('@SnapshotXml',[Data.SqlDbType]::Xml);$xml.Value=$writer.ToString()
    $first=[Data.DataTable]::new();$issueAdapter=[Data.SqlClient.SqlDataAdapter]::new($issue);$null=$issueAdapter.Fill($first)
    if($first.Rows.Count-ne1 -or $first.Rows[0]['BillNumber']-notmatch '^B-\d{4}-\d{5,}$'){throw 'The actual report did not receive a proper bill number.'}
    $restored=Read-BillTable ([string]$first.Rows[0]['SnapshotXml'])
    if($restored.Rows.Count-ne$data.Rows.Count -or ($restored.Rows|Measure-Object -Property Amount -Sum).Sum-ne$amount){throw 'SQL XML storage changed the actual report data.'}
    $again=[Data.DataTable]::new();$null=$issueAdapter.Fill($again)
    if($again.Rows[0]['BillNumber']-ne$first.Rows[0]['BillNumber'] -or $again.Rows[0]['IssuedAt']-ne$first.Rows[0]['IssuedAt']){throw 'An actual C# snapshot reprint created another bill.'}
    $transaction.Rollback()
    Write-Output 'PASS: actual C# report XML issues, round-trips through SQL and reprints with the same number and date; fixture rolled back.'
} finally {
    if($transaction.Connection){$transaction.Rollback()}
    $transaction.Dispose();$connection.Dispose();$data.Dispose()
}
