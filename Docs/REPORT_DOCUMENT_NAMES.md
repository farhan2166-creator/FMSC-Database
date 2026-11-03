# Report preview and export names

The displayed document name is also used for the print job and the suggested
export filename. Exports append local date and time as `yyyy-MM-dd_HH-mm-ss`.
PDF, CSV and Excel exports from report grids use the displayed report title,
including the selected finance-report view, rather than an internal file prefix.

| Source/template | Displayed purpose |
| --- | --- |
| `StudentBill.rdlc` | Student Fee Bill; Student Fee Bills for a batch |
| `FamilyBill.rdlc` | Family Fee Bill; Family Fee Bills for a batch |
| `StudentReceipt.rdlc` | Student Payment Receipt; Student Payment Receipts for a batch |
| Annual use of `StudentReceipt.rdlc` | Annual Fee Receipts |
| Saved child's copy of a family receipt | Student Copy of Family Receipt |
| `FamilyReceipt.rdlc` | Family Payment Receipt; Family Payment Receipts for a batch |
| `FeeChanges.rdlc` | Fee Change Note; this does not represent new money received |
| `CreditBalanceRegister` reader | Student Advance Balance Register |
| Finance report grids | Report name followed by the selected view |
| Class sheet previews | Class Marks Sheet, Class Exam Attendance Sheet, Monthly Class Attendance Register |

A single fee document includes its saved document number and available student
identity. A batch includes its document count. Certificates include their number
and recipient when available. Each printed bill/receipt keeps its own singular
heading: a batch title describes the preview, not every individual sheet.

Template filenames, dataset names, snapshot table names and SQL identifiers stay
stable because saved document snapshots and existing readers depend on them.
`ReportDocumentNaming` is the shared mapping to user-facing names. This change
does not rename saved records or change their financial content.

Both preview paths use `ReportPreviewSearchBar`. Search indexes text from the
actual rendered PDF pages and navigates to matching page numbers. It does not
filter or remove documents from printing/export. Enter/F3 advances to the next
matching page; Shift+F3 goes back. Results wrap, and page setup invalidates the
cached page index. Search is prepared on first use and stays local to the preview.
