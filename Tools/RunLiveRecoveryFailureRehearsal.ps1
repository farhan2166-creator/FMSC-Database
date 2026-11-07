# DEVELOPMENT-ONLY. Controlled updater fault injection against this PC's existing
# FMSC installation. Intentionally creates a real SQL backup and changes
# protected updater journal/launcher files, but does not run SQL migrations.
# DO NOT run on a school/production PC. Requires PowerShell 5.1 elevated
# as the same Windows account that downloaded the update.
[CmdletBinding()]
param([string]$Fixture = 'C:\FMSC-Recovery-Failure-Fixture')

$ErrorActionPreference = 'Stop'
function Assert-Test([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw "REFUSED: $Description" }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
Assert-Test ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) 'Run PowerShell as Administrator using the SAME Windows account.'
Assert-Test ($PSVersionTable.PSVersion.Major -eq 5) 'Use Windows PowerShell 5.1 (not PowerShell 7).'
Assert-Test ($identity.User.Value -match '^S-1-5-21-[0-9-]+$') 'Unexpected user SID.'
Assert-Test (-not (Get-Process -Name FMSC_SMS -ErrorAction SilentlyContinue)) 'Close FMSC completely before testing.'

$programData = [Environment]::GetFolderPath('CommonApplicationData')
$local = [Environment]::GetFolderPath('LocalApplicationData')
$fixturePath = [IO.Path]::GetFullPath($Fixture).TrimEnd('\')
Assert-Test ($fixturePath -ieq 'C:\FMSC-Recovery-Failure-Fixture') 'Use only the dedicated developer fixture.'
$app = 'C:\FMSC School Management'
$exe = Join-Path $app 'FMSC_SMS.exe'
$rollback = "$app.rollback"
$staging = "$app.staging"
$recovery = Join-Path $programData 'FMSC_Updater'
$journalPath = Join-Path $recovery 'recovery.json'
$databaseDir = Join-Path $programData 'FMSC_Database'
Assert-Test (Test-Path -LiteralPath $exe -PathType Leaf) 'Installed old application missing.'
Assert-Test ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -match '^1\.0\.8(?:\+|$)') 'Installed application must still be 1.0.8.'
Assert-Test (-not (Test-Path -LiteralPath $rollback)) 'An application rollback folder exists.'
Assert-Test (-not (Test-Path -LiteralPath $staging)) 'An application staging folder exists.'
Assert-Test (-not (Test-Path -LiteralPath $journalPath)) 'An active/terminal journal exists; inspection required first.'
Assert-Test (Test-Path -LiteralPath (Join-Path $databaseDir 'FMSC_DB.mdf') -PathType Leaf) 'Live database MDF missing.'
Assert-Test (Test-Path -LiteralPath (Join-Path $databaseDir 'FMSC_DB_log.ldf') -PathType Leaf) 'Live database LDF missing.'

$sourceApp = Join-Path $fixturePath 'application'
$srcManifest = Join-Path $fixturePath 'metadata\update-manifest.json'
$srcCore = Join-Path $sourceApp 'UpdateSupport\core.ps1'
$srcHelper = Join-Path $sourceApp 'UpdateSupport\UpdateRecovery.ps1'
$srcExe = Join-Path $sourceApp 'FMSC_SMS.exe'
Assert-Test ((Test-Path -LiteralPath $srcManifest -PathType Leaf) -and
    (Test-Path -LiteralPath $srcCore -PathType Leaf) -and
    (Test-Path -LiteralPath $srcHelper -PathType Leaf) -and
    (Test-Path -LiteralPath $srcExe -PathType Leaf)) 'Developer fixture is incomplete.'
Assert-Test (-not @(Get-ChildItem -LiteralPath $fixturePath -Recurse -Force | Where-Object {
    $_.Attributes -band [IO.FileAttributes]::ReparsePoint
}).Count) 'Fixture contains links.'
$manifest = Get-Content -LiteralPath $srcManifest -Raw | ConvertFrom-Json
Assert-Test ($manifest.format -eq 'fmsc-app-update/v1' -and
    $manifest.version -eq '1.0.10' -and
    $manifest.requiredDatabaseSchema -eq 7 -and @($manifest.files).Count -gt 700) 'Wrong test package manifest.'
$coreText = Get-Content -LiteralPath $srcCore -Raw
$helperText = Get-Content -LiteralPath $srcHelper -Raw
Assert-Test ($coreText.Contains('FMSC DEV TEST: failure AFTER verified backup and BEFORE migration 6.')) 'Fault injection missing.'
Assert-Test ($coreText.Contains('throw ''FMSC DEV TEST GUARD: schema 5 and migrations 6/7 required; no migration was run.''')) 'Schema guard missing.'
Assert-Test ($helperText.Contains("Set-FmscRecoveryPhase 'Completed'") -and
    $helperText.Contains("-NotePropertyValue 'Aborted'")) 'Backward-compatible recovery fix missing.'
Assert-Test ((Get-Item -LiteralPath $srcExe).VersionInfo.ProductVersion -match '^1\.0\.10(?:\+|$)') 'Developer fixture EXE version mismatch.'
# Verify all 732 manifest digests before copying.
$allFiles = @(Get-ChildItem -LiteralPath $sourceApp -Recurse -File)
Assert-Test ($allFiles.Count -eq @($manifest.files).Count) 'Manifest file-count mismatch.'
foreach ($file in $manifest.files) {
    $relative = [string]$file.path
    Assert-Test ($relative -and -not $relative.Contains('\') -and
        -not $relative.Contains(':') -and -not $relative.StartsWith('/') -and
        -not @($relative.Split('/') | Where-Object { $_ -in @('','.','..') }).Count) 'Unsafe manifest path.'
    $full = [IO.Path]::GetFullPath((Join-Path $sourceApp $relative))
    Assert-Test ($full.StartsWith($sourceApp + '\',[StringComparison]::OrdinalIgnoreCase)) 'Manifest path escapes source.'
    $item = Get-Item -LiteralPath $full -ErrorAction Stop
    Assert-Test (-not $item.PSIsContainer -and $item.Length -eq $file.length) "Invalid file: $relative"
    Assert-Test ((Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -eq $file.sha256) "Digest mismatch: $relative"
}

$beforeExeHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
$downloadRoot = Join-Path $local 'FMSC\Updates'
Assert-Test (Test-Path -LiteralPath $downloadRoot -PathType Container) 'FMSC update cache missing.'
$id = [guid]::NewGuid().ToString('N')
$prepared = Join-Path $downloadRoot ("prepared-$id")
Assert-Test (-not (Test-Path -LiteralPath $prepared)) 'Generated prepared folder already exists.'

Write-Host 'PRECHECK PASSED: previous app 1.0.8, valid schema-7 test package, injection and file hashes.' -ForegroundColor Green
Write-Host "Creating isolated test package under: $prepared"
$null = New-Item -ItemType Directory -Path $prepared
Copy-Item -LiteralPath $sourceApp -Destination (Join-Path $prepared 'application') -Recurse -ErrorAction Stop
Copy-Item -LiteralPath (Join-Path $fixturePath 'metadata') -Destination (Join-Path $prepared 'metadata') -Recurse -ErrorAction Stop

# The real updater requires a matching original process identity and waits for
# it to exit. Simulate a user-originating process using a temporary, same-user
# PowerShell child. No FMSC process is forged or killed.
$ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$marker = Start-Process -FilePath $ps -ArgumentList '-NoProfile -NonInteractive -Command "Start-Sleep -Seconds 8"' -WindowStyle Hidden -PassThru
Start-Sleep -Milliseconds 300
$ticks = $marker.StartTime.ToUniversalTime().Ticks
Write-Host 'Starting real updater with deliberate pre-migration failure. A failure dialog is EXPECTED.' -ForegroundColor Yellow
Write-Host 'Close the FMSC failure dialog to see the test verdict.'
& $ps -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $prepared 'application\UpdateSupport\core.ps1') -PreparedRoot $prepared -OriginalProcessId $marker.Id -OriginalStartTicks $ticks -OriginalUserSid $identity.User.Value
$installerCode = $LASTEXITCODE

# Verdict from the protected journal, preserved app and verified SQL backup.
# Recovery itself independently checked real SQL schema version 5.
Write-Host "Updater exit code: $installerCode (1 is expected)."
Assert-Test ($installerCode -eq 1) 'Updater did not fail as intended.'
Assert-Test (Test-Path -LiteralPath $journalPath -PathType Leaf) 'No durable recovery journal was created.'
$j = Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json
Assert-Test ($j.Phase -eq 'Completed' -and $j.Outcome -eq 'Aborted' -and
    $j.BeforeSchema -eq 5 -and $j.LastSchema -eq 0 -and $j.Schema -eq 7) 'Safe abort journal fields are not correct.'
Assert-Test ($j.DownloadRoot -ieq $prepared) 'Journal points at an unexpected update package.'
Assert-Test ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -eq $beforeExeHash) 'Old app executable changed.'
Assert-Test (-not (Test-Path -LiteralPath $rollback)) 'Rollback folder unexpectedly appeared.'
Assert-Test ((Test-Path -LiteralPath $j.Backup -PathType Leaf) -and
    (Get-FileHash -LiteralPath $j.Backup -Algorithm SHA256).Hash -eq $j.BackupSha256) 'Real SQL backup missing or digest mismatch.'

Write-Host ''
Write-Host 'PASS: REAL updater failed after verified SQL backup, before migration 6.' -ForegroundColor Green
Write-Host 'PASS: Recovery Completed/Aborted; baseline schema 5 verified; old app unchanged.' -ForegroundColor Green
Write-Host 'PASS: Verified SQL backup retained, no automatic SQL restore, no rollback folder.' -ForegroundColor Green
Write-Host 'Now open the original FMSC desktop shortcut. It should launch without a recovery loop.' -ForegroundColor Cyan
