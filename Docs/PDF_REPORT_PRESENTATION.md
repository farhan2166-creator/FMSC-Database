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

Owner decision — 2 November 2026: receipt and bill presentation is restored to
its finalized form before PR #74. Individual receipts use Net payable,
Payment received and Total, with the original print-only combined discount/credit
deduction. Family documents use GROUP SUMMARY and the finalized Net total,
Net discount, Net payable, Net received and Net due captions where applicable.
The extra Fees total and separate Credit used rows introduced by PR #74 are
removed, and the original Document Date caption is restored. The four financial
RDLC templates and receipt presentation helper match that prior revision.
Saved financial values, payment allocations and history are unchanged.
Management report PDF/preview improvements above remain in place.
The existing narrow paper width and automatic roll sizing are retained. The A4
annual receipt selector now correctly describes three columns. No schema version
change or SQL migration is required for these presentation changes.

## Verification

- Portable report checks cover authoritative summaries, money/count formatting,
  total/detail counts and new due-status metrics.
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
