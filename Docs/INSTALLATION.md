# Installation and Upgrade Guide — Windows x64

This describes the **existing FMSC_SMS installer**; it does not replace it. Use a tested Windows machine and approved, verified data. For backup/restore see [BACKUP_RECOVERY.md](BACKUP_RECOVERY.md).

## What is delivered

The [Complete Windows Installer ZIP](../.github/workflows/fmsc-complete-installer.yml) is designed to include:

```text
FMSC_SMS/                 Published self-contained app and resources
Database/                 You provide FMSC_DB.mdf and FMSC_DB_log.ldf for a fresh install
Tools/                    Signed Microsoft SqlLocalDB.msi and verified sqlcmd.exe
Migrations/               Empty at first-production schema 1; future numbered upgrades begin at 2.sql
FMSC.Setup.exe             Native Windows GUI entry point for new installs
Install FMSC_SMS.bat        Compatibility shortcut to FMSC.Setup.exe
core.ps1                   Existing protected install/update engine
READ_ME_FIRST.txt         Packaging instructions
COMMUNICATION_CENTER.md  Optional add-on review, consent and deployment instructions
SHA256SUMS.txt            Package content hashes
```

**Important:** The GitHub build deliberately excludes school MDF/LDF files. The source repository alone is **not** a ready-to-install fresh database. A complete-installer workflow must first run successfully and upload its artifact; do not assume an installer exists from a passing ordinary CI build.

After fixes are merged, start a **new** Complete Windows Installer ZIP run with **Run workflow → main**. Rerunning an older run rebuilds its original source commit. Check the source commit in the run summary and extracted `READ_ME_FIRST.txt`; do not mix an older `core.ps1` with newer application files. Packaging verifies the copied installer against that checked-out revision and checks the migration folder inventory.

Historical installer guides and dated tests may describe development schemas 19–21 and migrations 20/21. Those are **pre-baseline history**, not install instructions for schema 1. Do not ship or replay those numbered scripts against the current production baseline.

The installer no longer offers the already-applied Communication Center setup. Its runtime definition remains embedded for verification; fresh installs that need the module must use a snapshot containing it.

The installer checks `Migrations` for top-level `*.sql` files. An empty or missing folder skips migration execution. Filenames must be positive integer versions (for example `8.sql`); the installer reads `dbo.DatabaseVersion`, ignores files at or below that version, and applies pending files in numeric order. It creates one checksum-verified recovery backup before the first pending file. Each file must use a balanced transaction, preserve the installer's outer transaction, and record its filename version in `dbo.DatabaseVersion`. A SQL failure or incorrect recorded version stops installation before app activation. Pending versions must match the schema supported by the application release; update the application's and installer's required version together when shipping a future schema change.

## Before a fresh install

1. Choose the intended **Windows operator account** and confirm it can access the database after elevated installation. LocalDB instances are per-Windows-user; installation as a different administrator is a known cross-account risk.
2. Download the database-free installer artifact from the [Actions page](https://github.com/farhan2166-creator/FMSC-Source/actions). Verify the exact source commit, successful job, artifact integrity and contents.
3. Extract the ZIP **completely** into a normal writable folder. Do not run scripts inside the compressed archive.
4. Obtain the owner-approved, compatible **detached** database pair. Both `FMSC_DB.mdf` and `FMSC_DB_log.ldf` must come from the **same** verified snapshot. Never copy an attached/live database or replace a production database with a DACPAC.
5. Put the two files inside the extracted top-level `Database/` folder (beside `core.ps1`); check exact filenames. Neither file belongs in GitHub, email, or CI.
6. Confirm the Windows machine has enough storage, administrator access for the installer, and school-approved antivirus/security policies. Back up any existing installation separately before an upgrade.

## Run the installer

1. Close FMSC_SMS and other sessions connected to its LocalDB instance.
2. Double-click **FMSC.Setup.exe**. Approve the Windows administrator/UAC prompt. The existing **Install FMSC_SMS.bat** is a compatibility shortcut to the same GUI launcher.
3. The FMSC setup dialog shows all eight installation stages, current verification activity, and completion or failure. The graphical executable runs the existing PowerShell installation engine with its console hidden. It checks app/process state, LocalDB, database-file presence, staging, version/migration compatibility, and preserves existing school data.
4. **Never ignore a blocked or failed validation.** Record the exact message and installer logs and stop before retrying or adjusting data files.
5. After successful installation, launch the application under the intended operator account; verify connection, login, schema compatibility, a read-only student search, a report preview, and permitted user actions.

The app expects `FMSC_DB` under `C:\ProgramData\FMSC_Database` and uses `(localdb)\MSSQLLocalDB` by default. Current required **database schema version: 1** (first-production baseline, 5 November 2026); application version is **1.0.0**. See [first-release baseline](FIRST_RELEASE_BASELINE.md). Future app-only releases can change the application version without changing the database schema.

If silent LocalDB installation fails (for example, MSI code 1603), the setup dialog offers a Yes/No choice to open Microsoft's official installer wizard and reports a verbose log under `C:\ProgramData\FMSC_Database` . A failed/cancelled wizard or restart-required result stops installation safely. Code 1603 alone does not identify the underlying MSI problem; retain the verbose log for diagnosis. An immediate `SQL Server LocalDB installation failed. MSI exit code: 1603` message without this fallback belongs to the older installer: verify the artifact's source commit before retrying.

## Updating an existing installation

The in-app updater launches the verified **UpdateSupport/FMSC.Setup.exe** from its prepared release and displays the same branded eight-stage installation progress window after it closes FMSC safely. A completed update restarts the application through the desktop shell; a failed update shows an actionable error without hiding its rollback or log information. The GUI executable has separate fresh-install and update entry modes, but both use `core.ps1` for validation, SQL safeguards and recovery. The installer still supports a console-compatible non-GUI mode for unattended, explicitly isolated test execution.


- Take and **verify** a recoverable backup first and note its storage path.
- Do **not** copy a replacement MDF/LDF over an installed school's existing pair.
- Run the same verified installer with the app closed. The first-production release requires **schema 1**, supplied in the approved fully updated detached database. There are **no production numbered migration files** at this baseline; future schema changes start with `2.sql` and require a continuous, verified and backed-up upgrade path. Legacy pre-release schemas carrying higher version labels are **not** silently downgraded to 1; use the separately approved fresh-baseline installation procedure in [first-release baseline](FIRST_RELEASE_BASELINE.md).
- Record the automatic pre-migration safety backup, installer status, original/current version, and post-upgrade application compatibility. Recovery requires a tested restore path.
- If anything fails, **do not manually rerun SQL migrations on the live school database**. Follow [recovery](BACKUP_RECOVERY.md), and use a verified disposable clone for diagnosis.

## Disposable Windows installation acceptance in GitHub Actions

Run **FMSC Disposable Windows Installation Acceptance** from **Actions → Run workflow** on the reviewed branch/commit. This manual workflow uses a fresh GitHub-hosted `windows-2022` runner, checks the approved data-free schema fixture, verifies Microsoft's signed LocalDB MSI and pinned sqlcmd, publishes the real full installer, creates a disposable LocalDB schema-only test database containing **no real school records**, detaches it, then runs the actual `Installer/core.ps1` against that synthetic database. The workflow verifies installation completion, on-disk application and database files, and the installed application's `--verify-installation` exit code.

The Release Readiness workflow also calls this test after security, desktop and SQL checks pass on a release branch. It requires a GitHub-hosted Windows runner, refuses existing FMSC assets or catalogs, and removes only its owned synthetic database, installed files and temporary package after success or failure. It uses no repository secrets or write credentials. Its result artifact contains only a sanitized JSON report and Markdown summary, including cleanup status. The workflow does **not** export the synthetic database, the raw installer logs, or secrets. It must fail or report blocked when prerequisites, elevation, validation, or the real installer cannot run. If the GitHub Windows runner already has LocalDB, the report explicitly says the initial clean MSI installation was **not tested**. This is not a guaranteed pristine Windows VM or an interactive MSI wizard/printer/UI test, and it does not replace manual clean-PC acceptance using an owner-approved detached database pair. Self-hosted execution is refused.

## What must be tested before broad deployment

Installation under the intended Windows user; existing-data upgrade and rollback on a restored clone; backup verification; reports and printer; permissions; actual SQL disconnect/reconnect; and representative money transactions. Refer to [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) and [master audit](../Audit/FMSC_MASTER_AUDIT.md).

## Database-free builds

The app-only ZIP is **not** a fresh-install package. The full-installer workflow includes prerequisites but will not contain student data until you supply the detached pair locally. Never claim release-ready without a confirmed successful installer artifact and manual acceptance.
