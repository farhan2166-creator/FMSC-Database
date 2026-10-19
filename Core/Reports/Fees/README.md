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

- `FeeBillPreview`: loads selected students' posted dues. Quick Registration
  uses the whole linked family; Fee Collection uses the selected children.
  A settled registration can therefore open a sibling's bill if only that sibling owes money.
- `FeeBillDocuments`: saves bill snapshots and numbers, and opens bill history.
- `FeeReceiptDocuments`: reads saved collections and chooses the receipt type.
- `FeeBillDataBuilder` / `FeeReceiptDataBuilder`: shared preparation for the
  paired document builders; these files do not define a visual layout.
- `FeeReceiptQueries`: read-only identity, payment and allocation queries.
- `FeeDocumentData`: common column contract and month formatting. Its internal
  XML table name `FeeReceiptData` is retained so existing saved bill snapshots
  remain compatible with the database; each RDLC has its own dataset name above.
- `FeeDocumentLayout`: paper sizing; `FeeDocumentSheetLayout`: A4 sheet flow.
  Annual receipts use `StudentReceiptReport` and `StudentReceipt.rdlc` on a sheet.

The older admission-bill and fee-payment receipt templates are retired. Build
and publish cleanup removes their stale output copies. Historical receipts and
saved bills render through the four current templates without changing their numbers.
