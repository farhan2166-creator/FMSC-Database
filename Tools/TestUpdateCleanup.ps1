# Disposable folders and mocked verification only; never execute the installer.
$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $PSScriptRoot '../Installer/core.ps1') -Raw
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Installer does not parse.' }
foreach ($name in @('Remove-VerifiedUpdateDirectory','Remove-OldPreMigrationBackups','Remove-SupersededPreparedUpdates','Complete-VerifiedApplicationUpdate')) {
    $definition = $ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name}, $true)
    Invoke-Expression $definition.Extent.Text
}
function Get-FmscDownloadParent { return $script:state }
function Write-Log { param($Message) }
function Write-WarningMessage { param($Message) $script:warnings++ }
function Test-StagedApplication { param($Source,$Staging) if ($script:badFiles) { throw 'Damaged activated files' } }
function Test-PreparedUpdate { param($Root,$Version,$Schema,$Application) if ($script:badManifest) { throw 'Damaged manifest' } }
function Invoke-InstallationDatabaseCheck { param($Application) return $script:verification }
function Invoke-SqlMaster { param($Query) if ($script:badBackup) { throw 'Invalid backup' } }
$AppVersion = '1.0.0'; $DatabaseVersion = '1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('fmsc-cleanup-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $root 'app'; $rollback = "$app.rollback"
$DatabaseDir = Join-Path $root 'database'
$script:state = Join-Path $root 'FMSC/Updates'
$state = $script:state
$prepared = Join-Path $state ('prepared-' + [Guid]::NewGuid().ToString('N'))
$cache = Join-Path $state ('download-cache/' + ('a' * 64))
$stale1 = Join-Path $state ('prepared-' + [Guid]::NewGuid().ToString('N'))
$stale2 = Join-Path $state ('prepared-' + [Guid]::NewGuid().ToString('N'))
$unrelated = Join-Path $state 'user-notes'
$unknownCache = Join-Path $state 'download-cache/manual-keep'
try {
    New-Item -ItemType Directory -Path $app,$rollback,$prepared,$cache,$stale1,$stale2,$unrelated,$unknownCache | Out-Null
    $key = Join-Path $state 'device-key.v1.dpapi'
    $backup = Join-Path $root 'school.bak'
    Set-Content $key 'protected fixture'; Set-Content $backup 'recovery fixture'
    Set-Content (Join-Path $rollback 'old.exe') 'previous'
    Set-Content (Join-Path $prepared 'file') 'latest verified package'
    Set-Content (Join-Path $stale1 'file') 'old prepared package'
    Set-Content (Join-Path $stale2 'file') 'older prepared package'
    Set-Content (Join-Path $unrelated 'memo.txt') 'must stay untouched'
    Set-Content (Join-Path $unknownCache 'note.txt') 'must not prune unknown cache subfolder'
    Set-Content (Join-Path $cache 'part') 'verified chunk'
    foreach ($failure in @('files','manifest','database')) {
        $script:badFiles = $failure -eq 'files'; $script:badManifest = $failure -eq 'manifest'
        $script:verification = 1
        $failed = $false
        try { Complete-VerifiedApplicationUpdate $app $prepared $prepared } catch { $failed = $true }
        if (-not $failed -or -not (Test-Path $rollback) -or -not (Test-Path $prepared) -or -not (Test-Path $cache)) {
            throw "Recovery files were not preserved after $failure failure."
        }
    }
    $script:badFiles = $false; $script:badManifest = $false; $script:verification = 0; $script:warnings = 0
    $refused = $false
    try { Remove-VerifiedUpdateDirectory $root $rollback } catch { $refused = $true }
    if (-not $refused -or -not (Test-Path $root)) { throw 'Out-of-scope cleanup accepted.' }
    # A locked old executable must yield a cleanup warning, not undo success.
    $locked = [IO.File]::Open((Join-Path $rollback 'old.exe'), 'Open', 'Read', 'None')
    try { Complete-VerifiedApplicationUpdate $app $prepared $prepared } finally { $locked.Dispose() }
    if ($script:warnings -ne 1 -or -not (Test-Path $rollback) -or -not (Test-Path $prepared) -or (Test-Path $cache) -or
        (Test-Path $stale1) -or (Test-Path $stale2) -or -not (Test-Path $unrelated) -or -not (Test-Path $unknownCache)) {
        throw 'Locked-file cleanup handling failed.'
    }
    # Repeat cleanup is idempotent and never removes the retained latest package.
    Complete-VerifiedApplicationUpdate $app $prepared $prepared
    if (-not (Test-Path $prepared) -or -not (Test-Path $key) -or -not (Test-Path $backup)) { throw 'Retained update or protected state was removed.' }
    Complete-VerifiedApplicationUpdate $app $app
    if ((Test-Path $rollback) -or -not (Test-Path $app) -or -not (Test-Path $key) -or -not (Test-Path $backup)) {
        throw 'Successful cleanup removed protected data or retained rollback.'
    }
    $backupFolder = Join-Path $DatabaseDir 'Backups'
    New-Item -ItemType Directory -Path $backupFolder | Out-Null
    $old = Join-Path $backupFolder 'FMSC_DB_BeforeMigration_v1_to_v2_20261101_090000.bak'
    $recent = Join-Path $backupFolder 'FMSC_DB_BeforeMigration_v2_to_v3_20261102_090000.bak'
    $latest = Join-Path $backupFolder 'FMSC_DB_BeforeMigration_v2_to_v3_20261102_090000_1.bak'
    $manual = Join-Path $backupFolder 'manual-school-backup.bak'
    foreach ($path in @($old,$recent,$latest,$manual)) { Set-Content -LiteralPath $path 'backup fixture' }
    $script:badBackup = $true
    Complete-VerifiedApplicationUpdate $app $app
    if (-not (Test-Path $old)) { throw 'Unverified retained backups allowed pruning.' }
    $script:badBackup = $false
    Complete-VerifiedApplicationUpdate $app $app
    if ((Test-Path $old) -or -not (Test-Path $recent) -or -not (Test-Path $latest) -or -not (Test-Path $manual)) {
        throw 'Pre-migration backup retention removed the wrong files.'
    }
    $link = Join-Path $root 'linked-rollback'
    New-Item -ItemType Junction -Path $link -Target $app | Out-Null
    try {
        $refused = $false
        try { Remove-VerifiedUpdateDirectory $link $link } catch { $refused = $true }
        if (-not $refused -or -not (Test-Path $app)) { throw 'Linked cleanup was accepted.' }
    } finally { [IO.Directory]::Delete($link) }
    $activation = $source.IndexOf('    Enable-StagedApplication -Staging')
    $cleanup = $source.IndexOf('    Complete-VerifiedApplicationUpdate -ApplicationDirectory $AppDir -Source $AppSource')
    if ($activation -lt 0 -or $cleanup -le $activation) { throw 'Cleanup must follow activation.' }
    $completed = $source.IndexOf("'STEP 8 OF 8  -  COMPLETED'")
    if ($completed -le $cleanup) { throw 'Completed must follow activated-application verification.' }
    Write-Output 'PASS: verification failures preserve recovery; successful activation keeps latest prepared package, prunes old packages and chunks, ignores unrelated files, handles locks, protects backup and enrollment.'
} finally {
    $absolute = [IO.Path]::GetFullPath($root)
    if (-not $absolute.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup.' }
    Remove-Item -LiteralPath $absolute -Recurse -Force -ErrorAction SilentlyContinue
}
