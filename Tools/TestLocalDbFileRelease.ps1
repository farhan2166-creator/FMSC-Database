# Inert regressions: mocked SQL processes/controller and temporary empty files.
# Never connects to SQL or stops a real instance.
param([string]$HelperPath=(Join-Path $PSScriptRoot '../Installer/Stop FMSC LocalDB.ps1'))
$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
[void][Management.Automation.Language.Parser]::ParseFile($HelperPath,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Stop helper does not parse.'}
. $HelperPath
$scratch=Join-Path ([IO.Path]::GetTempPath()) ('FMSC-stop-checks-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$data=Join-Path $scratch 'test.mdf';$log=Join-Path $scratch 'test.ldf'
[IO.File]::WriteAllText($data,'data fixture');[IO.File]::WriteAllText($log,'log fixture')
$master=Join-Path $scratch 'master.mdf'
$checks=0;$script:processRows=@();$script:clients=@();$script:controllerCalls=0;$script:shutdownCalls=0
function Check($Condition,$Message){if(-not $Condition){throw $Message};$script:checks++}
function Get-CimInstance {param($ClassName,$Filter,$ErrorAction) $script:processRows}
function Get-Process {param($Name,$ErrorAction) $script:clients}
function Get-FmscInstanceDirectory {param($Name) $scratch}
function Invoke-FmscControllerStop {param($Name) $script:controllerCalls++}
function Start-Sleep {param($Milliseconds)}
try {
    $right=[pscustomobject]@{ProcessId=41;CommandLine='sqlservr.exe -sLOCALDB#ABC123 -d"'+$master+'"'}
    $foreign=[pscustomobject]@{ProcessId=42;CommandLine='sqlservr.exe -sLOCALDB#OTHER -d"'+$master+'.other"'}
    $script:processRows=@($right,$foreign)
    Check (@(Get-FmscEngineProcesses $scratch).Count -eq 1) 'Only the exact master file identifies the target engine.'
    [IO.File]::WriteAllText((Join-Path $scratch 'error.log'),'ready to accept connection on [ \\.\pipe\LOCALDB#OLD\tsql\query ].'+[Environment]::NewLine+'ready to accept connection on [ \\.\pipe\LOCALDB#ABC123\tsql\query ].')
    $pipes=@(Get-FmscEndpointCandidates $scratch)
    Check ($pipes.Count -eq 1 -and $pipes[0] -eq 'np:\\.\pipe\LOCALDB#ABC123\tsql\query') 'Live command line and latest log endpoint deduplicate without using a starting alias.'
    Check (-not (Get-FmscReleaseState $scratch $data $log).Ready) 'A lingering SQL process prevents success even when controller status says stopped.'
    $script:processRows=@()
    $hold=[IO.File]::Open($log,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
    try {
        Check (-not (Get-FmscReleaseState $scratch $data $log).Ready) 'A locked LDF prevents success when the MDF is readable.'
        $probe=[IO.File]::Open($data,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None);$probe.Dispose()
        Check $true 'A failed pair check closes its earlier MDF handle.'
    } finally {$hold.Dispose()}
    Check ((Get-FmscReleaseState $scratch $data $log).Ready) 'Both files can be released after the real lock is closed.'
    $originalCim=${function:Get-CimInstance}
    function Get-CimInstance {throw 'Process inspection denied'}
    Check (-not (Get-FmscReleaseState $scratch $data $log).Ready) 'Failure to inspect processes cannot report success.'
    Set-Item Function:Get-CimInstance $originalCim

    # Reproduce the actual controller bug: normal stop returns, SQL still holds
    # its instance, and only the verified direct shutdown releases it.
    $script:processRows=@($right)
    function Invoke-FmscVerifiedShutdown {
        param($Endpoint,$Directory)
        if($Endpoint -ne 'np:\\.\pipe\LOCALDB#ABC123\tsql\query' -or $Directory -ne $scratch){throw 'Wrong shutdown target'}
        $script:shutdownCalls++;$script:processRows=@();return $true
    }
    Invoke-FmscLocalDbStop -Data $data -Log $log -Name 'FMSC_StopMock' -Timeout 10
    Check ($script:controllerCalls -eq 1 -and $script:shutdownCalls -eq 1) 'A falsely successful controller stop falls back to the verified live endpoint.'

    # A restart during verification must reset the stable-success count.
    $script:states=[Collections.Generic.Queue[bool]]::new()
    foreach($value in @($true,$false,$true,$true,$true)){$script:states.Enqueue($value)}
    $realState=${function:Get-FmscReleaseState}
    function Get-FmscReleaseState {param($Directory,$Data,$Log) @{Ready=$script:states.Dequeue();Reason='Synthetic restart'}}
    function Get-FmscEndpointCandidates {param($Directory) @()}
    Invoke-FmscLocalDbStop -Data $data -Log $log -Name 'FMSC_StopMock' -Timeout 10
    Check ($script:states.Count -eq 0) 'An intervening restart requires three fresh successful checks.'
    function Get-FmscReleaseState {param($Directory,$Data,$Log) @{Ready=$false;Reason='Still locked'}}
    try{Invoke-FmscLocalDbStop -Data $data -Log $log -Name 'FMSC_StopMock' -Timeout 0;throw 'Locked files reported success'}
    catch{if($_.Exception.Message -notmatch 'not released'){throw};Check $true 'Persistent locks fail without force-killing SQL or changing files.'}
    Set-Item Function:Get-FmscReleaseState $realState
    $script:clients=@([pscustomobject]@{ProcessName='devenv'})
    $before=$script:controllerCalls
    try{Invoke-FmscLocalDbStop -Data $data -Log $log;throw 'Open Visual Studio was accepted'}
    catch{if($_.Exception.Message -notmatch 'Close these applications'){throw};Check ($script:controllerCalls -eq $before) 'Open reconnecting applications are refused before stopping SQL.'}
    Check (([IO.File]::ReadAllText($data) -eq 'data fixture') -and ([IO.File]::ReadAllText($log) -eq 'log fixture')) 'The release helper never edits either database file.'
    Write-Output "PASS: $checks LocalDB file-release regression checks."
} finally {
    $resolved=[IO.Path]::GetFullPath($scratch)
    $temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if(-not $resolved.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^FMSC-stop-checks-[0-9a-f]{32}$'){throw 'Unsafe fixture cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
