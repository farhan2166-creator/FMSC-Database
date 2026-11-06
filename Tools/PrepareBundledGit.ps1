param([Parameter(Mandatory=$true)][string]$DestinationDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Installer/MaintenanceTools.ps1')
if(Test-Path -LiteralPath $DestinationDirectory){throw 'Transport output already exists.'}
Receive-FmscTool 'Git' ([IO.Path]::GetFullPath($DestinationDirectory))
