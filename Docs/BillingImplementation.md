# Agreed billing implementation — 18 October 2026

Work in the project folder only. No live database changes or Git operations.
Existing migrations 7–13 are pending and must not be rewritten or assumed applied.
New migrations remain unapplied until separately deployed.

## Approved rules

- Run monthly billing when the application opens, after authentication; also check after reconnection and a month change.
- Require an explicit billing commencement, completed legacy activation, confirmed placement, active status and valid academic-year/assignment dates. Skip blocked students with a reason.
- Keep legacy records, including records marked Active. Never infer billing commencement from historical admission/enrolment dates.
- Current-month billing is automatic. Administrator chooses whether to include missed months or leave them for later review, with months/student count/amount shown.
- Automatic billing never recreates cancelled charges. Reviewed manual reposting remains available.
- Allocate oldest tuition first, then other fees oldest first. Preserve manual allocation.
- Fee Matrix edits do not change assignments unless explicitly applied. Promotion uses the latest matrix and resets concessions/discounts.
- Preserve discounts on rate changes, but automatically cap the discount to the new fee if necessary. Zero/blank means no discount.
- Admission/assignment discount changes start next month and end at academic-year end. Collection adjustments affect only selected unpaid balances, including the unpaid part of a partial bill.
- Free/Scholarship/Waived are effective-dated reporting labels; fee assignments determine what remains chargeable. They follow the same future/collection distinction as discounts.
- Leaving before the profile cutoff removes that month's monthly fees; on/after cutoff retains them. Stop subsequent months. Preserve non-monthly charges and earlier arrears.
- Active students accrue only within the academic year; next-year billing requires selected promotion/readmission and confirmation.
- Class transfer before cutoff applies new monthly rates this month; on/after cutoff next month. Preserve settled money and correct only differences.
- Existing bills can be explicitly corrected for matrix changes. Preserve original financial records. Reductions of paid amounts offer credit/refund; concessions do not reverse paid amounts.
- Excess payment becomes credit, automatically used against future fees until zero. Credit follows the student across years and leaving/readmission. Administrator and accountant can refund or transfer credit to siblings.
- Remove audit-table writes from procedures being changed. Do not mass-remove auditing from unrelated procedures. Preserve transaction, permission and financial validation, bill/payment/allocation and lifecycle records.
- Retire redundant procedures only after the replacement passes verification and all SQL/C#/report/test callers have been checked.

## Implementation checkpoints

- [x] Shared eligibility, atomic automatic posting and administrator recovery preview
- [x] Startup/reconnection/month-change integration and billing exception workspace
- [x] Tuition-first chronological allocation and multi-enrolment collection
- [x] Future assignment discounts and capped discounts on fee reductions
- [x] Effective-dated concessions and family/report presentation
- [x] Credit, explicit rate corrections, leaving and transfer cutoff integration
- [x] Disposable SQL and application regression verification
- [x] Caller inventory; remove duplicated inline discount writer, retain procedures with recovery/report/validation callers

## Changes and deployment boundary

Migrations 14–19 contain the new design. Existing migrations 7–13 remain intact. The application now requires schema 19; deploy through the existing backup/migration/compatibility checks. None of these migrations have been applied to the live school database.

The three small additions have separate necessary purposes: BillingAutomationState establishes the authorized recovery boundary; StudentCreditEntries retains unallocated money and its movement; FeeBillCorrections records issued-bill differences. Charges and their settlement still use BillingCharges, FeePayments and FeePaymentAllocations. Noncash credit application is excluded from cash collection and closing totals.

- 14: one shared monthly eligibility query, posting/review/status reuse and startup orchestration.
- 15: prospective discounts and collection across historical enrolments.
- 16: student credit, surplus receipts and cash-report separation.
- 17: fee-specific concession labels, family receipts and concession reporting.
- 18: selected existing-bill rate corrections with paid credit/refund choices.
- 19: one shared cutoff, admission/promotion/readmission, leaving and transfer integration.

Free monthly assignments generate no zero-value bills. Family documents read their effective assignments; Concession Assignments in Billing & Discounts reports their fee, concession and effective period separately from billed totals. Ordinary admission and Quick Registration expose the same labels. New concessions/discounts begin next month; an existing unpaid bill is adjusted in Fee Collection.

## Verification completed

- `Tools/TestAutomaticBilling.ps1`: 95 assertions passed on a unique, owned, disposable LocalDB instance, published from the data-free schema and migrated through 19. The fixture is deleted afterward. Production startup/restore schema validation passed against that fixture.
- Covered legacy exclusion and explicit commencement; activation floor; duplicate and cancelled-bill protection; stale preview; missed-month decisions; two concurrent posting clients; injected failure rollback/retry; prospective and capped discounts; partial-balance concessions; credit/deposit/refund/sibling transfer and cash totals; fee corrections; family receipts and the production family-bill query; free/transport treatment; admission/Quick Registration cutoff; transfer; leaving; readmission; selected promotion with prior-year arrears and return confirmation.
- Application build: zero warnings/errors. Four offscreen billing dialog renders passed and were visually reviewed. These renders do not connect to SQL.
- Existing AuditFixChecks regression groups passed, including money/TVPs, permissions, quotation, recovery journals, admission confirmation, family billing and display density.
- Installer inert verification passed 15 scenarios and wrapper/cleanup/activation-order checks. Lifecycle safety: 10 passed; Level 2 safety: 5 passed, 1 skipped; Level 3 reporting: 8 passed.
- Docker lifecycle and interactive end-to-end desktop scenarios were not run. No release package or live migration was attempted. Offscreen renders and disposable tests do not claim those checks.

## Retirement findings

The old inline `FeeDiscountPeriod.Save` was removed; the UI now uses the one SQL discount policy. Audit-table writes and unused audit JSON preparation were removed from modified write procedures; unrelated auditing remains.

`PostSelectedMonthlyFees` is now shared by automation, Manual Recovery and assignment posting. `PreviewMonthlyFees` and `GetPostableMonths` still support reviewed administrative recovery. `ReverseMonthlyFees` still serves the reversal screen. Opening, transfer and lifecycle procedures retain active callers. `GetMissingChargeCandidates` and `GetBillingCycleStatus` remain compatibility/diagnostic validation interfaces; financial projections still use the bounded recurring-month function. No SQL procedure was proven safe to drop in this scope, so none was dropped merely because daily posting became automatic.

Reference SQL under Schema has been synchronized with the numbered migrations. Git operations, branches, commits, pull requests and live data were not used or changed.


## Authorized live alignment — 18 October 2026

At the user's explicit request after restoring the original backup, applied migrations 7–19 to `(localdb)\MSSQLLocalDB`, `FMSC_DB`, at `C:\ProgramData\FMSC_Database\FMSC_DB.mdf`. This supersedes the earlier implementation-stage statement that migrations were unapplied.

The existing installer helpers created and checksum-verified `C:\ProgramData\FMSC_Database\Backups\FMSC_DB_BeforeMigration_v6_to_v19_20261018_014956.bak` before transactional migrations. The current C# installation compatibility check returned 0 and DBCC CHECKDB passed. Student/enrolment counts and checksums were unchanged: 6,590 students, 727 enrolments. Assignments, charges and payments remain zero.

Read-only automatic billing preview returned zero current/missed charges and zero pending credit applications. All 5,342 active legacy references were excluded. Automation commencement is October 2026. No billing run, student activation or synthetic test data was submitted to live. The user will take a fresh upgraded backup and test the workflow with their own data.

Migration log: `artifacts/LiveBillingMigration_20261018_014955.log`. No Git operations were performed.
