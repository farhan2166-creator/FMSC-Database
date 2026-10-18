# Release candidate: school workflow readiness

Candidate: 1.1.0-rc.1, database schema 20.

Status: automated release acceptance and complete installer packaging PASS.
Ready for school-PC acceptance; live rollout follows the checks below.

## Implemented

- Admission workspace groups direct intake, pending registrations and existing-student billing setup.
- Billing Center resolves exceptions into the selected student's setup/collection; duplicate navigation removed.
- Payment corrections exclude credit entries and money already released to credit.
- App, installer, worker and webhook host align on schema 20. Migration 20 upgrades schema 19 without rewriting school, financial or permission rows.
- Revised procedures follow gradual audit retirement; operational financial/lifecycle and recovery records remain intact.
- Removed unused GetRoles, GetUserRoles, GetRolesAndPermissions and GetAdjustableFeePayments procedures after checking C#, SQL module, report and validation callers; supported role administration uses the existing AdministrationStore paths.
- Restored deleted verification sources and tools; retired fixture migrations stay outside release packages.
- Repaired Help Center evidence links and explicit release checks for installation, security and packaging.
- Registration displays monthly plans separately from yearly/one-time fees and directs actual dues to the posted bill.
- Installer accepts release-candidate versions and prevents a final release from being replaced by an older RC; schema-package checksums are validated before SQL deployment.

## Verification

Tested code: `bcd6d8ce049382e0f89d9f8a17464cb7e29839cd`.
[Release verification](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/39114451824):

- Security: PASS, including full available history and historical DACPAC contents.
- Windows: PASS — Release build, 1,850 focused native control checks, 254 component checks with 240 dialog cycles and a 19-page PDF, 1,922 Help checks and 32,183 performance/state checks.
- SQL Server 2022: PASS — 76 stages, zero failed/blocked stages. Includes admission, prospective discounts/cutoff, historical admission-fee acknowledgement, partial/family collection, credit/refunds, repeated rate corrections, promotion/return, real rollback and request recovery, concurrency, permissions, backup/restore, CHECKDB and optional communication using fake providers.
- Installer/publishing regression: PASS — full PortableRelease validation, unsafe-target refusal, backup/version/activation-order checks.
- Actual disposable Windows installation: PASS — schema-19 upgrade to 20, installed-app compatibility, update/reinstall without a replacement database, fictional record/database identity/theme preservation, unsafe prerequisite refusal, activation rollback and package integrity.
- Additional LocalDB billing checks: PASS — 95 cases and production startup/restore compatibility.
- Final complete-installer package: PASS — self-contained Windows x64 app, Microsoft-signed LocalDB MSI, checksum-pinned sqlcmd, migration 20 and checksum manifest; no school database or backup files.

The linked run is the authoritative result for the tested code. Evidence-only
documentation may follow that commit without changing the application or installer.
The revised callback also removes its audit insert, preserving inbox hashes and delivery recovery records.
No physical printer, school Windows profile or live delivery acceptance is claimed.

## Installer artifact

[Download the complete Windows x64 installer](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/39114451824/artifacts/12131634176).

Artifact: `FMSC_SMS-Complete-Windows-x64-Add-Database-Files` (161,001,881 bytes).
GitHub ZIP SHA256:
`df853be4c5bcbec700dcebfe3b25ec6dab599c3f91088a06c337849c5a49b45f`.
This Actions artifact is retained until 2026-11-01; its source commit and
`SHA256SUMS.txt` are included in the extracted package. Follow
[INSTALLATION.md](INSTALLATION.md) and [SCHOOL_OPERATIONS.md](SCHOOL_OPERATIONS.md).

Fresh installs use the established owner-supplied detached database pair locally.
Existing installations preserve their database; schema 19 upgrades through the
verified-backup migration. Historical test migrations never enter this package.
The hosted image already had LocalDB, so pristine MSI installation, an interactive
MSI wizard and a Windows reboot remain outside this run's coverage.

## Database changes and retirement evidence

The schema model comparison against main `8300557` removes exactly four procedures
and changes only `comm.IngestMeta`; no table, column, key, constraint or other module
is changed. Current DACPAC SHA256:
`db7650b3477f99a35f2404b355eced984c13deba8aa4b51db79a9d760d806c1b`.

| Retired procedure | Supported replacement / evidence |
|---|---|
| GetRoles | AdministrationStore catalog reads; no current C#/SQL-module/report/validation caller |
| GetUserRoles | Current authentication/administration role queries; no current caller |
| GetRolesAndPermissions | AdministrationStore access catalog; no current caller |
| GetAdjustableFeePayments | Full correction history plus PaymentAdjustmentState eligibility; no current caller |

Historical fixture migrations keep their original definitions to test the upgrade.
Migration 20 retires the APIs transactionally; dbo.RolePermissions and user/student/
financial rows are preserved. An owned SQL upgrade scenario verifies preserved
student counts, receipts, exact tuition balance and optional module compatibility.
Rollback uses the installer's verified pre-migration backup and matching earlier
application; do not point schema-20 restore validation at a schema-19 rollback target.

## School-PC acceptance

A disposable hosted installation does not exercise the school's Windows account,
printer, monitor scaling or local backup media. Before rollout, use a disposable
copy and verify: login as the intended operator; direct/pending admission; legacy
activation with prior admission fees acknowledged; partial and family payment;
credit/refund; transfer/return with earlier dues; statement and physical receipt
printing; restart after an interrupted operation; and verified backup/restore.

No live school database has been accessed or changed by this work. The existing
owner-supplied detached MDF/LDF release preparation remains unchanged.
