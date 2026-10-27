# Run production read selection and receipt/acknowledgement checks only in an
# owned disposable clone. The school database is read only for COPY_ONLY backup.
param([string]$Instance='',[string]$TestAssembly='')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$Instance){
 $log=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Microsoft/Microsoft SQL Server Local DB/Instances/MSSQLLocalDB/error.log'
 $endpointMatches=[regex]::Matches((Get-Content -LiteralPath $log -Raw),'ready to accept connection on \[ (.*?) \]')
 if($endpointMatches.Count -eq 0){throw 'No running local database endpoint was found.'}
 $Instance='np:'+$endpointMatches[$endpointMatches.Count-1].Groups[1].Value
}
$id=[guid]::NewGuid().ToString('N');$catalog='FMSC_HistoricalReturn_'+$id
$run=Join-Path $root ('artifacts/historical-returns/'+$id)
[void](New-Item -ItemType Directory -Path $run -Force)
$backup=Join-Path $run 'fixture.bak';$created=$false
$cn=[Data.SqlClient.SqlConnection]::new("Server=$Instance;Database=FMSC_DB;Integrated Security=True;TrustServerCertificate=True;Pooling=False;Connect Timeout=5")
function Query([string]$sql){
 $q=$cn.CreateCommand();$q.CommandText=$sql;$q.CommandTimeout=180
 $data=[Data.DataSet]::new();$a=[Data.SqlClient.SqlDataAdapter]::new($q)
 try{[void]$a.Fill($data);return ,$data}finally{$a.Dispose();$q.Dispose()}
}
try{
 $cn.Open()
 if([string](Query 'SELECT physical_name FROM sys.database_files WHERE file_id=1;').Tables[0].Rows[0][0] -ine 'C:\ProgramData\FMSC_Database\FMSC_DB.mdf'){throw 'Unexpected source database.'}
 [void](Query ("BACKUP DATABASE FMSC_DB TO DISK=N'"+$backup.Replace("'","''")+"' WITH COPY_ONLY,CHECKSUM,INIT;"))
 $list=Query ("RESTORE FILELISTONLY FROM DISK=N'"+$backup.Replace("'","''")+"';")
 $moves=@();$index=0
 foreach($file in $list.Tables[0].Rows){
  if($file.Type -notin @('D','L')){throw 'Unexpected backup file type.'}
  $path=Join-Path $run ('fixture-'+$index+$(if($file.Type -eq 'L'){'.ldf'}else{'.mdf'}))
  $moves+="MOVE N'$(([string]$file.LogicalName).Replace("'","''"))' TO N'$($path.Replace("'","''"))'";$index++
 }
 $cn.ChangeDatabase('master')
 [void](Query ("RESTORE DATABASE [$catalog] FROM DISK=N'"+$backup.Replace("'","''")+"' WITH "+($moves -join ',')+",CHECKSUM,RECOVERY;"));$created=$true
 $cn.ChangeDatabase($catalog)
 $version=[int](Query 'SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC;').Tables[0].Rows[0][0]
 if($version -eq 56){
  $migrationTransaction=$cn.BeginTransaction()
  try{
   foreach($batch in [regex]::Split([IO.File]::ReadAllText((Join-Path $root 'Migrations/57.sql')),'(?im)^\s*GO\s*\r?$')){
    if([string]::IsNullOrWhiteSpace($batch)){continue}
    $command=$cn.CreateCommand();$command.Transaction=$migrationTransaction;$command.CommandText=$batch
    try{[void]$command.ExecuteNonQuery()}finally{$command.Dispose()}
   }
   $migrationTransaction.Commit()
  }catch{$migrationTransaction.Rollback();throw}finally{$migrationTransaction.Dispose()}
 }elseif($version -ne 57){throw 'The historical-return checks require schema 56 or 57.'}
 $env:FMSC_HISTORICAL_RETURN_TEST_CONNECTION="Server=$Instance;Database=$catalog;Integrated Security=True;TrustServerCertificate=True;Pooling=False;Connect Timeout=5"
 if(!$TestAssembly){$TestAssembly=Join-Path $root 'Tests/ThemeSmokeChecks/bin/Debug/net8.0-windows/ThemeSmokeChecks.dll'}
 & dotnet $TestAssembly --historical-return-sql
 if($LASTEXITCODE -ne 0){throw 'The historical admission/return integration checks failed.'}
 Write-Output 'PASS: school records unchanged; promotion and return fixtures ran only in the owned clone.'
}finally{
 Remove-Item Env:FMSC_HISTORICAL_RETURN_TEST_CONNECTION -ErrorAction SilentlyContinue
 if($cn.State -eq 'Open' -and $created){
  if($catalog -notmatch '^FMSC_HistoricalReturn_[0-9a-f]{32}$'){throw 'Invalid owned clone identity; cleanup refused.'}
  $cn.ChangeDatabase('master');[void](Query "ALTER DATABASE [$catalog] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$catalog];")
 }
 $cn.Dispose()
}
