# Shared by the existing installer and its stable launcher. Loading defines helpers only.
function Get-FmscRecoveryRoot {
    return Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'FMSC_Updater'
}
function Get-FmscDownloadParent {
    return Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FMSC/Updates'
}
function Assert-FmscNoLinks {
    param([string]$Path)
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    while ($null -ne $item) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked recovery paths are forbidden.' }
        $item = $item.Parent
    }
    if (Test-Path -LiteralPath $Path -PathType Container) {
        foreach ($entry in Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction Stop) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Recovery files contain a link.' }
        }
    }
}
function Assert-FmscRecoveryStorage {
    param([string]$Root)
    Assert-FmscNoLinks $Root
    $trusted = @('S-1-5-18','S-1-5-32-544')
    $items = @((Get-Item -LiteralPath $Root -Force)) + @(Get-ChildItem -LiteralPath $Root -Recurse -Force)
    foreach ($item in $items) {
        $acl = Get-Acl -LiteralPath $item.FullName
        if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trusted) {
            throw 'Recovery storage has an untrusted owner. Contact support.'
        }
        foreach ($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
            if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin $trusted -and
                ([int]$rule.FileSystemRights -band 0xD0156)) {
                throw 'Recovery storage permits unprivileged modification. Contact support.'
            }
        }
    }
}
function Initialize-FmscRecoveryStorage {
    param([string]$Root)
    if (Test-Path -LiteralPath $Root) { Assert-FmscRecoveryStorage $Root; return }
    $null = New-Item -ItemType Directory -Path $Root
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $admins = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')
    $acl.SetOwner($admins)
    $acl.SetAccessRuleProtection($true,$false)
    foreach ($sid in @('S-1-5-18','S-1-5-32-544','S-1-5-32-545')) {
        $rights = if ($sid -eq 'S-1-5-32-545') { 'ReadAndExecute' } else { 'FullControl' }
        $rule = New-Object Security.AccessControl.FileSystemAccessRule(
            (New-Object Security.Principal.SecurityIdentifier($sid)), $rights, 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Root -AclObject $acl
    if (@(Get-ChildItem -LiteralPath $Root -Force).Count) { throw 'Unexpected files in newly created recovery storage.' }
    Assert-FmscRecoveryStorage $Root
}
function Write-FmscAtomicFile {
    param([string]$Path, [byte[]]$Bytes)
    $temporary = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $stream = New-Object IO.FileStream($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write,
            [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
        try { $stream.Write($Bytes,0,$Bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        # Files created by an elevated user's token must also be owned by Administrators.
        $acl = Get-Acl -LiteralPath $temporary
        $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
        Set-Acl -LiteralPath $temporary -AclObject $acl
        if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary,$Path,[System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($temporary,$Path) }
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}
function Read-FmscRecoveryJournal {
    param([string]$Root)
    $path = Join-Path $Root 'recovery.json'
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    Assert-FmscRecoveryStorage $Root
    if ((Get-Item -LiteralPath $path).Length -gt 32768) { throw 'Recovery record is too large.' }
    $record = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($record.Format -ne 'fmsc-recovery/v1' -or $record.Transaction -notmatch '^[a-f0-9]{32}$' -or
        $record.Phase -notin @('Prepared','Migrating','Activating','Verified','Completed') -or
        $record.UserSid -notmatch '^S-1-5-21-[0-9-]+$' -or
        $record.Version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?$' -or
        $record.Schema -isnot [int] -or $record.Schema -le 0 -or
        $record.BeforeSchema -isnot [int] -or $record.BeforeSchema -lt 0 -or $record.BeforeSchema -gt $record.Schema -or
        $record.LastSchema -isnot [int] -or $record.LastSchema -lt 0 -or $record.LastSchema -gt $record.Schema -or
        $record.ScriptSha256 -notmatch '^[a-fA-F0-9]{64}$' -or $record.HelperSha256 -notmatch '^[a-fA-F0-9]{64}$') {
        throw 'Recovery record is invalid. FMSC was not opened; contact support.'
    }
    if ($record.Backup -and ($record.BackupSha256 -notmatch '^[a-fA-F0-9]{64}$' -or $record.BeforeSchema -le 0)) {
        throw 'Recorded migration backup information is invalid.'
    }
    if ($record.UserSid -eq [Security.Principal.WindowsIdentity]::GetCurrent().User.Value) {
        $download = [IO.Path]::GetFullPath($record.DownloadRoot).TrimEnd('\')
        if ((Split-Path -Leaf $download) -notmatch '^prepared-[a-f0-9]{32}$' -or
            (Split-Path -Parent $download) -ine [IO.Path]::GetFullPath((Get-FmscDownloadParent)).TrimEnd('\')) {
            throw 'Recorded download cleanup path is outside this Windows user updater storage.'
        }
    }
    return $record
}
function Save-FmscRecoveryJournal {
    param([string]$Root, $Record)
    Write-FmscAtomicFile (Join-Path $Root 'recovery.json') ([Text.Encoding]::UTF8.GetBytes(($Record | ConvertTo-Json -Depth 5)))
}
function Assert-FmscResumeIdentity {
    param($Record, [string]$Root, [string]$Version, [int]$Schema, [string]$Script)
    if ($Record.UserSid -ne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -or
        $Record.Version -ne $Version -or $Record.Schema -ne $Schema -or
        (Get-FileHash -LiteralPath $Script -Algorithm SHA256).Hash -ne $Record.ScriptSha256 -or
        (Get-FileHash -LiteralPath (Join-Path $Root 'UpdateRecovery.ps1') -Algorithm SHA256).Hash -ne $Record.HelperSha256) {
        throw 'Recovery user, installer version or installer digest did not match. Recovery stopped.'
    }
}
function Set-FmscRecoveryPhase {
    param([string]$Phase)
    if (-not $script:RecoveryJournal) { return }
    $script:RecoveryJournal.Phase = $Phase
    Save-FmscRecoveryJournal $script:RecoveryRoot $script:RecoveryJournal
}
function Install-FmscStableLauncher {
    param([string]$Source, [string]$Root, [string]$ShortcutPath, [string]$Application)
    Initialize-FmscRecoveryStorage $Root
    foreach ($name in @('UpdateRecovery.ps1','Launch-FMSC.ps1')) {
        Write-FmscAtomicFile (Join-Path $Root $name) ([IO.File]::ReadAllBytes((Join-Path $Source "UpdateSupport/$name")))
    }
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
    $shortcut.Arguments = '-NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + (Join-Path $Root 'Launch-FMSC.ps1') + '"'
    $shortcut.WorkingDirectory = $Root
    $shortcut.Description = 'FMSC School Management System'
    $shortcut.IconLocation = "$Application,0"
    $shortcut.Save()
}
function Initialize-FmscRecoveryTransaction {
    param([string]$Prepared, [string]$Root, [string]$Version, [int]$Schema, [string]$UserSid)
    Initialize-FmscRecoveryStorage $Root
    $prior = Read-FmscRecoveryJournal $Root
    if ($prior -and $prior.Phase -ne 'Completed') { throw 'An unfinished update must be recovered before installing another release.' }
    $transaction = [Guid]::NewGuid().ToString('N')
    $bytes = [long](Get-ChildItem -LiteralPath (Join-Path $Prepared 'application') -Recurse -File | Measure-Object Length -Sum).Sum
    $drive = New-Object IO.DriveInfo([IO.Path]::GetPathRoot($Root))
    if ($drive.AvailableFreeSpace -lt ($bytes * 2 + 256MB)) { throw 'Insufficient disk space for the protected recovery copy and application staging.' }
    $transactions = Join-Path $Root 'transactions'
    $null = New-Item -ItemType Directory -Path $transactions -Force
    # Only completed/orphaned snapshots are removed, before the next transaction starts.
    foreach ($entry in Get-ChildItem -LiteralPath $transactions -Directory) {
        if ($entry.Name -match '^[a-f0-9]{32}$') {
            Remove-VerifiedUpdateDirectory $entry.FullName (Join-Path $transactions $entry.Name)
        }
    }
    $snapshot = Join-Path $transactions $transaction
    $null = New-Item -ItemType Directory -Path (Join-Path $snapshot 'metadata') -Force
    Copy-ApplicationFiles -Source (Join-Path $Prepared 'application') -Destination (Join-Path $snapshot 'application')
    Copy-Item -LiteralPath (Join-Path $Prepared 'metadata/update-manifest.json') -Destination (Join-Path $snapshot 'metadata/update-manifest.json')
    # New child items inherit protected rights; assign protected owners as well.
    foreach ($entry in @((Get-Item $transactions)) + @(Get-ChildItem $transactions -Recurse -Force)) {
        $acl = Get-Acl -LiteralPath $entry.FullName
        $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
        Set-Acl -LiteralPath $entry.FullName -AclObject $acl
    }
    Test-PreparedUpdate -Root $snapshot -Version $Version -Schema $Schema
    $support = Join-Path $snapshot 'application/UpdateSupport'
    foreach ($name in @('core.ps1','UpdateRecovery.ps1','Launch-FMSC.ps1')) {
        $destination = if ($name -eq 'core.ps1') { 'Recovery.ps1' } else { $name }
        Write-FmscAtomicFile (Join-Path $Root $destination) ([IO.File]::ReadAllBytes((Join-Path $support $name)))
    }
    $record = [pscustomobject]@{
        Format = 'fmsc-recovery/v1'; Transaction = $transaction; Phase = 'Prepared'; Version = $Version; Schema = $Schema
        UserSid = $UserSid; DownloadRoot = [IO.Path]::GetFullPath($Prepared); Backup = ''; BackupSha256 = ''; BeforeSchema = 0; LastSchema = 0; DatabaseIdentity = ''
        ScriptSha256 = (Get-FileHash (Join-Path $Root 'Recovery.ps1') -Algorithm SHA256).Hash
        HelperSha256 = (Get-FileHash (Join-Path $Root 'UpdateRecovery.ps1') -Algorithm SHA256).Hash
    }
    # The working shortcut is durable before the transaction can authorize SQL.
    Install-FmscStableLauncher -Source (Join-Path $snapshot 'application') -Root $Root -ShortcutPath (Join-Path ([Environment]::GetFolderPath('Desktop')) 'FMSC SMS.lnk') -Application 'C:\FMSC School Management\FMSC_SMS.exe'
    Save-FmscRecoveryJournal $Root $record
    return $record
}
function Assert-FmscRecoveryDatabaseIdentity {
    if (-not $script:RecoveryJournal) { return }
    $name = $DatabaseName.Replace("'", "''")
    $lines = Invoke-SqlMaster -Query "SET NOCOUNT ON; SELECT CONVERT(nvarchar(36),database_guid) FROM sys.database_recovery_status WHERE database_id=DB_ID(N'$name');"
    $identity = @($lines | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ -match '^[a-fA-F0-9]{8}-[a-fA-F0-9-]{27}$' })
    if ($identity.Count -ne 1) { throw 'Cannot confirm the school database identity. Recovery stopped.' }
    if ($script:RecoveryJournal.DatabaseIdentity -and $script:RecoveryJournal.DatabaseIdentity -ne $identity[0]) {
        throw 'The school database identity changed since this update began. Contact support; no recovery migration was run.'
    }
    $script:RecoveryJournal.DatabaseIdentity = $identity[0]
    Save-FmscRecoveryJournal $script:RecoveryRoot $script:RecoveryJournal
}
function Confirm-FmscRecoveryBackup {
    param([int]$CurrentSchema)
    if (-not $script:RecoveryJournal -or -not $script:RecoveryJournal.Backup) { return }
    $script:RecoveryBackupPath = $script:RecoveryJournal.Backup
    if ($CurrentSchema -lt $script:RecoveryJournal.BeforeSchema -or
        (Get-FileHash -LiteralPath $script:RecoveryBackupPath -Algorithm SHA256).Hash -ne $script:RecoveryJournal.BackupSha256) {
        throw 'The recorded pre-migration backup or database baseline changed. Recovery stopped.'
    }
    $sqlPath = $script:RecoveryBackupPath.Replace("'", "''")
    Invoke-SqlMaster -Query "RESTORE VERIFYONLY FROM DISK = N'$sqlPath' WITH CHECKSUM" | Out-Null
}
function Complete-FmscRecoveryTransaction {
    if (-not $script:RecoveryJournal) { return }
    $snapshot = Join-Path $script:RecoveryRoot ('transactions/' + $script:RecoveryJournal.Transaction)
    # Verified is durable before any rollback or payload cleanup. This independent
    # manifest survives partially deleted snapshots and allows verification again.
    if ($script:RecoveryJournal.Phase -ne 'Verified') {
        $verification = Join-Path $script:RecoveryRoot 'verification'
        $null = New-Item -ItemType Directory -Path (Join-Path $verification 'metadata') -Force
        foreach ($directory in @($verification,(Join-Path $verification 'metadata'))) {
            $acl = Get-Acl $directory
            $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
            Set-Acl $directory $acl
        }
        Write-FmscAtomicFile (Join-Path $verification 'metadata/update-manifest.json') ([IO.File]::ReadAllBytes((Join-Path $snapshot 'metadata/update-manifest.json')))
        Set-FmscRecoveryPhase 'Verified'
    }
}
function Remove-FmscCompletedSnapshot {
    if (-not $script:RecoveryJournal) { return }
    $snapshot = Join-Path $script:RecoveryRoot ('transactions/' + $script:RecoveryJournal.Transaction)
    try { Remove-VerifiedUpdateDirectory $snapshot $snapshot }
    catch { Write-WarningMessage "Verified recovery snapshot cleanup is pending: $($_.Exception.Message)" }
    Set-FmscRecoveryPhase 'Completed'
}
