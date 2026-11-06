# Fetch only the pinned standalone Microsoft go-sqlcmd for approved x64 builds.
# The complete installer and every application update both carry this tool.
param([Parameter(Mandatory=$true)][string]$DestinationFile)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$DestinationFile=[IO.Path]::GetFullPath($DestinationFile)
if (Test-Path -LiteralPath $DestinationFile) {
    throw "Refusing to overwrite an existing SQL tool: $DestinationFile"
}
$uri='https://github.com/microsoft/go-sqlcmd/releases/download/v1.10.0/sqlcmd-windows-amd64.zip'
$expected='A4C28332FCC6E497D655E53AC8F4939F4AB170F9CFD32D0FA5081B60F4D9D691'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('fmsc-sqlcmd-'+[Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    $zip=Join-Path $temp 'sqlcmd-windows-amd64.zip'
    Invoke-WebRequest -Uri $uri -OutFile $zip -MaximumRedirection 5
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expected) {
        throw 'Pinned Microsoft go-sqlcmd archive SHA-256 mismatch.'
    }
    $extract=Join-Path $temp 'unpacked'
    Expand-Archive -LiteralPath $zip -DestinationPath $extract
    $exe=@(Get-ChildItem -LiteralPath $extract -Filter 'sqlcmd.exe' -File -Recurse)
    if ($exe.Count -ne 1 -or $exe[0].Length -le 0) {
        throw 'Verified go-sqlcmd archive must contain exactly one nonempty sqlcmd.exe.'
    }
    New-Item -ItemType Directory -Path (Split-Path $DestinationFile -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $exe[0].FullName -Destination $DestinationFile
    Write-Host 'Pinned Microsoft go-sqlcmd v1.10.0 (x64) verified for offline packaging.'
} catch {
    if (Test-Path -LiteralPath $DestinationFile) { Remove-Item -LiteralPath $DestinationFile -Force }
    throw
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}
