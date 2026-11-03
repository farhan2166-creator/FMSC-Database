# Direct Export → PDF samples

The **Direct PDF export samples** Windows workflow runs `--direct-pdf-review`:
the shared grid, student-identity and class-template fixtures, followed by a verified merge of
their exported PDFs. It starts on relevant main/PR changes or can be run manually.
Desktop CI runs the same fixture and publishes the same single-file download.

Download **REVIEW-ALL.pdf** from the workflow run or its summary link. It downloads
directly as a PDF, without a ZIP, individual duplicate files or PNG previews,
matching the fee-document review workflow. PDF bookmarks navigate sixteen report
scenarios. Every source page, paper size, orientation and page order is preserved;
wide reports retain all of their pages. The download is retained for seven days,
separately from installer downloads.

The focused workflow summary records the source commit, check outcome, scenario
and page counts, and combined PDF checksum. The source commit is also in the PDF's
Subject metadata. Detailed page ranges and individual checksums are generated in
`artifacts/direct-pdf-review/provenance.json` for local review and CI summary use;
that metadata file is not bundled with the PDF download.

The grid samples call the production `GridReportPreview.Build` and
`GridReportPreview.ExportPdf` methods used by the screens' direct **Export → PDF**
action. Class-report samples render the actual production RDLC templates with the
same Windows reporting engine used by print preview and its PDF export.
They do not exercise the Save File dialog, school SQL queries or every screen's
column/filter setup. They are not screenshots or recreated PDF designs.

The data and school/operator names are fictional. The review combines these
direct exports (individual files remain available when running locally):

- `collections-receipts-export.pdf`: the actual Whole School receipt preparation,
  including the grand total.
- `collections-by-campus-export.pdf`: two campus groups, headings, subtotals and
  the grand total, using the same grouping preparation as the screen.
- `outstanding-aging-export.pdf`: a compact student outstanding report.
- `wide-multipage-export.pdf`: a wide table with repeated identity columns.
- `empty-report-export.pdf`: the empty-result document layout.

It also includes a family receipt with grouped sibling names and the shared father
once, ordered class/section entries, actual payment methods and Payment Received
captions. Student List, Outstanding & Aging with the combined student
identity, Student Advance Balance Register — Families, Student Changes and Certificate Issue Register from the report identity
fixtures are included too. Those samples also use `GridReportPreview.ExportPdf`.

Class & Section Summary, Class Outstanding Charges, Class Marks Sheet, Class Exam
Attendance Sheet and Class Monthly Attendance Register are included from their
RDLC templates. Heading checks verify centered text, full report/copy widths and
the school heading's position in the rendered PDF. The current combined review
contains 16 scenarios and 22 pages.

The combined file is published only after all fixture suites and the merge checks
pass. A failed PDF check cannot publish a partial review or reuse a previous
review file. Desktop CI can retain a completed review if an unrelated later suite
fails; check the run's overall result separately. Use the workflow run for the
source version being reviewed.

Earlier files labelled `*-portable.pdf` were diagnostic recreations of the older
exporter, using substitute measurement and fonts. They are not evidence that the
current application's exported PDF matches a proposed visual design. Exact
comparison with a school's own export also needs its selected fields, data and
application version.
