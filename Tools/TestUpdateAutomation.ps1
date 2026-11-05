# Extract helpers only. No elevation, application activation, real restart or SQL.
$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $PSScriptRoot '../Installer/core.ps1') -Raw
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
if ($errors.Count) { throw 'Installer does not parse.' }
foreach ($name in @('Pause-Step','Pause-Long','Start-UpdatedFmsc','Start-FmscAfterInstallation','Open-UpdateWindow','Set-UpdateWindowStatus')) {
    $definition = $ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    Invoke-Expression $definition.Extent.Text
}
function Read-Host { param($Prompt) throw 'Prepared update must not prompt.' }
function Start-Sleep { throw 'Prepared update must skip cosmetic pauses.' }
function Start-Process { throw 'Prepared update must not directly launch an elevated app.' }
function Write-Log { param($Message) }
$script:launches = 0
$desktop = [pscustomobject]@{}
$desktop | Add-Member ScriptMethod ShellExecute {
    param($Application,$Arguments,$Directory,$Verb,$Display)
    if ($Application -ne 'C:\fixture\FMSC_SMS.exe' -or $Arguments -ne '' -or
        $Directory -ne 'C:\fixture' -or $Verb -ne 'open' -or $Display -ne 1) { throw 'Invalid shell restart request.' }
    $script:launches++
}
function Get-FmscDesktopShell { return $desktop }
$PreparedRoot = 'fixture'
Pause-Step; Pause-Long
Start-FmscAfterInstallation 'C:\fixture\FMSC_SMS.exe'
if ($script:launches -ne 1) { throw 'Successful prepared installation must restart exactly once.' }
# Exercise the real SQL-free installer progress window and its cleanup.
try {
    Open-UpdateWindow
    Set-UpdateWindowStatus 'Verifying the installed application...'
    if (-not $script:UpdateWindow.Visible -or $script:UpdateWindow.ControlBox -or
        -not $script:UpdateStatus.Text.StartsWith('Installing')) { throw 'Installer progress window state is invalid.' }
} finally { if ($script:UpdateWindow) { $script:UpdateWindow.Dispose(); $script:UpdateWindow = $null } }
$finish = $source.IndexOf('    Start-FmscAfterInstallation -Application $InstalledExe')
$verified = $source.IndexOf('    Complete-VerifiedApplicationUpdate -ApplicationDirectory $AppDir -Source $AppSource')
$catch = $source.IndexOf("'                    INSTALLATION FAILED'")
if ($finish -le $verified -or $finish -ge $catch) { throw 'Restart must occur only on the verified success path.' }
$handoff = Get-Content (Join-Path $PSScriptRoot '../Core/Updates/UpdateInstallerHandoff.cs') -Raw
if (-not $handoff.Contains('-STA -WindowStyle Hidden') -or -not $handoff.Contains('Verb = "runas"')) {
    throw 'Prepared updates must elevate while hiding the PowerShell console.'
}
Write-Output 'PASS: prompt-free prepared update, no cosmetic waits, normal-user shell restart exactly once after verification, installer progress window and hidden elevated handoff.'
