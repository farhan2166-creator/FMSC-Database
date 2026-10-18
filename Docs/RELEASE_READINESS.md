# Release candidate: school workflow readiness

Candidate: 1.1.0-rc.1, database schema 20.

Status: implementation and verification in progress. This file does not certify
school-PC deployment until the acceptance evidence below is completed.

## Implemented

- Admission workspace groups direct intake, pending registrations and existing-student billing setup.
- Billing Center resolves exceptions into the selected student's setup/collection; duplicate navigation removed.
- Payment corrections exclude credit entries and money already released to credit.
- App, installer, worker and webhook host align on schema 20. Migration 20 upgrades schema 19 without rewriting school, financial or permission rows.
- Revised procedures follow gradual audit retirement; operational financial/lifecycle and recovery records remain intact.
- Removed unused GetRoles, GetUserRoles, GetRolesAndPermissions and GetAdjustableFeePayments procedures after checking C#, SQL module, report and validation callers; supported role administration uses the existing AdministrationStore paths.
- Restored deleted verification sources and tools; retired fixture migrations stay outside release packages.
- Repaired Help Center evidence links and explicit release checks for installation, security and packaging.

## Verification

Tested code: `073623f70750b633b109cfdb2347ba6877c80eef`.
[Release verification](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/39112885814):

- Security: PASS, including full available history and historical DACPAC contents.
- Windows: PASS — Release build, 1,850 focused native control checks, 254 component checks with 240 dialog cycles and a 19-page PDF, 1,922 Help checks and 32,183 performance/state checks.
- SQL Server 2022: PASS — 76 stages, zero failed/blocked stages. Includes admission, prospective discounts/cutoff, historical admission-fee acknowledgement, partial/family collection, credit/refunds, repeated rate corrections, promotion/return, real rollback and request recovery, concurrency, permissions, backup/restore, CHECKDB and optional communication using fake providers.
- Installer/publishing regression: PASS — full PortableRelease validation, unsafe-target refusal, backup/version/activation-order checks.
- Actual disposable Windows installation: exposed prerelease-version parsing failure; fixed in the installer and awaiting a new complete run. Final package remains pending.

Earlier attempts exposed missing fixture fields and old schema-version assumptions;
those were corrected. The linked run is the authoritative current-code result.
The revised callback also removes its audit insert, preserving inbox hashes and delivery recovery records.
No physical printer, school Windows profile or live delivery acceptance is claimed.

## Database changes and retirement evidence

The schema model comparison against main `8300557` removes exactly four procedures
and changes only `comm.IngestMeta`; no table, column, key, constraint or other module
is changed. Current DACPAC SHA256:
`62f77e4dd41307ef8bc6195622fcd80999552b4ecbeaca3c6ac9ebc30b73ffc1`.

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
