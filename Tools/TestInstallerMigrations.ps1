# Inert migration/verification tests: never connects to SQL or launches the app.
param([string]$InstallerPath = (Join-Path $PSScriptRoot '../Installer/core.ps1'))
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput((Get-Content -LiteralPath $InstallerPath -Raw),[ref]$tokens,[ref]$errors)
if ($errors.Count) { throw 'Installer PowerShell does not parse.' }
foreach ($name in @('Convert-ToDbVersion','Invoke-PendingMigrations','Invoke-SqlMigration','Invoke-InstallationDatabaseCheck')) {
    $functions = @($ast.FindAll({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name },$true))
    if ($functions.Count -ne 1) { throw "Expected one $name helper." }
    Invoke-Expression $functions[0].Extent.Text
}
$sqlRunner = ${function:Invoke-SqlMigration}
$activations = @($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Enable-StagedApplication'},$true))
if ($activations.Count -ne 1) { throw 'Review all application activation paths.' }
$flow = $activations[0].Parent
while ($null -ne $flow.Parent -and $flow.Parent -isnot [Management.Automation.Language.StatementBlockAst]) { $flow=$flow.Parent }
$prior = @($flow.Parent.Statements | Where-Object { $_.Extent.StartOffset -lt $flow.Extent.StartOffset })
if (-not ($prior | Where-Object { $_.Extent.Text -match '^Invoke-PendingMigrations ' }) -or
    -not ($prior | Where-Object { $_.Extent.Text -match 'Invoke-InstallationDatabaseCheck' })) { throw 'Migrations and compatibility verification must precede activation.' }
function Write-Log { param($Message) }
function Write-Step { param($Message) }
function Get-DatabaseVersion { $script:reads++; return "$script:current" }
function New-PreMigrationBackup { param($FromVersion,$ToVersion) $script:events.Add("backup:$FromVersion->$ToVersion"); if($script:backupFails){throw 'backup failed'}; return 'verified.bak' }
function Invoke-SqlMigration { param($FilePath,$VersionBefore,$ExpectedVersion)
    if($VersionBefore -ne $script:current){throw 'Wrong version precondition'}
    $script:events.Add("apply:$ExpectedVersion")
    if($script:sqlFails){throw 'sql failed'}
    if(-not $script:wrongVersion){$script:current=$ExpectedVersion}
}
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('FMSC migration checks ' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$checks=0
function Scenario($Files,$Current,$Required,$Expected,$ErrorText='',$Missing=$false) {
    $directory=Join-Path $scratch ([Guid]::NewGuid().ToString('N'))
    if(-not $Missing){New-Item -ItemType Directory -Path $directory | Out-Null}
    foreach($file in $Files){Set-Content -LiteralPath (Join-Path $directory $file) -Value '-- inert fixture'}
    $script:current=$Current; $script:reads=0; $script:events=[Collections.Generic.List[string]]::new()
    $failure=''
    try{Invoke-PendingMigrations -Directory $directory -RequiredVersion $Required}catch{$failure=$_.Exception.Message}
    if($ErrorText){if(-not $failure.Contains($ErrorText)){throw "Expected '$ErrorText', got '$failure'"}}elseif($failure){throw $failure}
    if(($script:events -join ',') -ne $Expected){throw "Wrong migration order: $($script:events -join ',')"}
    if(($Missing -or @($Files | Where-Object {$_ -like '*.sql'}).Count -eq 0) -and $script:reads -ne 0){throw 'Empty migration folder must not read a migration version.'}
    $script:checks++
}
try {
    Scenario @() 6 6 '' '' $true
    Scenario @() 6 6 ''
    Scenario @('readme.txt') 6 6 ''
    Scenario @('5.sql','6.sql') 6 6 ''
    Scenario @('8.sql') 6 8 'backup:6->8,apply:8'
    Scenario @('10.sql','2.sql','1.sql') 1 10 'backup:1->10,apply:2,apply:10'
    Scenario @('1.sql','2.sql') 10 10 ''
    Scenario @('8.SQL') 6 8 'backup:6->8,apply:8'
    Scenario @('notes.sql') 6 6 '' 'Invalid migration filename'
    Scenario @('08.sql') 6 8 '' 'Invalid migration filename'
    Scenario @('9.sql') 6 8 '' 'supported schema version'
    Scenario @('8.sql') 6 9 '' 'Available migrations stop'
    $script:backupFails=$true
    Scenario @('8.sql') 6 8 'backup:6->8' 'backup failed'
    $script:backupFails=$false; $script:sqlFails=$true
    Scenario @('8.sql','9.sql') 6 9 'backup:6->9,apply:8' 'sql failed'
    $script:sqlFails=$false; $script:wrongVersion=$true
    Scenario @('8.sql','9.sql') 6 9 'backup:6->9,apply:8' 'expected database version'
    $script:wrongVersion=$false
    # Inspect the real runner's same-connection wrapper through an inert sqlcmd replacement.
    function Invoke-FakeSqlcmd { param($S,[switch]$E,$d,[switch]$C,[switch]$b,$f,$i)
        $script:wrapperPath=$i; $script:wrapper=Get-Content -LiteralPath $i -Raw
        $global:LASTEXITCODE=$script:sqlExit
    }
    $script:SqlCmdPath='Invoke-FakeSqlcmd'; $SqlInstance='inert'; $DatabaseName='inert'
    $script:sqlExit=0
    & $sqlRunner -FilePath (Join-Path $scratch '8.sql') -VersionBefore 6 -ExpectedVersion 8
    if($script:wrapper -notmatch 'sp_getapplock' -or $script:wrapper -notmatch 'BEGIN TRANSACTION;' -or
       $script:wrapper -notmatch ':r "' -or $script:wrapper -notmatch '@After<>8' -or $script:wrapper -notmatch 'COMMIT TRANSACTION;') {throw 'Migration transaction/version wrapper is incomplete.'}
    if(Test-Path -LiteralPath $script:wrapperPath){throw 'Wrapper was not removed.'}
    $script:sqlExit=1; $failure=''
    try{& $sqlRunner -FilePath (Join-Path $scratch '8.sql') -VersionBefore 6 -ExpectedVersion 8}catch{$failure=$_.Exception.Message}
    if(-not $failure.Contains('Migration 8 failed') -or (Test-Path -LiteralPath $script:wrapperPath)){throw 'SQL failure/cleanup must be reported.'}
    Write-Output "PASS: $checks folder/version/backup scenarios plus SQL wrapper, failure cleanup and activation-order checks."
} finally {
    $resolved=[IO.Path]::GetFullPath($scratch)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase)){throw 'Refusing cleanup outside test temp directory.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
$global:LASTEXITCODE = 0
