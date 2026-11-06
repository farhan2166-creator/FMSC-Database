# Retained entry point: installer resolution uses permanent tools, never PATH.
param([string]$InstallerPath=(Join-Path $PSScriptRoot '../Installer/core.ps1'))
$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($InstallerPath,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Installer does not parse.'}
$function=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Resolve-SqlCmd'},$true))
if($function.Count -ne 1){throw 'Expected one SQL resolver.'}
Invoke-Expression $function[0].Extent.Text
$script:AppSource='synthetic-app-source'
$script:resolved=$false
function Install-FmscMaintenanceTool([string]$Name,[string]$Application){
    if($Name -ne 'SqlCmd' -or $Application -ne $script:AppSource){throw 'Wrong SQL tool context.'}
    $script:resolved=$true
    return Join-Path ([IO.Path]::GetTempPath()) 'synthetic-retained-tools'
}
function Get-Command {throw 'Unexpected system PATH lookup.'}
$result=Resolve-SqlCmd
if(-not $script:resolved -or $result -ne (Join-Path ([IO.Path]::GetTempPath()) 'synthetic-retained-tools/sqlcmd.exe')){throw 'SQL resolver must use verified permanent tool storage.'}
& (Join-Path $PSScriptRoot 'TestMaintenanceTools.ps1')
Write-Host 'PASS: installer SQL uses verified retained storage without setup-folder or PATH dependence.'
