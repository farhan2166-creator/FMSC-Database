# SQL-free, network-free tests: on installed FMSC, updates and recovery
# resolve bundled go-sqlcmd without depending on Windows PATH.
param([string]$InstallerPath=(Join-Path $PSScriptRoot '../Installer/core.ps1'))
$ErrorActionPreference='Stop'
$source=Get-Content -LiteralPath $InstallerPath -Raw
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
if ($errors.Count -gt 0) { throw "Installer PowerShell syntax errors: $($errors -join '; ')" }
$functions=@($ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Resolve-SqlCmd'
},$true))
if($functions.Count -ne 1){throw 'Expected one SQL prerequisite resolver.'}
Invoke-Expression $functions[0].Extent.Text
function Write-Success { param([string]$Message) }
function Get-InstallerToolsDirectory { return $script:TestReleaseTools }
# A fully bundled release should never inspect the system PATH.
function Get-Command { throw 'SQL resolution unexpectedly depended on system PATH.' }
$root=Join-Path ([IO.Path]::GetTempPath()) ('fmsc-offline-sql-'+[Guid]::NewGuid().ToString('N'))
try {
    $script:AppSource=Join-Path $root 'staged-app'
    $script:AppDir=Join-Path $root 'installed-app'
    $script:TestReleaseTools=Join-Path $root 'release/Tools'
    $bundled=Join-Path $script:AppSource 'UpdateSupport/Tools/sqlcmd.exe'
    $release=Join-Path $script:TestReleaseTools 'sqlcmd.exe'
    $installed=Join-Path $script:AppDir 'UpdateSupport/Tools/sqlcmd.exe'
    foreach($file in @($bundled,$release,$installed)){
        New-Item -ItemType Directory -Path (Split-Path $file -Parent) -Force | Out-Null
        Set-Content -LiteralPath $file -Value 'Synthetic resolver fixture only'
    }
    if((Resolve-SqlCmd) -ne $bundled) { throw 'Prepared update did not prefer bundled SQL command.' }
    Remove-Item -LiteralPath $bundled
    if((Resolve-SqlCmd) -ne $release) { throw 'Complete installer did not prefer release SQL command.' }
    Remove-Item -LiteralPath $release
    if((Resolve-SqlCmd) -ne $installed) { throw 'Recovery did not use installed SQL command.' }
    Write-Host 'PASS: prepared update, complete installer and recovery choose bundled sqlcmd without PATH or network.'
} finally {
    if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}
}
