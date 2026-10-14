param([Parameter(Mandatory=$true)][string]$ProjectDirectory)
$ErrorActionPreference='Stop'
# Use the same streaming .NET SHA256 implementation for the package and model.
# Publishing must not depend on the FileHash PowerShell script module loading.
function Get-StreamSha256([IO.Stream]$Stream){
    $sha=[Security.Cryptography.SHA256]::Create()
    try{return ([BitConverter]::ToString($sha.ComputeHash($Stream))).Replace('-','').ToLowerInvariant()}
    finally{$sha.Dispose()}
}
$package=Join-Path $ProjectDirectory 'FMSC.dacpac'
$contract=Get-Content -Raw (Join-Path $ProjectDirectory 'Core/DatabaseCompatibilityRequirements.json')|ConvertFrom-Json
$approved=Get-Content -Raw (Join-Path $ProjectDirectory 'Core/ApprovedSchemaArtifact.json')|ConvertFrom-Json
$packageStream=[IO.File]::OpenRead($package)
try{$hash=Get-StreamSha256 $packageStream}
finally{$packageStream.Dispose()}
if($hash -ne $contract.schemaArtifact.sha256 -or $hash -ne $approved.packageSha256){
    throw 'DACPAC package differs from the reviewed artifact/compatibility contract. Review it and refresh the approved fingerprints before publishing.'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($package)
try{
    $entry=$zip.GetEntry('model.xml')
    if($null -eq $entry){throw 'DACPAC model.xml is missing.'}
    $stream=$entry.Open()
    try{$modelHash=Get-StreamSha256 $stream}
    finally{$stream.Dispose()}
    if($modelHash -ne $approved.modelSha256){throw 'DACPAC schema/module definitions differ from the reviewed model.'}
}finally{$zip.Dispose()}
Write-Output 'Reviewed DACPAC provenance and SQL model fingerprint verified.'
