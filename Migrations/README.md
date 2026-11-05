# Production database baseline: 1

The first production release uses application **1.0.0**, database **1**.
The supplied database already contains the complete approved schema. No numbered
SQL is required for this first release. Future changes start at **2.sql**.

Historical development migrations are test fixtures under
`Tests/LifecycleSqlChecks/Migrations` and must never be shipped or replayed
on baseline 1. See [the baseline decision](../Docs/FIRST_RELEASE_BASELINE.md).

# Apply migrations without the installer

Use **`Apply-Migrations.bat`** (double-click on Windows) or run
**`Apply-Migrations.ps1`** from this folder. This updates an **already attached**
FMSC SQL database; it does **not** install LocalDB, attach an MDF/LDF,
replace database files, install the desktop app, or activate an app release.

1. Run under the **same Windows user account** that owns the intended
   `(localdb)\MSSQLLocalDB` database. Close FMSC and other database users.
   Confirm you have an approved maintenance window and a separate tested backup.
2. Double-click `Apply-Migrations.bat`. The script displays the actual SQL
   instance, database, Windows identity, detected schema, required schema,
   pending versions and backup location. Type **APPLY** to proceed, or close
   the window/cancel without modifying SQL data.
3. Before the first pending script, the applier makes a
   `BACKUP DATABASE ... WITH CHECKSUM`, validates it with
   `RESTORE VERIFYONLY WITH CHECKSUM`, and checks that its `.bak` file exists
   and is non-empty. The default backup folder is
   `C:\ProgramData\FMSC_Database\Backups`. It records the recovery point
   in `LastPreMigrationBackup.txt` and writes `ApplyMigrations.log`.
4. Each pending numbered `*.sql` is executed in ascending order inside the
   same-connection, exclusive-lock, outer-transaction and schema-version
   wrapper used by the installer. Already-applied files are skipped. The
   applier verifies the complete consecutive sequence from the database's
   current version to the required schema **before taking a backup**. If an
   intermediate file is missing, it refuses to run instead of skipping ahead.
   The process stops on any SQL/version/backup error; do not blindly retry.
5. Verify the final version and test the updated FMSC app against the database.
   **Only the full installer performs its staged-application compatibility
   checks and activation**; this standalone tool does not certify app
   compatibility merely by reaching the numbered schema.

For a **read-only migration plan** (database connection and version query, no
backup or SQL changes):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Apply-Migrations.ps1 -PlanOnly
```

For an approved **disposable database clone** with a different name, or when
`sqlcmd.exe` is already installed elsewhere:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Apply-Migrations.ps1 -DatabaseName FMSC_DB_Test -BackupRoot 'C:\FMSC-Clone-Backups'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Apply-Migrations.ps1 -SqlCmdPath 'C:\Tools\sqlcmd.exe' -PlanOnly
```

`-Apply` skips the interactive **APPLY** prompt (use only for intentionally
approved unattended runs). By default the applier reads the required version
from the app/installer source next to `Migrations` and refuses if they
disagree. If only the migration folder is copied, the highest numbered SQL
file becomes the target and the script warns that application compatibility
must be checked separately. If `sqlcmd.exe` is missing, the same pinned and
SHA-256-checked Microsoft go-sqlcmd build as the installer is obtained.

**This is a database mutation tool. Test on a disposable copy first.**
See `Docs/BACKUP_RECOVERY.md` for recovery guidance.
