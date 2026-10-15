# Recovery cleanup for an interrupted run. Accepts only an exact GUID, not an
# instance, connection string, database path, pattern or school catalog.
param([Parameter(Mandatory=$true)][guid]$RunId)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$catalog='FMSC_Level2_'+$RunId.ToString('N')
$parent=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/level2/private'))+[IO.Path]::DirectorySeparatorChar
$root=[IO.Path]::GetFullPath((Join-Path $parent $catalog))
if(-not $root.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($root) -ne $catalog -or
    (Get-Content -LiteralPath (Join-Path $root 'owner.txt') -Raw).Trim() -ne $RunId.ToString('D')) {throw 'Private directory ownership differs.'}
$escaped=$root.Replace("'","''")
$query=@"
SET NOCOUNT ON;
IF DB_NAME()<>N'$catalog' OR (SELECT COUNT(*) FROM sys.databases WHERE database_id>4)<>1
    THROW 51000,'Unexpected catalog on private instance',1;
IF NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_Level2Owner' AND CONVERT(nvarchar(36),value)=N'$RunId')
    THROW 51000,'Owner marker mismatch; cleanup refused',1;
IF EXISTS(SELECT 1 FROM sys.database_files WHERE LEFT(physical_name,LEN(N'$escaped\database\'))<>N'$escaped\database\')
    THROW 51000,'Database path mismatch; cleanup refused',1;
SELECT N'OWNED';
"@
$result=@(& sqlcmd -S "(localdb)\$catalog" -E -d $catalog -b -l 20 -t 30 -h -1 -W -Q $query 2>&1)
if($LASTEXITCODE -ne 0 -or ($result -join '').Trim() -ne 'OWNED') {throw 'Could not establish database ownership. State retained for inspection.'}
& SqlLocalDB stop $catalog -k
if($LASTEXITCODE -ne 0) {throw 'Owned instance did not stop.'}
& SqlLocalDB delete $catalog
if($LASTEXITCODE -ne 0) {throw 'Owned instance was not removed.'}
# Absolute target was resolved and checked before this recursive removal.
Remove-Item -LiteralPath $root -Recurse -Force
Write-Host 'PASS: exact owned Level 2 instance and private files removed; evidence retained.'
