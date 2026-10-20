# Finance wording and examples — 20 October 2026

Owner decision: financial screens should use plain action names, explain the effect before saving, and offer a short rupee example on hover. Essential consequences remain visible. This is a wording and guidance change, preserving calculations, permissions, transaction codes, review snapshots and recovery rules.

## Shared meanings

- **Add/post fees:** add to what a student owes; no payment is received.
- **Amount received:** new money in this transaction, separate from advance already held.
- **Student advance:** unused money held for that student. Using it settles fees without receiving new cash.
- **Discount:** the rupee reduction from the full fee, rather than the amount to pay.
- **Reduce Recorded Payment:** enter the amount to keep as paid. Reducing 1,000 to 800 adds 200 back to outstanding; it does not record a cash refund.
- **Cancel unpaid charge:** remove an eligible fee from dues. Payment history remains a restriction, including after payment reductions or refunds.
- **Reduced fee already paid:** choose whether the excess payment becomes advance money or a refund. This differs from reducing the recorded payment.
- **Review:** display the proposed effect; confirmation performs the save.

Examples are conditional illustrations, not promises that a particular charge is eligible. Existing checks still determine eligibility.

## Areas reviewed

| Area | Guidance and wording |
|---|---|
| Individual, family and quick-counter collection | New money versus advance; discount example; Choose Fees to Pay / Use Suggested Split; receipt reprinting |
| Student advance balance | Deposit, refund and sibling transfer examples follow the selected action; entered amounts and internal action codes are preserved |
| Payment adjustments | Amount to keep as paid, refund, whole-payment reversal; existing exact outstanding-increase confirmation |
| Student fee plans, transport, pending fees | Fee-plan versus existing-bill changes; stopping transport; adding pending fees does not collect payment |
| Future fee estimate | Explicitly read-only; estimates do not create dues |
| Fee and discount changes | Full fee before discount, full waiver, inclusive months, saved plan versus existing bills, handling excess paid money |
| Monthly and additional posting | Adding dues versus collecting payment; whole-school monthly cancellation scope and payment-history restriction |
| One-off purchases | Uses the shared additional-charge entry with fee and discount examples |
| Rate grid, cell details, copy and bulk changes | Saving class settings versus applying rates to students; percentage/rupee examples; preview before applying |
| Applying saved rates and changing existing bills | Effective period, separate bill review and excess-payment handling |
| Legacy activation | Placement history and billing start are independent; billing-date cutoff example remains intact |
| Transfers and readmission | Start of new fees, retained old debt, yearly-fee waiver, excess-payment choice and replacing unbilled plans |
| Leaving and registration closure | Retained dues, selected reversals, payment review, view-only history checkbox and excess-payment handling |
| Student statements, outstanding-bill selection and financial reports | Opening/closing balance examples, report/print versus transaction, balance filtering, payment-reduction terminology |
| Daily closing | Counted money versus fees settled with advance; existing blank-versus-zero and refund/reduction notes retained |
| Fee types, history and workspace wrappers | Frequency caption clarified; wrappers reuse the improved entry/report screens rather than adding competing guidance |

`Core/FinanceHelp.cs` owns the shared examples. Tooltips also attach to the native children of wrapped inputs, include accessible descriptions and are disposed with their owning screen. Tooltips do not change the selected choice or entered values. Stored values such as `Correction`, `Credit`, `Refund`, `Deposit` and `Transfer` remain unchanged.

No database migration is required: this review introduces no schema or procedure changes and writes no live financial records.

## Verification

| Check | Result |
|---|---|
| Release solution build | Passed; zero warnings and errors |
| Financial examples, wrapped-input hover targets, accessibility, lifetime, action codes and review safety | 89 checks passed |
| Existing Windows workflow regression suite | 1,876 checks passed |
| Financial presentation at different window sizes/scales | 65 checks passed |
| Individual/family advance allocation | 22 checks passed |
| Leaving-fee options and view-only checkbox | 20 checks passed |
| Repository schema, permissions and security fixture checks | 7 tests passed |

Financial presentation PNGs were inspected in `artifacts/finance-presentation`. The native export regression used a workspace-owned temporary directory because the sandbox refused replacement of a temporary export outside it. No production export code was changed for that restriction. The native test project's existing NU1900 advisory-feed warning remains: NuGet's vulnerability feed could not be reached. This is not a fresh dependency-security assessment.
