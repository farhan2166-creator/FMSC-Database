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
    if ($text -notmatch 'LocalDB prerequisite is present, nonempty and readable\.') {
        throw "Installer asset validation must execute:`n$text"
    }
    if ($env:PSModulePath -ne (Join-Path $PSHOME 'Modules')) {
        throw 'Validation changed the parent PowerShell module path.'
    }
    foreach ($relative in @('FMSC_SMS/FMSC_SMS.exe', 'FMSC_SMS/FMSC_SMS.dll',
        'FMSC_SMS/FMSC_SMS.deps.json', 'FMSC_SMS/FMSC_SMS.runtimeconfig.json',
        'core.ps1', 'Install FMSC_SMS.bat', 'Tools/SqlLocalDB.msi')) {
        $file = Join-Path $release $relative
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
            throw "Full publish did not copy its required output: $relative"
        }
    }
    foreach ($pair in @(
        ,@('Installer/core.ps1', 'core.ps1')
    )) {
        $expected = (Get-FileHash -LiteralPath (Join-Path $ProjectDirectory $pair[0]) -Algorithm SHA256).Hash
        $actual = (Get-FileHash -LiteralPath (Join-Path $release $pair[1]) -Algorithm SHA256).Hash
        if ($actual -ne $expected) { throw "Published installer asset differs from checked-out source: $($pair[1])" }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $release 'Migrations') -PathType Container)) { throw 'Package must include its migrations folder even when empty.' }
    $sourceSql = @(Get-ChildItem -LiteralPath (Join-Path $ProjectDirectory 'Migrations') -Filter '*.sql' -File)
    $copiedSql = @(Get-ChildItem -LiteralPath (Join-Path $release 'Migrations') -Filter '*.sql' -File)
    if ($sourceSql.Count -ne $copiedSql.Count) { throw 'Packaged migration inventory differs from source.' }
    # Exercise the actual copied helper, not merely its source-tree counterpart.
    # Use a child scope so the test's mocked process/console commands stay local.
    & (Join-Path $ProjectDirectory 'Tools/TestInstallerLocalDb.ps1') -InstallerPath (Join-Path $release 'core.ps1')
    & (Join-Path $ProjectDirectory 'Tools/TestInstallerMigrations.ps1') -InstallerPath (Join-Path $release 'core.ps1')
    # Exercise the real published WinExe's headless path. These targets must be
    # refused before any network/SQL connection or interactive startup occurs.
    foreach ($arguments in @(
        @('--verify-installation', 'remote.invalid', 'FMSC_DB'),
        @('--verify-installation', '(localdb)\MSSQLLocalDB', 'master'),
        @('--verify-installation'),
        @('--install-communication', 'remote.invalid', 'FMSC_DB', 'missing.sql')
    )) {
        $process = Start-Process -FilePath (Join-Path $application 'FMSC_SMS.exe') -ArgumentList $arguments -PassThru
        if (-not $process.WaitForExit(10000)) { $process.Kill(); throw 'Installer verification unexpectedly opened interactive startup.' }
        if ($process.ExitCode -ne 23) { throw "Installer verification did not refuse an unsafe target: $($process.ExitCode)" }
    }
    Write-Host 'PASS: published installer CLI refuses remote, wrong-catalog and incomplete targets without interactive startup.'
    Write-Host 'PASS: published installer matches the checked-out revision and migration inventory.'
    Write-Host 'PASS: full PortableRelease publish, asset validation and installer/migration copies with a PowerShell-7-only parent module path.'

} finally { $package.Dispose() }
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
