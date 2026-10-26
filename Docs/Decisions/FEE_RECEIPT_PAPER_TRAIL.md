# Fee documents and the parent paper trail — 26 October 2026

## Visit-based clarification — 27 October 2026

Migration 56 supersedes the cumulative presentation below. A visit begins with
each fee's closing outstanding on the previous relevant receipt. Only subsequent
fees, rate/payment adjustments and further concessions affect the new document.
Settled fees and their earlier concessions remain in history. Bills use the same
position as receipts; printing a bill does not consume the receipt baseline.

New version-3 snapshots save both the visit's printable lines and the underlying
account state. A monotonic visit sequence makes same-second and backdated visits
unambiguous. Existing version-2 snapshots are projected from their saved data and
earlier receipts when read; their stored financial records are never rewritten.

Document student order follows the existing allocator exactly: configured class
catalog order, then numeric admission number and suffix. It does not change cash
or credit already allocated to children. New student-order metadata is saved with
the receipt, keeping its order stable on reprint.

The owner's actual receipts FR-2026-00012, 00013 and 00014 verify family due
14,400 -> 7,400 -> 0. Visit three shows Shayan's opening 600 less a further 200,
payment 400; Abdullah's opening 6,800 less a further 5,200, payment 1,600. Faryal's
fees, settled on visit two, no longer appear.

`Tools/TestFeeVisits.ps1` tests migration preservation, the actual three visits,
new fees, partial payments, additional discounts, waivers, rate increases,
withdrawn concessions, bill issuance and concurrent acknowledgements in an owned
COPY_ONLY clone. It exports actual reader data for RDLC rendering checks.
`Tools/ApplyFeeVisits.ps1 -Apply` verifies a backup and unchanged business records
before committing schema 55 -> 56.

## Earlier cumulative snapshot implementation

The receipt previously saved only charges receiving cash, credit or an additional
collection discount. A waiver saved before collection could disappear; an existing
discount appeared only as an already reduced fee. Admission 17 exposed both cases.

Migration 55 adds complete version-2 collection snapshots. SQL validates the actual
payment allocations, then independently captures all selected children's billed
charges, including zero-payable waivers, prior settlements and cancelled-charge
notices. Correction charges are consolidated into their original fee. Agreed fee
schedules are also captured, including effective periods and cancelled agreements.

On paper, each fee shows its plain name and full amount. Per-fee concession
explanations and reasons are omitted; the combined concession/waiver appears in
the summary deduction. The complete saved breakdown is retained. A visible
"Less: Previously paid" line reconciles partial and repeated collections. The
summary remains: Less: Discount / Waived, Net payable, Payment received, Balance
due. Printed cumulative concessions are separate from discounts newly applied in
this transaction, preventing repeated collection-report totals.

Every new receipt retains its issued amounts and recorded balances on reprint,
including annual reprints. Later refunds/corrections are flagged; the original
amounts are not replaced by today's fees. Old snapshots are not silently repaired:
their existing numbers and data remain intact and a legacy limitation is disclosed.

Fee Collection's Student Fees menu includes **Print Fee Changes**. It saves and
prints a numbered acknowledgement with no payment, so a waiver clearing all dues
or an agreement applying to future months still has parent-visible documentation.
Its saved agreement notes do not contribute to billed amounts. It cannot receive
cash or apply credit. Normal Print Outstanding still requires a positive due.

Collection reads a fee/account review token before and after loading each child.
The transaction validates those tokens under the billing lock before changing any
fees, credit or payments. Bills validate gross fees and concessions as well as due.

Verification:

- `Tools/TestReceiptPaperTrail.ps1`: migration and financial sequences in an owned
  COPY_ONLY clone, including admission 17, partial payment, waiver, fee increase,
  credit, refund, concession reduction, immutable originals and stale review.
- ThemeSmokeChecks `--fee-documents`: individual and family RDLC PNG/PDF output,
  complete fee breakdowns, previous-paid deductions and zero-money acknowledgements.
- `Tools/ApplyReceiptPaperTrail.ps1 -Apply`: verified backup, schema 54 -> 55 in a
  transaction, hashes of all existing business columns before/after. No existing
  receipt, charge, allocation, payment, fee agreement or correction is rewritten.
