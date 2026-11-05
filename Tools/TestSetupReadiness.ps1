# Runs the migration's actual procedure against temporary synthetic tables only.
param([string]$Instance='(localdb)\MSSQLLocalDB')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$migration=Get-Content (Join-Path $root 'Tests/LifecycleSqlChecks/Migrations/53.sql') -Raw
$batches=[regex]::Split($migration,'(?im)^GO\s*$')
$procedure=@($batches | Where-Object {$_ -match 'CREATE OR ALTER PROCEDURE'})
if($procedure.Count -ne 1){throw 'Expected one readiness procedure.'}
$procedure=$procedure[0].Replace('dbo.GetSetupReadiness','#GetSetupReadiness')
foreach($table in @('SchoolProfile','Campuses','CampusClassSections','Classes','Sections','AcademicYears','FeeTypes','FeeConfigurations')) {
    $procedure=$procedure.Replace('dbo.'+$table,'#'+$table)
}
# Keep permission branches intact, with a temporary permission stub for the fixture.
$procedure=$procedure.Replace('dbo.RequirePermission','#RequirePermission')
$checks=Get-Content (Join-Path $root 'Validation/SetupReadinessChecks.sql') -Raw
$checks=$checks.Replace('-- PROCEDURE', $procedure)
$tempSql=Join-Path ([IO.Path]::GetTempPath()) ('FMSC_SetupReadiness_'+[guid]::NewGuid().ToString('N')+'.sql')
try {
    [IO.File]::WriteAllText($tempSql,$checks)
    & sqlcmd -S $Instance -E -d master -b -l 15 -t 60 -i $tempSql
    if($LASTEXITCODE -ne 0){throw 'Setup readiness checks failed.'}
} finally {Remove-Item -LiteralPath $tempSql -ErrorAction SilentlyContinue}
