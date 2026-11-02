# Shared report PDF and print presentation — 2 November 2026

Management PDF export now uses the same GridReportPreview RDLC definition and
projected data as Print Preview. The shared document includes the school name,
report title, captured filters, authoritative summary cards, a logical detail-row
count, monetary formatting, repeating headings, printed-by information and Page X
of Y. Currency columns align right, appropriate counts/dates center, names remain
left aligned, and total rows have bold text and a top rule. Very wide reports use
identified column bands with repeated student/reference fields instead of tiny
text. Every selected source field remains in the document.

Canonical finance and certificate-register reports supply their captured totals
as structured metrics. No totals are re-read from SQL or obtained by adding
subtotal rows. Existing callers can still use the subtitle fallback, including
schema-70 due-status and reconciliation metrics. Report generation remains the
captured run timestamp; the footer shows printing/export time. Export does not
append another ambiguous Generated timestamp or count an appended TOTAL as a
record. Reports and columns changed during file selection/profile loading are
rejected before export. A saved PDF now requires the same School Profile identity
as printing. PDF replacement remains atomic and validated. CSV keeps its existing
raw-data route and selected columns; no layout-derived fields are added to it.

## Receipt and bill presentation

This supersedes the former print-only combined discount/credit deduction.
Individual and family child slips show Fees total, Discount/Waived, Fees payable,
Money received and a separate Credit used line when applicable. When credit is
used, Total settled makes the combined money/credit settlement explicit. Ordinary
money-only receipts avoid a redundant total line. Legacy adjustments retain a
Net settled caption and their original/refund/correction/reversal disclosures.

The saved discount, payable, cash, credit, settlement and balance remain unchanged.
For example, fees 1,000 less discount 100 means fees payable 900; credit 200 plus
money 400 settles 600, leaving 300. Credit does not reduce the fee's price.
Previously-paid deductions and advance retained/remaining-credit rows remain.
Family summaries use explicit family-scope labels. Monetary figures share a right
edge. Receipt footers say Payment Date; bill printed-on timestamps remain.
No snapshot, receipt identity, allocation or financial history is rewritten.

The existing narrow paper width and automatic roll sizing are retained. The A4
annual receipt selector now correctly describes three columns. No schema version
change or SQL migration is required for these presentation changes.

## Verification

- Portable report checks cover authoritative summaries, money/count formatting,
  total/detail counts, new due-status metrics, and discount/credit separation.
- Reporting-engine checks compile/process the actual StudentBill, FamilyBill,
  StudentReceipt, FamilyReceipt, FeeChanges and ClassOutstandingCharges templates
  with fictional data to XML; they also exercise signed collection subtotals.
- Native fixtures compile and cover shared preview/export rendering, summary
  cards, decimal precision, selected-field preservation, wide-column pagination,
  current receipt reconciliation and original-snapshot immutability.
- Desktop CI runs --grid-pdf alongside the existing --fee-documents suite.

Linux cannot execute WinForms, EMF/thermal printing or the Windows usp10.dll PDF
text renderer. Native PDF/thermal/A4 acceptance remains a Windows check; successful
XML processing is not physical-print certification. Earlier portable visual
reproductions remain labelled diagnostic, not current native output.
