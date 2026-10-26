# Execute the production latest-receipt selection against session-local fixtures.
param([string]$Instance='(localdb)\MSSQLLocalDB')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$source=Get-Content (Join-Path $root 'Core/Reports/Fees/FeeReceiptQueries.cs') -Raw
$query=[regex]::Match($source,'(?s)private const string BalanceStateSql = """\s*(.*?)\s*""";').Groups[1].Value
if(-not $query){throw 'Production balance policy SQL not found.'}
foreach($table in @('CollectionReceiptPayments','CollectionReceipts','FeePayments')){$query=$query.Replace('dbo.'+$table,'#'+$table)}
$query=$query.Replace('dbo.RequirePermission','#RequirePermission')
$cn=New-Object System.Data.SqlClient.SqlConnection ('Data Source='+$Instance+';Initial Catalog=master;Integrated Security=True;TrustServerCertificate=True')
function Execute([string]$sql){$command=$cn.CreateCommand();try{$command.CommandText=$sql;[void]$command.ExecuteNonQuery()}finally{$command.Dispose()}}
function State([string]$number){
    $command=$cn.CreateCommand()
    try{
        $command.CommandText=$query
        [void]$command.Parameters.AddWithValue('@ReceiptNumber',$number)
        [void]$command.Parameters.AddWithValue('@UserId',1)
        [void]$command.Parameters.AddWithValue('@ReadPermission','Fees.View')
        $data=New-Object System.Data.DataTable
        $adapter=New-Object System.Data.SqlClient.SqlDataAdapter $command
        try{[void]$adapter.Fill($data)}finally{$adapter.Dispose()}
        return ,$data
    }finally{$command.Dispose()}
}
function Check([bool]$condition,[string]$message){if(-not $condition){throw $message};Write-Output ('PASS: '+$message)}
try{
    $cn.Open()
    Execute @'
CREATE TABLE #CollectionReceipts(CollectionReceiptId uniqueidentifier,ReceiptNumber nvarchar(40),CreatedAt datetime2(7),SnapshotJson nvarchar(max));
CREATE TABLE #CollectionReceiptPayments(CollectionReceiptId uniqueidentifier,FeePaymentId int);
CREATE TABLE #FeePayments(FeePaymentId int,AdmissionId int,CreatedAt datetime2(7),RelatedFeePaymentId int NULL,ReceiptNumber nvarchar(40),PaymentDate date);
INSERT #CollectionReceipts VALUES
 ('00000000-0000-0000-0000-000000000001',N'FAMILY-OLD','2026-10-01T10:00:00',N'{"Students":[{"AdmissionId":1,"BalanceRemaining":4000},{"AdmissionId":2,"BalanceRemaining":5000}]}'),
 ('00000000-0000-0000-0000-000000000002',N'NEW','2026-10-02T10:00:00',N'{"Students":[{"AdmissionId":2,"BalanceRemaining":300}]}');
INSERT #FeePayments VALUES
 (10,1,'2026-10-01T09:59:59',NULL,N'ALIAS-OLD','2026-10-01'),
 (20,2,'2026-10-02T09:59:59',NULL,N'NEW','2026-10-02'),
 (30,3,'2026-10-03T09:59:59',NULL,N'UNRELATED','2026-10-03');
INSERT #CollectionReceiptPayments VALUES('00000000-0000-0000-0000-000000000001',10),('00000000-0000-0000-0000-000000000002',20);
'@
    Execute 'CREATE PROCEDURE #RequirePermission @UserId int,@Permission nvarchar(100) AS RETURN;'
    $old=State 'FAMILY-OLD';$alias=State 'ALIAS-OLD';$latest=State 'NEW'
    Check (-not [bool]$old.Rows[0]['IsLatest'] -and $old.Rows.Count -eq 2 -and ($old.Rows|Measure-Object RecordedBalance -Sum).Sum -eq 9000) 'A newer receipt for an unpaid child makes the entire earlier family receipt historical and keeps both snapshots.'
    Check (-not [bool]$alias.Rows[0]['IsLatest'] -and $alias.Rows.Count -eq 2) 'A linked old payment number resolves to the same family snapshot.'
    Check ([bool]$latest.Rows[0]['IsLatest']) 'An unrelated student payment does not make this student receipt historical.'
    Execute "INSERT #FeePayments VALUES(21,2,'2026-10-02T10:00:00',NULL,N'BACKDATED','2025-01-01');"
    $latest=State 'NEW';$backdated=State 'BACKDATED'
    Check (-not [bool]$latest.Rows[0]['IsLatest'] -and [bool]$backdated.Rows[0]['IsLatest']) 'Issue order and payment IDs handle a newer backdated receipt, including identical timestamps.'
    Execute @'
INSERT #CollectionReceipts VALUES('00000000-0000-0000-0000-000000000003',N'WAIVER','2026-10-04T10:00:00',N'{"Students":[{"AdmissionId":2,"BalanceRemaining":0}]}');
'@
    $backdated=State 'BACKDATED';$waiver=State 'WAIVER'
    Check (-not [bool]$backdated.Rows[0]['IsLatest'] -and [bool]$waiver.Rows[0]['IsLatest']) 'A later collection with no cash payment still makes the earlier legacy receipt historical.'
    Execute "INSERT #FeePayments VALUES(100,2,'2026-10-05T10:00:00',21,N'REFUND','2026-10-05');"
    $waiver=State 'WAIVER'
    Check ([bool]$waiver.Rows[0]['IsLatest']) 'A linked refund remains an adjustment rather than a new original receipt.'
}finally{$cn.Dispose()}
