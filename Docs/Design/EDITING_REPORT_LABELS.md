# Editing printed report labels

Edit the source RDLC files in `Pages/Reports/Templates`, using the report designer or the XML editor. Each bill and receipt has its own template:

| Template | Printed document |
|---|---|
| `StudentBill.rdlc` | Individual fee bill |
| `FamilyBill.rdlc` | Combined family fee bill |
| `StudentReceipt.rdlc` | Individual payment receipt and annual receipt sheets |
| `FamilyReceipt.rdlc` | Combined family payment receipt |

Headings, field captions and fixed wording are plain text. For example:

```xml
<Textbox Name="BalanceLabel">
  <!-- Within Paragraphs / Paragraph / TextRuns / TextRun: -->
  <Value>Student outstanding</Value>
</Textbox>
```

You can change that wording without changing the amount beside it. The internal textbox name `BalanceLabel` and data field `BalanceRemaining` remain stable identifiers; they do not determine the printed caption.

Dates, page numbers, class details and similar mixed content use separate text runs: one for the editable words, another for the dynamic value. For example:

```xml
<TextRun><Value>Payment Date: </Value><!-- Style --></TextRun>
<TextRun><Value>=First(Fields!PaymentDateText.Value, "ReceiptPageGroup")</Value><!-- Style --></TextRun>
```

Edit the words in the first run. The value beginning with `=` belongs to the report formula. Keep field names, dataset/group names, number/date formatting and visibility expressions intact. Conditional certificate wording, optional information and student-count grammar still use expressions where the wording depends on the data. Most fixed captions in admission, class sheets, outstanding, class summary and aging reports have also been separated from the data expressions.

Receipt amount meanings:

| Value | Meaning |
|---|---|
| `FeesCovered` | Full fee amounts listed on this receipt; not the entire account debt |
| `CashReceived` | New payment received for this transaction, separate from previously held credit |
| `CreditApplied` | Existing account credit applied to fees |
| `StudentSettled` | Payment plus credit applied for this student |
| `StudentOutstanding` | This student's unpaid fees after the transaction, including fees beyond the printed lines |
| `BalanceRemaining` | Outstanding for the students included in the saved receipt snapshot; the family receipt uses the combined amount |
| `CreditRemaining` | Credit held for future use; not an amount owed to the school |
| `NetPaid` | Original settlement after recorded refund, correction and reversal effects |

The receipt label says **outstanding** for money owed to the school. Credit remains labelled separately. Historical receipts use their saved amounts; editing captions does not recalculate history.

Keep changed captions short enough for the narrow receipt width. Receipt paper sizing now measures every text run, including edited literals, instead of assuming particular English labels. Confirm the final preview after longer edits.

Build the application to copy source templates into its `Reports` output folder. Avoid editing only the generated copy: a later build can replace it. Run the offline checks after formula/layout edits:

```powershell
dotnet run --project Tests/ThemeSmokeChecks -- --fee-documents
dotnet run --project Tests/ThemeSmokeChecks -- --report-captions
```

The first command covers bills, receipts, partial payments, credit, historical receipts and A4 output. The second renders representative admission, class-sheet, outstanding, summary and aging reports using fixture data. Generated PDF/PNG samples appear under `artifacts/fee-documents` and `artifacts/report-captions`.
