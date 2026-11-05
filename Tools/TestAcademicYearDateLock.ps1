# Execute the production procedures using session-local synthetic tables only.
param([string]$Instance='(localdb)\MSSQLLocalDB')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$migration=((Get-Content (Join-Path $root 'Tests/LifecycleSqlChecks/Baseline/Sql/GetAcademicYears.sql') -Raw), (Get-Content (Join-Path $root 'Tests/LifecycleSqlChecks/Baseline/Sql/SaveAcademicYear.sql') -Raw)) -join "`nGO`n"
$procedures=@([regex]::Split($migration,'(?im)^GO\s*$') | Where-Object {$_ -match 'CREATE OR ALTER PROCEDURE'})
if($procedures.Count -ne 2){throw 'Expected the two academic-year procedures.'}
$fixture=Get-Content -LiteralPath (Join-Path $root 'Validation/AcademicYearDateLockChecks.sql') -Raw
$localProcedures=foreach($procedure in $procedures){
 foreach($name in @('GetAcademicYears','SaveAcademicYear','RequirePermission','RequireAuthenticatedUser','AcademicYears','Enrollments','PromotionBatches')){
  $procedure=$procedure.Replace('dbo.'+$name,'#'+$name)
 }
 $procedure+"`r`nGO`r`n"
}
$sql=$fixture.Replace('-- PRODUCTION PROCEDURES',($localProcedures -join "`r`n"))
$directory=Join-Path $root ('artifacts/academic-year-checks/'+[guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $directory -Force)
$path=Join-Path $directory 'checks.sql'
[IO.File]::WriteAllText($path,$sql)
& sqlcmd -S $Instance -E -d master -b -l 10 -t 60 -i $path
if($LASTEXITCODE -ne 0){throw 'Academic-year date-lock SQL checks failed.'}
