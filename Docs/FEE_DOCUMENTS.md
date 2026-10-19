# Bills and receipts

Each document has its own editable RDLC layout, based on the established
`FeePaymentReceipt.rdlc` design: `StudentBill.rdlc`, `FamilyBill.rdlc`,
`StudentReceipt.rdlc` and `FamilyReceipt.rdlc`. C# supplies report data and opens
the existing preview. The shared helper sizes the roll-paper length; document
labels, sections, columns, totals and grouping live in the RDLC files.

Bills use a Month / Fee / Due table styled like the finalized individual receipt:
a shaded header with a top rule, followed by borderless detail rows.
Family bills repeat a full-width
student heading, grouped by admission/registration reference, above each child's
fee rows. Each fee retains its own period, including one-time fees, while the
printed month appears only on the first fee row for that month and student.
Family receipts retain separate student and month headings above borderless fee
and settlement columns. The older admission and annual receipt templates also
contain editable Month / Fee / Amount columns directly in RDLC. Receipt
templates remain independent. The focused Fee document rendering workflow exports
fictional PNG/PDF previews without connecting to a database.
| Document | Use |
|---|---|
| Student bill | Posted outstanding charges for one student |
| Family bill | Posted outstanding charges for the selected siblings together |
| Student receipt | One saved collection covering one student |
| Family receipt | One saved collection covering the selected siblings together |

Printing a bill reads posted charges without posting fees, reserving credit or
recording money received. Registration and Fee Collection share the same bill
preview. Returning admission uses the same bill template.

A collection receipt identifies the money received, existing credit used,
discount, total settled, outstanding balance and credit remaining. Money retained
as additional credit is identified separately. Credit is never counted as new
cash received. A family document lists each child, fee periods, individual totals
and one combined summary; it does not force a separate receipt page per child.

`CollectionDiscountWriter` records the payment, credit movements, receipt links
and receipt snapshot in one transaction. A receipt-validation failure rolls back
the collection. Receipt numbers follow the existing receipt sequence; a credit-only
collection receives a normal printable receipt number without creating a cash
payment. Internal credit movement references remain unchanged.

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
