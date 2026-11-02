# Current-release verification and acceptance — 1 November 2026

Source baseline: `12b37c39a6915c5a9f5730f4191e9c92a445d992` on `main` before this repair.
Application: FMSC School Management System 1.1.0-rc.2. Database schema: **69**.

## Integration update — 2 November 2026

Reconciled with main `db5f8b3` (class/section consolidation and report fixes).
The current application, installer and SQL fixture contexts require schema 70.
The schema-69 baseline above is historical. Retain migrations 67–69 header repairs
and the installer gap guard; do not overwrite newer report migration 70 or
reintroduce already-fixed shortcut signatures. PR 71 overlaps this repair and
can be superseded after the integrated change lands. No school database is
updated merely by merging these sources. Current candidate CI and native/SQL
acceptance remain distinct from source/build checks.

## Scope

This repair aligns the installer's missing-migration refusal with the existing
standalone migration applier, updates the **installer-only** inert migration tests,
removes obsolete schema 21 wording from generated installer instructions,
identifies schema 21 release-readiness material as historical, and reconciles
publication-security documentation with the repository's already-public state.

**Intentionally excluded at owner request:** adding dedicated automated execution
coverage of `Migrations/Apply-Migrations.ps1`. The standalone applier is unchanged.
Existing installer migration regression tests remain in scope.

## Focused execution — 2 November integration

- Release app/native harness compilation: zero warnings and errors.
- Report data/contract checks: 292 passed.
- Tools Python checks: 8 passed.
- Installer-only inert PowerShell suite: 15 version cases, 19 folder/version/backup
  scenarios, SQL wrapper/failure cleanup and activation-order checks passed.
- SQL Server syntax parsing: migrations 67, 68, 69 and 70 passed.

These are source, compilation and inert/synthetic checks. Native Windows execution,
actual SQL upgrades, grants preservation in SQL Server and complete-installer CI
are not certified by these results. No school database was changed.

## Required CI evidence

The existing `FMSC Release Readiness` workflow on `release/**` runs security,
desktop Windows CI, real disposable SQL, disposable Windows installer and full
package assembly. Record all job conclusions at the **final** candidate SHA:

- Candidate SHA:
- Workflow URL:
- Security:
- Desktop:
- Disposable SQL:
- Disposable Windows installation:
- Package:
- Failed/blocked assertions and disposition:

The previous successful full run at `b6005e58`
([run 41283058444](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/41283058444))
predates the admissions and migration 67–69 changes plus this repair.
A successful security check alone is **not** full release clearance.

## Disposable data and operator gates

These are not demonstrated by source review or hosted CI alone. Check and sign
off using **an approved disposable copy**, not the live school's database:

- Matching, detached MDF/LDF pair; verify attached database identity and schema.
- Older supported version upgrades with each consecutive SQL file present.
- Deliberate missing-file and future-version refusal; no database changes.
- Pre-migration backup, RESTORE VERIFYONLY, actual restore, and application reconnect.
- Login, role boundaries, admissions, cancellation/reopen, transfer vs section move,
  readmission, fees/postings, receipts, certificates, reports and communication.
- Offline/Windows-account/LocalDB first-install path; high-DPI, native preview,
  paper printer, operator confirmation and failure recovery on target hardware.
- Privacy review of screenshots, documents, workflow artifacts and external attachments.

Do **not** mark the source as production certified without the matching CI run
and required local operator acceptance. Missing private school snapshots during
development are expected, not themselves a release defect.

## Outcome

**Release certification: PENDING.** This report is a worklist/traceability record,
not fabricated test evidence. Do not claim this markdown file executes or certifies
the local scenarios.
