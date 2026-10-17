# Remaining Level 3 local acceptance

Cloud evidence does not complete these **17 composite scenarios**. This checklist is preparation, not a request to run them before cloud testing finishes. Use only fictional data. Historical Level 2 results are useful context and are not fresh Level 3 outcomes.

## Safe interactive entry point

Windows 10/11 x64, .NET 8 Desktop Runtime, .NET SDK 8+, Python 3.11+, SQL Server LocalDB, sqlcmd, and an unlocked interactive session are required. Start in a clean checkout of the reviewed test revision. Use the existing guarded harness, never the normal installed school application:

```powershell
git status --short
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Run-Level2.ps1 -PreflightOnly
# A preflight-only report deliberately does not claim UI scenarios passed.
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Run-Level2.ps1
```

The harness creates `FMSC_Level2_<GUID>` instance/catalog, private credentials, a test-only application and fictional reference data. It refuses operational targets. It cleans the owned instance/private files in `finally`. Keep only the sanitized evidence under `artifacts/level2/evidence/`. Do not upload private directories. No administrator rights are normally needed for this existing LocalDB harness; installing prerequisites may require them.

The full harness automates major interactions. Further manual actions below must occur **only while using that isolated test application/instance**, or in a disposable VM containing no school installation. Do not weaken guards or open the normal `C:\FMSC School Management` installation for tests. If a manual action cannot be safely performed before automatic cleanup, extend the harness's scenario code and rerun; do not disable cleanup or substitute an operational connection.

For each item retain the exact commit, environment, scenario ID, expected/actual, sanitized screenshot and independent SQL persistence assertion. Existing `Tests/Level2UiChecks/Identity.cs` / database helpers enforce the target before writes. Keep screenshots free of passwords. Finish by confirming no owned test process, instance or private file remains. One run may complete several rows; record each separately.

| ID | Objective and exact operator actions | Why cloud is insufficient | Required environment / rights | Expected and evidence | Cleanup / safety |
|---|---|---|---|---|---|
| L3-REC-06 | In isolated admission/collection, change a meaningful field, choose Cancel/Discard, reopen; cancel a pending supported read. | SQL cancellation is tested; a real operator's discard path was not freshly executed. | Interactive Windows; normal user. | Cancelled personal/financial changes absent in independent SQL; screenshot before/after. | Existing harness ownership and cleanup; never cancel an uncertain committed write by resubmitting it. |
| L3-DB-05 | On a disposable VM, make copies of a synthetic detached pair. Test missing log, exclusive file lock, deliberately corrupted copy and wrong owner marker, one at a time. Restore the VM snapshot between cases. | Linux SQL container does not exercise Windows MDF/LDF attach/file locks. | Isolated Windows VM, LocalDB, admin recommended for installer. | Clear refusal; original synthetic pair and existing compatible records unchanged; no unsafe attach/replacement. Capture hashes, error and process exit. | Never edit the original fixture pair; destroy VM/checkpoints after evidence. Use generated synthetic fixtures only. |
| L3-INS-04 | On clean VM without LocalDB, install signed prerequisite; repeat with a missing MSI and an inert broken copy. Test cancelled wizard and reboot-required outcome when reproducible. | Hosted images may have LocalDB already; unit tests do not prove MSI/wizard behavior. | Disposable Windows VM, snapshot, admin. | No application activation after failed prerequisite; clear diagnostic/retry path; record MSI exit code and installed state. | No school software/data in VM; restore snapshot, do not uninstall working LocalDB on school PC. |
| L3-SEC-03 | Log into test application with restricted account. Try sidebar, command palette, shortcuts and direct destination navigation. | Noninteractive control contracts do not prove full user navigation. | Interactive Windows; harness fictional restricted account. | Unauthorized destinations/actions refused consistently; SQL unchanged. | Harness cleanup; never grant extra permissions to make actions available. |
| L3-SEC-04 | Start authorized test session; revoke its test role through supported administration from a second test session, then retry operation and sign out/in. | SQL role recheck passes; interactive stale session behavior still needs observation. | Two isolated test sessions; test Administrator. | Current permissions enforced at write boundary; stale UI cannot authorize write. | Restore only fixture roles; dispose both sessions before harness cleanup. |
| L3-SEC-05 | Trigger fictional validation/connection errors; review sanitized test logs and exported statements/receipts, authentication and provider settings screens. | Scanner/allowlists cannot prove every operator-visible error/export is safe. | Interactive Windows; all fictional identities, no live provider credentials. | No credential leakage; expected student context only; capture redacted evidence. | Delete synthetic exports and private logs locally; upload only reviewed evidence. |
| L3-UX-01 | Set each supported monitor resolution 1280×720, 1366×768, 1920×1080 and 2560×1440 where supported. At 100/125/150/200%, restart test app and open admission/date fields, collection, promotion/history, dialogs, grids, tabs and ReportViewer; move between different-DPI monitors. | Headless geometry tests and window resizing do not change physical Windows display/DPI. | Real displays or appropriate Windows VM display adapter; sign-out may be needed; settings permissions. | Essential controls visible/scrollable, readable text, correct dates/focus, no double scaling; screenshot each actual configuration. | Restore original resolution/scaling; record unsupported combinations as blocked rather than passed. |
| L3-UX-02 | Complete login, admission and collection with Tab/Shift+Tab/Enter/Escape; inspect focus on validation, dialogs and grids. | Visible keyboard/focus acceptance needs interactive desktop. | Interactive Windows, normal user. | Logical order, visible focus, safe default/escape decisions; screenshots and harness assertions. | Existing fictional data and cleanup. |
| L3-UX-03 | Change campus→year→class→section; choose an empty campus and incompatible source/destination; change parent selections repeatedly. | Component calculations do not establish full live combo interaction. | Interactive Windows, two fictional campuses and class/section references. | Child options reset/filter correctly; no stale wrong placement persisted. | Harness reference fixtures only. |
| L3-UX-04 | Edit admission/profile/financial draft, navigate/close; choose Stay, then Discard. Test blank dates/small setup drafts according to accepted policy. | Native contracts supplement actual operator prompts and navigation. | Interactive Windows; normal user. | Meaningful work guarded, deliberate small/blank drafts freely abandonable, no unwanted writes. | Reopen and verify SQL, then harness cleanup. |
| L3-UX-05 | Find existing/nonexistent student; alternate typed admission number and campus/year/class/status filters, reset, refresh, change row, open and return. | Headless search contracts do not establish visible grid and selection feedback. | Interactive Windows; fictional siblings, empty campus. | Mutually reset search modes, accurate counts/empty guidance, safe selection/context. | No real admission numbers or records; harness cleanup. |
| L3-PRF-02 | Cycle admission/profile/collection/statement/promotion/history 100 times in test app; save no unintended data. | Cloud measures 240 real dialog cycles, not a complete DB-backed screen loop. | Interactive Windows, Task Manager/Process Explorer; normal user. | No stale context, exceptions or continuously retained handles; record opening duration samples and before/after process resources. | Close all windows/processes; remove owned fixture via harness. |
| L3-PRF-03 | Repeat representative searches, statements, navigation and reports for 60 minutes, sampling memory/handles/CPU every 5 minutes at comparable idle points. | Six bounded component batches do not prove a sustained school workday. | Interactive Windows; fictional DB, fixed display/theme, performance tools. | No unbounded trend or freeze; record hardware, workload, durations and numeric samples, not a subjective 'fast'. | End test application; verify no retained process/fixture; do not infer production-scale SLA. |
| L3-RPT-01 | Collect fictional 4000 partial then 8000 balance; open each receipt, collection report and reprint. | Cloud verifies production SQL totals/IDs; visible RDLC receipt render is not proven. | Interactive Windows with ReportViewer dependencies. | Receipt numbers/amounts/enrollment and totals match independently queried test SQL, including repeat print. | Delete synthetic exports; harness cleans DB. |
| L3-RPT-02 | Open two-year statement and supported fictional student/staff certificates with long names/addresses. | SQL statements/certificate recovery do not prove final report rendering. | Interactive Windows, fictional lifecycle/certificate fixtures. | Original-year/current-year balances match SQL; correct identities, readable layout and dates. | Keep only sanitized screenshots; remove exports/fixture. |
| L3-RPT-03 | Export long-text multi-page actual SQL-backed reports to PDF/CSV, reopen and compare row counts, grand totals, quoting, page breaks and final rows. Test locked destination without losing old export. | Cloud parses synthetic multi-page PDF/CSV and checks file durability, but cannot inspect rendered SQL-backed pages. | Interactive Windows, PDF reader, spreadsheet/text reader; normal user. | Exact SQL totals and records; no truncation, formula injection or damaged replacement; capture representative pages. | Export to owned temp folder, delete afterwards; never use school paths. |
| L3-RPT-04 | Open print preview, print to Microsoft Print to PDF and a physical printer; verify margins, pages, totals, cancel and printer unavailable. | No confirmed interactive printer/driver or physical output in cloud. | Interactive Windows, configured virtual driver and physical printer; driver setup may need admin. | Correct print selection/content, safe cancellation/error, physical readability; photograph only fictional output. | Delete virtual files, clear fictional print jobs, shred fictional sheets, restore printer defaults. |

## Disposable installation/file-test VM preparation

Use Windows 10/11 or Windows Server 2022 x64 VM with a clean snapshot and no school data/installed FMSC. Generate only synthetic schema-6 data from `Tests/LifecycleSqlChecks/Baseline/schema.dacpac`; set reference rows/type using the existing fixture setup, then independently validate the runtime contract. Do not use `fmsc.dacpac` exports as a data source. The existing GitHub Windows acceptance script documents the exact synthetic provisioning/attachment sequence, but intentionally refuses arbitrary local execution. Do not set fake GitHub environment variables to bypass that guard. A future local VM adapter must retain equivalent ownership and pristine-machine checks; until then these VM-only destructive file/MSI cases stay LOCAL REQUIRED.

A real power cut, disk failure and reboot-interrupted installation are not proven by the injected cloud activation exception. If later tested, use snapshots on this disposable VM only, inspect old application availability and database integrity before retry, and retain a verified synthetic backup. Never perform those experiments on the operator PC or a school database.

## Windows checks deferred because the GitHub limit is exhausted

The owner confirmed the Actions limit is exhausted. Four additional scenarios remain **BLOCKED**, rather than being added to the 17 LOCAL REQUIRED count: **L3-INS-01, L3-INS-02, L3-INS-03, L3-INS-05**. They were implemented for a pristine GitHub-hosted Windows machine but never executed there. No automatic retry is needed until capacity is available. The Windows Desktop CI, component checks and printing-related checks also await actual Windows execution.

On a **disposable Windows profile/VM**, these SQL-free regression commands reproduce the noninteractive Windows layer (normal user; no real school application/database):

```powershell
dotnet restore FMSC_SMS.sln
dotnet build FMSC_SMS.sln -c Release --no-restore
dotnet build Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -c Release
dotnet run --project Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -c Release --no-build -- --post-fix
dotnet run --project Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -c Release --no-build -- --level3
dotnet run --project Tests/AuditFixChecks/AuditFixChecks.csproj -c Release
dotnet run --project Tests/PerformanceChecks/PerformanceChecks.csproj -c Release
dotnet run --project Tests/CommunicationChecks/CommunicationChecks.csproj -c Release
powershell -NoProfile -File Tools/TestInstallerMigrations.ps1
pwsh -NoProfile -File Tools/TestInstallerLocalDb.ps1
pwsh -NoProfile -File Tests/Level3Checks/TestActivation.ps1
python -m unittest discover -s Tests/Level3Checks -p 'test_*.py' -v
python Tools/test_security.py --gitleaks C:\Tools\gitleaks.exe
python Tools/check_security.py --gitleaks C:\Tools\gitleaks.exe
```

Use the official checksum-pinned Gitleaks 8.30.1 CLI documented in DEVELOPMENT_WORKFLOW.md. Do not set `FMSC_LEVEL3_WINDOWS_EVIDENCE` on a normal PC: automated component evidence export intentionally accepts only the designated runner temporary root. Record sanitized console totals manually and keep native/visual results separate.

| Blocked ID | VM/operator continuation | Expected | Evidence and cleanup |
|---|---|---|---|
| L3-INS-01 | In a fresh, snapshot-backed VM with no school installation, publish the reviewed PortableRelease with Microsoft-signed LocalDB and pinned sqlcmd; add a generated **synthetic** detached schema-6 pair, then run `Install FMSC_SMS.bat` as administrator. Use the safe synthetic preparation sequence below. | Installer activates correct app, attaches only the synthetic pair, and installed schema verification succeeds. | Record source/hash, MSI state, application verification exit and SQL owner marker. Destroy/revert VM after test; no database uploads. |
| L3-INS-02 | Preserve a synthetic school/setup row and theme file. Record database GUID plus hashes/counts and theme bytes; remove packaged database templates; run the same installer as an app-only update. | Existing database GUID, records and settings unchanged; app updated; no database template required. | Before/after SQL checks from separate connections, theme/app hashes; revert VM snapshot. |
| L3-INS-03 | Repeat installer against the same synthetic compatible installation and settings. | Reinstallation preserves all previous records/identity/settings. | Before/after SQL and settings checks; delete no operational path outside VM. |
| L3-INS-05 | Run `pwsh -NoProfile -File Tests/Level3Checks/TestActivation.ps1` inside the VM, then examine the real installer update/rollback folder behavior using only synthetic VM assets. Any process-kill/reboot extension requires a snapshot and verified synthetic backup first. | Injected partial move fails, exact old executable bytes restored, partial output removed, SQL unchanged. Physical interruption must be recorded separately. | Script output/cleanup, old/new hashes, independent SQL snapshot. No real power-loss claim from the injected exception. |

Do not run `RunDisposableWindowsInstall.ps1` directly on the PC or bypass its hosted-runner guards. It is not a local VM adapter. The manual VM installation steps above are separate acceptance, requiring an explicitly prepared disposable machine and administrator rights.

### Synthetic detached-pair preparation for the disposable installation VM

Run **only inside the empty snapshot-backed VM**, after installing .NET 8 SDK, LocalDB 2022 and sqlcmd and checking out the reviewed revision. This produces synthetic files; it does not install or upgrade any school database. Keep the PowerShell session open so its generated ownership values remain available. Stop on any error.

```powershell
$ErrorActionPreference = 'Stop'
$vm = Get-CimInstance Win32_ComputerSystem
if ($vm.Model -notmatch 'Virtual Machine|VMware|VirtualBox|KVM|HVM|QEMU') {
    throw 'Use an identifiable disposable VM; this is not an operator-PC preparation script.'
}
if ((Test-Path 'C:\FMSC School Management') -or (Test-Path 'C:\ProgramData\FMSC_Database')) {
    throw 'Existing FMSC assets found: revert to a clean VM snapshot.'
}
$repo = (Get-Location).Path
$owner = [guid]::NewGuid()
$instance = 'FMSC_AuditChecks_L3VM_' + $owner.ToString('N')
$catalog = $instance
$root = Join-Path $env:TEMP ('FMSC_Level3Vm_' + $owner.ToString('N'))
if (Test-Path -LiteralPath $root) { throw 'Test directory already exists.' }
New-Item -ItemType Directory -Path $root | Out-Null
$owner.ToString() | Set-Content (Join-Path $root 'owner.txt')
& sqllocaldb create $instance 16.0
if ($LASTEXITCODE) { throw 'Unique LocalDB creation failed; do not substitute MSSQLLocalDB.' }
& sqllocaldb start $instance
if ($LASTEXITCODE) { throw 'Unique LocalDB start failed.' }
$server = '(localdb)\' + $instance
$conn = "Server=$server;Database=$catalog;Integrated Security=True;Encrypt=False;TrustServerCertificate=True"
Push-Location (Join-Path $repo 'Tests/LifecycleSqlChecks')
try {
    & dotnet tool restore
    if ($LASTEXITCODE) { throw 'Pinned SqlPackage restore failed.' }
    & dotnet tool run sqlpackage -- '/Action:Publish' "/SourceFile:$repo/Tests/LifecycleSqlChecks/Baseline/schema.dacpac" "/TargetConnectionString:$conn" '/p:CreateNewDatabase=True'
    if ($LASTEXITCODE) { throw 'Synthetic schema publication failed.' }
} finally { Pop-Location }
$seed = @"
SET NOCOUNT ON;
IF EXISTS(SELECT 1 FROM dbo.Students) OR EXISTS(SELECT 1 FROM dbo.Users)
 OR EXISTS(SELECT 1 FROM dbo.DatabaseVersion) THROW 51000,'Nonempty synthetic target refused',1;
INSERT dbo.DatabaseVersion(VersionNumber) VALUES(N'6');
INSERT dbo.AdmissionNumberCounter(CounterId,LastNumber) VALUES(1,1000);
DECLARE @Owner uniqueidentifier='$owner', @Scope uniqueidentifier=NEWID();
EXEC sys.sp_addextendedproperty @name=N'FMSC_Level3VmOwner',@value=@Owner;
EXEC sys.sp_updateextendedproperty @name=N'FMSC_QuickRegistrationScope',@value=@Scope;
"@
& sqlcmd -S $server -E -C -d $catalog -b -Q $seed
if ($LASTEXITCODE) { throw 'Synthetic reference/owner setup failed.' }
$guard = "SET NOCOUNT ON; IF DB_NAME()<>N'$catalog' OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_Level3VmOwner' AND TRY_CONVERT(uniqueidentifier,value)=CONVERT(uniqueidentifier,'$owner')) THROW 51001,'Wrong test identity',1;"
& sqlcmd -S $server -E -C -d $catalog -b -Q $guard
if ($LASTEXITCODE) { throw 'Ownership verification failed; do not detach/drop anything.' }
$mdf = ((& sqlcmd -S $server -E -C -d $catalog -b -h -1 -W -Q 'SET NOCOUNT ON; SELECT physical_name FROM sys.database_files WHERE type=0;') -join '').Trim()
if ($LASTEXITCODE) { throw 'Synthetic data file lookup failed.' }
$ldf = ((& sqlcmd -S $server -E -C -d $catalog -b -h -1 -W -Q 'SET NOCOUNT ON; SELECT physical_name FROM sys.database_files WHERE type=1;') -join '').Trim()
if ($LASTEXITCODE) { throw 'Synthetic log file lookup failed.' }
foreach ($file in @($mdf,$ldf)) {
    if (-not [IO.Path]::GetFileName($file).StartsWith($catalog,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unexpected fixture file identity; detach/copy refused.'
    }
}
# Detach only this freshly created, owner-verified catalog; no real database name is used.
& sqlcmd -S $server -E -C -d $catalog -b -Q ($guard + " USE master; EXEC master.sys.sp_detach_db @dbname=N'$catalog';")
if ($LASTEXITCODE) { throw 'Synthetic detach failed.' }
$pair = Join-Path $root 'Database'
New-Item -ItemType Directory -Path $pair | Out-Null
Copy-Item -LiteralPath $mdf -Destination (Join-Path $pair 'FMSC_DB.mdf')
Copy-Item -LiteralPath $ldf -Destination (Join-Path $pair 'FMSC_DB_log.ldf')
Get-FileHash (Join-Path $pair '*') -Algorithm SHA256
```

Copy **only that generated pair** into the reviewed full installer's `Database` folder **inside this VM**. Prepare/publish non-database assets using the existing complete-installer workflow/INSTALLATION instructions: genuine Microsoft-signed LocalDB MSI, pinned sqlcmd and reviewed application, never inert prerequisites from unit tests. This preparation sequence is documented for future local use and **has not been executed on Windows in this Level 3 run**. If DacFx produces different file names, stop and review the owned metadata rather than removing the file-identity check.

After fresh installation, independently query the new VM-only `(localdb)\MSSQLLocalDB`, `FMSC_DB` and verify the preserved `FMSC_Level3VmOwner` equals this session's `$owner`, schema 6 and expected physical paths before any further mutation. Use the fictional role/user/profile setup from `Tools/RunDisposableWindowsInstall.ps1` for update preservation assertions; do not copy school records. Record database GUID, row hashes/counts and theme bytes before/after each update/reinstall. Inspect installation logs and installed app `--verify-installation` exit (base-compatible/module-absent code 10 is expected for this data-free fixture). Never manually raise a version on an existing database.

On completion, close all test apps, stop/delete **only the recorded unique test instance**, and revert/destroy the entire disposable VM snapshot to erase both source and installed synthetic files. Do not transfer the MDF/LDF, logs containing private setup, or any backup off the VM. If ownership cannot be re-established, stop individual cleanup and destroy the disposable VM rather than guessing at database/file names.
