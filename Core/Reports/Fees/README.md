# Fee report file map

| Document | Preview/binding | Report data | Editable RDLC | Dataset |
|---|---|---|---|---|
| Student bill | StudentBillReport.cs | StudentBillReport.Data.cs | StudentBill.rdlc | StudentBillData |
| Family bill | FamilyBillReport.cs | FamilyBillReport.Data.cs | FamilyBill.rdlc | FamilyBillData |
| Student receipt | StudentReceiptReport.cs | StudentReceiptReport.Data.cs | StudentReceipt.rdlc | StudentReceiptData |
| Family receipt | FamilyReceiptReport.cs | FamilyReceiptReport.Data.cs | FamilyReceipt.rdlc | FamilyReceiptData |

The RDLC files live in `Pages/Reports/Templates`. Edit those files to change
labels, columns, grouping or styling. Edit the matching `.Data.cs` to change
the values supplied to that document. Template and dataset constants are kept
together in each report's `.cs` file.

Shared files:

- `FeeBillPreview`: shared bill entry point for registration, admission, returns
  and the family counter. Registration/admission print this child; Fee Collection
  supplies its explicitly checked children. Unselected siblings are never added.
- `FeeBillSelection`: groups only the selected owing students by stored FamilyId.
  The existing collection, outstanding and account screens expose Bill grouping:
  Individual (default) or By family. The action opens one document preview directly.
- `FeeBillBatch`: retains confirmed results on partial failure. Only failures show
  a recovery message directing the operator to saved Issued Fee Bills; a connection
  failure can leave an unconfirmed final server commit.
- `FeeBillDocuments`: saves bill snapshots and numbers, and opens bill history.
- `FeeReceiptDocuments`: reads saved collections and chooses the receipt type.
- `FeeBillDataBuilder` / `FeeReceiptDataBuilder`: shared preparation for the
  paired document builders; these files do not define a visual layout.
- `FamilyFeeDocumentPresentation`: child calculations and the final net summary
  for continuous family printouts. The school heading prints once; child sections
  have no page breaks. Receipts retain children with payments, advances or debt;
  bills retain children with an unpaid balance. Original document records stay intact.
- `FeeReceiptDocuments` reads each child's original linked cash payment so an
  advance appears only under its actual owner, including when no fee was paid.
- `FeeReceiptQueries`: read-only identity, payment and allocation queries.
- `FeeDocumentData`: common column contract and month formatting. Its internal
  XML table name `FeeReceiptData` is retained so existing saved bill snapshots
  remain compatible with the database; each RDLC has its own dataset name above.
- `FeeDocumentLayout`: paper sizing; `FeeDocumentSheetLayout`: A4 sheet flow.
  Annual receipts use `StudentReceiptReport` and `StudentReceipt.rdlc` on a sheet.
- `FeeBillRollDocument` / `FeeBillRollPreview`: render each numbered thermal bill
  independently, fit its full text with a small bottom clearance using the actual
  PDF/EMF renderer, and show the differently sized slips in one preview. Export
  retains each PDF page's height; printing requests that slip's own paper height
  for every page, including a selected-page range. The longest bill never sets
  the paper length of shorter bills. A4 sheet printing remains on its existing path.
  The screen preview uses those same physical dimensions rather than EMF DPI
  metadata, which can describe a different scale. Single slips are centered on
  a contrasting canvas; surrounding screen space is not part of the bill paper.
- `AnnualReceiptPreview`: the shared annual receipt loader/preview, used directly
  from Student Account Statement and from the standalone year selector.
- `OutstandingBillsPreview`: reads selected accounts, checks readiness and prepares
  saved bills, then opens one combined preview without review/results dialogs.
  Missing-charge warnings appear only for actual return/placement readiness concerns.
  Individual Thermal/A4 behavior is unchanged. By family uses the existing family
  layout; every family or standalone bill starts a separate page, while siblings
  remain together. Saved bill numbers, dates and financial amounts stay intact.
  Numbered bills are saved even if the preview is closed without printing.
  The bulk Outstanding Bills page keeps placement, balance, paper, grouping,
  actions and the student grid in one compact card. Filters use up to five
  narrower columns (two rows on normal windows). It has no individual-student search; use Fee
  Collection for that. All loaded matches start selected, with optional row exclusions.
- `FeeReceiptCopyDialog` / `FeeReceiptChildCopy`: saved family receipts offer the
  original document or a clearly labelled child's share with the original receipt
  number/date. The copy uses saved membership, fees, plans and child cash/credit
  totals; it does not create payments or issue another receipt. Legacy incomplete
  snapshots retain their original document path.

The older admission-bill, fee-payment receipt and fee-reminder templates are retired. Build
and publish cleanup removes their stale output copies. Historical receipts and
saved bills render through the four current templates without changing their numbers.
