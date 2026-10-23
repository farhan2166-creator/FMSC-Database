# Own a fresh LocalDB instance. Read an existing local recovery backup only to
# create isolated communication and fee-matrix fixtures; never connect to the live database.
param([string]$Snapshot)
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $project 'Installer/Stop FMSC LocalDB.ps1')
if(-not $Snapshot){$Snapshot=(Get-ChildItem (Join-Path $project 'artifacts/database-migration-backups') -Filter 'restored-schema38-before39-40-*.bak' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName}
if(-not $Snapshot -or -not (Test-Path -LiteralPath $Snapshot -PathType Leaf)){throw 'Supply a local recovery snapshot for the isolated communication fixture.'}
$owner=[guid]::NewGuid().ToString('N');$instance='FMSC_SafetyChecks_'+$owner
$run=Join-Path $project ('artifacts/safety-sql/'+$owner);$directory=Get-FmscInstanceDirectory $instance
$created=$false;$connection=$null
function Native($message){if($LASTEXITCODE -ne 0){throw $message}}
try{
 New-Item -ItemType Directory -Path $run -Force | Out-Null
 & SqlLocalDB.exe create $instance -s | Out-Host
 $created=Test-Path -LiteralPath $directory -PathType Container
 Native 'Could not create the isolated SQL instance.'
 $endpoints=@(Get-FmscEndpointCandidates $directory);if(-not $endpoints.Count){throw 'Owned fixture endpoint missing.'}
 $endpoint=$endpoints[0]
 $connection=[Data.SqlClient.SqlConnection]::new("Server=$endpoint;Database=master;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5;Pooling=False");$connection.Open()
 $command=$connection.CreateCommand();$command.CommandTimeout=90
 $command.CommandText="EXEC sys.sp_addextendedproperty @name=N'FMSC_SafetyChecksOwner',@value=N'$owner';";[void]$command.ExecuteNonQuery()
 $command.CommandText='RESTORE FILELISTONLY FROM DISK=@Path;';[void]$command.Parameters.AddWithValue('@Path',$Snapshot)
 $files=[Data.DataTable]::new();[void]([Data.SqlClient.SqlDataAdapter]::new($command)).Fill($files);$command.Parameters.Clear()
 $moves=@();$index=0
 foreach($file in $files.Rows){
  if($file.Type -notin @('D','L')){throw 'Unexpected fixture file type.'}
  $path=Join-Path $run ('communication-'+$index+$(if($file.Type -eq 'L'){'.ldf'}else{'.mdf'}))
  $moves+="MOVE N'$(([string]$file.LogicalName).Replace("'","''"))' TO N'$($path.Replace("'","''"))'";$index++
 }
 $command.CommandText="RESTORE DATABASE [CommunicationFixture] FROM DISK=N'$($Snapshot.Replace("'","''"))' WITH "+($moves -join ',')+',CHECKSUM,RECOVERY;';[void]$command.ExecuteNonQuery()
 $command.CommandText='USE CommunicationFixture; SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC;'
 $current=[int]$command.ExecuteScalar()
 foreach($file in Get-ChildItem (Join-Path $project 'Migrations') -Filter '*.sql' | Sort-Object {[int]$_.BaseName}){
  if([int]$file.BaseName -le $current){continue}
  $transaction=$connection.BeginTransaction()
  try{
   foreach($batch in [regex]::Split((Get-Content -LiteralPath $file.FullName -Raw),'(?im)^\s*GO\s*\r?$')){
    if([string]::IsNullOrWhiteSpace($batch)){continue}
    $q=$connection.CreateCommand();$q.Transaction=$transaction;$q.CommandTimeout=45;$q.CommandText=$batch;[void]$q.ExecuteNonQuery()
   }
   $transaction.Commit()
  }catch{try{$transaction.Rollback()}catch{};throw}
 }
 $command.CommandText="SELECT COUNT(*) FROM sys.schemas WHERE name=N'comm';"
 if([int]$command.ExecuteScalar() -eq 0){
  $command.CommandText="EXEC sys.sp_set_session_context @key=N'FMSC_CommunicationInstall',@value=1;";[void]$command.ExecuteNonQuery()
  foreach($batch in [regex]::Split((Get-Content (Join-Path $project 'Services/Communication/Schema.sql') -Raw),'(?im)^\s*GO\s*\r?$')){
   if([string]::IsNullOrWhiteSpace($batch)){continue};$command.CommandText=$batch;[void]$command.ExecuteNonQuery()
  }
 }
 $connection.Dispose();$connection=$null
 $env:FMSC_SAFETY_CONNECTION="Server=$endpoint;Database=master;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5;Pooling=False"
 $env:FMSC_SAFETY_OWNER=$owner;$env:FMSC_SAFETY_ROOT=$run
 & dotnet (Join-Path $project 'Tests/SafetySqlChecks/bin/Release/net8.0-windows/SafetySqlChecks.dll')
 Native 'Communication/matrix/restore SQL checks failed.'
}finally{
 if($connection){$connection.Dispose()}
 Remove-Item Env:FMSC_SAFETY_CONNECTION,Env:FMSC_SAFETY_OWNER,Env:FMSC_SAFETY_ROOT -ErrorAction SilentlyContinue
 if($created){
  if($instance -ne ('FMSC_SafetyChecks_'+$owner) -or $owner -notmatch '^[0-9a-f]{32}$'){throw 'Unsafe test instance cleanup identity.'}
  foreach($candidate in @(Get-FmscEndpointCandidates $directory)){[void](Invoke-FmscVerifiedShutdown $candidate $directory)}
  $deadline=[DateTime]::UtcNow.AddSeconds(20)
  while(@(Get-FmscEngineProcesses $directory).Count -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 500}
  if(@(Get-FmscEngineProcesses $directory).Count){throw 'Owned SQL fixture remains running; retained its files.'}
  & SqlLocalDB.exe delete $instance | Out-Host
  Native 'Could not delete the owned test instance.'
 }
 $resolved=[IO.Path]::GetFullPath($run);$allowed=[IO.Path]::GetFullPath((Join-Path $project 'artifacts/safety-sql'))+[IO.Path]::DirectorySeparatorChar
 if(-not $resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -ne $owner){throw 'Unsafe test file cleanup path.'}
 if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
