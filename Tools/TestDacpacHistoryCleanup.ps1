#Requires -Version 5.1
# Disposable synthetic Git repositories only. No network and no application database.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskRun=Join-Path $taskRoot ('artifacts/dacpac-cleanup-tests/'+[guid]::NewGuid().ToString('N'))
$taskSource=Join-Path $taskRun 'source'
$taskMirror=Join-Path $taskRun 'preview.git'
New-Item -ItemType Directory -Path $taskSource -Force | Out-Null
function FixtureGit([string[]]$Arguments){
 $result=@(& git -C $taskSource @Arguments)
 if($LASTEXITCODE -ne 0){throw ('Fixture Git failed: '+($Arguments -join ' '))}
 return $result
}
function Check([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message};Write-Output ('PASS: '+$Message)}
& git init --initial-branch=main $taskSource | Out-Null
if($LASTEXITCODE -ne 0){throw 'Fixture initialization failed.'}
[void](FixtureGit @('config','user.name','Fictional Cleanup Tester'))
[void](FixtureGit @('config','user.email','fixture@invalid'))
[void](FixtureGit @('config','core.autocrlf','false'))
New-Item -ItemType Directory -Path (Join-Path $taskSource 'Tests/LifecycleSqlChecks/Baseline'),(Join-Path $taskSource 'folder with spaces') -Force | Out-Null
'synthetic root package one'|Set-Content -LiteralPath (Join-Path $taskSource 'fmsc.dacpac')
'preserved nested test fixture'|Set-Content -LiteralPath (Join-Path $taskSource 'Tests/LifecycleSqlChecks/Baseline/schema.dacpac')
'original application text'|Set-Content -LiteralPath (Join-Path $taskSource 'app.cs')
'Unicode and spaced filename'|Set-Content -LiteralPath (Join-Path $taskSource ('folder with spaces/na'+[char]0x00ef+'ve.txt')) -Encoding UTF8
[void](FixtureGit @('add','--all'));[void](FixtureGit @('commit','-m','Initial synthetic fixture'))
[void](FixtureGit @('switch','-c','feature'))
'preserved feature text'|Set-Content -LiteralPath (Join-Path $taskSource 'feature.cs')
[void](FixtureGit @('add','--all'));[void](FixtureGit @('commit','-m','Feature with spaces and Unicode'))
[void](FixtureGit @('switch','main'))
'changed application text'|Set-Content -LiteralPath (Join-Path $taskSource 'app.cs')
[void](FixtureGit @('add','--all'));[void](FixtureGit @('commit','-m','Independent main change'))
[void](FixtureGit @('merge','--no-ff','feature','-m','Preserved merge'))
[void](FixtureGit @('branch','-d','feature'))
'synthetic root package two'|Set-Content -LiteralPath (Join-Path $taskSource 'fmsc.dacpac')
[void](FixtureGit @('add','--all'));[void](FixtureGit @('commit','-m','DACPAC-only commit must remain'))
$taskHead=[string](FixtureGit @('rev-parse','HEAD'))
$taskCount=[int]([string](FixtureGit @('rev-list','--count','--all')))
$taskFixture=[string](FixtureGit @('rev-parse','HEAD:Tests/LifecycleSqlChecks/Baseline/schema.dacpac'))
$taskPackage=[string](FixtureGit @('rev-parse','HEAD:fmsc.dacpac'))
$taskScript=Join-Path $PSScriptRoot 'PrepareDacpacHistoryCleanup.ps1'
& $taskScript -Repository $taskSource -OutputDirectory $taskMirror
Check (([string](FixtureGit @('rev-parse','HEAD'))) -ceq $taskHead) 'Source checkout and branch head remain unchanged.'
Check (([string](FixtureGit @('rev-parse','HEAD:fmsc.dacpac'))) -ceq $taskPackage) 'Original source package remains untouched.'
$taskRemaining=@(& git -C $taskMirror log --all --format=%H -- fmsc.dacpac)
Check ($LASTEXITCODE -eq 0 -and $taskRemaining.Count -eq 0) 'Only the root DACPAC is removed from reachable mirror history.'
$taskAfterCount=[int]([string](& git -C $taskMirror rev-list --count --all))
Check ($LASTEXITCODE -eq 0 -and $taskAfterCount -eq $taskCount) 'Every commit remains, including the now-empty DACPAC-only commit.'
$taskAfterFixture=[string](& git -C $taskMirror rev-parse 'HEAD:Tests/LifecycleSqlChecks/Baseline/schema.dacpac')
Check ($LASTEXITCODE -eq 0 -and $taskAfterFixture -ceq $taskFixture) 'The separate nested DACPAC fixture is preserved byte for byte.'
$taskHelpers=@(& git -C $taskMirror for-each-ref --format='%(refname)' refs/original refs/dacpac-cleanup-state)
Check ($LASTEXITCODE -eq 0 -and $taskHelpers.Count -eq 0) 'Temporary rewrite refs cannot keep the old package reachable.'
Check (Test-Path -LiteralPath (Join-Path $taskMirror 'DACPAC_COMMIT_MAP.txt')) 'A reviewable commit map is saved.'
$taskReport=Get-Content -LiteralPath (Join-Path $taskMirror 'DACPAC_CLEANUP_REVIEW.txt') -Raw
Check ($taskReport.Contains('commit messages, author/committer identities/dates and merge parents are preserved')) 'All-commit preservation checks completed successfully.'
[void](FixtureGit @('tag','fixture-tag'))
$taskRefused=$false
try{& $taskScript -Repository $taskSource -OutputDirectory (Join-Path $taskRun 'tagged-preview.git')}catch{if($_.Exception.Message -notlike 'Tags were found*'){throw};$taskRefused=$true}
Check $taskRefused 'Unreviewed tags stop the preview before any rewrite.'
Write-Output ('Synthetic fixture retained for inspection: '+$taskRun)
