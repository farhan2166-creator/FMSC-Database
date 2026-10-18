# Run on Windows pwsh. Does not launch msiexec or touch school databases.
param([string]$InstallerPath = (Join-Path $PSScriptRoot '../Installer/core.ps1'))
$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath $InstallerPath -Raw
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw "Installer parse errors: $($parseErrors -join '; ')" }
$fn = @($ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Invoke-LocalDbMsiInstallation'
}, $true))
if ($fn.Count -ne 1) { throw 'Expected exactly one LocalDB MSI helper.' }
$calls = @($ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.CommandAst] -and
        $node.GetCommandName() -eq 'Invoke-LocalDbMsiInstallation'
}, $true))
if ($calls.Count -ne 1) { throw 'Expected exactly one LocalDB MSI call in the installation path.' }
for ($parent = $calls[0].Parent; $null -ne $parent; $parent = $parent.Parent) {
    if ($parent -is [System.Management.Automation.Language.FunctionDefinitionAst]) {
        throw 'The LocalDB MSI call must be in the installer flow, not accidentally nested in a helper.'
    }
}
Invoke-Expression $fn[0].Extent.Text

function Write-Log { param([string]$Message) }
function Write-Step { param([string]$Message) }
function Write-Success { param([string]$Message) }
function Write-WarningMessage { param([string]$Message) }
function Write-Host { param([Parameter(ValueFromRemainingArguments=$true)]$Args) }
$script:codes = @()
$script:launches = @()
$script:answer = 'N'
function Read-Host { param([string]$Prompt) return $script:answer }
function Start-Process {
    param([string]$FilePath, [object[]]$ArgumentList, [switch]$Wait, [switch]$PassThru)
    if ($FilePath -ne 'msiexec.exe') { throw "Unexpected process: $FilePath" }
    $script:launches += ,@($ArgumentList)
    $index = $script:launches.Count - 1
    if ($index -ge $script:codes.Count) { throw 'Unexpected extra MSI attempt' }
    return [pscustomobject]@{ ExitCode = $script:codes[$index] }
}
function Assert($Condition, $Message) {
    if (-not $Condition) { throw $Message }
}
function Try-Scenario([int[]]$Codes, [string]$Answer, [string]$ErrorText, [int]$ExpectedCount) {
    $script:codes = $Codes
    $script:answer = $Answer
    $script:launches = @()
    $failure = $null
    try {
        Invoke-LocalDbMsiInstallation -MsiPath 'C:\fake folder\SqlLocalDB.msi' -LogDirectory $env:TEMP
    } catch { $failure = $_.Exception.Message }
    if ($ErrorText) {
        Assert ($null -ne $failure -and $failure.Contains($ErrorText)) "Expected error '$ErrorText', got '$failure'"
    } else {
        Assert ($null -eq $failure) "Unexpected MSI error: $failure"
    }
    Assert ($script:launches.Count -eq $ExpectedCount) "Expected $ExpectedCount MSI attempts, got $($script:launches.Count)"
    $silent = $script:launches[0] -join ' '
    Assert ($silent.Contains('/qn') -and $silent.Contains('/L*V') -and $silent.Contains('/norestart')) 'Silent setup must be logged and non-restarting'
    if ($ExpectedCount -eq 2) {
        $wizard = $script:launches[1] -join ' '
        Assert (-not $wizard.Contains('/qn') -and $wizard.Contains('/L*V') -and $wizard.Contains('/norestart')) 'Wizard must be interactive, logged, and non-restarting'
    }
}
Try-Scenario @(0) N '' 1
Try-Scenario @(1603, 0) Y '' 2
Try-Scenario @(1603) N '1603' 1
Try-Scenario @(3010) Y 'restart' 1
Try-Scenario @(1641) Y 'restart' 1
Try-Scenario @(1603, 3010) Y 'restart' 2
Try-Scenario @(1603, 1641) Y 'restart' 2
Try-Scenario @(1603, 1602) Y 'wizard MSI code: 1602' 2
Try-Scenario @(1603, 1603) Y 'wizard MSI code: 1603' 2
Write-Output 'PASS: LocalDB silent setup, interactive retry, refusal, verbose logging and restart guards.'
