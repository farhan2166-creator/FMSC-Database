# Offline fixtures only: extract validation, never execute the installer entry point.
$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Installer/core.ps1') -Raw
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Installer does not parse.' }
$validator = $ast.Find({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Test-PreparedUpdate' }, $true)
Invoke-Expression $validator.Extent.Text
$waiter = $ast.Find({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Wait-OriginalFmscExit' }, $true)
Invoke-Expression $waiter.Extent.Text
# Use this test process as a still-running process. A short timeout must refuse
# replacement, not terminate the process. Wrong start time and user also fail.
$current = Get-Process -Id $PID
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
foreach ($case in @(
    @{ ProcessId = $PID; StartTicks = $current.StartTime.ToUniversalTime().Ticks; UserSid = $sid; TimeoutMilliseconds = 5 },
    @{ ProcessId = $PID; StartTicks = 1; UserSid = $sid; TimeoutMilliseconds = 5 },
    @{ ProcessId = $PID; StartTicks = $current.StartTime.ToUniversalTime().Ticks; UserSid = 'S-1-0-0'; TimeoutMilliseconds = 5 }
)) {
    $refused = $false
    try { Wait-OriginalFmscExit @case } catch { $refused = $true }
    if (-not $refused) { throw 'Unsafe original-process identity or running process accepted.' }
}
$child = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe') `
    -ArgumentList @('-NoProfile', '-Command', 'Start-Sleep -Milliseconds 300') -WindowStyle Hidden -PassThru
try {
    $started = $child.StartTime.ToUniversalTime().Ticks
    Wait-OriginalFmscExit -ProcessId $child.Id -StartTicks $started -UserSid $sid -TimeoutMilliseconds 10000
    if (-not $child.HasExited) { throw 'Original process exit was not confirmed.' }
} finally { $child.Dispose() }
$root = Join-Path ([IO.Path]::GetTempPath()) ('fmsc-prepared-fixture-' + [Guid]::NewGuid().ToString('N'))
try {
    $app = Join-Path $root 'application'
    New-Item -ItemType Directory -Path (Join-Path $app 'UpdateSupport'), (Join-Path $root 'metadata') | Out-Null
    Set-Content -LiteralPath (Join-Path $app 'FMSC_SMS.exe') -Value 'inert fixture'
    Set-Content -LiteralPath (Join-Path $app 'UpdateSupport/core.ps1') -Value '# inert fixture'
    $files = @('FMSC_SMS.exe', 'UpdateSupport/core.ps1') | ForEach-Object {
        $path = Join-Path $app $_
        @{ path = $_; length = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    }
    $manifest = @{ format = 'fmsc-app-update/v1'; version = '2.0.0'; requiredDatabaseSchema = 123; files = @($files) }
    $metadata = Join-Path $root 'metadata/update-manifest.json'
    function Save-Manifest { $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $metadata }
    function Reject($Reason) {
        $failed = $false
        try { Test-PreparedUpdate $root '2.0.0' 123 } catch { $failed = $true }
        if (-not $failed) { throw "Unsafe update accepted: $Reason" }
    }
    Save-Manifest
    Test-PreparedUpdate $root '2.0.0' 123
    $manifest.version = '1.0.0'; Save-Manifest; Reject 'installer version mismatch'
    $manifest.version = '2.0.0'; $manifest.requiredDatabaseSchema = 122; Save-Manifest; Reject 'schema mismatch'
    $manifest.requiredDatabaseSchema = 123; Save-Manifest
    Set-Content -LiteralPath (Join-Path $app 'unexpected.txt') -Value 'extra'
    Reject 'unlisted file'; Remove-Item -LiteralPath (Join-Path $app 'unexpected.txt')
    $files[0].sha256 = '0' * 64; Save-Manifest; Reject 'incorrect digest'
    $files[0].sha256 = (Get-FileHash -LiteralPath (Join-Path $app 'FMSC_SMS.exe')).Hash
    $files[0].path = '../escape'; Save-Manifest; Reject 'path traversal'
    $files[0].path = 'school.mdf'; Save-Manifest; Reject 'database payload'
    if ($source.IndexOf('Wait-OriginalFmscExit -ProcessId') -gt $source.IndexOf("    `$script:InstallerMutex = New-Object")) { throw 'Original process exit must precede installer activation and SQL.' }
    Write-Output 'PASS: prepared installer version/schema, inventory, digest, traversal and database-payload refusal.'
}
finally {
    if (-not [IO.Path]::GetFullPath($root).StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Fixture cleanup escaped temporary storage.'
    }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
& (Join-Path $PSScriptRoot 'TestUpdateAutomation.ps1')
