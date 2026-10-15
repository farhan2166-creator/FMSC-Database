# Installation and Upgrade Guide — Windows x64

This describes the **existing FMSC_SMS installer**; it does not replace it. Use a tested Windows machine and approved, verified data. For backup/restore see [BACKUP_RECOVERY.md](BACKUP_RECOVERY.md).

## What is delivered

The private [Complete Windows Installer ZIP](../.github/workflows/fmsc-complete-installer.yml) is designed to include:

```text
FMSC_SMS/                 Published self-contained app and resources
Database/                 You provide FMSC_DB.mdf and FMSC_DB_log.ldf for a fresh install
Tools/                    Signed Microsoft SqlLocalDB.msi and verified sqlcmd.exe
Migrations/               2.sql through 6.sql; optional Communication Center add-on 7.sql
Install FMSC_SMS.bat
core.ps1
READ_ME_FIRST.txt         Packaging instructions
COMMUNICATION_CENTER.md  Optional add-on review, consent and deployment instructions
SHA256SUMS.txt            Package content hashes
```

**Important:** The GitHub build deliberately excludes school MDF/LDF files. The source repository alone is **not** a ready-to-install fresh database. A complete-installer workflow must first run successfully and upload its artifact; do not assume an installer exists from a passing ordinary CI build.

After fixes are merged, start a **new** Complete Windows Installer ZIP run with **Run workflow → main**. Rerunning an older run rebuilds its original source commit. Check the source commit in the run summary and extracted `READ_ME_FIRST.txt`; do not mix an older `core.ps1` with newer application files. Packaging verifies the copied installer recovery helper and communication SQL against that checked-out revision.

`Migrations/7.sql` is optional Communication Center support. It does not advance the approved base version from 6. After base compatibility verification, the installer asks **Install Communication Center database support now?** Choose **Y** to create a verified backup and install/verify it transactionally before application activation. Enter or N skips it; a valid existing module is preserved and an incompatible module is refused. Follow [Communication Center deployment](COMMUNICATION_CENTER.md#deployment-gate) on a disposable copy first. No provider is enabled and no existing role gains access automatically.

## Before a fresh install

1. Choose the intended **Windows operator account** and confirm it can access the database after elevated installation. LocalDB instances are per-Windows-user; installation as a different administrator is a known cross-account risk.
2. Download the private installer artifact from the [Actions page](https://github.com/farhan2166-creator/FMSC-Database/actions). Verify the exact source commit, successful job, artifact integrity and contents.
3. Extract the ZIP **completely** into a normal writable folder. Do not run scripts inside the compressed archive.
4. Obtain the owner-approved, compatible **detached** database pair. Both `FMSC_DB.mdf` and `FMSC_DB_log.ldf` must come from the **same** verified snapshot. Never copy an attached/live database or replace a production database with a DACPAC.
5. Put the two files inside the extracted top-level `Database/` folder (beside `core.ps1`); check exact filenames. Neither file belongs in GitHub, email, or CI.
6. Confirm the Windows machine has enough storage, administrator access for the installer, and school-approved antivirus/security policies. Back up any existing installation separately before an upgrade.

## Run the installer

1. Close FMSC_SMS and other sessions connected to its LocalDB instance.
2. Right-click **Install FMSC_SMS.bat** and choose **Run as administrator**.
3. Follow `Installer/core.ps1` prompts. It checks app/process state, LocalDB, database-file presence, staging, version/migration compatibility, and attempts to preserve an existing database.
4. **Never ignore a blocked or failed validation.** Record the exact message and installer logs and stop before retrying or adjusting data files.
5. After successful installation, launch the application under the intended operator account; verify connection, login, schema compatibility, a read-only student search, a report preview, and permitted user actions.

The app expects `FMSC_DB` under `C:\ProgramData\FMSC_Database` and uses `(localdb)\MSSQLLocalDB` by default. Current required **database schema version: 6**; application assembly version is tracked separately in `FMSC_SMS.csproj`.

If silent LocalDB installation fails (for example, MSI code 1603), the current installer reports a verbose log under `C:\ProgramData\FMSC_Database` and asks whether to open the official Microsoft installer wizard. A failed/cancelled wizard or restart-required result stops installation safely. Code 1603 alone does not identify the underlying MSI problem; retain the verbose log for diagnosis. An immediate `SQL Server LocalDB installation failed. MSI exit code: 1603` message without this fallback belongs to the older installer: verify the artifact's source commit before retrying.

## Updating an existing installation

- Take and **verify** a recoverable backup first and note its storage path.
- Do **not** copy a replacement MDF/LDF over an installed school's existing pair.
- Run the same verified installer with the app closed. Its database version gates and ordered migration chain must be allowed to operate; a newer schema must not be downgraded.
- Record the automatic pre-migration safety backup, installer status, original/current version, and post-upgrade application compatibility. Recovery requires a tested restore path.
- If anything fails, **do not manually rerun SQL migrations on the live school database**. Follow [recovery](BACKUP_RECOVERY.md), and use a verified disposable clone for diagnosis.

## What must be tested before broad deployment

Installation under the intended Windows user; existing-data upgrade and rollback on a restored clone; backup verification; reports and printer; permissions; actual SQL disconnect/reconnect; and representative money transactions. Refer to [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) and [master audit](../Audit/FMSC_MASTER_AUDIT.md).

## Private builds

The app-only ZIP is **not** a fresh-install package. The full-installer workflow includes prerequisites but will not contain student data until you supply the detached pair locally. Never claim release-ready without a confirmed successful installer artifact and manual acceptance.
