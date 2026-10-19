# Bills and receipts

The four current fee documents have matching C# files in `Core/Reports/Fees`
and editable layouts in `Pages/Reports/Templates`:

| Preview and binding | Data builder | RDLC | RDLC dataset |
|---|---|---|---|
| StudentBillReport.cs | StudentBillReport.Data.cs | StudentBill.rdlc | StudentBillData |
| FamilyBillReport.cs | FamilyBillReport.Data.cs | FamilyBill.rdlc | FamilyBillData |
| StudentReceiptReport.cs | StudentReceiptReport.Data.cs | StudentReceipt.rdlc | StudentReceiptData |
| FamilyReceiptReport.cs | FamilyReceiptReport.Data.cs | FamilyReceipt.rdlc | FamilyReceiptData |

Each report class declares its template and dataset names together. Annual receipt
sheets also use StudentReceipt.rdlc. The retired admission-bill and fee-payment
templates have been removed, including stale copies in build/publish output.
C# supplies report data and opens the preview; labels, sections, columns, totals
and grouping remain editable in RDLC. See [the report-folder guide](../Core/Reports/Fees/README.md).

Bills and receipts use the approved Month / Fee / Rs. table:
a shaded header with a top rule, followed by borderless detail rows.
Family bills repeat a full-width
student heading, grouped by admission/registration reference, above each child's
fee rows. Each fee retains its own period, including one-time fees, while the
printed month appears only on the first fee row for that month and student.
Family receipts use the same full-width student headings and three fee columns,
followed by each child's settlement and outstanding figures. Months appear once
per month and child. Bills use Bill No. and TOTAL DUE; receipts retain Receipt No.
and their payment summaries. Receipt templates remain independent.
The focused fee-document rendering workflow exports
fictional PNG/PDF previews without connecting to a database.
| Document | Use |
|---|---|
| Student bill | Posted outstanding charges for one student |
| Family bill | Posted outstanding charges for the selected siblings together |
| Student receipt | One saved collection covering one student |
| Family receipt | One saved collection covering the selected siblings together |

Titles are Student Fee Bill, Family Fee Bill, Student Payment Receipt and
Family Payment Receipt. Bill summaries use Discount included and TOTAL DUE.
Receipt summaries use Charges covered, Discount, Amount received, Credit used,
TOTAL SETTLED, Outstanding balance and Credit remaining, hiding inapplicable rows.

New numbers are B-2026-00001, FB-2026-00001, R-2026-00001 and FR-2026-00001.
Each prefix has its own calendar-year sequence. The year follows the issue date
for bills and the transaction date for receipts. Credit-only settlements use
normal R/FR receipts. Existing numbers remain unchanged. Internal credit movement
references remain internal; they do not replace the public collection number.

Migration 24 stores each issued bill's report snapshot, source charge references,
number and date. An unchanged bill reuses that record; changed charges or amounts
create a new bill. Reprints from Fee Collection → Student Fees → Issued Fee Bills
read the saved snapshot, even after payment changes the current balance.
The database checks posted balances again before issuing a new bill. Numbering
and bill creation are atomic and do not post fees or allocate money. Issued
numbers are retained; a failed transaction never creates an issued document.

Printing a bill reads posted charges without posting fees, reserving credit or
recording money received. Registration and Fee Collection share the same bill
preview. Returning admission uses the same bill template.

A collection receipt identifies the money received, existing credit used,
discount, total settled, outstanding balance and credit remaining. Fee Collection
accepts no more than the payable balance after discount and
selected credit. Intentional deposits must use Student Credit. Historical
receipts retain their earlier excess-credit details. Credit is never counted as new
cash received. A family document lists each child, fee periods, individual totals
and one combined summary; it does not force a separate receipt page per child.

`CollectionDiscountWriter` records the payment, credit movements, receipt links
and receipt snapshot in one transaction. A receipt-validation failure rolls back
the collection. New receipts use the R/FR sequences above; a credit-only
collection receives a normal printable receipt number without creating a cash
payment. Internal credit movement references remain unchanged.

Print Receipt loads the selected student's latest printable receipt across
placements, even when nothing is due or no payment was made in this session.
Selecting another student clears the previous receipt immediately. A new
collection makes its receipt available for printing.
Print Receipt and Recent Payments use the same saved document. Reprinting reads
the issue snapshot and does not apply credit again. Later refunds, corrections or
reversals are identified on the original receipt; Payment History reflects the
updated net settlement. Existing cash receipts retain their original numbers and
use the same combined family layout when reprinted. Standalone historical or automatic
credit applications can print individually with a clear current-balance notice.

Migration 23 creates the receipt records. Earlier credit applications have no
reliable batch identifier: they must not be grouped merely because timestamps
match. A historical receipt can be issued only for explicitly identified payments
whose allocation totals and current balances reconcile; it does not post or
settle those payments again.
