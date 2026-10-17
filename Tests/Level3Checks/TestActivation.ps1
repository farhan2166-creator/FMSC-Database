# Narrow filesystem fault seam around the unchanged production activation function.
param(
    [string]$ProjectDirectory = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [string]$ScratchRoot = [IO.Path]::GetTempPath()
)
$ErrorActionPreference='Stop'
$parent=[IO.Path]::GetFullPath($ScratchRoot)
$allowed=@([IO.Path]::GetTempPath(),$env:RUNNER_TEMP) | Where-Object { $_ }
if (-not @($allowed | Where-Object {
    $base=[IO.Path]::GetFullPath($_).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $parent -eq $base -or $parent.StartsWith($base+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
}).Count) { throw 'Activation fault fixture must remain inside the temporary test root.' }
$owner=[guid]::NewGuid().ToString('N')
$fixture=Join-Path $parent ('FMSC-Level3-activation-'+$owner)
if (Test-Path -LiteralPath $fixture) { throw 'Activation fixture already exists; refusing overwrite.' }
$null=New-Item -ItemType Directory -Path $fixture
$marker=Join-Path $fixture 'owner.txt'
$owner | Set-Content -LiteralPath $marker
try {
    $tokens=$null; $parseErrors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $ProjectDirectory 'Installer/core.ps1'),[ref]$tokens,[ref]$parseErrors)
    if ($parseErrors.Count) { throw 'Production installer does not parse.' }
    $definition=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Enable-StagedApplication'},$true))
    if ($definition.Count -ne 1) { throw 'Expected one production activation function.' }
    Invoke-Expression $definition[0].Extent.Text
    function Write-Log { param($Message) }
    $fixtureApp=Join-Path $fixture 'live'
    $fixtureStage=Join-Path $fixture 'stage'
    $fixtureRollback=Join-Path $fixture 'rollback'
    New-Item -ItemType Directory -Path $fixtureApp,$fixtureStage -Force | Out-Null
    'old executable sentinel' | Set-Content -LiteralPath (Join-Path $fixtureApp 'FMSC_SMS.exe')
    'new executable sentinel' | Set-Content -LiteralPath (Join-Path $fixtureStage 'FMSC_SMS.exe')
    $oldHash=(Get-FileHash -LiteralPath (Join-Path $fixtureApp 'FMSC_SMS.exe')).Hash
    function Move-Item {
        [CmdletBinding()] param([string]$LiteralPath,[string]$Destination,[switch]$Force)
        if ($LiteralPath -eq $fixtureStage -and $Destination -eq $fixtureApp) {
            New-Item -ItemType Directory -Path $Destination -Force | Out-Null
            'partial activation' | Set-Content -LiteralPath (Join-Path $Destination 'partial.txt')
            throw [IO.IOException]::new('Controlled test-only activation interruption')
        }
        Microsoft.PowerShell.Management\Move-Item @PSBoundParameters
    }
    $refused=$false
    try { Enable-StagedApplication -Staging $fixtureStage -AppDir $fixtureApp -RollbackDir $fixtureRollback }
    catch { if ($_.Exception.Message -notmatch 'Controlled test-only activation interruption') { throw }; $refused=$true }
    if (-not $refused -or (Get-FileHash -LiteralPath (Join-Path $fixtureApp 'FMSC_SMS.exe')).Hash -ne $oldHash -or
        (Test-Path -LiteralPath (Join-Path $fixtureApp 'partial.txt')) -or (Test-Path -LiteralPath $fixtureRollback)) {
        throw 'Partial activation did not restore previous bytes and remove unsafe partial output.'
    }
    Write-Host 'PASS: production activation rollback restores exact prior bytes after simulated partial move failure.'
}
finally {
    if ((Get-Content -LiteralPath $marker -Raw).Trim() -ne $owner -or [IO.Path]::GetFileName($fixture) -ne ('FMSC-Level3-activation-'+$owner)) {
        throw 'Activation cleanup ownership changed; deletion refused.'
    }
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
if (Test-Path -LiteralPath $fixture) { throw 'Activation fixture cleanup was not confirmed.' }
Write-Host 'PASS: activation fixture cleanup confirmed; no app, SQL or MSI was launched.'
