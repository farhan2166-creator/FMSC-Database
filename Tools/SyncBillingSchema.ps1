# Synchronize reference-only SQL modules with the unapplied numbered billing migrations.
# Does not connect to SQL, alter the DACPAC baseline or perform Git operations.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
foreach($migration in Get-ChildItem (Join-Path $root 'Migrations') -Filter '*.sql' | Where-Object {[int]$_.BaseName -ge 14} | Sort-Object {[int]$_.BaseName}) {
 foreach($batch in [regex]::Split((Get-Content -LiteralPath $migration.FullName -Raw),'(?im)^\s*GO\s*\r?$')) {
  $match=[regex]::Match($batch,'CREATE OR ALTER (PROCEDURE|FUNCTION|TRIGGER|VIEW) dbo\.([A-Za-z0-9_]+)')
  if(-not $match.Success){continue}
  $kind=$match.Groups[1].Value;$name=$match.Groups[2].Value
  $directory=switch($kind){'PROCEDURE'{'StoredProcedures'} 'FUNCTION'{'Functions'} 'TRIGGER'{'Triggers'} 'VIEW'{'Views'}}
  $folder=Join-Path $root ('Schema/'+$directory)
  $existing=@(Get-ChildItem -LiteralPath $folder -Filter "dbo_._$name-*.sql")
  $path=if($existing.Count){$existing[0].FullName}else{Join-Path $folder "dbo_._$name.sql"}
  $body=$batch.Substring($match.Index).Trim().Replace("CREATE OR ALTER $kind","CREATE $kind")
  $body=($body -split '\r?\n' | ForEach-Object {$_.TrimEnd()}) -join "`n"
  Set-Content -LiteralPath $path -Value ("-- Reference only; deploy numbered migrations, never execute directly on school data.`n`n"+$body+"`n") -Encoding utf8 -NoNewline
 }
}
Write-Output 'Synchronized billing SQL module references. No database was contacted.'
