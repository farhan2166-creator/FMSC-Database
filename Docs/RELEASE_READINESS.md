# Release candidate: school workflow readiness

Status: implementation and verification in progress. This file does not certify
school-PC deployment until the acceptance evidence below is completed.

## Implemented

- Admission workspace groups direct intake, pending registrations and existing-student billing setup.
- Billing Center resolves exceptions into the selected student's setup/collection; duplicate navigation removed.
- Payment corrections exclude credit entries and money already released to credit.
- App, installer, worker and webhook host align on schema 20. Migration 20 upgrades schema 19 without rewriting school, financial or permission rows.
- Removed unused GetRoles, GetUserRoles, GetRolesAndPermissions and GetAdjustableFeePayments procedures after checking C#, SQL module, report and validation callers; supported role administration uses the existing AdministrationStore paths.
- Restored deleted verification sources and tools; retired fixture migrations stay outside release packages.
- Repaired Help Center evidence links and explicit release checks for installation, security and packaging.

## Verification

Current checks and final GitHub run links will be recorded here when verification
finishes. Earlier passes do not certify later changes.

## School-PC acceptance

A disposable hosted installation does not exercise the school's Windows account,
printer, monitor scaling or local backup media. Before rollout, use a disposable
copy and verify: login as the intended operator; direct/pending admission; legacy
activation with prior admission fees acknowledged; partial and family payment;
credit/refund; transfer/return with earlier dues; statement and physical receipt
printing; restart after an interrupted operation; and verified backup/restore.

No live school database has been accessed or changed by this work. The existing
owner-supplied detached MDF/LDF release preparation remains unchanged.
