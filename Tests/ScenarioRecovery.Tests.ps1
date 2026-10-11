# Run after dotnet build. Exercises compiled C# without connecting to any database.
param([string]$Snapshot = "$PSScriptRoot/../bin/ScenarioAudit_20261011/numbering-fixture.json")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFrom("$root/bin/Debug/net8.0-windows/FMSC_SMS.dll")
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$instance = [Reflection.BindingFlags]'Instance,NonPublic'
$journal = $assembly.GetType('FMSC_SMS.Pages.Finance.Collection.CollectionRecoveryJournal', $true)
function Invoke-Journal($name, [object[]]$arguments) {
    $plain = [object[]]@($arguments | ForEach-Object { $_.PSObject.BaseObject })
    $journal.GetMethod($name, $flags).Invoke($null, $plain)
}
$count = 0
function Assert($condition, $message) {
    if (!$condition) { throw "FAILED: $message" }
    $script:count++
    Write-Output "PASS: $message"
}
function Assert-Rejected([scriptblock]$action, $message) {
    $rejected = $false
    try { & $action | Out-Null } catch { $rejected = $true }
    Assert $rejected $message
}
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('FMSC-RecoveryTest-' + [Guid]::NewGuid())
$path = Join-Path $testDirectory 'Pending.json'
try {
    Assert ($null -eq (Invoke-Journal ReadPending @($path))) 'No pending intent initially'
    $active = Invoke-Journal Begin @('Single/family collection test', $path)
    Assert (Test-Path $path) 'Intent exists before financial work'
    Assert-Rejected { Invoke-Journal ReadPending @($path) } 'Active writer cannot be reviewed'
    Assert-Rejected { Invoke-Journal Begin @('second', $path) } 'Concurrent writer blocked'
    $active.Dispose()
    $pending = Invoke-Journal ReadPending @($path)
    Assert ($pending.Summary -eq 'Single/family collection test') 'Interrupted intent survives disposal/restart'
    Assert-Rejected { Invoke-Journal Begin @('retry', $path) } 'Blind retry blocked after interruption'
    Assert-Rejected { Invoke-Journal ClearReviewed @([Guid]::NewGuid(), $path) } 'Wrong recovery identity cannot clear intent'
    Invoke-Journal ClearReviewed @($pending.Id, $path)
    Assert (!(Test-Path $path)) 'Reviewed intent can be cleared'
    $confirmed = Invoke-Journal Begin @('confirmed commit or rollback', $path)
    $journal.GetMethod('Complete', $instance).Invoke($confirmed, @()) | Out-Null
    $confirmed.Dispose()
    Assert (!(Test-Path $path)) 'Confirmed outcome clears intent'
    [IO.File]::WriteAllText($path, '{broken')
    Assert-Rejected { Invoke-Journal ReadPending @($path) } 'Damaged recovery file blocks collection'
} finally {
    if ($active) { $active.Dispose() }
    if ($confirmed) { $confirmed.Dispose() }
    if (Test-Path $path) { Remove-Item -LiteralPath $path }
    if (Test-Path $testDirectory) { Remove-Item -LiteralPath $testDirectory }
}
# Use the saved, read-only real-data snapshot; this test never opens a SQL connection.
$rows = ((Get-Content -Raw $Snapshot) -replace '[\r\n]', '') | ConvertFrom-Json
Assert ($rows.Count -eq 6592) 'Full 6,592-student snapshot loaded'
$table = [Data.DataTable]::new()
[void]$table.Columns.Add('AdmissionDate', [datetime])
[void]$table.Columns.Add('AdmissionNumber', [string])
foreach ($row in $rows) {
    $date = if ($row.AdmissionDate) { [datetime]$row.AdmissionDate } else { [DBNull]::Value }
    [void]$table.Rows.Add($date, $row.AdmissionNumber)
}
$policy = $assembly.GetType('FMSC_SMS.Core.AdmissionNumberPolicy', $true)
$calculate = $policy.GetMethod('Calculate', $flags)
function Plan($date, $start, $end, $historical) {
    $calculate.Invoke($null, @($table, [datetime]$date, [datetime]$start, [datetime]$end, [bool]$historical))
}
function Validate($plan, $number) { $plan.GetType().GetMethod('Validate', $instance).Invoke($plan, @($number)) }
$normal = Plan '2005-04-15' '2005-04-01' '2006-03-31' $false
Assert ($null -ne $normal.Error) 'Ordinary mode retains legacy-order conflict guard'
$historical = Plan '2005-04-15' '2005-04-01' '2006-03-31' $true
Assert ($null -eq $historical.Error) 'Verified historical mode accepts legacy-order conflict'
Assert ($null -eq (Validate $historical '1030b')) 'Unused verified historical register number accepted'
Assert ($null -ne (Validate $historical '1030A')) 'Normalized historical duplicate rejected'
Assert ($null -ne (Validate $historical 'invalid')) 'Malformed historical number rejected'
Assert ($null -ne (Plan '2004-04-15' '2005-04-01' '2006-03-31' $true).Error) 'Historical date outside academic year rejected'
$tomorrow = [datetime]::Today.AddDays(1)
Assert ($null -ne (Plan $tomorrow $tomorrow.AddDays(-1) $tomorrow.AddYears(1) $true).Error) 'Future historical date rejected'
$current = Plan ([datetime]::Today) ([datetime]::Today.AddMonths(-6)) ([datetime]::Today.AddMonths(6)) $false
Assert ($current.Suggested -eq '6591') 'Ordinary current admission still uses next number'
Write-Output "$count regression checks passed. No database connections or writes."

