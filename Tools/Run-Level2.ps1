# Run from an unlocked operator desktop. Never accepts a database connection string,
# school MDF/LDF, existing catalog or existing SQL instance as input.
param(
    [string]$PythonPath,
    [switch]$PreflightOnly,
    [switch]$StartupOnly,
    [switch]$AdmissionsOnly,
    [switch]$FamilyCounterOnly
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$owner=[guid]::NewGuid()
$catalog='FMSC_Level2_'+$owner.ToString('N')
$run=Join-Path $repo "artifacts/level2/private/$catalog"
$evidence=Join-Path $repo "artifacts/level2/evidence/$catalog"
$stage='Environment'
$engine='Not reached'
$created=$false
$instanceCreationAttempted=$false
$binaryHash=$null
$schemaModel=$null
$fixtureHash=$null
$desktopGate=$null
$ownsDesktopGate=$false
$exitCode=1
New-Item -ItemType Directory -Path $run,$evidence,(Join-Path $run 'database') | Out-Null
$owner.ToString('D') | Set-Content -LiteralPath (Join-Path $run 'owner.txt')
$saved=@{}
foreach($key in @('FMSC_LEVEL2_OWNER','FMSC_LEVEL2_ROOT','FMSC_LEVEL2_COMMIT','FMSC_LEVEL2_PROBE_BINARY','FMSC_LEVEL2_STARTUP_ONLY','FMSC_LEVEL2_ADMISSIONS_ONLY','MSBuildEnableWorkloadResolver')) {
    $saved[$key]=[Environment]::GetEnvironmentVariable($key)
}
$env:FMSC_LEVEL2_OWNER=$owner.ToString('D')
$env:FMSC_LEVEL2_ROOT=$run
$env:FMSC_LEVEL2_COMMIT=(& git -C $repo rev-parse HEAD)
$env:FMSC_LEVEL2_STARTUP_ONLY=if($StartupOnly){'1'}else{'0'}
$env:FMSC_LEVEL2_ADMISSIONS_ONLY=if($AdmissionsOnly){'1'}else{'0'}
$workingTreeDirty=-not [string]::IsNullOrWhiteSpace((& git -C $repo status --porcelain | Out-String))
$env:MSBuildEnableWorkloadResolver='false'
function Native([string]$Operation) { if($LASTEXITCODE -ne 0) {throw "$Operation failed ($LASTEXITCODE)."} }
function Stage([string]$Name) {$script:stage=$Name;Write-Host "LEVEL 2: $Name"}
function Owned {
    if([IO.Path]::GetFileName([IO.Path]::GetFullPath($script:run)) -ne $script:catalog -or
        (Get-Content -LiteralPath (Join-Path $script:run 'owner.txt') -Raw).Trim() -ne $script:owner.ToString('D')) {
        throw 'Directory ownership differs.'
    }
}
function Sql([string]$Database,[string]$Query) {
    Owned
    $output=@(& sqlcmd -S "(localdb)\$script:catalog" -E -d $Database -b -l 20 -t 90 -h -1 -W -Q $Query 2>&1)
    Native 'Isolated SQL operation'
    return $output
}
function VerifyTarget {
    $result=Sql $script:catalog @"
SET NOCOUNT ON;
IF DB_NAME()<>N'$script:catalog' OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_Level2Owner' AND CONVERT(nvarchar(36),value)=N'$script:owner')
    THROW 51000,'Level 2 owner mismatch',1;
IF (SELECT COUNT(*) FROM sys.databases WHERE database_id>4)<>1
    THROW 51000,'Unexpected other catalog on private instance',1;
IF EXISTS(SELECT 1 FROM sys.database_files WHERE LEFT(physical_name,LEN(N'$($script:run.Replace("'","''"))\database\'))<>N'$($script:run.Replace("'","''"))\database\')
    THROW 51000,'Level 2 files outside private directory',1;
SELECT N'OWNED';
"@
    if(($result -join '').Trim() -ne 'OWNED') {throw 'Database ownership verification failed.'}
}
function BlockedReport([string]$Reason) {
    if(Test-Path -LiteralPath (Join-Path $script:evidence 'report.json')) {return}
    $rows=Get-Content -LiteralPath (Join-Path $repo 'Tests/Level2UiChecks/scenarios.json') -Raw | ConvertFrom-Json
    foreach($row in $rows) {
        $row | Add-Member Actual $Reason -Force
        $row | Add-Member Status 'Blocked' -Force
        $row | Add-Member Evidence 'environment.json' -Force
        $row | Add-Member Defect 'Environment prerequisite; no UI success claimed.' -Force
        $row | Add-Member DatabaseVerification 'Not executed.' -Force
    }
    [ordered]@{level=2;sourceCommit=$env:FMSC_LEVEL2_COMMIT;runId=$script:owner;stage=$script:stage;
        total=$rows.Count;counts=@{Pass=0;Fail=0;Blocked=$rows.Count;'Not Run'=0};scenarios=$rows;level3Ready=$false} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $script:evidence 'report.json') -Encoding utf8
    $summary=@('# FMSC_SMS Level 2 desktop evidence','',"Blocked at $script:stage. $Reason",'',
        "Total: $($rows.Count); Pass: 0; Fail: 0; Blocked: $($rows.Count); Not Run: 0",'',
        '| Test ID | Module | Scenario | Expected | Actual | Status | Evidence | Defect | Database verification |',
        '|---|---|---|---|---|---|---|---|---|')
    $summary+=@($rows | ForEach-Object {"| $($_.Id) | $($_.Module) | $($_.ScenarioName) | $($_.Expected) | $Reason | Blocked | environment.json | Environment prerequisite | Not executed |"})
    $summary+=@('','Readiness for Level 3: Not established.')
    $summary | Set-Content -LiteralPath (Join-Path $script:evidence 'summary.md') -Encoding utf8
}
try {
    if([int][bool]$PreflightOnly+[int][bool]$StartupOnly+[int][bool]$AdmissionsOnly+[int][bool]$FamilyCounterOnly -gt 1) {throw 'Choose only one scoped mode.'}
    if(-not $IsWindows -and $PSVersionTable.PSEdition -ne 'Desktop') {throw 'Windows is required.'}
    $desktopGate=[Threading.Mutex]::new($false,'Local\FMSC_SMS.Level2Desktop')
    try {$ownsDesktopGate=$desktopGate.WaitOne(0)}
    catch [Threading.AbandonedMutexException] {$ownsDesktopGate=$true}
    if(-not $ownsDesktopGate) {throw 'Another Level 2 run owns this desktop.'}
    $oldApps=@(Get-Process FMSC_SMS -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -and $_.Path.StartsWith((Join-Path $repo 'artifacts/level2/private'),[StringComparison]::OrdinalIgnoreCase)
    })
    if($oldApps.Count -gt 0) {throw 'An earlier fictional test application is still open; clean up that exact run first.'}
    # Generated fixture credentials and recovery files are local to this operator.
    $acl=Get-Acl -LiteralPath $run
    $acl.SetAccessRuleProtection($true,$false)
    $rule=[Security.AccessControl.FileSystemAccessRule]::new(
        [Security.Principal.WindowsIdentity]::GetCurrent().User,'FullControl',
        'ContainerInherit,ObjectInherit','None','Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $run -AclObject $acl
    if(-not $PythonPath) {
        $pythonCommand=Get-Command python -ErrorAction SilentlyContinue
        if($pythonCommand -and $pythonCommand.Source -notlike '*WindowsApps*') {$PythonPath=$pythonCommand.Source}
        else {$PythonPath=Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'}
    }
    foreach($tool in @('dotnet','SqlLocalDB','sqlcmd',$PythonPath)) {Get-Command $tool -ErrorAction Stop | Out-Null}
    Stage 'Data-free SQL fixture validation'
    Push-Location $repo
    try {
        $schemaModel=& $PythonPath -c "import runpy, hashlib, zipfile; m=runpy.run_path('Tests/LifecycleSqlChecks/run.py'); m['verify_baseline'](); print(hashlib.sha256(zipfile.ZipFile('Tests/LifecycleSqlChecks/Baseline/schema.dacpac').read('model.xml')).hexdigest())"
        Native 'Data-free SQL fixture validation'
        $fixtureHash=(Get-FileHash -LiteralPath (Join-Path $repo 'Tests/LifecycleSqlChecks/Baseline/schema.dacpac') -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Host 'PASS: existing Level 1 data-free fixture validation; observed hashes recorded as evidence, not approval fingerprints.'
    } finally {Pop-Location}
    Stage 'Build application and external UI driver'
    $appOutput=Join-Path $run 'app'
    & dotnet build (Join-Path $repo 'FMSC_SMS.csproj') -c Release '-p:FmscLevel2=true' -o $appOutput -v quiet
    Native 'Test application build'
    $binaryHash=(Get-FileHash -LiteralPath (Join-Path $appOutput 'FMSC_SMS.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    foreach($project in @('Level2Fixture','Level2UiChecks')) {
        & dotnet build (Join-Path $repo "Tests/$project/$project.csproj") -c Release -o (Join-Path $run $project) -v quiet
        Native "$project build"
    }
    if($FamilyCounterOnly) {
        & dotnet build (Join-Path $repo 'Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj') -c Release '-p:FmscLevel2=true' -o (Join-Path $run 'FamilyCounterChecks') -v quiet
        Native 'Family counter native/SQL harness build'
    }
    & dotnet (Join-Path $appOutput 'FMSC_SMS.dll') '--verify-level2-guards'
    Native 'Operational target rejection regressions'
    $env:FMSC_LEVEL2_PROBE_BINARY=Join-Path $appOutput 'FMSC_SMS.dll'
    & $PythonPath -m unittest discover -s (Join-Path $repo 'Tests/Level2UiChecks') -p 'test_*.py' -v
    Native 'Level 2 isolation and report safety regressions'
    if($PreflightOnly) {BlockedReport 'Preflight-only run; UI scenarios deliberately not executed.';$exitCode=2}
    else {
    Stage 'Create unique private LocalDB instance'
    Owned
    $instanceCreationAttempted=$true
    & SqlLocalDB create $catalog -s
    Native 'Create private LocalDB instance'
    $created=$true
    $engine=((Sql master "SET NOCOUNT ON; SELECT CONVERT(nvarchar(40),SERVERPROPERTY('ProductVersion'));" ) -join '').Trim()
    $escaped=$run.Replace("'","''")
    Sql master @"
IF DB_ID(N'$catalog') IS NOT NULL THROW 51000,'Unexpected existing catalog',1;
CREATE DATABASE [$catalog] ON PRIMARY (NAME=N'$catalog',FILENAME=N'$escaped\database\fixture.mdf')
LOG ON (NAME=N'${catalog}_log',FILENAME=N'$escaped\database\fixture_log.ldf');
"@ | Out-Null
    Sql $catalog "EXEC sys.sp_addextendedproperty @name=N'FMSC_Level2Owner',@value=N'$owner';" | Out-Null
    VerifyTarget
    Stage 'Publish verified record-free test schema'
    Push-Location (Join-Path $repo 'Tests/LifecycleSqlChecks')
    try {
        & dotnet tool restore
        Native 'Pinned SQLPackage restore'
        VerifyTarget
        $fixture=Join-Path $repo 'Tests/LifecycleSqlChecks/Baseline/schema.dacpac'
        $connection="Server=(localdb)\$catalog;Initial Catalog=$catalog;Integrated Security=True;TrustServerCertificate=True"
        & dotnet tool run sqlpackage -- '/Action:Publish' "/SourceFile:$fixture" "/TargetConnectionString:$connection" '/p:DropObjectsNotInSource=False' '/p:DropExtendedPropertiesNotInSource=False' '/p:ScriptDatabaseOptions=False' '/Quiet:True'
        Native 'Publish approved schema'
        Sql $catalog "INSERT dbo.DatabaseVersion(VersionNumber) VALUES(N'6');" | Out-Null
        foreach($migration in @(Get-ChildItem (Join-Path $repo 'Tests/LifecycleSqlChecks/Migrations') -Filter '*.sql'; Get-ChildItem (Join-Path $repo 'Migrations') -Filter '*.sql') | Sort-Object {[int]$_.BaseName}) {
            if([int]$migration.BaseName -le 6 -or [int]$migration.BaseName -gt 20){continue}
            VerifyTarget
            $migrationScript=Join-Path $run ('migration-'+$migration.BaseName+'.sql')
            "SET XACT_ABORT ON;`nBEGIN TRANSACTION;`n"+(Get-Content -LiteralPath $migration.FullName -Raw)+"`nCOMMIT TRANSACTION;" | Set-Content -LiteralPath $migrationScript -Encoding utf8
            & sqlcmd -S "(localdb)\$catalog" -E -d $catalog -b -l 20 -t 90 -i $migrationScript | Out-Null
            Native "Disposable migration $($migration.BaseName)"
        }

    } finally {Pop-Location}
    Stage 'Seed fictional reference data'
    VerifyTarget
    & dotnet (Join-Path $run 'Level2Fixture/Level2Fixture.dll')
    Native 'Reference fixture'
    VerifyTarget
    if($FamilyCounterOnly) {
        Stage 'Family counter SQL scenarios (no operator UI or physical printing)'
        & dotnet (Join-Path $run 'FamilyCounterChecks/ThemeSmokeChecks.dll') '--family-counter-sql'
        $exitCode=$LASTEXITCODE
        [ordered]@{sourceCommit=$env:FMSC_LEVEL2_COMMIT;status=if($exitCode -eq 0){'Pass'}else{'Fail'};
            scope='Owned fictional family SQL scenarios only; no Level 2 interactive acceptance claimed.'} |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'family-counter-results.json') -Encoding utf8
    } else {
        Stage 'Interactive operator workflows'
        & dotnet (Join-Path $run 'Level2UiChecks/Level2UiChecks.dll') (Join-Path $appOutput 'FMSC_SMS.exe') $evidence
        $exitCode=$LASTEXITCODE
    }
    }
} catch {
    # Exception text/native output may contain machine details; publish a bounded reason only.
    BlockedReport "Prerequisite failed at $stage ($($_.Exception.GetType().Name)); no dependent UI outcome is claimed."
    Write-Warning "Level 2 stopped at $stage. Inspect local console diagnostics."
} finally {
    $cleanup='Not required'
    if($created) {
        try {
            Owned
            VerifyTarget
            # No record cleanup; remove only this run's verified private instance.
            & SqlLocalDB stop $catalog -k
            Native 'Stop owned instance'
            & SqlLocalDB delete $catalog
            Native 'Delete owned instance'
            $cleanup='Pass: owned instance removed'
            $expectedParent=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/level2/private'))+[IO.Path]::DirectorySeparatorChar
            $resolved=[IO.Path]::GetFullPath($run)
            if(-not $resolved.StartsWith($expectedParent,[StringComparison]::OrdinalIgnoreCase)) {throw 'Cleanup path outside private workspace.'}
            Remove-Item -LiteralPath $resolved -Recurse -Force
        } catch {$cleanup='Fail: retained private state; inspect ownership before cleanup';$exitCode=1}
    }
    elseif(-not $instanceCreationAttempted) {
        try {
            Owned
            $expectedParent=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/level2/private'))+[IO.Path]::DirectorySeparatorChar
            $resolved=[IO.Path]::GetFullPath($run)
            if(-not $resolved.StartsWith($expectedParent,[StringComparison]::OrdinalIgnoreCase) -or
                (Get-ChildItem -LiteralPath (Join-Path $run 'database') -File).Count -ne 0) {throw 'Unexpected private cleanup target.'}
            Remove-Item -LiteralPath $resolved -Recurse -Force
            $cleanup='Pass: no instance attempted; owned private build files removed'
        } catch {$cleanup='Fail: private preflight state retained';$exitCode=1}
    }
    [ordered]@{sourceCommit=$env:FMSC_LEVEL2_COMMIT;stage=$stage;cleanup=$cleanup;
        engine=$engine;schemaModel=$schemaModel;fixtureSha256=$fixtureHash;
        windows=[Environment]::OSVersion.Version.ToString();workingTreeDirty=$workingTreeDirty;applicationSha256=$binaryHash;
        utc=[DateTime]::UtcNow.ToString('o')} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'environment.json') -Encoding utf8
    $reportPath=Join-Path $evidence 'report.json'
    if(Test-Path -LiteralPath $reportPath) {
        $report=Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $report | Add-Member cleanup $cleanup -Force
        $report | Add-Member workingTreeDirty $workingTreeDirty -Force
        $report | Add-Member applicationSha256 $binaryHash -Force
        $report.level3Ready=($report.counts.Pass -eq $report.total -and $cleanup -like 'Pass:*')
        $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportPath -Encoding utf8
        Add-Content -LiteralPath (Join-Path $evidence 'summary.md') -Value "`nCleanup: $cleanup"
    }
    foreach($key in $saved.Keys) {[Environment]::SetEnvironmentVariable($key,$saved[$key])}
    if($ownsDesktopGate) {$desktopGate.ReleaseMutex()}
    if($desktopGate) {$desktopGate.Dispose()}
    Write-Host "Level 2 evidence: $evidence"
}
exit $exitCode
