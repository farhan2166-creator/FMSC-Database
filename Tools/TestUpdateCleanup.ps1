# Disposable folders and mocked verification only; never execute the installer.
$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $PSScriptRoot '../Installer/core.ps1') -Raw
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Installer does not parse.' }
foreach ($name in @('Remove-VerifiedUpdateDirectory','Complete-VerifiedApplicationUpdate')) {
    $definition = $ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name}, $true)
    Invoke-Expression $definition.Extent.Text
}
function Write-Log { param($Message) }
function Write-WarningMessage { param($Message) $script:warnings++ }
function Test-StagedApplication { param($Source,$Staging) if ($script:badFiles) { throw 'Damaged activated files' } }
function Test-PreparedUpdate { param($Root,$Version,$Schema,$Application) if ($script:badManifest) { throw 'Damaged manifest' } }
function Invoke-InstallationDatabaseCheck { param($Application) return $script:verification }
$AppVersion = '1.0.0'; $DatabaseVersion = '1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('fmsc-cleanup-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $root 'app'; $rollback = "$app.rollback"
$state = Join-Path $root 'FMSC/Updates'
$prepared = Join-Path $state ('prepared-' + [Guid]::NewGuid().ToString('N'))
$cache = Join-Path $state ('download-cache/' + ('a' * 64))
try {
    New-Item -ItemType Directory -Path $app,$rollback,$prepared,$cache | Out-Null
    $key = Join-Path $state 'device-key.v1.dpapi'
    $backup = Join-Path $root 'school.bak'
    Set-Content $key 'protected fixture'; Set-Content $backup 'recovery fixture'
    Set-Content (Join-Path $rollback 'old.exe') 'previous'
    Set-Content (Join-Path $prepared 'file') 'staging'
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
    if ($script:warnings -ne 1 -or -not (Test-Path $rollback) -or (Test-Path $prepared) -or (Test-Path $cache)) {
        throw 'Locked-file cleanup handling failed.'
    }
    Complete-VerifiedApplicationUpdate $app $app
    if ((Test-Path $rollback) -or -not (Test-Path $app) -or -not (Test-Path $key) -or -not (Test-Path $backup)) {
        throw 'Successful cleanup removed protected data or retained rollback.'
    }
    $link = Join-Path $root 'linked-rollback'
    New-Item -ItemType Junction -Path $link -Target $app | Out-Null
    try {
        $refused = $false
        try { Remove-VerifiedUpdateDirectory $link $link } catch { $refused = $true }
        if (-not $refused -or -not (Test-Path $app)) { throw 'Linked cleanup was accepted.' }
    } finally { [IO.Directory]::Delete($link) }
    $activation = $source.IndexOf('    Enable-StagedApplication -Staging')
    $cleanup = $source.IndexOf('    Complete-VerifiedApplicationUpdate -ApplicationDirectory')
    if ($activation -lt 0 -or $cleanup -le $activation) { throw 'Cleanup must follow activation.' }
    Write-Output 'PASS: verification failures preserve recovery; successful activation cleanup, locked files, scope guards, database backup and enrollment preservation.'
} finally {
    $absolute = [IO.Path]::GetFullPath($root)
    if (-not $absolute.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup.' }
    Remove-Item -LiteralPath $absolute -Recurse -Force -ErrorAction SilentlyContinue
}
