# Student Fee Account validation — 28 October 2026

Implementation map and design: [workspace decision](BUSINESS_DECISIONS.md#bd-20).

## Executed and passed

| Check | Observed result |
|---|---|
| Application and native harness build | Successful, zero compiler warnings/errors in the final build. Earlier builds reported temporary NuGet vulnerability-feed unavailability; a successful build is not a dependency-security audit. |
| `--fee-account` | 55 checks: individual/family context, receipt deduplication, separate dues/credit, read-only permissions, previous-user rejection, stale asynchronous responses, credit eligibility, contextual student guard, navigation, keyboard focus, snapshot versions 1/2/3 presentation, three window sizes, synthetic 96/120/144/192 DPI, and existing correction/recovery checks. |
| `--statement-actions` | 61 checks: original single/group statement behavior, async assembly, stale/failed-regeneration export guards, and related-report permissions/context. |
| `--receipt-breakdown` | 23 checks: original sibling shares, whole-receipt totals, adjustments and layout. |
| `--current-receipt-balances` | 14 checks: existing receipt balance behavior. |
| `--payment-adjustments` | 17 checks: existing correction wording, eligibility, selection generations and unresolved-save protection. |
| `--grid-pdf` | 835 checks: shared PDF layout, field preservation and pagination/export machinery. |
| `--fee-documents` | 923 checks, including existing receipt/bill RDLC PNG/PDF rendering and snapshot/presentation cases. |
| `Tools/Run-Level2.ps1 -FeeAccountOnly` | 22 SQL assertions passed on private instance `FMSC_Level2_a03bf2c3502c4cd5b14ab1ac5150f9b0`. The record-free fixture was migrated to schema 58 and populated with fictional siblings. Production database identity guard and six Python isolation/evidence checks also passed. The private instance was stopped/deleted and its private files removed. |
| Scope inspection / `git diff --check` | No changes to Fee Collection files, Student Admission files, production database helper, migrations, DACPAC or financial stored procedures. No whitespace errors. |

The SQL suite actually exercised the existing family collection writer to create
a partial cash payment and concession, read both saved student shares, deposited
advance credit, reduced a payment, refunded it with a simulated lost commit
acknowledgement, resolved that exact request and replayed it without a second
refund, reversed another payment, verified eligibility and SQL read-permission
denial, applied existing credit against a posted charge, and rendered the original
family receipt to PDF after corrections. The original receipt JSON hash remained
unchanged. All financial writes in this task's tests were to the owned fictional
database.

Evidence is generated under `artifacts/fee-account`, `artifacts/fee-documents`,
and `artifacts/level2/evidence`; generated/private files are not committed.

## Limits and unsuccessful broader attempts

- An unscoped native harness invocation stopped at **“Theme must preserve control
  structure and geometry”**, in its shared-control theme test. That test does not
  construct the new account workspace. The full native suite is **not** claimed
  green; the focused suites listed above did execute and pass.
- The older family-counter SQL suite's fixed **21,000 opening-dues** assertion
  failed against the current schema-58 fixture. Its financial expectations were
  not rewritten to make this task pass. The account suite instead creates its own
  fictional records through the established procedures and verifies the account
  operations independently. Earlier preflight/test-fixture build issues were
  repaired as documented in the decision note.
- Synthetic DPI geometry and keyboard focus checks do not establish physical
  multi-monitor, screen-reader or printer acceptance. Visual captures were
  inspected; a clipped period-summary layout was corrected and a visibility
  assertion added.
- New receipt-detail layouts are exercised with version-1/2/3 headers; the existing
  document suite tests the shared snapshot/report logic. No historical school
  document was altered or physically printed. No broad claim is made that every
  legacy snapshot variant has been manually inspected.
- The original statement's SQL remains authoritative. It does not expose a full
  immutable event ledger or a verified running balance. Reversals, unused credit
  movements and fee-change history are available through their existing dedicated
  views inside the account workflow. No new financial calculations were added to
  fill those historical gaps.
- Complex fee-plan/charge editing, school-wide reports and actual receiving of
  payments retain their established owners. The account adds contextual access to
  existing payment reduction/refund/reversal operations, not new historical edits.

Before rollout, an operator should walk a representative real account read-only,
check a historical receipt preview and confirm the layout on the intended monitor
and printer. Financial correction behavior has been tested only with fictional
records in the disposable database.

## Redundant-page retirement follow-up

The standalone correction destination and its obsolete title are removed; its
implementation is renamed to the internal `PaymentCorrectionReview` component.
The duplicate operational account card is removed from Reports, and the aging
report's contextual link opens Student Fee Account. Help links are updated,
including one admission help sentence; admission behavior is unchanged.

Collect-only operators use the same account destination with Receipts available
under existing fee permissions. Report SQL, date filters and exports remain
restricted by report permission. The independent Check Save Outcome action opens
a recovery-only review that cannot submit a fresh correction, even when the
original payment has become ineligible. Native checks cover these access paths.

The shared statement implementation and Fee Collection history wrapper remain
because they are active dependencies. Multi-family group statements and annual
receipt printing retain capabilities beyond the operational account and remain
available. Fee Collection source files and financial SQL remain unchanged.

Follow-up verification: build succeeded with zero warnings/errors; the account
suite passed 65 checks, payment adjustment/recovery 19, original statement actions
61, and finance help/transaction safety 98. SQL integration was not repeated for
this navigation/component retirement; the earlier 22-assertion disposable run is
reported above for the consolidation implementation, not claimed as a new run.
