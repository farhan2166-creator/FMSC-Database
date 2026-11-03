# Shared report PDF and print presentation — 2 November 2026

Management PDF export now uses the same GridReportPreview RDLC definition and
projected data as Print Preview. The shared document includes the school name,
report title, captured filters, authoritative summary cards, a logical detail-row
count, monetary formatting, repeating headings, printed-by information and Page X
of Y. Owner-approved styling — 2 November 2026: use black text and rules on white
paper, without colored fills or alternating shading. Center school/report titles,
filter context, summary labels/values and all table headings. Amounts, counts,
dates, references and short placement fields center; names and descriptions stay
left aligned. Receipt group headings and monetary subtotals follow the same
alignment. Total rows have bold text and a top rule. Summary rows reserve enough
height for wrapped labels and values. Very wide reports use
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

## Natural report columns — 3 November 2026

The shared print/PDF layout now orders identity, current placement, historical
admission/leaving or change details, contacts, dates, counts and financial amounts.
Selected fields are grouped into labelled cells where they belong together:

- Student/admission/father: `163 - Kausar d/o Ali`, with selected DOB and student
  status on separate labelled lines. The recorded gender determines s/o or d/o;
  unknown gender uses `child of`. Missing parents leave admission and name only.
- Campus, current class/section and academic year share a placement column.
  Admission and leaving classes remain distinct from current placement. Change
  reports label previous/new class and status as From/To, even when values match.
- Certificate registers group certificate, recipient/reference, recording and
  collection details. Collection dates and collectors remain identifiable.
- Receipt/date, payment method/receiver, family/members and oldest-due/aging
  details share cells. Counts and financial measures retain separate columns;
  their values and calculations are unchanged.

Column selection is respected. CSV and Excel retain separate raw fields and the
selected column order. Hidden parent/gender metadata supports Outstanding & Aging
without exposing extra export columns; the advance balance reader carries recorded
gender through its student projection. No student or financial records are changed.
Migration `74.sql` updates the outstanding reader; app, installer and current SQL
fixture schema pins were 74 for that change. Its procedure body differs from migration 70 only in
the parent/gender metadata additions.

Native verification: `--report-student-identity` renders Student List, Outstanding
& Aging, Student Changes and Certificate Issue Register samples to PNG and PDF in
`artifacts/report-student-identity`. It checks selected-field coverage, identity
fallbacks, column order, unchanged numeric/export data and one-page sample output.
`--grid-pdf` checks receipt totals, selected fields and wide-report pagination.
Portable report checks parse migration 74 and compare its financial logic with 70.

Local SQL verification — 3 November 2026: the standalone applier accepted the
73 → 74 plan after the app/installer schema pins were aligned. It created and
verified `FMSC_DB_BeforeMigration_v73_to_v74_20261103_123458.bak`, then applied 74
inside its normal transaction wrapper. The expected installed MDF identity and
final schema 74 were checked. Hashes of all eight Students, Charges, Families and
Aging detail/summary result sets matched their pre-migration hashes after removing
only the new private metadata columns; Students and Charges expose both fields.
This verifies the local upgrade and report reader, not a release-package install.

## Collections receipt details — 3 November 2026

Receipt rows keep multiple family members' names together and append their shared
father once: `Hadia, Hamid, Kausar s/d/o Ali`. All-male or all-female groups use s/o
or d/o. Different recorded fathers are kept with their own children. Saved receipt
names and fathers take precedence over current student details. The class/section
column lists one entry per student in the same order, including repeated classes;
historical receipt lines take precedence over later placements. Campus remains
available for filters/grouping but is omitted from the receipt table.

Migration 75 returns private snapshot/roster metadata and the actual distinct
payment methods (for example `Cash + Account Credit`) instead of Mixed. Display,
summary, print and export captions use Payment Received. Existing raw column keys
remain compatible with saved layouts. Financial aggregation, receipt identity,
permissions, filters and summary calculations remain unchanged. The app, installer
and current SQL fixture contexts require schema 75.

Native `--collection-receipt-presentation` checks cover sibling formatting,
historical class order, repeated classes, missing data, the real column catalogue,
export metadata exclusion, unchanged amounts and rendered PDF text. Its fictional
sample joins the single CI review PDF. A live rollback-only migration check
compared all 32 detail/summary result sets across views, method filters and campus
scopes; column types and values matched apart from method text/private metadata.
The applier subsequently created and verified
`FMSC_DB_BeforeMigration_v74_to_v75_20261103_133143.bak`, applied 75 in its normal
transaction and passed the same 32 comparisons against the upgraded database.

## Family advance-balance identity — 3 November 2026

The Families view now retains admission references, names and parent details when
grouping advance balances. One member reads `163 - Kausar d/o Ali`; siblings keep
their names/references together and append a shared father once. Student reports,
receipt families and advance families share the same plain-text formatter.
Empty family codes fall back to public admission references. Current/latest
class/section entries follow the member order, and actual campus names replace
the generic Multiple campuses label. These placement fields share a printed cell.
The source query, ledger sums, student counts, latest activity and per-student
minimum filtering are unchanged. No migration is required.

The native identity fixture renders the family report in the single CI review PDF
and verifies parent formatting, fallback references, ordered placements, balances,
counts, minimum filtering and source immutability. Portable credit projection
checks cover family and standalone groups and reconcile student/family totals.

## Verification

Legacy class templates — 3 November 2026: Class & Section Summary centers its
school, title, scope, explanatory note and combined Prepared line. Class
Outstanding Charges combines its placement and academic-year/generated context
into centered full-width lines. Marks and exam-attendance sheets center class,
exam, paper and date context within each of their two copies. Existing monthly
attendance headings remain centered; its Student Name column heading now matches
the other centered headings. Data rows, totals, paper sizes and margins are
preserved. `--report-heading-alignment` renders all five templates to PNG/PDF and
checks paragraph alignment, report/copy widths, school text geometry and complete
page rendering. These samples are also in the single CI review PDF. No migration
is needed.

Billing Summary async regression — 3 November 2026: the shared finance loader
captures the total-row setting on the UI thread before awaiting data. The previous
worker-thread continuation evaluated Billing Summary's view-dependent property and
read a WinForms dropdown, causing the logged cross-thread report failure. Native
`--finance-report-async` runs the actual Billing Summary page with a delayed
fictional response, verifies the headings and authoritative total, and checks that
print/export become available without a failure dialog. No SQL change is required.

- Portable report checks cover authoritative summaries, money/count formatting,
  total/detail counts and new due-status metrics.
- Reporting-engine checks compile/process the actual StudentBill, FamilyBill,
  StudentReceipt, FamilyReceipt, FeeChanges and ClassOutstandingCharges templates
  with fictional data to XML; they also exercise signed collection subtotals.
- Native fixtures compile and cover shared preview/export rendering, summary
  cards, decimal precision, selected-field preservation, wide-column pagination,
  current receipt reconciliation and original-snapshot immutability.
- Desktop CI runs --direct-pdf-review (grid PDF and identity fixtures, plus a
  bookmarked combined PDF) alongside the existing --fee-documents suite.
- Desktop CI retains the resulting direct-export PDFs for review. See
  [direct export samples](DIRECT_PDF_EXPORT_SAMPLES.md) for artifact access,
  fictional data and verification limits.

Linux cannot execute WinForms, EMF/thermal printing or the Windows usp10.dll PDF
text renderer. Native PDF/thermal/A4 acceptance remains a Windows check; successful
XML processing is not physical-print certification. Earlier portable visual
reproductions remain labelled diagnostic, not current native output.
