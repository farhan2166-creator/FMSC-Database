# Release Checklist — Windows x64 Installer

**Release candidate commit:** `________________`  
**Application version:** `________________`  
**Required DB schema version:** `21` (confirm against current code)\
**Reviewer and test date:** `________________`  
**Windows account / OS / target hardware:** `________________`

**Rule:** An automated green check is not permission to install on a live school database. Mark a stage done only with a matching artifact/run URL, output and recorded result. For full risk context see [master audit](../Audit/FMSC_MASTER_AUDIT.md).

## 1. Source and changes

- [ ] Freeze the exact Git commit and review its changes and any outstanding security/deployment findings.
- [ ] Explain user-visible and business-rule changes in the pull request; update [CHANGELOG.md](../CHANGELOG.md) and guides.
- [ ] Confirm no school MDF/LDF, .bak files, credentials, student exports or sensitive logs were committed.
- [ ] Review any permission, admission, fee, recovery or schema change with the relevant operator.

## 2. Automated validation

- [ ] [Desktop CI](../.github/workflows/fmsc-desktop-ci.yml) passed **at the release candidate SHA**.
- [ ] C# Release compilation and focused WinForms contract fixtures passed.
- [ ] Portable regression and synthetic performance checks passed.
- [ ] Disposable SQL fixture safety, real SQL integration and installer acceptance passed (as applicable).
- [ ] If DB code changed, the SQL integration tests passed **against a disposable copy**; record connection, fixtures and schema.
- [ ] Confirm known runner warnings are understood; no test was disabled to make an artifact appear.

## 3. Database-free complete installer artifact

- [ ] [Complete installer workflow](../.github/workflows/fmsc-complete-installer.yml) finished **successfully** at the same SHA.
- [ ] Downloaded database-free Actions artifact and recorded URL, run ID and checksum.
- [ ] Package contains `FMSC_SMS/`, `Install FMSC_SMS.bat`, `core.ps1`, `Tools/SqlLocalDB.msi`, `Tools/sqlcmd.exe`, a `Migrations/` folder (empty when there are no pending scripts), required app resources, readme and communication deployment guide. Historical migrations 7–19 are test fixtures only; migrations 20/21 support the schema-19/20 baselines. Future numbered scripts must record their versions and match the application release; the installer backs up before applying them.
- [ ] Copied installer and any pending numbered SQL files match the source commit; copied-helper migration discovery/version tests passed. Start a new workflow run for new source rather than rerunning an older run.
- [ ] Microsoft LocalDB MSI signature and pinned sqlcmd digest were verified by the workflow; double-check source of prerequisites if changed.
- [ ] DB snapshot is **not** included in Actions. The owner locally supplies matching **detached** `FMSC_DB.mdf` and `FMSC_DB_log.ldf` only for fresh-install validation.
- [ ] Confirm expected `FMSC_DB` schema and database version against supplied snapshot; **do not replace existing school data or publish DACPAC over MDF/LDF**.

## 4. Isolated installation and recovery acceptance

- [ ] Fresh install on a disposable Windows test PC with a verified matching database pair.
- [ ] Elevated install and operation under the **actual Windows user**; file ACL/LocalDB instance/attach validated.
- [ ] Existing schema version upgraded with correct migration order using a restored **disposable clone**; original data and recovery scope preserved.
- [ ] Verified pre-migration backup and rollback/recovery scenario tested.
- [ ] App startup and full schema compatibility check pass after install/upgrade.
- [ ] Communication module: current installers preserve the supplied/installed module and verify its runtime contract; the migrations align the supported callback and retire its audit write. The retired optional setup is not offered again. Verify contacts/history/settings are preserved and sending remains disabled until explicitly configured.
- [ ] Deliberately invalid, newer, missing and mismatched database/prerequisite scenarios refuse unsafe installation.

## 5. School workflow acceptance

- [ ] Test login, role permissions, user/role maintenance and school setup.
- [ ] Test direct admissions, Quick Registration confirm/cancel, student movement and unsaved-edit protections.
- [ ] Test whole-rupee fees, postings, collections, adjustments/refunds, and uncertain SQL response recovery without duplicate financial writes.
- [ ] Test backup, `RESTORE VERIFYONLY`, safety backup, actual restore and application reconnection on a test database.
- [ ] Test reports, PDFs/CSVs, certificates, previews, real printer and expected DPI/window sizes.
- [ ] Confirm no known blocker or undocumented manual SQL workaround remains.

## 6. Final release decision

- [ ] **GO** — All applicable gates passed with recorded evidence; authorized by release owner.
- [ ] **NO-GO** — Any missing evidence or unsafe behavior is documented in [FMSC_MASTER_AUDIT.md](../Audit/FMSC_MASTER_AUDIT.md).

**Outcome / blockers:** `________________`  
**CI run:** `________________`  
**Installer run and artifact:** `________________`  
**Backup / rollback location:** `________________`  
**Approver:** `________________`

Artifact visibility follows this public repository. Include no school database, backup, credentials or private logs. Broad school deployment requires the owner’s recorded acceptance above.
