# Exercise the full PortableRelease publish from PowerShell 7 in a disposable
# directory. Its prerequisite is a readability fixture, not a real MSI; this
# output is never uploaded/installed. No SQL connection or school data is used.
param([string]$ProjectDirectory = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsWindows -or $PSVersionTable.PSVersion.Major -lt 7) {
    throw 'Run this regression with PowerShell 7 on Windows.'
}
$ProjectDirectory = [IO.Path]::GetFullPath($ProjectDirectory)
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('FMSC publish check ' + [Guid]::NewGuid().ToString('N'))
$modulePathBefore = $env:PSModulePath
try {
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
    $database = Join-Path $scratch 'empty database'
    New-Item -ItemType Directory -Path $database | Out-Null
    $prerequisite = Join-Path $scratch 'readability fixture.txt'
    Set-Content -LiteralPath $prerequisite -Value 'Test fixture; not an installer.'

    # Explicitly make Get-FileHash unusable in Windows PowerShell. The verifier
    # must still validate both SHA256 fingerprints using streaming .NET APIs.
    $withoutFileHash = Join-Path $scratch 'verify without FileHash.ps1'
    @'
param([string]$ProjectDirectory)
$ErrorActionPreference = 'Stop'
$env:PSModulePath = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/Modules'
function Get-FileHash { throw 'This verifier must not call Get-FileHash.' }
& (Join-Path $ProjectDirectory 'Installer/VerifySchemaArtifact.ps1') -ProjectDirectory $ProjectDirectory
'@ | Set-Content -LiteralPath $withoutFileHash
    $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $withoutFileHash -ProjectDirectory $ProjectDirectory 2>&1
    $code = $LASTEXITCODE
    if ($code -ne 0 -or ($output -join "`n") -notmatch 'Reviewed DACPAC provenance and SQL model fingerprint verified\.') {
        throw "DACPAC verification must work without Get-FileHash:`n$($output -join "`n")"
    }
    Write-Host 'PASS: Windows PowerShell verifies package/model SHA256 without Get-FileHash.'

    # Deliberately omit Windows PowerShell modules from the parent environment.
    # The real MSBuild Exec tasks must supply the correct path to both children.
    $env:PSModulePath = Join-Path $PSHOME 'Modules'
    $release = Join-Path $scratch 'release output'
    $application = Join-Path $release 'FMSC_SMS'
    $output = & dotnet publish (Join-Path $ProjectDirectory 'FMSC_SMS.csproj') `
        -c Release -r win-x64 --self-contained true -p:PublishProfile=PortableRelease `
        "-p:PublishDir=$application/" "-p:ReleaseRoot=$release/" `
        "-p:LocalDbMsiPath=$prerequisite" "-p:DeploymentDatabaseDirectory=$database" `
        -p:PublishSingleFile=false -p:PublishTrimmed=false 2>&1
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        throw "Full PortableRelease publish failed from PowerShell 7:`n$($output -join "`n")"
    }
    $text = $output -join "`n"
    if ($text -notmatch 'Reviewed DACPAC provenance and SQL model fingerprint verified\.' -or
        $text -notmatch 'LocalDB prerequisite is present, nonempty and readable\.') {
        throw "Both real validation scripts must execute:`n$text"
    }
    if ($env:PSModulePath -ne (Join-Path $PSHOME 'Modules')) {
        throw 'Validation changed the parent PowerShell module path.'
    }
    foreach ($relative in @('FMSC_SMS/FMSC_SMS.exe', 'FMSC_SMS/FMSC_SMS.dll',
        'FMSC_SMS/FMSC_SMS.deps.json', 'FMSC_SMS/FMSC_SMS.runtimeconfig.json',
        'core.ps1', 'Install FMSC_SMS.bat', 'Tools/SqlLocalDB.msi',
        'Migrations/2.sql', 'Migrations/3.sql', 'Migrations/4.sql', 'Migrations/5.sql', 'Migrations/6.sql', 'Migrations/7.sql')) {
        $file = Join-Path $release $relative
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
            throw "Full publish did not copy its required output: $relative"
        }
    }
    foreach ($pair in @(
        @('Installer/core.ps1', 'core.ps1'),
        @('Migrations/7.sql', 'Migrations/7.sql')
    )) {
        $expected = (Get-FileHash -LiteralPath (Join-Path $ProjectDirectory $pair[0]) -Algorithm SHA256).Hash
        $actual = (Get-FileHash -LiteralPath (Join-Path $release $pair[1]) -Algorithm SHA256).Hash
        if ($actual -ne $expected) { throw "Published installer asset differs from checked-out source: $($pair[1])" }
    }
    # Exercise the actual copied helper, not merely its source-tree counterpart.
    # Use a child scope so the test's mocked process/console commands stay local.
    & (Join-Path $ProjectDirectory 'Tools/TestInstallerLocalDb.ps1') -InstallerPath (Join-Path $release 'core.ps1')
    Write-Host 'PASS: published LocalDB recovery and top-level communication SQL match the checked-out revision.'
    Write-Host 'PASS: full PortableRelease publish, both validation scripts and installer/migration copies with a PowerShell-7-only parent module path.'

    # A disposable project copy proves that provenance rejection still blocks
    # the same target. Never change the reviewed package in the working tree.
    $copy = Join-Path $scratch 'tampered project'
    New-Item -ItemType Directory -Path $copy | Out-Null
    foreach ($relative in @('FMSC_SMS.csproj', 'FMSC.dacpac',
        'Core/ApprovedSchemaArtifact.json', 'Core/DatabaseCompatibilityRequirements.json',
        'Installer/VerifySchemaArtifact.ps1', 'Installer/ValidateReleaseAssets.ps1',
        'Installer/core.ps1', 'Installer/Install FMSC_SMS.bat')) {
        $destination = Join-Path $copy $relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $ProjectDirectory $relative) -Destination $destination
    }
    Copy-Item -LiteralPath (Join-Path $ProjectDirectory 'Migrations') -Destination $copy -Recurse
    $package = [IO.File]::Open((Join-Path $copy 'FMSC.dacpac'), [IO.FileMode]::Append)
    try { $package.WriteByte(0) } finally { $package.Dispose() }
    $output = & dotnet msbuild (Join-Path $copy 'FMSC_SMS.csproj') `
        -target:ValidateDeploymentAssets -nologo -verbosity:minimal `
        "-property:LocalDbMsiPath=$prerequisite" "-property:DeploymentDatabaseDirectory=$database" 2>&1
    $code = $LASTEXITCODE
    if ($code -eq 0 -or ($output -join "`n") -notmatch 'DACPAC package differs from the reviewed artifact') {
        throw "Changed DACPAC did not fail the provenance check:`n$($output -join "`n")"
    }
    Write-Host 'PASS: the MSBuild validation target still rejects an altered DACPAC.'
} finally {
    $env:PSModulePath = $modulePathBefore
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
# GitHub's pwsh wrapper propagates the last native exit code. The deliberate
# rejection above is a passing assertion, rather than a failing workflow step.
$global:LASTEXITCODE = 0
