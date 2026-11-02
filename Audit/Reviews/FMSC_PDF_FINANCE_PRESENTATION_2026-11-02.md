# PDF and financial RDLC presentation review — 2 November 2026

Source: current main `806440f` (PRs 70, 72 and 73 merged). Read-only review of
presentation; no application, financial rules or SQL changed. Earlier pending
saved-view work in the original checkout was preserved.

## Overall assessment

The information is useful, but most saved management PDFs are materially weaker
than their Print Preview equivalents. Standardize those two presentation routes
before adding decorations or changing the financial logic. Transaction bills and
receipts already have compact purpose-built layouts, but several captions should
make the accounting meaning easier for parents.

## Evidence and limits

No recent school-generated management PDFs were present in the reviewed checkout.
Older certificate-header PDF previews in a different checkout were not treated as
current financial output. Reviewed PDF export routing and all financial RDLC
expressions/styles. All 22 RDLC definitions were XML parsed.

Two fictional-data PDF/PNG samples were rendered and visually inspected using an
external copy of the current PdfTableExporter. MigraDoc content/flow/sizing logic
was retained; Windows font measurement was replaced with PDFsharp measurement,
fonts resolved to DejaVu Sans, and name-normalization stubbed for fictional names.
They establish a useful visual reproduction, **not exact native Windows output**.
Samples and limitations are in `/workspace/report-presentation-review-2026-11-02`.

Native reporting-engine PDF rendering was attempted with the actual
ClassOutstandingCharges.rdlc. It failed on missing Windows `usp10.dll` before PDF
output; no native RDLC PDF or physical-paper acceptance is claimed. SQL/data
correctness is outside this review, except to interpret the displayed amounts.

## Findings

### PP-01 — High: PDF export and Print Preview use different report designs

`V2Page.WritePdfExport` uses PdfTableExporter by default. That path receives the
report title and a text subtitle, not institutional identity or a typed summary.
It emits a normal title/paragraph/table. `GridReportPreview.Build` instead produces
school/title/scope, summary boxes, amount alignment, combined identity cells,
identified column bands, and a printed-by/page footer. Collections Receipts has
an explicit override using this richer RDLC design; its other views fall back to
the plain exporter.

This affects saved PDFs for ordinary billing, outstanding, credit, reconciliation,
student and certificate-register reports. The same rows/columns may be retained,
but users cannot assume Export PDF matches Print Preview visually.

Recommendation: use one report document definition for preview/print/PDF and keep
CSV as data only. Pass the same school identity, captured filters, totals and
record count. Do not re-query a different dataset when saving a previewed report.

### PP-02 — High: money alignment, separators and total emphasis are missing

PdfTableExporter converts non-date values with Convert.ToString (line 84), with
no explicit numeric alignment. Only column headings are bold; TOTAL/NET rows
follow the same styling as details. In the portable Billing Summary sample,
22400/4000/18400 are left-aligned ordinary cells although the subtitle displays
Rs. 22,400/Rs. 4,000/Rs. 18,400. The total is visually indistinguishable from a row.

Recommendation: right-align money, use consistent thousands separators while
preserving legitimate decimal precision, center appropriate counts/dates, keep
names left-aligned, and style authoritative total rows with bold text/top rule.
Show currency in amount headers or one clear report-wide legend. Do not format
all numeric IDs as money or round historical fractions for cosmetic consistency.

### PP-03 — High: wide financial exports become dense without identified bands

The plain exporter scales all selected columns into one portrait/landscape width.
If the minimum widths cannot fit, it divides the available width across columns;
it never creates the identified column bands already supported by GridReportColumns.
The current debt report has more columns after schema 70's due-status split.

The portable wide-debt sample wraps admission references, names, class labels and
dates across lines, and clips long unbroken heading text. It spans six pages for
65 fictional rows; exact native pagination may differ. The need for clearer
column allocation is also apparent in source: all fields share a fixed 9pt table
and no per-report financial priorities.

Recommendation: retain useful identity merges (student/reference, campus/class),
keep money columns wide enough for their formatted values, and split truly wide
reports into clearly identified continuation bands with repeated stable references.
Consider a concise debt-follow-up column preset rather than showing every charge
metric by default. Never silently omit selected columns to make the page fit.

### PP-04 — Medium: continuation pages and metadata need context

PdfTableExporter repeats table headings, which is good, but does not define page
headers or footers. Its title/scope appear once. Page 2 of the portable example
starts with column headings only: no report name, campus/year scope or page number.
It has no printed-by indicator or visible school identity.

It also appends a new Generated timestamp even when the captured subtitle already
contains Report generated. These may refer to different times. `table.Rows.Count`
includes appended TOTAL rows: the one-row billing sample plus TOTAL says two
records. This is an output-count problem, not a financial-total error.

Recommendation: repeat concise school/report/scope context; Page X of Y in the
footer; distinguish Report generated from Exported/Printed; use the caller's
logical detail count. Keep extensive filter explanations readable without one
long metadata sentence.

### PP-05 — Medium: richer preview summary recognition lags the new reports

`GridReportPreview` recognizes metrics using a hard-coded label set (line 89).
It does not include Overdue Amount, Due Today, Not Yet Due or Due Date Missing,
nor several reconciliation/register metrics. Those values can be retained in
the text subtitle but are treated as filters instead of readable summary boxes.

Recommendation: pass structured metrics from the report's totals table instead
of parsing a sentence. At minimum recognize the new fields and separate scope
from monetary totals consistently. Keep per-cycle concession rates separate;
do not invent a cross-frequency grand monetary total.

### PP-06 — Medium: receipt deductions and the word Total need clearer meaning

`FeeReceiptPresentation.Apply` intentionally combines discount/waiver with used
account credit for print, and subtracts credit from ReceiptNetPayable. Underlying
saved financial values remain separate. StudentReceipt/FamilyReceipt then show
Net payable, Payment received and a generic Total based on total settlement.
A reader can interpret Total as the fee total or as a duplicate payment line.

Illustrative simple case: fees Rs.1,000; discount Rs.100; credit used Rs.200;
cash received Rs.400; total settled Rs.600; balance Rs.300. Those are valid values,
but one combined Less: Discount / Waived / Credit and an unexplained Total make
the path harder to follow.

Recommendation for ordinary receipts: Fees total → Discount/waiver → Fees payable
→ Credit used → Money received → Total settled → Balance due. Keep previous
payments, advance retained, remaining credit and later refund/correction notices
only when relevant, with their existing meanings. This is a proposed presentation
change: do not rewrite snapshots, allocations or the existing owner-approved
full-fee/previously-paid paper trail. Keep payment date distinct from print time.

Family group headings Net total, Net payable, Net received and Net due also need
scope labels such as Family fees, Family payable, Money received and Family
balance due. Choose each label from the actual field's meaning. A full family
receipt and a child copy must remain clearly distinguished.

## Financial RDLC assessment

| Output | Assessment |
|---|---|
| Student / family bills | Plain fee names, fee amounts, concessions, already-settled deduction and TOTAL DUE form a useful sequence. Preserve the printed-on line and zero-due wording. Avoid turning bills into payment acknowledgements. |
| Student / family receipts | Actual fee lines, cash received, credit and balance data support the purpose. Improve PP-06 captions and consider right-aligning monetary figures; current amount cells are predominantly centered. Preserve originals and adjustment disclosures. |
| Fee-change note | Correctly says No payment was received. Keep that prominent so a waiver/change acknowledgement cannot be mistaken for a cash receipt. |
| Annual receipts | Intentionally reprints receipt blocks, not an annual ledger. Code flows them through three A4 columns (not the stale two-column screen description). Small receipt fonts are not enlarged by moving them to A4. Clarify the selector wording and test long receipts crossing columns. |
| Class Outstanding A4 | Useful readable management format with school/scope, grouped class-section totals, page numbers and remarks space. Its amount label already includes Rs. Keep it distinct from transaction receipts. Native print verification remains required. |
| Collections RDLC | Stronger design: grouped headings, signed money subtotals, distinct credit and page identification. Use as the management-PDF standard while retaining receipt identity/order. |
| Generic financial print preview | Uses readable 9pt body, repeated headings and column bands; stronger than generic PDF export. Expand the new-metric summary support (PP-05). |
| FeeAgingReport.rdlc | No production C# caller found. Current Outstanding printing uses shared grid/A4 routes. Treat this as a possible retained legacy template, not the active aging output; verify references before any deletion. |

## Size, legibility and verification plan

The transaction templates are thermal-width documents: page width 2.28in, body
2.05in, with Month/Fee/Amount columns .52/1.03/.50in. Ordinary fonts are about
6–7.5pt and institutional headings 9.5pt. This is a deliberate compact format,
not proof of an A4 layout fault. A4 annual flow keeps small blocks. Check a real
thermal printer before changing global font sizes; a blanket increase can break
long names, roll sizing and the existing receipt layout rules.

Representative acceptance cases: long fee/student/father names; five- and
seven-digit amounts; decimal historical values; cash-only/credit-only/mixed
payment; partial payment; prior-paid deduction; zero-due waiver acknowledgement;
advance retained/refund/correction; large family and child copy; 65-row and very
wide management reports; empty and single-row output; grayscale printing;
PDF/preview parity and page numbers. Use fictional data and the existing native
fixtures. No broad business-rule rewrite is needed for these improvements.

## Suggested implementation order

1. Unify management PDF with the existing RDLC preview design; keep CSV data-only.
2. Make monetary columns and authoritative totals clear, and fix page metadata/counts.
3. Add structured due-status/reconciliation summaries and sensible wide layouts.
4. Review the parent-facing receipt caption changes with the owner; preserve
   saved financial values, receipt identity and all original/correction history.
5. Verify thermal/A4 printing on Windows at the chosen release commit.
