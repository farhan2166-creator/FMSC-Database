# Student Fee Account workspace

## Decision and scope

The principal daily workflows are Admission, Fee Collection and Student Fee
Account. The account workspace consolidates investigation and existing payment
corrections. Fee Collection, admission flows, financial procedures, migrations,
the DACPAC and receipt issuance/printing implementations are unchanged.

## Existing overlap and reuse

| Existing component | Account use |
|---|---|
| `StudentAccountStatementPage` / `GetFeeStatementTransactions` | Existing date filters, family selection, verified opening/period totals, fee balances and PDF/CSV export. Raw reader rows also appear chronologically; no running balance is invented. |
| `StudentFeeAdjustmentsPage` | Existing payment history reader, allocation details, eligibility, reduction/refund/reversal form and confirmation. A contextual constructor pins the original student and payment. |
| `PaymentAdjustmentState` | The same remaining-payment and eligible-action rules drive both account display and correction availability. |
| `RecoverableMutationWriter` / `MutationRecoveryJournal` | Existing authorization, transactions, exact request identity, recovery, rejected saves and unknown outcomes. No alternate financial writer is introduced. |
| `GetStudentCollectionReceipts` | Original document inventory; a family document is deduplicated by receipt number. Per-child figures are hidden in this list rather than presented as family totals. |
| `GetCollectionReceipt` / `FeeReceiptDocuments` | Saved header, original participants, fee lines, discounts and plans. Reprint uses the existing snapshot-aware report pipeline. |
| `CollectionReceiptPayments` | Read-only linkage from a saved receipt to its original payments and subsequent corrections, using the existing payment-history reader. Participants come from the original document, not today's family links. |
| `FeeReceiptQueries.QueryCurrentBalanceAsync` / `GetStudentCredit` | Current outstanding dues and available credit, separately labelled and never netted. Credit movement history remains read-only. |
| `StudentFeeChangeHistory` | Existing fee-plan/concession history report, retaining its permission requirements and incomplete-history disclosures. |

## Workspace structure

Search once; Include Siblings toggles the existing family selection in place.
Switching views preserves student, scope and statement dates.

- **Statement:** established Fee Balances presentation and export, Chronological
  Entries from the same SQL result, selected entry details, Related Payment and
  existing Fee Change History.
- **Receipts:** Issued Documents, Payments & Adjustments, and Account Credit.
  Opening a receipt shows original student shares, original fee details and plans,
  plus a separate Payments & Later Changes view. Eligible originals can open the
  established correction form without another student search.
- **Reports menu:** existing family/group statement, annual receipts and current
  unpaid-bill report shortcuts. Their scope and permissions remain distinct.

Current all-date dues/credit and receipt history are explicitly distinguished
from the date-filtered period totals above them. Individual fee balances are
not described as running ledger balances.

The account is available under Daily Work, the welcome shortcuts and the Reports
hub using existing `Reports.View` access, matching the old statement reader.
Receipt/payment/credit readers additionally require `Fees.View`. Corrections
require `Fees.Adjust` in the UI and the existing SQL/writer authorization.
Annual/group reports retain `Fees.Statement.View` checks.

The redundant corrections menu is hidden for operators who can use the account;
the original route is retained for collect-only operators without report access.
Old statement and correction implementations remain available to existing callers.
School-wide reports and collection's history dialog keep their existing behavior.

## Safeguards and tradeoffs

- No new financial procedure, schema change or migration is needed.
- Contextual corrections cannot switch to a different student/payment. Recovery
  for another student can be resolved without replacing the selected account.
- Account actions are invalidated immediately when selection/dates change. Late
  responses and responses from a previous signed-in user cannot enable actions.
- Reloading after a review includes cancelled reviews and recovery resolution;
  it does not replay writes. Payment/receipt selections are restored where found.
- Original receipt tables remain separate from current adjustment tables. The
  existing legacy snapshot warning is retained; missing historical details are
  not synthesized from today's balances or family membership.
- Busy/unsaved-change guards remain on correction dialogs; receipt preview has
  a busy-close guard. The form uses existing HumanUi, AppTheme and scaling helpers.
- The statement SQL exposes charges and completed payment allocations, not a
  complete immutable event ledger. Reversed originals are visible in Payments &
  Adjustments, credit movements in Account Credit, and concessions in the existing
  history/report views. Building a new running ledger is outside this change.
- Existing fee-plan/charge-changing workflows remain owned by their established
  pages/dialogs. This workspace adds no new way to edit a historical charge.

## Validation

Focused native checks are selected with `--fee-account`; existing statement,
payment-recovery, receipt-breakdown, receipt rendering and PDF export checks are
also run. The new `Run-Level2.ps1 -FeeAccountOnly` mode creates an ownership-checked,
private LocalDB instance, publishes the existing record-free schema fixture,
applies numbered migrations and creates fictional account records. It never
accepts a school connection string or restores school data.

The test preflight's stale schema-42 requirement and the reference fixture's
schema-44 constant are aligned to 58. A missing async reader is added to the
test-only database adapter so the current shared writer can compile. These are
test-infrastructure repairs, not production database changes.

Actual run results and remaining verification limits are recorded in
`Docs/StudentFeeAccountValidation.md`.
