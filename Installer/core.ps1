$ErrorActionPreference = 'Stop'
$AppName = 'FMSC School Management System'
$AppVersion = '1.0.0'
$DatabaseVersion = '2'
$ReleaseRoot = $PSScriptRoot
$AppSource      = Join-Path $ReleaseRoot 'FMSC_SMS'
$DatabaseSource = Join-Path $ReleaseRoot 'Database'
$MigrationsDir  = Join-Path $ReleaseRoot 'Migrations'
$LocalDBSource  = Join-Path $ReleaseRoot 'LocalDB'
$AppDir = 'C:\FMSC School Management'
$DatabaseDir = 'C:\ProgramData\FMSC_Database'
# The new application is copied to a staging folder beside the live install and only renamed into place once it has been verified.
$AppStagingDir  = "$AppDir.staging"
$AppRollbackDir = "$AppDir.rollback"
$LiveMdf = Join-Path $DatabaseDir 'FMSC_DB.mdf'
$LiveLdf = Join-Path $DatabaseDir 'FMSC_DB_log.ldf'
$TemplateMdf = Join-Path $DatabaseSource 'FMSC_DB.mdf'
$TemplateLdf = Join-Path $DatabaseSource 'FMSC_DB_log.ldf'
$LogFile = Join-Path $DatabaseDir 'Install.log'
$SqlInstance = '(localdb)\MSSQLLocalDB'
$DatabaseName = 'FMSC_DB'
$ColorTitle   = 'Cyan'
$ColorInfo    = 'White'
$ColorStep    = 'Yellow'
$ColorSuccess = 'Green'
$ColorWarning = 'Yellow'
$ColorError   = 'Red'
$ColorDim     = 'DarkGray'
$ColorAccent  = 'Magenta'
try {
    $Host.UI.RawUI.WindowTitle =
        "$AppName - Installer"
    $WindowSize =
        $Host.UI.RawUI.WindowSize
    if ($WindowSize.Width -lt 110) {
        $WindowSize.Width = 110
    }
    if ($WindowSize.Height -lt 35) {
        $WindowSize.Height = 35
    }
    $Host.UI.RawUI.WindowSize = $WindowSize
}
catch {
}
function Show-Banner {
    Clear-Host
    Write-Host ''
    Write-Host '  ==============================================================' -ForegroundColor $ColorTitle
    Write-Host '                                                                ' -ForegroundColor $ColorTitle
    Write-Host '              FMSC SCHOOL MANAGEMENT SYSTEM                   ' -ForegroundColor $ColorTitle
    Write-Host '                    INSTALLER / UPDATER                        ' -ForegroundColor $ColorAccent
    Write-Host '                                                                ' -ForegroundColor $ColorTitle
    Write-Host '  ==============================================================' -ForegroundColor $ColorTitle
    Write-Host ''
    Write-Host "  Application Release : $AppVersion" -ForegroundColor $ColorInfo
    Write-Host "  Required DB Schema  : $DatabaseVersion" -ForegroundColor $ColorInfo
    Write-Host ''
}
function Write-Section {
    param(
        [string]$Title
    )
    Write-Host ''
    Write-Host '  --------------------------------------------------------------' -ForegroundColor $ColorDim
    Write-Host "  $Title" -ForegroundColor $ColorStep
    Write-Host '  --------------------------------------------------------------' -ForegroundColor $ColorDim
    Write-Host ''
}
function Write-Step {
    param(
        [string]$Message
    )
    Write-Host "  > $Message" -ForegroundColor $ColorInfo
}
function Write-Success {
    param(
        [string]$Message
    )
    Write-Host "  [OK] $Message" -ForegroundColor $ColorSuccess
}
function Write-WarningMessage {
    param(
        [string]$Message
    )
    Write-Host "  [!] $Message" -ForegroundColor $ColorWarning
}
function Write-Failure {
    param(
        [string]$Message
    )
    Write-Host "  [X] $Message" -ForegroundColor $ColorError
}
function Pause-Step {
    Start-Sleep -Milliseconds 700
}
function Pause-Long {
    Start-Sleep -Milliseconds 1200
}
function Write-Log {
    param(
        [string]$Message
    )
    $Time =
        Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $Line =
        "[$Time] $Message"
    Write-Host "  $Line" -ForegroundColor $ColorDim
    try {
        if (-not (Test-Path -LiteralPath $DatabaseDir)) {
            New-Item `
                -ItemType Directory `
                -Path $DatabaseDir `
                -Force |
                Out-Null
        }
        Add-Content `
            -Path $LogFile `
            -Value $Line `
            -ErrorAction SilentlyContinue
    }
    catch {
    }
}
function Convert-ToVersion {
    param(
        [string]$Version
    )
    try {
        return [System.Version]::Parse($Version)
    }
    catch {
        throw "Invalid version number: $Version"
    }
}
function Convert-ToDbVersion {
    param(
        [string]$Version
    )
    if ($Version -notmatch '^\d+$') {
        throw "Invalid database schema version: $Version (expected a plain integer, e.g. '1', '2', '3')"
    }
    return [int]$Version
}
function Invoke-SqlMaster {
    param(
        [string]$Query
    )
    $Output =
        & $script:SqlCmdPath `
            -S $SqlInstance `
            -E `
            -d master `
            -b `
            -Q $Query `
            2>&1
    if ($LASTEXITCODE -ne 0) {
        $Message =
            $Output -join "`r`n"
        throw (
            "SQL command failed:`r`n$Message"
        )
    }
    return $Output
}
function Invoke-SqlDatabase {
    param(
        [string]$Query
    )
    $Output =
        & $script:SqlCmdPath `
            -S $SqlInstance `
            -E `
            -d $DatabaseName `
            -b `
            -Q $Query `
            2>&1
    if ($LASTEXITCODE -ne 0) {
        $Message =
            $Output -join "`r`n"
        throw (
            "FMSC_DB SQL command failed:`r`n$Message"
        )
    }
    return $Output
}
function Invoke-SqlFile {
    param(
        [string]$FilePath
    )
    if (-not (Test-Path -LiteralPath $FilePath)) {
        throw `
            "SQL migration file was not found: $FilePath"
    }
    Write-Log `
        "Executing migration against database: $DatabaseName"
    Write-Log `
        "Migration file: $FilePath"
    $Output =
        & $script:SqlCmdPath `
            -S $SqlInstance `
            -E `
            -d $DatabaseName `
            -b `
            -i $FilePath `
            2>&1
    if ($Output) {
        foreach ($Line in $Output) {
            Write-Log $Line.ToString()
        }
    }
    if ($LASTEXITCODE -ne 0) {
        throw `
            "SQL migration failed: $FilePath"
    }
    Write-Log `
        "Migration completed successfully: $FilePath"
}
<#
FIX 38 - transactional / recovery semantics for a migration step.
Invoke-SqlFile alone cannot give a migration atomicity: sqlcmd sends the file
batch by batch (split on GO), and each batch that succeeds commits before the
next is attempted. A failure half way through therefore leaves the database
partially migrated while dbo.DatabaseVersion still reports the OLD version -
the version is not falsely advanced, but the schema has moved, and re-running
the migration will typically fail on objects the first run already created.
What this wrapper adds:
  * the version BEFORE the run is captured, so a failure reports the exact state
    ("still at N, expected M") instead of a bare "SQL migration failed";
  * after a successful run the recorded version is ASSERTED to equal the
    migration's own version. A migration that completes without recording its
    version, or records a different one, is reported as the defect it is rather
    than passed silently to the next migration;
  * a failure message that names the recovery position, so the database is left
    in a state an operator can act on.
This is a checkpoint, not a magic transaction. A migration needing true
atomicity must itself use SET XACT_ABORT ON with BEGIN TRANSACTION / COMMIT
around its DDL and record its version inside that transaction; a migration
spanning multiple GO batches cannot be atomic and should be written to be
idempotent instead.
#>
function New-PreMigrationBackup {
    param(
        [string]$FromVersion,
        [string]$ToVersion
    )
    # A recovery point taken automatically, immediately before the first migration runs.
    $BackupFolder =
        Join-Path `
            $DatabaseDir `
            'Backups'
    New-Item `
        -ItemType Directory `
        -Path $BackupFolder `
        -Force |
        Out-Null
    $BackupBaseName =
        "FMSC_DB_BeforeMigration_v${FromVersion}_to_v${ToVersion}_" +
        (Get-Date -Format 'yyyyMMdd_HHmmss')
    $BackupPath =
        Join-Path `
            $BackupFolder `
            "$BackupBaseName.bak"
    # Never overwrite an existing recovery point: a second upgrade attempt in the same second must not destroy the first attempt's backup.
    $CollisionCounter = 1
    while (Test-Path -LiteralPath $BackupPath) {
        $BackupPath =
            Join-Path `
                $BackupFolder `
                "$BackupBaseName`_$CollisionCounter.bak"
        $CollisionCounter++
    }
    # A single quote is the only character that can break the literal, and a T-SQL string literal escapes it by doubling.
    $SqlPath =
        $BackupPath.Replace("'", "''")
    Write-Step `
        'Creating verified recovery backup...'
    Write-Log `
        "Creating pre-migration recovery backup: $BackupPath"
    try {
        Invoke-SqlMaster `
            -Query (
                "BACKUP DATABASE [$DatabaseName] " +
                "TO DISK = N'$SqlPath' " +
                "WITH INIT, CHECKSUM, STATS = 10"
            ) |
            Out-Null
    }
    catch {
        throw (
            "The pre-migration recovery backup could not be created, so no " +
            "migration was run.`r`n`r`n" +
            "Backup file : $BackupPath`r`n" +
            "Database    : $DatabaseName`r`n`r`n" +
            "The database has NOT been changed.`r`n`r`n" +
            "Underlying error: $($_.Exception.Message)"
        )
    }
    Write-Step `
        'Verifying recovery backup...'
    try {
        Invoke-SqlMaster `
            -Query (
                "RESTORE VERIFYONLY FROM DISK = N'$SqlPath' WITH CHECKSUM"
            ) |
            Out-Null
    }
    catch {
        throw (
            "The pre-migration recovery backup did not pass RESTORE " +
            "VERIFYONLY, so no migration was run.`r`n`r`n" +
            "Backup file : $BackupPath`r`n`r`n" +
            "A backup that cannot be verified is not a recovery point. The " +
            "database has NOT been changed.`r`n`r`n" +
            "Underlying error: $($_.Exception.Message)"
        )
    }
    if (-not (Test-Path -LiteralPath $BackupPath)) {
        throw (
            "SQL Server reported that the pre-migration backup succeeded, but " +
            "the backup file does not exist, so no migration was run.`r`n`r`n" +
            "Backup file : $BackupPath"
        )
    }
    $BackupSize =
        (Get-Item -LiteralPath $BackupPath).Length
    if ($BackupSize -le 0) {
        throw (
            "The pre-migration backup file was created but is empty, so no " +
            "migration was run.`r`n`r`n" +
            "Backup file : $BackupPath"
        )
    }
    $BackupSizeMb =
        [math]::Round(
            $BackupSize / 1MB,
            2
        )
    $RecordPath =
        Join-Path `
            $BackupFolder `
            'LastPreMigrationBackup.txt'
    Set-Content `
        -LiteralPath $RecordPath `
        -Value @(
            "Backup file       : $BackupPath"
            "Backup size (MB)  : $BackupSizeMb"
            "Created (UTC)     : $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss'))"
            "Database          : $DatabaseName"
            "Schema version    : $FromVersion -> $ToVersion"
            "Installer version : $AppVersion"
            "Verified with     : RESTORE VERIFYONLY WITH CHECKSUM"
        ) `
        -Encoding UTF8
    Write-Success `
        "Recovery backup created and verified ($BackupSizeMb MB)."
    Write-Host `
        "    $BackupPath" `
        -ForegroundColor $ColorDim
    Write-Log `
        "Recovery backup verified: $BackupPath ($BackupSizeMb MB)"
    Write-Log `
        "Recovery backup record: $RecordPath"
    Pause-Long
    return $BackupPath
}
function Invoke-SqlMigration {
    param(
        [string]$FilePath,
        [int]$ExpectedVersion,
        [string]$RecoveryBackupPath
    )
    $VersionBefore =
        Get-DatabaseVersion
    Write-Log `
        "Applying migration $ExpectedVersion (database is at $VersionBefore before the run)"
    try {
        Invoke-SqlFile `
            -FilePath $FilePath
    }
    catch {
        $VersionAfterFailure =
            Get-DatabaseVersion
        # The parameter is optional so the wrapper stays callable on its own, but the migration loop always passes the path it created and verified.
        if ([string]::IsNullOrWhiteSpace($RecoveryBackupPath)) {
            $RecoveryBackupDescription =
                '(no pre-migration backup was recorded for this run)'
        }
        else {
            $RecoveryBackupDescription =
                $RecoveryBackupPath
        }
        throw (
            "Migration $ExpectedVersion failed.`r`n`r`n" +
            "Database version before the run : $VersionBefore`r`n" +
            "Database version now            : $VersionAfterFailure`r`n`r`n" +
            "If the two differ, the migration's version update DID commit and only " +
            "a later step failed. If they are the same, the schema may still have " +
            "been partially changed by batches that committed before the failure.`r`n`r`n" +
            "Recovery: inspect the objects the migration was meant to create. " +
            "If the schema is inconsistent, restore this verified backup, taken " +
            "automatically immediately before the migration chain started:`r`n`r`n" +
            "    $RecoveryBackupDescription`r`n`r`n" +
            "then re-run the installer.`r`n`r`n" +
            "Underlying error: $($_.Exception.Message)"
        )
    }
    $VersionAfter =
        Get-DatabaseVersion
    $VersionAfterObject =
        Convert-ToDbVersion $VersionAfter
    if ($VersionAfterObject -ne $ExpectedVersion) {
        throw (
            "Migration $ExpectedVersion ran without error, but the database " +
            "recorded version $VersionAfter instead of $ExpectedVersion.`r`n`r`n" +
            "Refusing to continue: every later migration assumes this one's version " +
            "is current, and dbo.DatabaseVersion must never report a version the " +
            "schema has not actually reached.`r`n`r`n" +
            "Check that the migration file updates dbo.DatabaseVersion to its own " +
            "version, then re-run the installer."
        )
    }
    Write-Log `
        "Migration $ExpectedVersion applied and recorded (database is now at $VersionAfter)"
}
function Get-InstalledApplicationVersion {
    $InstalledExe =
        Join-Path $AppDir 'FMSC_SMS.exe'
    if (-not (Test-Path -LiteralPath $InstalledExe)) {
        return $null
    }
    try {
        $Version =
            (Get-Item $InstalledExe).VersionInfo.ProductVersion
        if ([string]::IsNullOrWhiteSpace($Version)) {
            return $null
        }
        $Version =
            $Version.Split('+')[0].Trim()
        if ($Version -match '^\d+\.\d+\.\d+$') {
            return $Version
        }
        if ($Version -match '^\d+\.\d+\.\d+\.\d+$') {
            return (
                ($Version -split '\.')[0..2] -join '.'
            )
        }
        return $null
    }
    catch {
        return $null
    }
}
function Get-DatabaseVersion {
    Write-Step `
        'Reading database schema version...'
    $Query =
        "SET NOCOUNT ON; " +
        "SELECT TOP 1 VersionNumber " +
        "FROM dbo.DatabaseVersion " +
        "ORDER BY VersionId DESC;"
    $Result =
        Invoke-SqlDatabase `
            -Query $Query
    $DetectedVersion = $null
    foreach ($Line in $Result) {
        $Text =
            $Line.ToString().Trim()
        if (
            -not [string]::IsNullOrWhiteSpace($Text) -and
            $Text -match '^\d+$'
        ) {
            $DetectedVersion = $Text
        }
    }
    if ([string]::IsNullOrWhiteSpace($DetectedVersion)) {
        throw `
            'Unable to read a valid database schema version from dbo.DatabaseVersion. Expected a plain integer (e.g. 1, 2, 3).'
    }
    return $DetectedVersion
}
function Test-IsAdministrator {
    $Identity =
        [Security.Principal.WindowsIdentity]::GetCurrent()
    $Principal =
        New-Object Security.Principal.WindowsPrincipal($Identity)
    return $Principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator
    )
}
function Get-InstallerToolsDirectory {
    # A release may ship sqlcmd.exe here so that an install needs no network at all, and anything this installer downloads is cached here too.
    $Tools =
        Join-Path `
            $ReleaseRoot `
            'Tools'
    if (-not (Test-Path -LiteralPath $Tools)) {
        New-Item `
            -ItemType Directory `
            -Path $Tools `
            -Force |
            Out-Null
    }
    return $Tools
}
function Invoke-PrerequisiteDownload {
    param(
        [string]$Url,
        [string]$Destination,
        [string]$Description,
        [string]$ExpectedSha256
    )
    Write-Step `
        "Downloading $Description..."
    Write-Log `
        "Download URL: $Url"
    # Windows PowerShell 5.1 negotiates TLS 1.0 on older builds, and both github.com and dot.net refuse it. Setting it on the process is enough and lasts only as long as the installer runs.
    [Net.ServicePointManager]::SecurityProtocol =
        [Net.ServicePointManager]::SecurityProtocol -bor
        [Net.SecurityProtocolType]::Tls12
    # The progress bar redraws per block in 5.1 and makes a download dramatically slower as well as noisy.
    $PreviousProgress =
        $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    try {
        Invoke-WebRequest `
            -Uri $Url `
            -OutFile $Destination `
            -UseBasicParsing
    }
    finally {
        $ProgressPreference =
            $PreviousProgress
    }
    if (-not (Test-Path -LiteralPath $Destination)) {
        throw `
            "$Description could not be downloaded."
    }
    $Downloaded =
        Get-Item `
            -LiteralPath $Destination
    if ($Downloaded.Length -le 0) {
        throw `
            "$Description downloaded as an empty file."
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha256)) {
        $Actual =
            (Get-FileHash `
                -LiteralPath $Destination `
                -Algorithm SHA256).Hash
        if ($Actual -ne $ExpectedSha256.ToUpperInvariant()) {
            Remove-Item `
                -LiteralPath $Destination `
                -Force `
                -ErrorAction SilentlyContinue
            throw (
                "$Description failed its integrity check, so it was discarded.`r`n`r`n" +
                "Expected SHA256: $ExpectedSha256`r`n" +
                "Actual SHA256:   $Actual"
            )
        }
        Write-Log `
            "Integrity check passed (SHA256 $Actual)."
    }
    Write-Success `
        "$Description downloaded."
}
function Resolve-SqlCmd {
    # sqlcmd is the prerequisite this installer genuinely needs and which no
    # Windows machine has by default. It used to stop here and tell the operator
    # to go and install the SQL Server command-line tools themselves. It now
    # resolves sqlcmd in three steps, cheapest first, and only touches the network
    # when it has to
    # 1. sqlcmd.exe already on PATH - a machine that has the SQL tools
    # 2. <ReleaseRoot>\Tools\sqlcmd.exe - placed there by a release, or by an
    # earlier run of this installer, which is what makes a fully offline
    # install possible
    # 3. download Microsoft's go-sqlcmd: a standalone executable with no ODBC
    # driver dependency and no administrator requirement, unlike the older
    # ODBC build which needs the ODBC driver installed as well
    $OnPath =
        Get-Command `
            'sqlcmd.exe' `
            -ErrorAction SilentlyContinue
    if ($OnPath) {
        Write-Success `
            "sqlcmd found: $($OnPath.Source)"
        return $OnPath.Source
    }
    $Tools =
        Get-InstallerToolsDirectory
    $LocalCopy =
        Join-Path `
            $Tools `
            'sqlcmd.exe'
    if (Test-Path -LiteralPath $LocalCopy) {
        Write-Success `
            "sqlcmd found in the release tools folder: $LocalCopy"
        return $LocalCopy
    }
    $Architecture =
        if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'amd64' }
    $AssetName =
        "sqlcmd-windows-$Architecture.zip"
    $Archive =
        Join-Path `
            $Tools `
            $AssetName
    # A version, its asset name and its digest are fixed here instead, so the same FMSC release always installs the same sqlcmd.
    $PinnedBuilds =
        @{
            'amd64' = @{
                Version =
                    'v1.10.0'
                Sha256  =
                    'A4C28332FCC6E497D655E53AC8F4939F4AB170F9CFD32D0FA5081B60F4D9D691'
            }
            'arm64' = @{
                Version =
                    'v1.10.0'
                Sha256  =
                    'AC6F257DCE55E7F34D618EA9AF736D9077854CADB6AF6FDB0259C90D42E0B8D2'
            }
        }
    $Pinned =
        $PinnedBuilds[$Architecture]
    $AssetUrl =
        'https://github.com/microsoft/go-sqlcmd/releases/download/' +
        $Pinned.Version + '/' + $AssetName
    $AssetSha256 =
        $Pinned.Sha256
    Write-Log `
        "sqlcmd is not packaged locally; pinned download in use: $($Pinned.Version) ($Architecture)."
    try {
        Invoke-PrerequisiteDownload `
            -Url $AssetUrl `
            -Destination $Archive `
            -Description "the sqlcmd tool (Microsoft go-sqlcmd, $Architecture)" `
            -ExpectedSha256 $AssetSha256
    }
    catch {
        throw (
            "sqlcmd is required for database registration and migration, and it " +
            "could not be obtained automatically.`r`n`r`n" +
            "Connect this machine to the internet and run the installer again, or " +
            "do this on a machine that has internet:`r`n`r`n" +
            "    1. Download:`r`n" +
            "       $AssetUrl`r`n`r`n" +
            "    2. Extract sqlcmd.exe from the zip.`r`n`r`n" +
            "    3. Copy sqlcmd.exe to:`r`n" +
            "       $LocalCopy`r`n`r`n" +
            "The installer uses that copy without needing the network at all.`r`n`r`n" +
            "Original error: $($_.Exception.Message)"
        )
    }
    Expand-Archive `
        -LiteralPath $Archive `
        -DestinationPath $Tools `
        -Force
    if (-not (Test-Path -LiteralPath $LocalCopy)) {
        # The zip keeps sqlcmd.exe at its root today; search for it rather than depend on that staying true.
        $Found =
            Get-ChildItem `
                -LiteralPath $Tools `
                -Filter 'sqlcmd.exe' `
                -Recurse `
                -File `
                -ErrorAction SilentlyContinue |
                Select-Object `
                    -First 1
        if (-not $Found) {
            throw `
                "sqlcmd.exe was not found inside $Archive after extracting it."
        }
        $LocalCopy =
            $Found.FullName
    }
    Remove-Item `
        -LiteralPath $Archive `
        -Force `
        -ErrorAction SilentlyContinue
    # Reported, deliberately not enforced: the download was already checked against the SHA-256 GitHub publishes for it.
    try {
        $Signature =
            Get-AuthenticodeSignature `
                -FilePath $LocalCopy
        Write-Log `
            "sqlcmd signature: $($Signature.Status) $($Signature.SignerCertificate.Subject)"
    }
    catch {
        Write-Log `
            'sqlcmd signature could not be read.'
    }
    Write-Success `
        "sqlcmd downloaded and ready: $LocalCopy"
    return $LocalCopy
}
function Copy-ApplicationFiles {
    param(
        [string]$Source,
        [string]$Destination
    )
    # Copies the published application into a destination folder that is not the live installation. The live folder is only ever replaced by Enable-StagedApplication, never written into file by file.
    New-Item `
        -ItemType Directory `
        -Path $Destination `
        -Force |
        Out-Null
    foreach (
        $Item in
        (Get-ChildItem -LiteralPath $Source -Force)
    ) {
        $Target =
            Join-Path `
                $Destination `
                $Item.Name
        if ($Item.PSIsContainer) {
            Copy-Item `
                -LiteralPath $Item.FullName `
                -Destination $Target `
                -Recurse `
                -Force
        }
        else {
            Copy-Item `
                -LiteralPath $Item.FullName `
                -Destination $Target `
                -Force
        }
    }
}
function Get-ApplicationFileManifest {
    param(
        [string]$Root
    )
    # A recursive inventory of an application folder: every directory and file below the root, keyed by relative path.
    $Manifest = @{}
    # The root is normalised through Get-Item because Get-ChildItem builds the paths of its children in the provider's normalised (long) form.
    $Prefix =
        (Get-Item -LiteralPath $Root).FullName.TrimEnd('\') + '\'
    foreach (
        $Directory in
        (Get-ChildItem -LiteralPath $Root -Recurse -Directory -Force)
    ) {
        $Manifest[
            $Directory.FullName.Substring($Prefix.Length).TrimStart('\')
        ] = -1
    }
    foreach (
        $File in
        (Get-ChildItem -LiteralPath $Root -Recurse -File -Force)
    ) {
        $Manifest[
            $File.FullName.Substring($Prefix.Length).TrimStart('\')
        ] = $File.Length
    }
    return $Manifest
}
function Test-StagedApplication {
    param(
        [string]$Source,
        [string]$Staging
    )
    # "the copy returned" is not the same as "the copy is complete". Returns the file count and total size for the log; throws, listing the first differences, when the two trees disagree.
    if (-not (Test-Path -LiteralPath $Staging)) {
        throw `
            "The staged application folder was not created: $Staging"
    }
    $SourceManifest =
        Get-ApplicationFileManifest -Root $Source
    $StagingManifest =
        Get-ApplicationFileManifest -Root $Staging
    $Problems = @()
    foreach ($RelativePath in $SourceManifest.Keys) {
        if (-not $StagingManifest.ContainsKey($RelativePath)) {
            $Problems += "missing: $RelativePath"
            continue
        }
        if ($StagingManifest[$RelativePath] -ne $SourceManifest[$RelativePath]) {
            $Problems += (
                "size differs: $RelativePath " +
                "(published $($SourceManifest[$RelativePath]) bytes, " +
                "staged $($StagingManifest[$RelativePath]) bytes)"
            )
        }
    }
    foreach ($RelativePath in $StagingManifest.Keys) {
        if (-not $SourceManifest.ContainsKey($RelativePath)) {
            $Problems += "unexpected: $RelativePath"
        }
    }
    if ($Problems.Count -gt 0) {
        $Shown =
            $Problems |
            Select-Object -First 20
        $Remainder = ''
        if ($Problems.Count -gt $Shown.Count) {
            $Remainder =
                "`r`n... and $($Problems.Count - $Shown.Count) more difference(s)."
        }
        throw (
            "The staged application does not match the published application.`r`n`r`n" +
            "Staged    : $Staging`r`n" +
            "Published : $Source`r`n`r`n" +
            ($Shown -join "`r`n") +
            $Remainder + "`r`n`r`n" +
            "Nothing has been changed: the installed application and the " +
            "database are both untouched."
        )
    }
    $FileCount = 0
    $TotalBytes = 0
    foreach ($RelativePath in $SourceManifest.Keys) {
        if ($SourceManifest[$RelativePath] -ne -1) {
            $FileCount++
            $TotalBytes +=
                $SourceManifest[$RelativePath]
        }
    }
    return [PSCustomObject]@{
        Files = $FileCount
        Bytes = $TotalBytes
    }
}
function Enable-StagedApplication {
    param(
        [string]$Staging,
        [string]$AppDir,
        [string]$RollbackDir
    )
    # activation is a pair of folder renames on the same volume, not a file-by-file overwrite of the live installation.
    if (-not (Test-Path -LiteralPath $Staging)) {
        throw `
            "There is no staged application to activate: $Staging"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $Staging 'FMSC_SMS.exe'))) {
        throw `
            "The staged application does not contain FMSC_SMS.exe: $Staging"
    }
    $HadPreviousInstall =
        Test-Path -LiteralPath $AppDir
    if ($HadPreviousInstall) {
        if (Test-Path -LiteralPath $RollbackDir) {
            Write-Log `
                "Removing the previous rollback copy: $RollbackDir"
            Remove-Item `
                -LiteralPath $RollbackDir `
                -Recurse `
                -Force
        }
        Write-Log `
            "Setting the installed application aside: $AppDir -> $RollbackDir"
        Move-Item `
            -LiteralPath $AppDir `
            -Destination $RollbackDir `
            -Force
    }
    try {
        Move-Item `
            -LiteralPath $Staging `
            -Destination $AppDir `
            -Force
    }
    catch {
        $ActivationError =
            $_.Exception.Message
        $Restored = $false
        # A directory move that fails part-way can leave a partially populated destination behind. Any $AppDir present at this point is such an artefact: the previous application was already moved aside into $RollbackDir.
        if (Test-Path -LiteralPath $AppDir) {
            try {
                Remove-Item `
                    -LiteralPath $AppDir `
                    -Recurse `
                    -Force
                Write-Log `
                    "Removed the partially activated application folder: $AppDir"
            }
            catch {
                Write-Log `
                    "The partially activated application folder could not be removed: $($_.Exception.Message)"
            }
        }
        if (
            $HadPreviousInstall -and
            (Test-Path -LiteralPath $RollbackDir) -and
            (-not (Test-Path -LiteralPath $AppDir))
        ) {
            try {
                Move-Item `
                    -LiteralPath $RollbackDir `
                    -Destination $AppDir `
                    -Force
                $Restored = $true
            }
            catch {
                Write-Log `
                    "The previous application could not be restored automatically: $($_.Exception.Message)"
            }
        }
        if ($Restored) {
            $Recovery =
                "The previous application was put back in place at $AppDir."
        }
        elseif (Test-Path -LiteralPath $RollbackDir) {
            $Recovery =
                "The previous application is intact at $RollbackDir and must " +
                "be moved back to $AppDir by hand."
        }
        else {
            $Recovery =
                "No previous application was installed."
        }
        throw (
            "The staged application could not be activated.`r`n`r`n" +
            "Staged             : $Staging`r`n" +
            "Application folder : $AppDir`r`n`r`n" +
            $Recovery + "`r`n`r`n" +
            "The database has not been migrated.`r`n`r`n" +
            "Underlying error: $ActivationError"
        )
    }
    if (-not (Test-Path -LiteralPath (Join-Path $AppDir 'FMSC_SMS.exe'))) {
        throw `
            "The application was activated, but FMSC_SMS.exe is not present in $AppDir."
    }
    Write-Log `
        "Application activated: $AppDir"
}
try {
    Show-Banner
    Write-Log '================================================'
    Write-Log 'FMSC SMS installation started.'
    Write-Log "Application version: $AppVersion"
    Write-Log "Required database version: $DatabaseVersion"
    Write-Log "Release root: $ReleaseRoot"
    Write-Log "Application directory: $AppDir"
    Write-Log "Database directory: $DatabaseDir"
    Write-Log "Migrations directory: $MigrationsDir"
    Write-Log '================================================'
    Pause-Step
    Write-Section `
        'STEP 1 OF 8  -  INSTALLATION PREPARATION'
    Write-Step `
        'Checking administrator privileges...'
    if (-not (Test-IsAdministrator)) {
        Write-Failure `
            'Administrator privileges are required.'
        Write-Host ''
        Write-Host `
            '  Please right-click Install.bat and select:' `
            -ForegroundColor $ColorWarning
        Write-Host `
            '  Run as administrator' `
            -ForegroundColor $ColorWarning
        Write-Host ''
        exit 1
    }
    Write-Success `
        'Administrator privileges confirmed.'
    Write-Step `
        'Checking release files...'
    if (-not (Test-Path -LiteralPath $AppSource)) {
        throw `
            "Published application folder was not found: $AppSource"
    }
    if (-not (Test-Path -LiteralPath $MigrationsDir)) {
        throw `
            "Migrations folder was not found: $MigrationsDir"
    }
    $SourceExe =
        Join-Path $AppSource 'FMSC_SMS.exe'
    if (-not (Test-Path -LiteralPath $SourceExe)) {
        throw `
            "FMSC_SMS.exe was not found: $SourceExe"
    }
    Write-Success `
        'Release files verified.'
    if (-not (Test-Path -LiteralPath $DatabaseDir)) {
        New-Item `
            -ItemType Directory `
            -Path $DatabaseDir `
            -Force |
            Out-Null
    }
    Pause-Long
    Write-Section `
        'STEP 2 OF 8  -  APPLICATION VERSION CHECK'
    $ReleaseAppVersion =
        Convert-ToVersion $AppVersion
    $InstalledAppVersion =
        Get-InstalledApplicationVersion
    if ($InstalledAppVersion) {
        Write-Step `
            "Installed application version : $InstalledAppVersion"
        Write-Step `
            "Release application version   : $AppVersion"
        $InstalledVersionObject =
            Convert-ToVersion $InstalledAppVersion
        if ($InstalledVersionObject -gt $ReleaseAppVersion) {
            throw (
                "The installed application is newer than this release.`r`n`r`n" +
                "Installed application: $InstalledAppVersion`r`n" +
                "This release: $AppVersion`r`n`r`n" +
                "Installation stopped to prevent an application downgrade."
            )
        }
        if ($InstalledVersionObject -eq $ReleaseAppVersion) {
            Write-Success `
                'Same application version detected.'
            Write-Host `
                '  Existing application files may be refreshed.' `
                -ForegroundColor $ColorDim
        }
        else {
            Write-Success `
                'A newer application release is available.'
        }
    }
    else {
        Write-Success `
            'No existing FMSC SMS application detected.'
        Write-Host `
            '  This will be treated as a new application installation.' `
            -ForegroundColor $ColorDim
    }
    Pause-Long
    Write-Section `
        'STEP 3 OF 8  -  APPLICATION SAFETY CHECK'
    Write-Step `
        'Checking whether FMSC SMS is currently running...'
    $RunningApp =
        Get-Process `
            -Name 'FMSC_SMS' `
            -ErrorAction SilentlyContinue
    if ($RunningApp) {
        throw (
            "FMSC SMS is currently running.`r`n`r`n" +
            "Please close the application and run Install.bat again.`r`n`r`n" +
            "The installer will not replace application files while FMSC SMS is running."
        )
    }
    Write-Success `
        'Application is not running.'
    Pause-Step
    Write-Section `
        'STEP 4 OF 8  -  DATABASE ENGINE AND TOOLS'
    Write-Step `
        'Checking SQL Server LocalDB...'
    $SqlLocalDb =
        Get-Command `
            'sqllocaldb.exe' `
            -ErrorAction SilentlyContinue
    if (-not $SqlLocalDb) {
        Write-WarningMessage `
            'SQL Server LocalDB was not found.'
        # The self-contained win-x64 application includes its .NET Desktop Runtime. Tools carries only database prerequisites: sqlcmd.exe and SqlLocalDB.msi.
        $Tools =
            Get-InstallerToolsDirectory
        $MsiPath =
            Join-Path `
                $Tools `
                'SqlLocalDB.msi'
        if (-not (Test-Path -LiteralPath $MsiPath)) {
            $LegacyMsi =
                Join-Path `
                    $LocalDBSource `
                    'SqlLocalDB.msi'
            if (Test-Path -LiteralPath $LegacyMsi) {
                $MsiPath =
                    $LegacyMsi
                Write-Log `
                    'SqlLocalDB.msi was read from the older LocalDB folder; Tools is the merged location now.'
            }
        }
        if (-not (Test-Path -LiteralPath $MsiPath)) {
            # Microsoft publishes no stable URL for SqlLocalDB.msi - it ships inside the SQL Server Express media, whose own unattended switches are not documented well enough to rely on.
            throw (
                "SQL Server LocalDB is not installed, and its installer was not " +
                "found.`r`n`r`n" +
                "Place SqlLocalDB.msi in the release's Tools folder, then run the " +
                "installer again:`r`n`r`n" +
                "    $Tools`r`n`r`n" +
                "(A release packaged before Tools and LocalDB were merged keeps " +
                "working: the older folder is still read.)`r`n`r`n" +
                "It is the LocalDB component of the SQL Server 2022 Express media " +
                "- see https://learn.microsoft.com/en-us/sql/database-engine/" +
                "configure-windows/sql-server-express-localdb`r`n`r`n" +
                "This step cannot be skipped: the application needs LocalDB at run " +
                "time, not only while installing."
            )
        }
        Write-Step `
            'Installing SQL Server LocalDB...'
        $MsiProcess =
            Start-Process `
                -FilePath 'msiexec.exe' `
                -ArgumentList @(
                    '/i',
                    "`"$MsiPath`"",
                    '/qn',
                    '/norestart'
                ) `
                -Wait `
                -PassThru
        if ($MsiProcess.ExitCode -ne 0) {
            throw (
                "SQL Server LocalDB installation failed. " +
                "MSI exit code: $($MsiProcess.ExitCode)"
            )
        }
        Write-Success `
            'SQL Server LocalDB installed.'
        $MachinePath =
            [Environment]::GetEnvironmentVariable(
                'Path',
                'Machine'
            )
        $UserPath =
            [Environment]::GetEnvironmentVariable(
                'Path',
                'User'
            )
        $env:Path =
            "$MachinePath;$UserPath"
    }
    else {
        Write-Success `
            'SQL Server LocalDB is already installed.'
    }
    Write-Step `
        'Checking sqlcmd...'
    # Resolve-SqlCmd looks on PATH first, then in the release's Tools folder, and only downloads Microsoft's standalone go-sqlcmd when neither has it.
    $script:SqlCmdPath =
        Resolve-SqlCmd
    Write-Step `
        'Starting MSSQLLocalDB...'
    $LocalDbOutput =
        & sqllocaldb.exe start MSSQLLocalDB 2>&1
    if ($LocalDbOutput) {
        foreach ($Line in $LocalDbOutput) {
            Write-Log $Line.ToString()
        }
    }
    if ($LASTEXITCODE -ne 0) {
        throw `
            'Unable to start the MSSQLLocalDB instance.'
    }
    Write-Success `
        'MSSQLLocalDB is running.'
    # Use the engine's current pipe rather than a stale LocalDB name resolution.
    $EndpointInfo = & sqllocaldb.exe info MSSQLLocalDB 2>&1
    $EndpointPattern = '(?:np:)?(' + [regex]::Escape('\\.\pipe\LOCALDB#') + '[A-Za-z0-9]+' + [regex]::Escape('\tsql\query') + ')'
    $EndpointMatch = [regex]::Match(($EndpointInfo -join "`n"), $EndpointPattern)
    if ($EndpointMatch.Success) { $script:SqlInstance = 'np:' + $EndpointMatch.Groups[1].Value }

    Pause-Long
    Write-Section `
        'STEP 5 OF 8  -  DATABASE PREPARATION'
    if (Test-Path -LiteralPath $LiveMdf) {
        Write-Step `
            'Existing database MDF found.'
        Write-Success `
            'Existing database WILL NOT be replaced.'
        Write-Host `
            '  Existing user data WILL be preserved.' `
            -ForegroundColor $ColorInfo
        if (-not (Test-Path -LiteralPath $LiveLdf)) {
            throw (
                "FMSC_DB.mdf exists but FMSC_DB.ldf is missing.`r`n`r`n" +
                "MDF: $LiveMdf`r`n" +
                "LDF: $LiveLdf`r`n`r`n" +
                "Installation stopped to protect the existing database."
            )
        }
    }
    else {
        Write-Step `
            'No existing database found.'
        Write-Step `
            'Installing the initial database template...'
        if (-not (Test-Path -LiteralPath $DatabaseSource)) {
            throw (
                "Database folder was not found: $DatabaseSource`r`n`r`n" +
                "This is a fresh install (no existing FMSC_DB was found at $LiveMdf), " +
                "which needs the database template. If this machine should already " +
                "have FMSC_DB installed, check that path before re-running - this " +
                "release does not include a Database folder, which is fine for an " +
                "app-only update but not for a first-time install."
            )
        }
        if (-not (Test-Path -LiteralPath $TemplateMdf)) {
            throw `
                "Database template MDF was not found: $TemplateMdf"
        }
        if (-not (Test-Path -LiteralPath $TemplateLdf)) {
            throw `
                "Database template LDF was not found: $TemplateLdf"
        }
        Copy-Item `
            -LiteralPath $TemplateMdf `
            -Destination $LiveMdf `
            -Force
        Copy-Item `
            -LiteralPath $TemplateLdf `
            -Destination $LiveLdf `
            -Force
        Write-Success `
            'Initial database files copied successfully.'
    }
    Write-Step `
        'Applying database folder permissions.'
    $CurrentUser =
        [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    & icacls.exe `
        $DatabaseDir `
        /inheritance:r |
        Out-Null
    & icacls.exe `
        $DatabaseDir `
        /grant:r `
        'SYSTEM:(OI)(CI)(F)' `
        'Administrators:(OI)(CI)(F)' `
        "${CurrentUser}:(OI)(CI)(F)" |
        Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw `
            'Failed to configure database folder permissions.'
    }
    Write-Success `
        'Database folder permissions configured.'
    Pause-Long
    Write-Step `
        'Checking FMSC_DB registration...'
    $DatabaseQuery =
        "SET NOCOUNT ON; " +
        "SELECT DB_NAME(database_id) AS DatabaseName, " +
        "type_desc, physical_name " +
        "FROM sys.master_files " +
        "WHERE database_id = DB_ID(N'$DatabaseName');"
    $DatabaseInfo =
        Invoke-SqlMaster `
            -Query $DatabaseQuery
    $DatabaseRegistered = $false
    $MdfRegisteredCorrectly = $false
    $LdfRegisteredCorrectly = $false
    foreach ($Line in $DatabaseInfo) {
        $Text =
            $Line.ToString().Trim()
        if ($Text -match 'FMSC_DB') {
            $DatabaseRegistered = $true
        }
        if ($Text -match [regex]::Escape($LiveMdf)) {
            $MdfRegisteredCorrectly = $true
        }
        if ($Text -match [regex]::Escape($LiveLdf)) {
            $LdfRegisteredCorrectly = $true
        }
    }
    if (-not $DatabaseRegistered) {
        Write-Step `
            'FMSC_DB is not registered.'
        Write-Step `
            'Registering FMSC_DB...'
        $AttachQuery =
            "CREATE DATABASE [$DatabaseName] " +
            "ON " +
            "(FILENAME = N'$LiveMdf'), " +
            "(FILENAME = N'$LiveLdf') " +
            "FOR ATTACH;"
        Invoke-SqlMaster `
            -Query $AttachQuery
        Write-Success `
            'FMSC_DB registered successfully.'
    }
    else {
        Write-Success `
            'FMSC_DB is already registered.'
        if (
            (-not $MdfRegisteredCorrectly) -or
            (-not $LdfRegisteredCorrectly)
        ) {
            throw (
                "FMSC_DB is registered, but it does not point to the " +
                "authoritative database files.`r`n`r`n" +
                "Expected MDF: $LiveMdf`r`n" +
                "Expected LDF: $LiveLdf"
            )
        }
        Write-Success `
            'FMSC_DB registration is correct.'
    }
    Write-Step `
        'Verifying database file locations...'
    $VerifyQuery =
        "SET NOCOUNT ON; " +
        "SELECT DB_NAME(database_id) AS DatabaseName, " +
        "type_desc, physical_name " +
        "FROM sys.master_files " +
        "WHERE database_id = DB_ID(N'$DatabaseName');"
    $VerifyResult =
        Invoke-SqlMaster `
            -Query $VerifyQuery
    $VerifyText =
        $VerifyResult -join "`r`n"
    Write-Log $VerifyText
    if ($VerifyText -notmatch [regex]::Escape($LiveMdf)) {
        throw `
            'FMSC_DB verification failed. MDF path is incorrect.'
    }
    if ($VerifyText -notmatch [regex]::Escape($LiveLdf)) {
        throw `
            'FMSC_DB verification failed. LDF path is incorrect.'
    }
    Write-Success `
        'Database file locations verified.'
    Write-Step `
        'Checking database status...'
    $StateQuery =
        "SET NOCOUNT ON; " +
        "SELECT state_desc " +
        "FROM sys.databases " +
        "WHERE name = N'$DatabaseName';"
    $StateResult =
        Invoke-SqlMaster `
            -Query $StateQuery
    $StateText =
        $StateResult -join "`r`n"
    if ($StateText -notmatch 'ONLINE') {
        throw `
            "FMSC_DB is not ONLINE. Current state: $StateText"
    }
    Write-Success `
        'FMSC_DB is ONLINE.'
    Pause-Long
    Write-Section `
        'STEP 6 OF 8  -  APPLICATION STAGING'
    # The new application is copied and verified here, before the database is touched at all, and is activated only in STEP 7.
    Write-Host `
        '  The new application is copied and verified first.' `
        -ForegroundColor $ColorInfo
    Write-Host `
        '  It is activated only after a verified database backup' `
        -ForegroundColor $ColorInfo
    Write-Host `
        '  exists, and before any migration runs.' `
        -ForegroundColor $ColorInfo
    Write-Host ''
    $InstalledExe =
        Join-Path `
            $AppDir `
            'FMSC_SMS.exe'
    Write-Step `
        'Staging the new application...'
    if (Test-Path -LiteralPath $AppStagingDir) {
        Write-Log `
            "Removing the staging folder left by a previous run: $AppStagingDir"
        Remove-Item `
            -LiteralPath $AppStagingDir `
            -Recurse `
            -Force
    }
    Copy-ApplicationFiles `
        -Source $AppSource `
        -Destination $AppStagingDir
    Write-Step `
        'Verifying the staged application...'
    $StagedApplication =
        Test-StagedApplication `
            -Source $AppSource `
            -Staging $AppStagingDir
    $StagedSizeMb =
        [math]::Round(
            $StagedApplication.Bytes / 1MB,
            2
        )
    Write-Success `
        "Application staged and verified ($($StagedApplication.Files) files, $StagedSizeMb MB)."
    Write-Log `
        "Application staged: $AppStagingDir ($($StagedApplication.Files) files, $StagedSizeMb MB)"
    Pause-Long
    Write-Section `
        'STEP 7 OF 8  -  APPLICATION ACTIVATION AND DATABASE UPDATE'
    $CurrentDatabaseVersion =
        Get-DatabaseVersion
    $CurrentVersionObject =
        Convert-ToDbVersion $CurrentDatabaseVersion
    $RequiredVersionObject =
        Convert-ToDbVersion $DatabaseVersion
    Write-Host ''
    Write-Host `
        '  DATABASE COMPATIBILITY' `
        -ForegroundColor $ColorAccent
    Write-Host ''
    Write-Host `
        "  Current database : $CurrentDatabaseVersion" `
        -ForegroundColor $ColorInfo
    Write-Host `
        "  Required by app  : $DatabaseVersion" `
        -ForegroundColor $ColorInfo
    Write-Host ''
    if ($CurrentVersionObject -gt $RequiredVersionObject) {
        Write-Failure `
            'Database is newer than this release supports.'
        throw (
            "The installed database is newer than this application release.`r`n`r`n" +
            "Installed database: $CurrentDatabaseVersion`r`n" +
            "Required by release: $DatabaseVersion`r`n`r`n" +
            "The installer will NOT downgrade the database."
        )
    }
    if ($CurrentVersionObject -eq $RequiredVersionObject) {
        Write-Success `
            'Database schema is already up to date.'
        Write-Success `
            'No database migration is required.'
        # nothing needs to be migrated, so the staged application can be activated immediately: the schema it expects is already in place.
        Write-Step `
            'Activating the staged application...'
        Enable-StagedApplication `
            -Staging $AppStagingDir `
            -AppDir $AppDir `
            -RollbackDir $AppRollbackDir
        Write-Success `
            'Application activated.'
    }
    else {
        Write-WarningMessage `
            'Database migration is required.'
        Write-Host ''
        Write-Host `
            "  Current database : $CurrentDatabaseVersion" `
            -ForegroundColor $ColorInfo
        Write-Host `
            "  Required database: $DatabaseVersion" `
            -ForegroundColor $ColorInfo
        Write-Host ''
        Write-Step `
            'Checking available migration files...'
        $MigrationFiles =
            Get-ChildItem `
                -Path $MigrationsDir `
                -Filter '*.sql' `
                -File |
            Sort-Object Name
        if (-not $MigrationFiles) {
            throw (
                "Database migration is required, but no SQL migration " +
                "files were found.`r`n`r`n" +
                "Current database: $CurrentDatabaseVersion`r`n" +
                "Required database: $DatabaseVersion`r`n" +
                "Migrations directory: $MigrationsDir"
            )
        }
        $ApplicableMigrations = @()
        foreach ($MigrationFile in $MigrationFiles) {
            $BaseName =
                [System.IO.Path]::GetFileNameWithoutExtension(
                    $MigrationFile.Name
                )
            if ($BaseName -notmatch '^\d+$') {
                Write-Log `
                    "Ignoring invalid migration filename: $($MigrationFile.Name) (expected a plain integer database schema version, e.g. 2.sql)"
                continue
            }
            $MigrationVersion =
                Convert-ToDbVersion $BaseName
            if (
                ($MigrationVersion -gt $CurrentVersionObject) -and
                ($MigrationVersion -le $RequiredVersionObject)
            ) {
                $ApplicableMigrations +=
                    [PSCustomObject]@{
                        File = $MigrationFile
                        Version = $MigrationVersion
                        VersionText = $BaseName
                    }
            }
        }
        if ($ApplicableMigrations.Count -eq 0) {
            throw (
                "No applicable database migration was found.`r`n`r`n" +
                "Current database: $CurrentDatabaseVersion`r`n" +
                "Required database: $DatabaseVersion"
            )
        }
        # The chain must be contiguous BEFORE anything runs. Merge/rebase dropped files and hand-numbered releases both produce exactly that gap.
        $MissingMigrationVersions = @()
        for (
            $ExpectedVersion = $CurrentVersionObject + 1
            $ExpectedVersion -le $RequiredVersionObject
            $ExpectedVersion++
        ) {
            if (
                -not ($ApplicableMigrations |
                    Where-Object { $_.Version -eq $ExpectedVersion })
            ) {
                $MissingMigrationVersions +=
                    "$ExpectedVersion.sql"
            }
        }
        if ($MissingMigrationVersions.Count -gt 0) {
            throw (
                "The database migration chain is incomplete.`r`n`r`n" +
                "Current database: $CurrentDatabaseVersion`r`n" +
                "Required by app : $DatabaseVersion`r`n`r`n" +
                "Missing migration file(s): " +
                ($MissingMigrationVersions -join ', ') + "`r`n`r`n" +
                "Migrations directory: $MigrationsDir`r`n`r`n" +
                "Refusing to migrate: applying only the migrations that happen " +
                "to be present would leave the database partially advanced."
            )
        }
        Write-Success `
            "Migration chain verified: $CurrentDatabaseVersion -> $DatabaseVersion has no gaps."
        Write-Success `
            "$($ApplicableMigrations.Count) database migration(s) available."
        Write-Host ''
        foreach (
            $Migration in
            ($ApplicableMigrations | Sort-Object Version)
        ) {
            Write-Host `
                "    - $($Migration.VersionText)" `
                -ForegroundColor $ColorDim
        }
        Write-Host ''
        Write-Host `
            '  ============================================================' `
            -ForegroundColor $ColorWarning
        Write-Host `
            '                 DATABASE UPDATE REQUIRED' `
            -ForegroundColor $ColorWarning
        Write-Host `
            '  ============================================================' `
            -ForegroundColor $ColorWarning
        Write-Host ''
        Write-Host `
            '  The application requires a newer database schema.' `
            -ForegroundColor $ColorInfo
        Write-Host `
            '  The new application is already staged and verified, but it' `
            -ForegroundColor $ColorInfo
        Write-Host `
            '  has NOT been activated. A verified database backup is taken' `
            -ForegroundColor $ColorInfo
        Write-Host `
            '  first, then migrations run, then the application is activated.' `
            -ForegroundColor $ColorInfo
        Write-Host ''
        Write-Host `
            '  If you choose NO, the application will NOT be updated.' `
            -ForegroundColor $ColorWarning
        Write-Host ''
        $MigrationApproval =
            Read-Host `
                '  Update the database now? (Y/N)'
        if ($MigrationApproval -notmatch '^[Yy]$') {
            Write-Host ''
            Write-Failure `
                'Database migration was declined.'
            Write-Host ''
            Write-Host `
                '  The new application was staged but NOT activated.' `
                -ForegroundColor $ColorWarning
            Write-Host `
                '  The installed application and the database are unchanged.' `
                -ForegroundColor $ColorWarning
            Write-Host ''
            Write-Host `
                '  Installation cancelled.' `
                -ForegroundColor $ColorWarning
            Write-Host ''
            exit 0
        }
        Write-Host ''
        Write-Success `
            'Database migration approved.'
        Pause-Long
        # One recovery point for the whole upgrade, taken after the operator approves the migration and before the first migration runs.
        $RecoveryBackupPath =
            New-PreMigrationBackup `
                -FromVersion $CurrentDatabaseVersion `
                -ToVersion $DatabaseVersion
        # Keep the currently installed application active until the entire
        # migration chain and final schema-version check have succeeded.
        foreach (
            $Migration in
            ($ApplicableMigrations | Sort-Object Version)
        ) {
            $MigrationFile =
                $Migration.File
            $MigrationVersion =
                $Migration.VersionText
            Write-Host ''
            Write-Host `
                '  ------------------------------------------------------------' `
                -ForegroundColor $ColorTitle
            Write-Host `
                "  DATABASE MIGRATION $MigrationVersion" `
                -ForegroundColor $ColorTitle
            Write-Host `
                '  ------------------------------------------------------------' `
                -ForegroundColor $ColorTitle
            Write-Log `
                "Applying migration: $MigrationVersion"
            Write-Log `
                "File: $($MigrationFile.FullName)"
            Write-Step `
                "Applying database migration $MigrationVersion..."
            # run through the migration wrapper, which captures the version before the run, reports a failure with the exact before/after state, and asserts the migration recorded exactly its own version.
            Invoke-SqlMigration `
                -FilePath $MigrationFile.FullName `
                -ExpectedVersion $Migration.Version `
                -RecoveryBackupPath $RecoveryBackupPath
            $CurrentDatabaseVersion =
                Get-DatabaseVersion
            Write-Success `
                "Database schema is now version $CurrentDatabaseVersion."
            Write-Log `
                "Database is now version $CurrentDatabaseVersion"
            Pause-Long
        }
        # compare as numbers. This was a string comparison, so '10' and '9' would compare as unequal even when both are version 10, and a leading zero ('05') would fail against '5'.
        $FinalRecordedVersionObject =
            Convert-ToDbVersion $CurrentDatabaseVersion
        if ($FinalRecordedVersionObject -ne $RequiredVersionObject) {
            throw (
                "Database migration did not reach the required version.`r`n`r`n" +
                "Current: $CurrentDatabaseVersion`r`n" +
                "Required: $DatabaseVersion"
            )
        }
        Write-Success `
            'All required database migrations completed successfully.'
        Write-Step `
            'Activating the staged application...'
        Enable-StagedApplication `
            -Staging $AppStagingDir `
            -AppDir $AppDir `
            -RollbackDir $AppRollbackDir
        Write-Success `
            'Application activated.'
        Write-Log `
            "Previous application kept for rollback: $AppRollbackDir"
        Pause-Long
    }
    # Reject older data missing required features; update scripts are no longer bundled.
    $NeedsLatestUpdates = Invoke-SqlMaster -Query "SET NOCOUNT ON; SELECT CASE WHEN COL_LENGTH('FMSC_DB.dbo.Students','AdmissionNumber') IS NULL OR OBJECT_ID('FMSC_DB.dbo.ReorderSchoolOfficials','P') IS NULL OR OBJECT_ID('FMSC_DB.dbo.SaveStudentDetailsWithFirstEnrollment','P') IS NULL OR OBJECT_ID('FMSC_DB.dbo.AdmitHistoricalStudent','P') IS NULL THEN 1 ELSE 0 END;"
    if (($NeedsLatestUpdates -join "`n") -match '(?m)^\s*1\s*$') {
        throw 'This database needs school-data updates that are not included in this application copy. Restore a backup made after the latest updates, or contact support to upgrade the older backup.'
    }
    Write-Step `
        'Performing final database compatibility check...'
    $FinalDatabaseVersion =
        Get-DatabaseVersion
    $FinalVersionObject =
        Convert-ToDbVersion $FinalDatabaseVersion
    if ($FinalVersionObject -lt $RequiredVersionObject) {
        throw (
            "Database is still below the required version.`r`n`r`n" +
            "Current: $FinalDatabaseVersion`r`n" +
            "Required: $DatabaseVersion"
        )
    }
    if ($FinalVersionObject -gt $RequiredVersionObject) {
        throw (
            "Database is newer than the version supported by this release.`r`n`r`n" +
            "Current: $FinalDatabaseVersion`r`n" +
            "Required: $DatabaseVersion"
        )
    }
    Write-Success `
        "Final database schema version: $FinalDatabaseVersion"
    Write-Host ''
    Write-Host `
        '  [OK] DATABASE IS COMPATIBLE WITH THIS APPLICATION' `
        -ForegroundColor $ColorSuccess
    Pause-Long
    Write-Host `
        '  Database compatibility confirmed.' `
        -ForegroundColor $ColorSuccess
    Write-Host `
        '  The application has been activated.' `
        -ForegroundColor $ColorInfo
    Write-Host ''
    # The previous application is kept, not deleted.
    if (Test-Path -LiteralPath $AppRollbackDir) {
        Write-Host `
            '  The previous application version is kept at:' `
            -ForegroundColor $ColorDim
        Write-Host `
            "    $AppRollbackDir" `
            -ForegroundColor $ColorDim
        Write-Host `
            '  It can be deleted once FMSC SMS has been used successfully.' `
            -ForegroundColor $ColorDim
        Write-Host ''
        Write-Log `
            "Previous application retained for rollback: $AppRollbackDir"
    }
    Pause-Long
    Write-Step `
        'Creating desktop shortcut...'
    $DesktopPath =
        [Environment]::GetFolderPath('Desktop')
    $ShortcutPath =
        Join-Path `
            $DesktopPath `
            'FMSC SMS.lnk'
    try {
        $WshShell =
            New-Object -ComObject WScript.Shell
        $Shortcut =
            $WshShell.CreateShortcut(
                $ShortcutPath
            )
        $Shortcut.TargetPath =
            $InstalledExe
        $Shortcut.WorkingDirectory =
            $AppDir
        $Shortcut.Description =
            $AppName
        $Shortcut.IconLocation =
            "$InstalledExe,0"
        $Shortcut.Save()
        Write-Success `
            "Desktop shortcut created: $ShortcutPath"
    }
    catch {
        Write-WarningMessage `
            "Could not create desktop shortcut: $($_.Exception.Message)"
    }
    Pause-Long
    Write-Section `
        'STEP 8 OF 8  -  INSTALLATION COMPLETE'
    Write-Host ''
    Write-Host `
        '  ============================================================' `
        -ForegroundColor $ColorSuccess
    Write-Host `
        '                FMSC SMS UPDATE COMPLETED' `
        -ForegroundColor $ColorSuccess
    Write-Host `
        '  ============================================================' `
        -ForegroundColor $ColorSuccess
    Write-Host ''
    Write-Host `
        "  Application version : $AppVersion" `
        -ForegroundColor $ColorInfo
    Write-Host `
        "  Database version    : $FinalDatabaseVersion" `
        -ForegroundColor $ColorInfo
    Write-Host `
        "  Application folder  : $AppDir" `
        -ForegroundColor $ColorInfo
    Write-Host `
        "  Database folder     : $DatabaseDir" `
        -ForegroundColor $ColorInfo
    Write-Host `
        "  SQL Server          : $SqlInstance" `
        -ForegroundColor $ColorInfo
    Write-Host `
        "  Database            : $DatabaseName" `
        -ForegroundColor $ColorInfo
    Write-Host `
        "  Desktop shortcut    : $ShortcutPath" `
        -ForegroundColor $ColorInfo
    # The recovery point is reported on success too, not only in the failure message. This is the file to restore if the new version has to be taken back off.
    if ($RecoveryBackupPath) {
        Write-Host `
            '  Pre-migration backup:' `
            -ForegroundColor $ColorInfo
        Write-Host `
            "    $RecoveryBackupPath" `
            -ForegroundColor $ColorDim
    }
    Write-Host ''
    Write-Log `
        '================================================'
    Write-Log `
        'FMSC SMS installation completed successfully.'
    Write-Log `
        '================================================'
    Pause-Long
    Write-Host ''
    Write-Host `
        '  ------------------------------------------------------------' `
        -ForegroundColor $ColorDim
    $StartApp =
        Read-Host `
            '  Start FMSC SMS now? (Y/N)'
    Write-Host ''
    if ($StartApp -match '^[Yy]$') {
        Write-Host `
            '  Starting FMSC SMS...' `
            -ForegroundColor $ColorSuccess
        Start-Process `
            -FilePath $InstalledExe
        Start-Sleep -Milliseconds 500
    }
    else {
        Write-Host `
            '  Installation finished. FMSC SMS was not started.' `
            -ForegroundColor $ColorInfo
    }
    Write-Host ''
    Write-Host `
        '  Thank you for using FMSC School Management System.' `
        -ForegroundColor $ColorTitle
    Write-Host ''
}
catch {
    try {
        Write-Log `
            "ERROR: $($_.Exception.Message)"
    }
    catch {
    }
    Write-Host ''
    Write-Host `
        '  ============================================================' `
        -ForegroundColor $ColorError
    Write-Host `
        '                    INSTALLATION FAILED' `
        -ForegroundColor $ColorError
    Write-Host `
        '  ============================================================' `
        -ForegroundColor $ColorError
    Write-Host ''
    Write-Host `
        "  $($_.Exception.Message)" `
        -ForegroundColor $ColorError
    Write-Host ''
    # A failure is exactly when these two locations matter.
    if (Test-Path -LiteralPath $AppRollbackDir) {
        Write-Host `
            '  Previous application kept at:' `
            -ForegroundColor $ColorWarning
        Write-Host `
            "  $AppRollbackDir" `
            -ForegroundColor $ColorInfo
        Write-Host ''
    }
    if ($RecoveryBackupPath) {
        Write-Host `
            '  Verified pre-migration database backup:' `
            -ForegroundColor $ColorWarning
        Write-Host `
            "  $RecoveryBackupPath" `
            -ForegroundColor $ColorInfo
        Write-Host ''
    }
    Write-Host `
        '  Installation log:' `
        -ForegroundColor $ColorWarning
    Write-Host `
        "  $LogFile" `
        -ForegroundColor $ColorInfo
    Write-Host ''
    Read-Host `
        '  Press ENTER to close'
    exit 1
}
