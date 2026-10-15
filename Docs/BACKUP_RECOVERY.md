# Database Backup and Recovery Runbook

**Applies to:** FMSC_SMS schema-6 SQL Server LocalDB database `FMSC_DB`. This guide prioritizes **keeping existing school records intact**. It is not an authorization to operate on real data without an approved maintenance window and verified backup.

## Back up from the application

1. Sign in with the required permissions and navigate to **System Maintenance → Backup / Restore**.
2. Choose an approved **Backup Folder** with enough disk space and access for the SQL process.
3. Select **Back Up Data**. Wait for completion; do not shut down the app or computer during the operation.
4. Confirm the success message and **note the exact `.bak` path**. The implementation runs SQL backup, checks file existence/non-empty size, and validates it with **RESTORE VERIFYONLY** before reporting verified success.
5. Move or copy a protected backup to an approved **separate** location according to school policy; verify it is readable and protect student data. A backup stored only on the same failing disk is not adequate disaster recovery.

The backup manager generates names like `FMSC_DB_Backup_YYYY-MM-DD_HHmm.bak`. A backup-history entry failing to write does **not** necessarily mean the backup failed; use the explicit message, verified file and log. See `Services/Backup/DatabaseBackupManager.cs`.

## Before you consider a restore

Communication Center installation uses the existing verified pre-migration backup helper before applying optional `Migrations/7.sql`. Its backup filename includes `v6_to_v6_Communication1`; this identifies the separate module setup, not a base schema-version change. The installer records the path in `Install.log` and `Backups/LastPreMigrationBackup.txt`. A failed backup prevents SQL installation; a failed or uncertain module installation prevents application activation. Do not blindly rerun SQL after a timeout or lost acknowledgement: rerun the installer to verify whether the module committed, preserving any existing communication history.

- **Restoring replaces current school data. Newer records may be lost.** Confirm restore point, time, operator approval and expected data-loss window.
- Close other app sessions; plan a maintenance window. Confirm SQL LocalDB user/instance and file permissions.
- Verify the `.bak` is an actual `FMSC_DB` backup and has passed header/integrity checks. Do not guess based on filename.
- Maintain at least one **independently retained and tested backup** of the present database, in addition to the application's required pre-restore safety backup.
- Prefer a **disposable clone** first for damage investigation, schema upgrades, and recovery rehearsal.

## Restore through the application (approved method)

1. Open **System Maintenance → Backup / Restore** using an account with `Backup.Restore`.
2. Choose **Choose Backup File…**. The UI checks whether the selected `.bak` has a readable, FMSC-compatible header and warns when it is old; wrong-database and unverified files are refused.
3. Read the displayed age/warning and confirm that losing records newer than this backup is acceptable.
4. Select **Restore Data** and follow confirmations. The implementation validates the backup, **requires a successful verified pre-restore safety backup**, switches to single-user mode, performs SQL restore with checksum, returns to multi-user mode, and validates the restored database structure.
5. Wait for explicit success. Reopen/reconnect and verify representative student, billing, fees, permissions and reporting data. Record the backup path, selected restore point, operator, and outcome.

The safety copy is named like `FMSC_DB_PreRestore_YYYY-MM-DD_HHmmss.bak`. If restore fails after it began, the application attempts to recover from this backup; **automatic recovery itself can fail**. Retain all paths in the message/log and escalate rather than trying repeated manual replacements.

## When the app cannot connect

- **Missing database file:** Check `C:\ProgramData\FMSC_Database\FMSC_DB.mdf`, correct Windows identity and file access. **Reconnect/repair cannot recreate lost school records.**
- **SQL LocalDB missing:** The connection screen can offer **Repair Connection**, but SQL installation needs internet/admin rights as described by the app. This repairs the engine, **not** missing student data.
- **Instance unavailable/busy:** Use the offered **Reconnect** or **Restart Connection** actions as appropriate. Do not delete or move attached MDF/LDF files.
- **Wrong engine or database compatibility error:** Stop and contact the administrator. Schema version alone is not a sufficient compatibility check; do not bypass startup validation.
- **Unknown save outcome:** Reconcile via the page's recovery/check-outcome flow first; do not blindly repeat fee posting, collections, admissions or corrections.

For persistent failures record **sanitized** application/installer logs and the error category. Do not send student data, database files, credentials or full sensitive logs to GitHub Issues.

## Recovery escalation checklist

- [ ] Identify affected machine, Windows user, expected database/instance, and last successful operation.
- [ ] Avoid further writes if integrity is uncertain; preserve the original MDF/LDF and backup evidence.
- [ ] Determine most recent verified backup and its SHA-256 or controlled inventory record.
- [ ] Try diagnosis and restore on a disposable clone with a matching schema/migration chain.
- [ ] Confirm `FMSC_DB` structural compatibility and records in the clone.
- [ ] Perform approved live recovery only with trained operators, maintenance window and rollback plan.
- [ ] Record the incident, restored point-in-time, verification evidence and follow-up.

**Never** replace an installed school's MDF/LDF directly with a packaged template or DACPAC. Never treat restoring a backup as lossless: changes since the backup may not be present.
