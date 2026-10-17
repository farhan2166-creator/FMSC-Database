param(
    [string]$DataFile = 'C:\ProgramData\FMSC_Database\FMSC_DB.mdf',
    [string]$LogFile = 'C:\ProgramData\FMSC_Database\FMSC_DB_log.ldf',
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$taskHandles = @()
try {
    $taskMaster = Join-Path $env:LOCALAPPDATA 'Microsoft\Microsoft SQL Server Local DB\Instances\MSSQLLocalDB\master.mdf'
    $taskProcesses = @(Get-CimInstance Win32_Process -Filter "Name = 'sqlservr.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($taskMaster, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
    if ($taskProcesses.Count -gt 0) {
        throw ('The MSSQLLocalDB SQL Server process is still running (PID ' + ($taskProcesses.ProcessId -join ', ') + ').')
    }
    foreach ($taskFile in @($DataFile, $LogFile)) {
        # Read-only exclusive handles prove both files are currently released.
        # Keep both handles until verification is complete; never write to either.
        $taskHandles += [IO.File]::Open([IO.Path]::GetFullPath($taskFile), [IO.FileMode]::Open,
            [IO.FileAccess]::Read, [IO.FileShare]::None)
    }
    if (-not $Quiet) { Write-Host 'PASS: SQL process exited and both database files can be opened exclusively.' }
    exit 0
} catch {
    if (-not $Quiet) { Write-Host ('NOT READY: ' + $_.Exception.Message) }
    exit 1
} finally {
    foreach ($taskHandle in $taskHandles) { $taskHandle.Dispose() }
}
