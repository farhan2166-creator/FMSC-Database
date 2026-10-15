# Mock process/backup tests. Never launches FMSC, SQL, LocalDB or a real installer.
param([string]$InstallerPath = (Join-Path $PSScriptRoot '../Installer/core.ps1'))
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput((Get-Content -LiteralPath $InstallerPath -Raw), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Installer PowerShell does not parse.' }
foreach ($name in @('Invoke-InstallationDatabaseCheck','Invoke-CommunicationSetup')) {
    $functions = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true))
    if ($functions.Count -ne 1) { throw "Expected exactly one $name helper." }
    Invoke-Expression $functions[0].Extent.Text
}
$activations = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Enable-StagedApplication' }, $true))
if ($activations.Count -ne 2) { throw 'Review all application activation paths.' }
foreach ($activation in $activations) {
    $statement = $activation.Parent
    while ($null -ne $statement.Parent -and $statement.Parent -isnot [System.Management.Automation.Language.StatementBlockAst]) { $statement = $statement.Parent }
    $prior = @($statement.Parent.Statements | Where-Object { $_.Extent.StartOffset -lt $activation.Extent.StartOffset })
    if (-not ($prior | Where-Object { $_.Extent.Text -match '^Invoke-CommunicationSetup ' })) {
        throw 'Application activation must follow communication/base verification in the same branch.'
    }
}
function Write-Log { param([string]$Message) }
function Write-WarningMessage { param([string]$Message) }
function Write-Host { param([Parameter(ValueFromRemainingArguments=$true)]$Args) }
function Read-Host { param([string]$Prompt) return $script:answer }
function New-PreMigrationBackup {
    param([string]$FromVersion,[string]$ToVersion)
    if ($FromVersion -ne '6' -or $ToVersion -ne '6_Communication1') { throw 'Incorrect backup identity.' }
    $script:events += 'backup'
    if ($script:backupFails) { throw 'Synthetic verified-backup failure' }
    return (Join-Path $script:scratch 'verified-fixture.bak')
}
function Start-Process {
    param([string]$FilePath,[object[]]$ArgumentList,[switch]$PassThru)
    if ($FilePath -ne 'C:\fake folder\FMSC_SMS.exe' -or -not $PassThru) { throw 'Unexpected process request.' }
    if ($ArgumentList[1] -ne '"(localdb)\MSSQLLocalDB"' -or $ArgumentList[2] -ne '"FMSC_DB"') { throw 'Incorrect connection target.' }
    $script:events += $ArgumentList[0]
    if ($ArgumentList[0] -eq '--install-communication' -and $ArgumentList[3] -ne ('"' + $script:migration + '"')) { throw 'Migration path must remain a single quoted argument.' }
    $index = $script:events.Count - 1 - @($script:events | Where-Object { $_ -eq 'backup' }).Count
    if ($index -ge $script:codes.Count) { throw 'Unexpected additional process launch.' }
    $result = [pscustomobject]@{ ExitCode = $script:codes[$index] }
    $result | Add-Member ScriptMethod WaitForExit { param($milliseconds) if ($milliseconds -ne 180000) { throw 'Missing bounded wait.' }; return -not $script:timeout }
    $result | Add-Member ScriptMethod Kill { $script:killed = $true }
    return $result
}
$SqlInstance = '(localdb)\MSSQLLocalDB'; $DatabaseName = 'FMSC_DB'
$script:scratch = Join-Path ([IO.Path]::GetTempPath()) ('FMSC communication setup ' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $script:scratch | Out-Null
$script:migration = Join-Path $script:scratch '7.sql'
Set-Content -LiteralPath $script:migration -Value '-- Synthetic path fixture; never executed.'
function Scenario($Codes,$Answer,$BackupFails,$Timeout,$Expected,$ErrorText) {
    $script:codes = @($Codes); $script:answer = $Answer; $script:backupFails = $BackupFails
    $script:timeout = $Timeout; $script:killed = $false; $script:events = @(); $failure = ''
    try { Invoke-CommunicationSetup -Application 'C:\fake folder\FMSC_SMS.exe' -MigrationFile $script:migration }
    catch { $failure = $_.Exception.Message }
    if ($ErrorText) { if (-not $failure.Contains($ErrorText)) { throw "Expected '$ErrorText', got '$failure'" } }
    elseif ($failure) { throw $failure }
    if (($script:events -join ',') -ne $Expected) { throw "Incorrect operation order: $($script:events -join ',')" }
    if ($Timeout -and -not $script:killed) { throw 'Timed-out helper must be terminated and reported uncertain.' }
}
try {
    Scenario @(0) Y $false $false '--verify-installation' ''
    Scenario @(10) N $false $false '--verify-installation' ''
    Scenario @(10) '' $false $false '--verify-installation' ''
    Scenario @(10,0) Y $false $false '--verify-installation,backup,--install-communication' ''
    foreach ($code in @(20,21,23)) { Scenario @($code) Y $false $false '--verify-installation' 'Database verification refused' }
    Scenario @(10) Y $true $false '--verify-installation,backup' 'Synthetic verified-backup failure'
    foreach ($code in @(21,22,23)) { Scenario @(10,$code) Y $false $false '--verify-installation,backup,--install-communication' 'setup did not complete' }
    Scenario @(10) Y $false $true '--verify-installation' 'timed out'
    Remove-Item -LiteralPath $script:migration
    Scenario @(10) Y $false $false '--verify-installation' 'migration file is missing'
    Write-Output 'PASS: 13 communication setup scenarios; explicit consent, verified backup ordering, refusal, timeout and pre-activation guards.'
} finally { Remove-Item -LiteralPath $script:scratch -Recurse -Force }
