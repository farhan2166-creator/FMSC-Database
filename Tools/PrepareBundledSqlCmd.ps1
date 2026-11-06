param([Parameter(Mandatory=$true)][string]$DestinationFile)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Installer/MaintenanceTools.ps1')
if(Test-Path -LiteralPath $DestinationFile){throw 'SQL tool output already exists.'}
$parent=Split-Path ([IO.Path]::GetFullPath($DestinationFile)) -Parent
$null=New-Item -ItemType Directory -Path $parent -Force
$bundle=Join-Path $parent 'SqlCmd'
Receive-FmscTool 'SqlCmd' $bundle
Copy-Item -LiteralPath (Join-Path $bundle 'sqlcmd.exe') -Destination $DestinationFile
