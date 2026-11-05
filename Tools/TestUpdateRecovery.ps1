param([string]$ChildRoot = '', [string]$Checkpoint = '')
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$helper = Get-Content (Join-Path $project 'Installer/UpdateRecovery.ps1') -Raw
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$ownerSid = 'S-1-5-32-544'
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    # A restricted local test token cannot assign Administrators as owner. Keep
    # the same DACL/ACL logic with a fixture owner; hosted elevated acceptance
    # runs the exact production Administrators/SYSTEM ownership unchanged.
    $ownerSid = $identity.User.Value
    $helper = $helper.Replace("'S-1-5-32-544'", "'$ownerSid'")
    if (-not $ChildRoot) { Write-Output 'LOCAL FIXTURE: current-user owner substituted; actual Administrators ownership is checked on the elevated Windows CI runner.' }
}
Invoke-Expression $helper
function Get-FmscDownloadParent { return Join-Path $script:fixture 'downloads/FMSC/Updates' }
$tokens = $null; $errors = $null
$source = Get-Content (Join-Path $project 'Installer/core.ps1') -Raw
$ast = [Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
if ($errors.Count) { throw 'Installer does not parse.' }
$version = [regex]::Match($source, '(?m)^\$AppVersion = ''([^'']+)''').Groups[1].Value
$schema = [int][regex]::Match($source, '(?m)^\$DatabaseVersion = ''([0-9]+)''').Groups[1].Value
foreach ($name in @('Enable-StagedApplication','Remove-VerifiedUpdateDirectory','Copy-ApplicationFiles','Get-ApplicationFileManifest','Test-StagedApplication','Test-PreparedUpdate')) {
    $definition = $ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    Invoke-Expression $definition.Extent.Text
}
function Write-Log { param($Message) }
function Write-Step { param($Message) }
function Write-WarningMessage { param($Message) }
function Stop-AtCheckpoint {
    Set-Content -LiteralPath (Join-Path $script:fixture 'checkpoint') -Value $Checkpoint
    while ($true) { Start-Sleep -Milliseconds 200 }
}
if ($ChildRoot) {
    $script:fixture = [IO.Path]::GetFullPath($ChildRoot)
    if (-not $script:fixture.StartsWith([IO.Path]::GetTempPath(),[StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $script:fixture) -notmatch '^fmsc-recovery-[a-f0-9]{32}$') { throw 'Invalid child fixture path.' }
    $script:RecoveryRoot = Join-Path $script:fixture 'protected'
    $script:RecoveryJournal = Read-FmscRecoveryJournal $script:RecoveryRoot
    $app = Join-Path $script:fixture 'app'; $stage = "$app.staging"
    function Move-Item {
        [CmdletBinding()]param([string]$LiteralPath,[string]$Destination,[switch]$Force)
        if ($Checkpoint -eq 'BeforeOldMove' -and $LiteralPath -eq $app) { Stop-AtCheckpoint }
        Microsoft.PowerShell.Management\Move-Item -LiteralPath $LiteralPath -Destination $Destination -Force:$Force
        if (($Checkpoint -eq 'AfterOldMove' -and $LiteralPath -eq $app) -or
            ($Checkpoint -eq 'AfterNewMove' -and $LiteralPath -eq $stage)) { Stop-AtCheckpoint }
    }
    if ($Checkpoint -eq 'DuringCleanup') {
        Complete-FmscRecoveryTransaction
        $snapshot = Join-Path $script:RecoveryRoot ('transactions/' + $script:RecoveryJournal.Transaction)
        Remove-Item -LiteralPath (Join-Path $snapshot 'application/FMSC_SMS.exe') -Force
        Stop-AtCheckpoint
    }
    Enable-StagedApplication $stage $app "$app.rollback"
    throw 'Child did not reach its interruption checkpoint.'
}
$script:fixture = Join-Path ([IO.Path]::GetTempPath()) ('fmsc-recovery-' + [Guid]::NewGuid().ToString('N'))
$script:RecoveryRoot = Join-Path $script:fixture 'protected'
$prepared = Join-Path $script:fixture 'downloads/FMSC/Updates'
$prepared = Join-Path $prepared ('prepared-' + [Guid]::NewGuid().ToString('N'))
$application = Join-Path $prepared 'application'
$app = Join-Path $script:fixture 'app'; $stage = "$app.staging"; $rollback = "$app.rollback"
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
function Install-FmscStableLauncher { param($Source,$Root,$ShortcutPath,$Application) $script:shortcutInstalled = $true }
function Require($Condition,$Message) { if (-not $Condition) { throw $Message } }
function Refuse([scriptblock]$Action) { $refused=$false; try { & $Action } catch { $refused=$true }; Require $refused 'Unsafe recovery was accepted.' }
function Kill-Checkpoint([string]$Name) {
    $marker = Join-Path $script:fixture 'checkpoint'
    Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
    $psi.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -ChildRoot "' + $script:fixture + '" -Checkpoint ' + $Name
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $psi.RedirectStandardError = $true; $psi.RedirectStandardOutput = $true
    $process = [Diagnostics.Process]::Start($psi)
    $stderr = $process.StandardError.ReadToEndAsync(); $stdout = $process.StandardOutput.ReadToEndAsync()
    try {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        while (-not (Test-Path -LiteralPath $marker)) {
            if ($process.HasExited) { throw "Fixture child failed: $($stderr.GetAwaiter().GetResult()) $($stdout.GetAwaiter().GetResult())" }
            if ($watch.Elapsed.TotalSeconds -gt 40) { throw "Checkpoint $Name timed out." }
            Start-Sleep -Milliseconds 50
        }
        $process.Kill(); $process.WaitForExit()
    } finally { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
}
try {
    $null = New-Item -ItemType Directory -Path (Join-Path $application 'UpdateSupport'),(Join-Path $prepared 'metadata') -Force
    Set-Content (Join-Path $application 'FMSC_SMS.exe') 'inert target application'
    foreach ($name in @('core.ps1','UpdateRecovery.ps1','Launch-FMSC.ps1')) {
        Copy-Item -LiteralPath (Join-Path $project "Installer/$name") -Destination (Join-Path $application "UpdateSupport/$name")
    }
    $files = @(Get-ChildItem $application -Recurse -File | ForEach-Object {
        @{path=$_.FullName.Substring($application.Length+1).Replace('\','/');length=$_.Length;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}
    })
    @{format='fmsc-app-update/v1';version=$version;requiredDatabaseSchema=$schema;files=$files} |
        ConvertTo-Json -Depth 8 | Set-Content (Join-Path $prepared 'metadata/update-manifest.json')
    $script:RecoveryJournal = Initialize-FmscRecoveryTransaction $prepared $script:RecoveryRoot $version $schema $sid
    Require $script:shortcutInstalled 'Stable shortcut must be installed before journaling permits SQL.'
    $record = Read-FmscRecoveryJournal $script:RecoveryRoot
    Assert-FmscResumeIdentity $record $script:RecoveryRoot $version $schema (Join-Path $script:RecoveryRoot 'Recovery.ps1')
    Refuse { Assert-FmscResumeIdentity $record $script:RecoveryRoot '9999.0.0' $schema (Join-Path $script:RecoveryRoot 'Recovery.ps1') }
    $record.UserSid = 'S-1-5-21-1-2-3-1234'
    Refuse { Assert-FmscResumeIdentity $record $script:RecoveryRoot $version $schema (Join-Path $script:RecoveryRoot 'Recovery.ps1') }
    $record.UserSid = $sid
    Refuse { Initialize-FmscRecoveryTransaction $prepared $script:RecoveryRoot $version $schema $sid }
    # A partially written replacement is never treated as the active journal.
    Set-Content (Join-Path $script:RecoveryRoot 'recovery.json.abandoned.tmp') '{partial'
    $acl = Get-Acl (Join-Path $script:RecoveryRoot 'recovery.json.abandoned.tmp')
    $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier($ownerSid)))
    Set-Acl (Join-Path $script:RecoveryRoot 'recovery.json.abandoned.tmp') $acl
    Require ((Read-FmscRecoveryJournal $script:RecoveryRoot).Phase -eq 'Prepared') 'Orphan temporary record displaced a committed record.'
    $record.Phase = 'Unknown'; Save-FmscRecoveryJournal $script:RecoveryRoot $record
    Refuse { Read-FmscRecoveryJournal $script:RecoveryRoot }
    $record.Phase = 'Prepared'; Save-FmscRecoveryJournal $script:RecoveryRoot $record
    $snapshot = Join-Path $script:RecoveryRoot ('transactions/' + $record.Transaction)
    foreach ($checkpointName in @('BeforeOldMove','AfterOldMove','AfterNewMove')) {
        foreach ($directory in @($app,$stage,$rollback)) { Remove-VerifiedUpdateDirectory $directory $directory }
        $null = New-Item -ItemType Directory -Path $app
        Set-Content (Join-Path $app 'FMSC_SMS.exe') 'old baseline - must survive'
        Copy-ApplicationFiles (Join-Path $snapshot 'application') $stage
        $script:RecoveryJournal = $record; Set-FmscRecoveryPhase 'Prepared'
        Kill-Checkpoint $checkpointName
        $script:RecoveryJournal = Read-FmscRecoveryJournal $script:RecoveryRoot
        Require ($script:RecoveryJournal.Phase -eq 'Activating') 'Rename intent was not durable before interruption.'
        Remove-VerifiedUpdateDirectory $stage $stage
        Copy-ApplicationFiles (Join-Path $snapshot 'application') $stage
        Test-PreparedUpdate $snapshot $version $schema $stage
        Enable-StagedApplication $stage $app $rollback
        Test-PreparedUpdate $snapshot $version $schema $app
        Require ((Get-Content (Join-Path $rollback 'FMSC_SMS.exe') -Raw).Contains('old baseline')) 'Recovery destroyed the correct rollback baseline.'
    }
    Kill-Checkpoint 'DuringCleanup'
    $script:RecoveryJournal = Read-FmscRecoveryJournal $script:RecoveryRoot
    Require ($script:RecoveryJournal.Phase -eq 'Verified') 'Verification was not committed before cleanup.'
    Test-PreparedUpdate (Join-Path $script:RecoveryRoot 'verification') $version $schema $app
    Complete-FmscRecoveryTransaction
    Remove-VerifiedUpdateDirectory $rollback $rollback
    Remove-FmscCompletedSnapshot
    Require ((Read-FmscRecoveryJournal $script:RecoveryRoot).Phase -eq 'Completed' -and -not (Test-Path $snapshot)) 'Interrupted cleanup did not converge to Completed.'
    # Fail closed when protection is weakened, rather than elevating mutable scripts.
    & icacls.exe $script:RecoveryRoot /grant '*S-1-5-32-545:(M)' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot weaken fixture ACL for refusal test.' }
    Refuse { Read-FmscRecoveryJournal $script:RecoveryRoot }
    Write-Output 'PASS: actual child-process termination before/after activation renames and during cleanup; verified forward recovery, baseline preservation, durable journal, malformed state, pending-update refusal, identity/version and storage permission guards. No SQL or live installation.'
} finally {
    $absolute = [IO.Path]::GetFullPath($script:fixture)
    if (-not $absolute.StartsWith([IO.Path]::GetTempPath(),[StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $absolute) -notmatch '^fmsc-recovery-[a-f0-9]{32}$') { throw 'Unsafe fixture cleanup.' }
    Remove-Item -LiteralPath $absolute -Recurse -Force -ErrorAction SilentlyContinue
}
