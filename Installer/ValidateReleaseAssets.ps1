param(
    [Parameter(Mandatory=$true)][string]$DatabaseDirectory,
    [Parameter(Mandatory=$true)][string]$LocalDbMsiPath
)
$ErrorActionPreference='Stop'
$mdf=Join-Path $DatabaseDirectory 'FMSC_DB.mdf'
$ldf=Join-Path $DatabaseDirectory 'FMSC_DB_log.ldf'
$hasMdf=Test-Path -LiteralPath $mdf
$hasLdf=Test-Path -LiteralPath $ldf
if($hasMdf -ne $hasLdf){
    $missing=if($hasMdf){$ldf}else{$mdf}
    throw "Database snapshot is incomplete: $missing is missing. Supply both detached files or omit both for an app-only update."
}
$assets=@($LocalDbMsiPath)
if($hasMdf){$assets+=@($mdf,$ldf)}
foreach($path in $assets) {
    $resolved=[IO.Path]::GetFullPath($path)
    if(!(Test-Path -LiteralPath $resolved -PathType Leaf)){throw "Required release file is missing: $resolved"}
    if((Get-Item -LiteralPath $resolved).Length -eq 0){throw "Required release file is empty: $resolved"}
    # A snapshot must be detached; copying files held by SQL Server is unsafe.
    $stream=$null
    try {$stream=[IO.File]::Open($resolved,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)}
    catch {throw "Release file cannot be read exclusively: $resolved. Supply a detached snapshot/readable prerequisite. $($_.Exception.Message)"}
    finally {if($null -ne $stream){$stream.Dispose()}}
}
if($hasMdf){Write-Output 'Optional detached database snapshot validated.'}
else{Write-Output 'No database snapshot supplied; publishing an app-only update. Fresh installations require a separate compatible snapshot.'}
Write-Output 'LocalDB prerequisite is present, nonempty and readable.'
