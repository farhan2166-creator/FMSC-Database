param(
    [string]$DataFile = 'C:\ProgramData\FMSC_Database\FMSC_DB.mdf',
    [string]$LogFile = 'C:\ProgramData\FMSC_Database\FMSC_DB_log.ldf',
    [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$InstanceName = 'MSSQLLocalDB',
    [ValidateRange(10,120)][int]$TimeoutSeconds = 45
)

$ErrorActionPreference = 'Stop'

function Get-FmscInstanceDirectory([string]$Name) {
    Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "Microsoft\Microsoft SQL Server Local DB\Instances\$Name"
}

function Get-FmscEngineProcesses([string]$Directory) {
    $master = [IO.Path]::GetFullPath((Join-Path $Directory 'master.mdf'))
    # Match the complete -d argument, not just an instance name or process ID.
    $pattern = '(?i)(?:^|\s)-d(?:"' + [regex]::Escape($master) + '"|' + [regex]::Escape($master) + '(?=\s|$))'
    @(Get-CimInstance Win32_Process -Filter "Name = 'sqlservr.exe'" -ErrorAction Stop |
        Where-Object { $_.CommandLine -and $_.CommandLine -match $pattern })
}

function Get-FmscReleaseState([string]$Directory,[string]$Data,[string]$Log) {
    $handles = @()
    try {
        $engines = @(Get-FmscEngineProcesses $Directory)
        if ($engines.Count) { return @{ Ready=$false; Reason=('SQL Server is still running (PID ' + ($engines.ProcessId -join ', ') + ').') } }
        foreach ($file in @($Data,$Log)) {
            $handles += [IO.File]::Open($file,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
        }
        if (@(Get-FmscEngineProcesses $Directory).Count) { return @{ Ready=$false; Reason='A database tool restarted LocalDB during verification.' } }
        return @{ Ready=$true; Reason='SQL process exited and both files opened exclusively.' }
    } catch {
        return @{ Ready=$false; Reason=$_.Exception.Message }
    } finally {
        foreach ($handle in $handles) { $handle.Dispose() }
    }
}

function Get-FmscEndpointCandidates([string]$Directory) {
    $candidates = [Collections.Generic.List[string]]::new()
    # Process arguments work even when the LocalDB controller reports Stopped.
    foreach ($engine in @(Get-FmscEngineProcesses $Directory)) {
        $match = [regex]::Match($engine.CommandLine,'(?i)(?:^|\s)-s(LOCALDB#[A-Za-z0-9]+)(?=\s|$)')
        if ($match.Success) { $candidates.Add('np:\\.\pipe\' + $match.Groups[1].Value + '\tsql\query') }
    }
    $path = Join-Path $Directory 'error.log'
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $stream = [IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        $reader = [IO.StreamReader]::new($stream)
        try {
            $pattern = '(?i)ready to accept connection on \[ (' + [regex]::Escape('\\.\pipe\LOCALDB#') + '[A-Za-z0-9]+' + [regex]::Escape('\tsql\query') + ') \]'
            $pipeMatches = [regex]::Matches($reader.ReadToEnd(),$pattern)
            if ($pipeMatches.Count) { $candidates.Add('np:' + $pipeMatches[$pipeMatches.Count-1].Groups[1].Value) }
        } finally { $reader.Dispose() }
    }
    @($candidates | Select-Object -Unique)
}

function Invoke-FmscVerifiedShutdown([string]$Endpoint,[string]$Directory) {
    $connection = [Data.SqlClient.SqlConnection]::new("Server=$Endpoint;Database=master;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=2;ConnectRetryCount=0;Pooling=False")
    try {
        # A direct pipe connection never starts an instance as an alias can.
        try { $connection.Open() } catch [Data.SqlClient.SqlException] { return $false }
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 5
        $command.CommandText = 'SELECT physical_name FROM sys.database_files WHERE file_id=1;'
        $actual = [string]$command.ExecuteScalar()
        $expected = [IO.Path]::GetFullPath((Join-Path $Directory 'master.mdf'))
        if (-not $actual -or -not [IO.Path]::GetFullPath($actual).Equals($expected,[StringComparison]::OrdinalIgnoreCase)) {
            throw 'The endpoint belongs to a different database instance. It was not stopped.'
        }
        Write-Host 'Requesting normal SQL shutdown through the verified running instance...'
        $command.CommandText = 'SHUTDOWN;'
        $command.CommandTimeout = 15
        try { [void]$command.ExecuteNonQuery() }
        catch [Data.SqlClient.SqlException] {
            # SQL normally disconnects this session during shutdown. A timeout
            # or refusal still cannot produce success: file/process checks follow.
            Write-Host ('Shutdown response: ' + $_.Exception.Message)
        }
        return $true
    } finally { $connection.Dispose() }
}

function Invoke-FmscControllerStop([string]$Name) {
    $utility = Get-Command SqlLocalDB.exe -ErrorAction SilentlyContinue
    if ($utility) {
        Write-Host "Requesting normal shutdown of $Name..."
        & $utility.Source stop $Name | Out-Host
    }
}

function Invoke-FmscLocalDbStop([string]$Data,[string]$Log,[string]$Name='MSSQLLocalDB',[int]$Timeout=45) {
    if ($Name -notmatch '^[A-Za-z0-9_-]+$') { throw 'Invalid LocalDB instance name.' }
    $dataPath = [IO.Path]::GetFullPath($Data)
    $logPath = [IO.Path]::GetFullPath($Log)
    if ($dataPath.Equals($logPath,[StringComparison]::OrdinalIgnoreCase)) { throw 'Choose the matching MDF and LDF, not the same file twice.' }
    foreach ($file in @($dataPath,$logPath)) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Database file not found: $file" }
    }
    if ($Name -eq 'MSSQLLocalDB') {
        $clients = @(Get-Process -Name FMSC_SMS,devenv,Ssms -ErrorAction SilentlyContinue)
        if ($clients.Count) { throw ('Close these applications first, then run this script again: ' + (($clients.ProcessName | Sort-Object -Unique) -join ', ') + '.') }
    }
    $directory = Get-FmscInstanceDirectory $Name
    # Capture real endpoints before asking the controller to stop; its status
    # and cached pipe can disappear while the SQL process still holds the files.
    $endpoints = @(Get-FmscEndpointCandidates $directory)
    Invoke-FmscControllerStop $Name
    # Neither the utility exit code nor its reported state proves file release.
    $sent = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $deadline = [DateTime]::UtcNow.AddSeconds($Timeout)
    $stable = 0
    do {
        $state = Get-FmscReleaseState $directory $dataPath $logPath
        if ($state.Ready) { $stable++ } else {
            $stable = 0
            $endpoints = @($endpoints + @(Get-FmscEndpointCandidates $directory) | Select-Object -Unique)
            foreach ($endpoint in $endpoints) {
                if (-not $sent.Contains($endpoint) -and (Invoke-FmscVerifiedShutdown $endpoint $directory)) { [void]$sent.Add($endpoint) }
            }
        }
        if ($stable -ge 3) {
            Write-Host 'PASS: SQL process exited and both MDF/LDF files passed repeated exclusive-access checks.'
            return
        }
        Start-Sleep -Milliseconds 600
    } while ([DateTime]::UtcNow -lt $deadline)
    throw ('The database files were not released. ' + $state.Reason + ' Keep database applications closed and retry. No files were changed.')
}

# Dot-sourcing loads the helpers for isolated checks without stopping an instance.
if ($MyInvocation.InvocationName -ne '.') {
    try {
        Invoke-FmscLocalDbStop -Data $DataFile -Log $LogFile -Name $InstanceName -Timeout $TimeoutSeconds
        exit 0
    } catch {
        Write-Host ('NOT READY: ' + $_.Exception.Message)
        exit 1
    }
}
