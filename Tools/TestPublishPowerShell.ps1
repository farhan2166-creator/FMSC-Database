# Exercise the actual MSBuild prerequisite target from PowerShell 7. No publish,
# installation or SQL connection occurs; the file fixture tests readability only.
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

    # Deliberately omit Windows PowerShell modules from the parent environment.
    # The real MSBuild Exec tasks must supply the correct path to both children.
    $env:PSModulePath = Join-Path $PSHOME 'Modules'
    $output = & dotnet msbuild (Join-Path $ProjectDirectory 'FMSC_SMS.csproj') `
        -target:ValidateDeploymentAssets -nologo -verbosity:minimal `
        "-property:LocalDbMsiPath=$prerequisite" "-property:DeploymentDatabaseDirectory=$database" 2>&1
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        throw "Publish prerequisite validation failed from PowerShell 7:`n$($output -join "`n")"
    }
    $text = $output -join "`n"
    if ($text -notmatch 'Reviewed DACPAC provenance and SQL model fingerprint verified\.' -or
        $text -notmatch 'LocalDB prerequisite is present, nonempty and readable\.') {
        throw "Both real validation scripts must execute:`n$text"
    }
    if ($env:PSModulePath -ne (Join-Path $PSHOME 'Modules')) {
        throw 'Validation changed the parent PowerShell module path.'
    }
    Write-Host 'PASS: both MSBuild validation scripts run with a PowerShell-7-only parent module path.'

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
