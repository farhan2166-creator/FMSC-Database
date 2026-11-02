# Direct Export → PDF samples

The **Direct PDF export samples** Windows workflow runs only the existing
`--grid-pdf` fixture. It starts on this verification branch's push and on relevant
main/PR changes, or can be started manually. The desktop CI workflow also retains
its PDF output. Both use the artifact name `fmsc-direct-pdf-exports-<run ID>`.
Open the relevant workflow run in GitHub Actions and download that artifact;
the focused workflow also links it in the run summary and records the source
commit, checks outcome and PDF checksums in `provenance.json`.
Artifacts are retained for seven days. This is separate from installer downloads.

These samples call the production `GridReportPreview.Build` and
`GridReportPreview.ExportPdf` methods used by the screens' direct **Export → PDF**
action. They render on Windows with the production reporting engine and font settings.
They do not exercise the Save File dialog, school SQL queries or every screen's
column/filter setup. They are not screenshots or recreated PDF designs.

The data and school/operator names are fictional. Each PDF ends in `-export.pdf`:

- `collections-receipts-export.pdf`: the actual Whole School receipt preparation,
  including the grand total.
- `collections-by-campus-export.pdf`: two campus groups, headings, subtotals and
  the grand total, using the same grouping preparation as the screen.
- `outstanding-aging-export.pdf`: a compact student outstanding report.
- `wide-multipage-export.pdf`: a wide table with repeated identity columns.
- `empty-report-export.pdf`: the empty-result document layout.

The artifact is uploaded even when a later regression check fails, provided at
least one export PDF was generated. Only generated files are included; a failed
PDF test may leave an incomplete set. Check the workflow's `--grid-pdf` result
before treating the set as verified output. The commit is recorded by the
workflow run; use a run for the version being reviewed.

Earlier files labelled `*-portable.pdf` were diagnostic recreations of the older
exporter, using substitute measurement and fonts. They are not evidence that the
current application's exported PDF matches a proposed visual design. Exact
comparison with a school's own export also needs its selected fields, data and
application version.
