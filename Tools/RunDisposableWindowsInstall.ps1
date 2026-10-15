# Real installation acceptance on a GitHub-hosted disposable Windows runner.
# Runs the production installer against an approved schema-only, record-free fixture.
param([string]$ProjectDirectory = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or
    $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:FMSC_DISPOSABLE_INSTALL -ne 'approved-synthetic-only') {
    throw 'Refusing to run outside a designated disposable Windows GitHub Actions job.'
}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=[Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Runner lacks elevation needed for the real installer.'
}
$project=[IO.Path]::GetFullPath($ProjectDirectory)
$root=Join-Path $env:RUNNER_TEMP 'fmsc-disposable-installer'
$evidence=Join-Path $env:RUNNER_TEMP 'fmsc-install-evidence'
$release=Join-Path $root 'release'
$app=Join-Path $release 'FMSC_SMS'
$tools=Join-Path $release 'Tools'
$database=Join-Path $release 'Database'
$localMsi=Join-Path $tools 'SqlLocalDB.msi'
$sqlcmd=Join-Path $tools 'sqlcmd.exe'
$instance='(localdb)\MSSQLLocalDB'
$installed='C:\FMSC School Management'
$live='C:\ProgramData\FMSC_Database'
$shortcut=Join-Path ([Environment]::GetFolderPath('Desktop')) 'FMSC SMS.lnk'
$ownedDatabase=$false
$ownedInstallation=$false
$ownedRoot=$false
$fixtureFiles=@()
$cleanupResult='NOT_REQUIRED'
$stage='Runner isolation'
$checks=[Collections.Generic.List[string]]::new()
$status='BLOCKED'
$msiCoverage='NOT_TESTED'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null

function Stage([string]$Name) { $script:stage=$Name; Write-Host "ACCEPTANCE STAGE: $Name" }
function NativeOk([string]$Name) { if ($LASTEXITCODE -ne 0) { throw "$Name exited with code $LASTEXITCODE" } }
function Sql([string]$Db, [string]$Query) {
    $result=@(& $script:sqlcmd -S $script:instance -E -d $Db -b -l 30 -t 60 -h -1 -W -Q $Query 2>&1)
    NativeOk "SQL verification on $Db"
    return $result
}
function LocalDbExe {
    $exe=Get-Command sqllocaldb.exe -ErrorAction SilentlyContinue
    if ($exe) { return $exe.Source }
    $known=Join-Path $env:ProgramFiles 'Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe'
    if (Test-Path -LiteralPath $known -PathType Leaf) { return $known }
    return $null
}
function WriteEvidence {
    $report=[ordered]@{
        sourceCommit=$env:GITHUB_SHA
        result=$script:status
        lastStage=$script:stage
        localDbMsiCoverage=$script:msiCoverage
        cleanup=$script:cleanupResult
        checksPassed=@($script:checks.ToArray())
        limitations=@(
          'GitHub-hosted Windows 2022 is disposable but may include preinstalled LocalDB.',
          'When LocalDB was preinstalled this run does not cover a pristine MSI installation or its interactive fallback.',
          'Only schema-only model and synthetic version marker are installed; no school data is used.',
          'Graphical login, printing, real production upgrades and Windows reboot scenarios remain manual.',
          'A passing run is not authorization to install on live school computers.'
        )
    }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $script:evidence 'report.json') -Encoding utf8
    $summary=@('# FMSC Disposable Windows Installation', '', "Commit: $env:GITHUB_SHA", "Result: **$script:status**",
        "Last stage: $script:stage", "LocalDB MSI: $script:msiCoverage", "Cleanup: $script:cleanupResult", '', '## Completed checks')
    $summary+=@($script:checks | ForEach-Object { "- $_" })
    $summary+=@('','## Coverage limits')
    $summary+=@($report.limitations | ForEach-Object { "- $_" })
    $summary | Set-Content -LiteralPath (Join-Path $script:evidence 'summary.md') -Encoding utf8
    if ($env:GITHUB_STEP_SUMMARY) { $summary | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 }
}

try {
    foreach ($path in @($installed,"$installed.staging","$installed.rollback",$live,$shortcut)) {
        if (Test-Path -LiteralPath $path) { throw 'Pre-existing FMSC installation assets: refusing to touch them.' }
    }
    if (Test-Path -LiteralPath $root) { throw 'Disposable test workspace already exists.' }
    New-Item -ItemType Directory -Path $root,$release,$app,$tools,$database,(Join-Path $root 'empty-source') -Force | Out-Null
    $ownedRoot=$true
    $checks.Add('No existing FMSC application or target database found')

    Stage 'Approved schema-only fixture verification'
    $approved=Get-Content -LiteralPath (Join-Path $project 'Core/ApprovedSchemaArtifact.json') -Raw | ConvertFrom-Json
    $provenance=Get-Content -LiteralPath (Join-Path $project 'Tests/LifecycleSqlChecks/Baseline/provenance.json') -Raw | ConvertFrom-Json
    $fixture=Join-Path $project 'Tests/LifecycleSqlChecks/Baseline/schema.dacpac'
    if ((Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant() -ne $provenance.fixtureSha256 -or
        $provenance.sourcePackageSha256 -ne $approved.packageSha256 -or
        $provenance.sourceModelSha256 -ne $approved.modelSha256) { throw 'Approved schema fixture provenance differs.' }
    Add-Type -AssemblyName System.IO.Compression
    $zip=[IO.Compression.ZipFile]::OpenRead($fixture)
    try {
        $names=@($zip.Entries | ForEach-Object { $_.FullName } | Sort-Object)
        $allowed=@('[Content_Types].xml','_rels/.rels','DacMetadata.xml','model.xml','Origin.xml' | Sort-Object)
        if (($names -join '|') -ne ($allowed -join '|')) { throw 'Unexpected DACPAC entries, possibly exported table data.' }
        $stream=$zip.GetEntry('model.xml').Open()
        try { $modelHash=(Get-FileHash -InputStream $stream -Algorithm SHA256).Hash.ToLowerInvariant() }
        finally { $stream.Dispose() }
        if ($modelHash -ne $approved.modelSha256) { throw 'Approved schema model changed.' }
    } finally { $zip.Dispose() }
    $checks.Add('Schema-only DACPAC hash, model and data-free entries verified')

    Stage 'Real Microsoft SQL Server LocalDB prerequisite'
    $existing=LocalDbExe
    $downloadedMsi=Join-Path $root 'SqlLocalDB.msi'
    Invoke-WebRequest -Uri 'https://download.microsoft.com/download/3/8/d/38de7036-2433-4207-8eae-06e247e17b25/SqlLocalDB.msi' -OutFile $downloadedMsi -MaximumRedirection 5
    if ((Get-Item -LiteralPath $downloadedMsi).Length -lt 1000000) { throw 'LocalDB MSI is incomplete.' }
    $sig=Get-AuthenticodeSignature -LiteralPath $downloadedMsi
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
        throw 'LocalDB MSI Microsoft signature validation failed.'
    }
    Copy-Item -LiteralPath $downloadedMsi -Destination $localMsi
    if ($existing) {
        $msiCoverage='PREINSTALLED_NOT_A_CLEAN_MSI_TEST'
        Write-Host 'LocalDB already present in GitHub runner image; MSI installation was NOT exercised.'
    } else {
        $msiCoverage='REAL_SILENT_MSI_ATTEMPTED'
        $log=Join-Path $root 'localdb-msi-private.log'
        $p=Start-Process -FilePath 'msiexec.exe' -ArgumentList @(
            '/i', ('"' + $downloadedMsi + '"'), '/qn', '/norestart', 'IACCEPTSQLLOCALDBLICENSETERMS=YES', '/L*V', ('"' + $log + '"')
        ) -Wait -PassThru
        if ($p.ExitCode -in @(3010,1641)) { throw 'LocalDB requires a Windows restart; acceptance is blocked until reboot.' }
        if ($p.ExitCode -ne 0) { throw "Real silent LocalDB MSI failed with code $($p.ExitCode); interactive recovery needs an operator." }
        $msiCoverage='REAL_SILENT_MSI_SUCCEEDED'
    }
    $localExe=LocalDbExe
    if (-not $localExe) { throw 'LocalDB executable missing after setup.' }
    $null=& $localExe info MSSQLLocalDB 2>&1
    if ($LASTEXITCODE -ne 0) {
        & $localExe create MSSQLLocalDB 16.0
        NativeOk 'Create isolated-runner SQL Server 2022 LocalDB instance'
    }
    & $localExe start MSSQLLocalDB
    NativeOk 'Start LocalDB instance'
    $info=@(& $localExe info MSSQLLocalDB 2>&1)
    NativeOk 'Read owned-runner LocalDB endpoint'
    $pipe=[regex]::Match(($info -join "`n"), '(?:np:)?(\\\\\.\\pipe\\LOCALDB#[A-Za-z0-9]+\\tsql\\query)')
    if (-not $pipe.Success) { throw 'Cannot resolve the local LocalDB named pipe; no database operations permitted.' }
    $instance='np:'+$pipe.Groups[1].Value
    $checks.Add('Actual LocalDB instance started using Microsoft-signed prerequisites')

    Stage 'Pinned SQL command and full installer publishing'
    $archive=Join-Path $root 'sqlcmd-windows-amd64.zip'
    Invoke-WebRequest -Uri 'https://github.com/microsoft/go-sqlcmd/releases/download/v1.10.0/sqlcmd-windows-amd64.zip' -OutFile $archive -MaximumRedirection 5
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne 'A4C28332FCC6E497D655E53AC8F4939F4AB170F9CFD32D0FA5081B60F4D9D691') {
        throw 'Pinned sqlcmd checksum validation failed.'
    }
    $unzip=Join-Path $root 'sqlcmd-extracted'
    Expand-Archive -LiteralPath $archive -DestinationPath $unzip
    $tool=Get-ChildItem -LiteralPath $unzip -Filter sqlcmd.exe -File -Recurse | Select-Object -First 1
    if (-not $tool) { throw 'Verified sqlcmd download does not contain executable.' }
    Copy-Item -LiteralPath $tool.FullName -Destination $sqlcmd
    & dotnet restore (Join-Path $project 'FMSC_SMS.sln')
    NativeOk 'dotnet restore'
    $arguments=@('publish',(Join-Path $project 'FMSC_SMS.csproj'),'-c','Release','-r','win-x64',
        '--self-contained','true','-p:PublishProfile=PortableRelease',
        "-p:PublishDir=$app/","-p:ReleaseRoot=$release/",
        "-p:LocalDbMsiPath=$localMsi",
        "-p:DeploymentDatabaseDirectory=$(Join-Path $root 'empty-source')",
        '-p:PublishSingleFile=false','-p:PublishTrimmed=false')
    & dotnet @arguments
    NativeOk 'Full PortableRelease publish'
    foreach ($file in @('FMSC_SMS/FMSC_SMS.exe','core.ps1','Install FMSC_SMS.bat',
        'Tools/SqlLocalDB.msi','Tools/sqlcmd.exe','Migrations/6.sql','Migrations/7.sql')) {
        if (-not (Test-Path -LiteralPath (Join-Path $release $file) -PathType Leaf)) {
            throw "Real installer asset missing: $file"
        }
    }
    $checks.Add('Actual validated PortableRelease published with MSI, sqlcmd and migrations')

    Stage 'Provision and detach synthetic version-6 LocalDB'
    $prior=@(Sql 'master' "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name=N'FMSC_DB';")
    if (($prior -join '').Trim() -ne '0') { throw 'FMSC_DB already exists; refusing to overwrite it.' }
    $ownedDatabase=$true # Catalog absent on this explicitly isolated runner; covers partial publish failure too.
    Push-Location (Join-Path $project 'Tests/LifecycleSqlChecks')
    try {
        & dotnet tool restore
        NativeOk 'Pinned SQLPackage tool restore'
        $conn="Server=$instance;Database=FMSC_DB;Integrated Security=True;Encrypt=False;TrustServerCertificate=True"
        & dotnet tool run sqlpackage -- '/Action:Publish' "/SourceFile:$fixture" "/TargetConnectionString:$conn" '/p:CreateNewDatabase=True'
        NativeOk 'Publish approved schema-only DACPAC'
    } finally { Pop-Location }
    $seed=@'
SET NOCOUNT ON;
IF EXISTS(SELECT 1 FROM dbo.DatabaseVersion) THROW 51000, 'Nonempty version fixture', 1;
IF EXISTS(SELECT 1 FROM dbo.Students) THROW 51001, 'Unexpected student rows', 1;
IF EXISTS(SELECT 1 FROM dbo.Users) THROW 51002, 'Unexpected user rows', 1;
INSERT INTO dbo.DatabaseVersion(VersionNumber) VALUES(N'6');
DECLARE @Scope uniqueidentifier=NEWID();
EXEC sys.sp_updateextendedproperty @name=N'FMSC_QuickRegistrationScope',@value=@Scope;
'@
    Sql 'FMSC_DB' $seed | Out-Null
    $paths=@(& $sqlcmd -S $instance -E -d 'FMSC_DB' -b -h -1 -W -Q 'SET NOCOUNT ON; SELECT physical_name FROM sys.database_files;' 2>&1)
    NativeOk 'Find fixture MDF and LDF'
    $mdf=$paths | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ -match '^[A-Za-z]:\\.+\.mdf$' } | Select-Object -First 1
    $ldf=$paths | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ -match '^[A-Za-z]:\\.+\.ldf$' } | Select-Object -First 1
    if (-not $mdf -or -not $ldf) { throw 'Synthetic LocalDB MDF/LDF paths unavailable.' }
    $fixtureFiles=@($mdf,$ldf)
    Sql 'master' "ALTER DATABASE [FMSC_DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; EXEC sys.sp_detach_db @dbname=N'FMSC_DB';" | Out-Null
    Copy-Item -LiteralPath $mdf -Destination (Join-Path $database 'FMSC_DB.mdf')
    Copy-Item -LiteralPath $ldf -Destination (Join-Path $database 'FMSC_DB_log.ldf')
    $checks.Add('Generated a record-free version-6 MDF/LDF pair from the reviewed SQL model')

    Stage 'Actual FMSC Windows installer execution'
    $installer=Join-Path $release 'core.ps1'
    $psi=[Diagnostics.ProcessStartInfo]::new('powershell.exe')
    foreach ($a in @('-NoProfile','-ExecutionPolicy','Bypass','-File',$installer)) { [void]$psi.ArgumentList.Add($a) }
    $psi.UseShellExecute=$false
    $psi.RedirectStandardInput=$true
    $psi.RedirectStandardOutput=$true
    $psi.RedirectStandardError=$true
    $ownedInstallation=$true
    $p=[Diagnostics.Process]::Start($psi)
    if (-not $p) { throw 'Failed to start installer process.' }
    $stdout=$p.StandardOutput.ReadToEndAsync()
    $stderr=$p.StandardError.ReadToEndAsync()
    $p.StandardInput.WriteLine('N')  # Do not apply optional Communication Center migration.
    $p.StandardInput.WriteLine('N')  # Do not start interactive WinForms UI.
    $p.StandardInput.Close()
    if (-not $p.WaitForExit(720000)) {
        try { $p.Kill($true) } catch { }
        throw 'Installer timed out; never claim success.'
    }
    $installerOutput=$stdout.GetAwaiter().GetResult()
    $installerError=$stderr.GetAwaiter().GetResult()
    # Retain synthetic diagnostics on the disposable runner only, never as artifacts.
    $installerOutput | Set-Content -LiteralPath (Join-Path $root 'installer-private.log')
    if ($p.ExitCode -ne 0) {
        $failure=Select-String -LiteralPath (Join-Path $live 'Install.log') -Pattern 'ERROR:' -ErrorAction SilentlyContinue | Select-Object -Last 1
        throw "Actual installer failed with exit code $($p.ExitCode). $($failure.Line) $installerError"
    }
    $installLog=Join-Path $live 'Install.log'
    if (-not (Test-Path -LiteralPath $installLog) -or
        -not (Select-String -LiteralPath $installLog -Pattern 'FMSC SMS installation completed successfully\.' -Quiet)) {
        throw 'Installer did not log its success marker.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $installed 'FMSC_SMS.exe')) -or
        -not (Test-Path -LiteralPath (Join-Path $live 'FMSC_DB.mdf')) -or
        -not (Test-Path -LiteralPath (Join-Path $live 'FMSC_DB_log.ldf'))) {
        throw 'Installer did not activate application and attach the synthetic database.'
    }
    $checks.Add('Ran actual core.ps1; application and LocalDB database installed successfully')

    Stage 'Installed app schema verification'
    $version=@(Sql 'FMSC_DB' 'SET NOCOUNT ON; SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC;')
    if (($version -join '').Trim() -ne '6') { throw 'Wrong schema version installed.' }
    $exe=Join-Path $installed 'FMSC_SMS.exe'
    $verify=Start-Process -FilePath $exe -ArgumentList @('--verify-installation',('"'+$instance+'"'),'FMSC_DB') -PassThru
    $null=$verify.Handle
    if (-not $verify.WaitForExit(180000)) { $verify.Kill(); throw 'Installed verification timed out.' }
    if ($verify.ExitCode -ne 10) { throw "Installed FMSC CLI verification failed (expected base-compatible/module-absent code 10; got $($verify.ExitCode))." }
    Sql 'FMSC_DB' @'
IF EXISTS(SELECT 1 FROM dbo.Students) OR EXISTS(SELECT 1 FROM dbo.Users)
    THROW 51003,'Installer unexpectedly created student/user data',1;
IF (SELECT COUNT(*) FROM sys.database_files WHERE physical_name IN
    (N'C:\ProgramData\FMSC_Database\FMSC_DB.mdf',N'C:\ProgramData\FMSC_Database\FMSC_DB_log.ldf'))<>2
    THROW 51004,'Installed database does not use the expected copied file pair',1;
'@ | Out-Null
    $checks.Add('Installed application launched in verification mode and accepted base SQL schema')
    $status='PASS'
    Write-Host 'PASS: Disposable real Windows installer and synthetic LocalDB acceptance.'
}
catch {
    if ($stage -notin @('Runner isolation','Approved schema-only fixture verification')) { $status='FAIL' }
    Write-Host "Installation acceptance stopped at '$stage': $($_.Exception.Message)"
    throw
}
finally {
    try {
        if ($ownedDatabase) {
            Sql 'master' "IF DB_ID(N'FMSC_DB') IS NOT NULL BEGIN ALTER DATABASE [FMSC_DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [FMSC_DB]; END;" | Out-Null
            if (((@(Sql 'master' "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name=N'FMSC_DB';")) -join '').Trim() -ne '0') {
                throw 'Owned disposable database still exists after cleanup.'
            }
        }
        foreach ($path in $fixtureFiles) {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
        if ($ownedInstallation) {
            foreach ($path in @($installed,"$installed.staging","$installed.rollback",$live,$shortcut)) {
                if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
            }
        }
        if ($ownedRoot) { Remove-Item -LiteralPath $root -Recurse -Force }
        $cleanupResult='PASS'
    }
    catch { $cleanupResult='FAIL'; $status='FAIL'; throw }
    finally { WriteEvidence }
}
