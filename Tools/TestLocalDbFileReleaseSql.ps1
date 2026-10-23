# Own one fresh LocalDB instance with synthetic data. No production connection
# or instance name is accepted, and no school database is copied or stopped.
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $project 'Installer/Stop FMSC LocalDB.ps1')
$owner=[guid]::NewGuid().ToString('N')
$instance='FMSC_StopChecks_'+$owner
$run=Join-Path $project ('artifacts/localdb-file-release/'+$owner)
$data=Join-Path $run 'fixture.mdf';$log=Join-Path $run 'fixture_log.ldf'
$directory=Get-FmscInstanceDirectory $instance
$created=$false;$connection=$null;$checks=0
function Check($Value,$Message){if(-not $Value){throw $Message};$script:checks++}
function Native($Message){if($LASTEXITCODE -ne 0){throw $Message}}
try {
    New-Item -ItemType Directory -Path $run -Force | Out-Null
    & SqlLocalDB.exe create $instance -s | Out-Host
    $created=Test-Path -LiteralPath $directory -PathType Container
    Native 'Creating the isolated LocalDB instance failed.'
    $endpoints=@(Get-FmscEndpointCandidates $directory)
    if(-not $endpoints.Count){throw 'No endpoint for the owned fixture.'}
    $endpoint=$endpoints[0]
    $connection=[Data.SqlClient.SqlConnection]::new("Server=$endpoint;Database=master;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5;Pooling=False")
    $connection.Open();$command=$connection.CreateCommand();$command.CommandTimeout=30
    $safeData=$data.Replace("'","''");$safeLog=$log.Replace("'","''")
    $command.CommandText="CREATE DATABASE [ReleaseFixture] ON PRIMARY(NAME=N'FixtureData',FILENAME=N'$safeData') LOG ON(NAME=N'FixtureLog',FILENAME=N'$safeLog');"
    [void]$command.ExecuteNonQuery()
    $command.CommandText='CREATE TABLE ReleaseFixture.dbo.Sample(Value int NOT NULL); INSERT ReleaseFixture.dbo.Sample VALUES(42);'
    [void]$command.ExecuteNonQuery()
    Check (-not (Get-FmscReleaseState $directory $data $log).Ready) 'The real SQL engine holds its attached database pair.'
    try { [void](Invoke-FmscVerifiedShutdown $endpoint (Join-Path $run 'different-instance'));throw 'Wrong instance accepted' }
    catch {if($_.Exception.Message -notmatch 'different database instance'){throw};Check $true 'The shutdown refuses a live endpoint whose master file does not match.'}
    $command.CommandText='SELECT Value FROM ReleaseFixture.dbo.Sample;'
    Check ([int]$command.ExecuteScalar() -eq 42) 'The refused wrong-instance shutdown leaves SQL running and data readable.'
    $connection.Dispose();$connection=$null
    # Deliberately leave SQL running while the controller pretends it stopped.
    function Invoke-FmscControllerStop {param($Name) Write-Host 'Simulating a stale controller reporting Stopped while SQL remains running.'}
    Invoke-FmscLocalDbStop -Data $data -Log $log -Name $instance -Timeout 20
    Check (@(Get-FmscEngineProcesses $directory).Count -eq 0) 'Verified direct shutdown makes the actual fixture SQL process exit.'
    Check ((Get-FmscReleaseState $directory $data $log).Ready) 'Both real MDF/LDF files can be opened exclusively after shutdown.'
    $dataCopy=Join-Path $run 'copy.mdf';$logCopy=Join-Path $run 'copy_log.ldf'
    Copy-Item -LiteralPath $data -Destination $dataCopy
    Copy-Item -LiteralPath $log -Destination $logCopy
    Check (((Get-FileHash -LiteralPath $data).Hash -eq (Get-FileHash -LiteralPath $dataCopy).Hash) -and
        ((Get-FileHash -LiteralPath $log).Hash -eq (Get-FileHash -LiteralPath $logCopy).Hash)) 'Both released files copy successfully and match their source hashes.'
    Write-Output "PASS: $checks real LocalDB file-release checks on an isolated instance."
} finally {
    if($connection){$connection.Dispose()}
    if($created){
        if($instance -ne ('FMSC_StopChecks_'+$owner) -or $owner -notmatch '^[0-9a-f]{32}$'){throw 'Unsafe instance cleanup identity'}
        if(@(Get-FmscEngineProcesses $directory).Count){
            foreach($candidate in @(Get-FmscEndpointCandidates $directory)){[void](Invoke-FmscVerifiedShutdown $candidate $directory)}
            $deadline=[DateTime]::UtcNow.AddSeconds(15)
            while(@(Get-FmscEngineProcesses $directory).Count -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 500}
        }
        if(@(Get-FmscEngineProcesses $directory).Count){throw 'Owned fixture is still running; retained its files for inspection.'}
        & SqlLocalDB.exe delete $instance | Out-Host
        Native 'Deleting the isolated instance failed.'
    }
    $resolved=[IO.Path]::GetFullPath($run)
    $allowed=[IO.Path]::GetFullPath((Join-Path $project 'artifacts/localdb-file-release'))+[IO.Path]::DirectorySeparatorChar
    if(-not $resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -ne $owner){throw 'Unsafe fixture cleanup path'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
