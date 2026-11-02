# Bill and receipt scenario review pack

The **Fee document rendering** Windows workflow generates a review batch from
the checked-in `StudentBill`, `FamilyBill`, `StudentReceipt` and `FamilyReceipt`
templates with static fixture data. It never connects to a school database,
issues a real numbered document, posts a payment or sends pages to a printer.
The application templates, wording and fonts are unchanged by this review setup.

Download **REVIEW-ALL.pdf** from the workflow's Artifacts section
or the link in its run summary. It is retained for seven days and downloads
directly as a PDF, without a ZIP or extraction. It contains all generated pages in one
file, with a scenario bookmark for each document. Original page sizes are retained.
Duplicate individual PDFs, PNG previews, indexes and logs are excluded from the
download. The workflow still generates them internally for its rendering checks.

The pack reuses the existing document fixtures, including these cases:

| Area | Cases to review |
| --- | --- |
| Student/family bills | Ordinary bills, concessions, full waiver, partly paid scholarship, several billing months, separate family/standalone bills |
| Receipts | Cash, bank, partial payment, credit-only, cash with credit, exhausted credit, remaining advance, overpayment retained as advance |
| Families | Paid and unpaid siblings, child copies, six children, mixed campuses, several months, advance-only payment |
| Changes and history | Legacy documents, corrections/adjustments, later waivers, complete fee paper trail, repeated family visits, fee-change acknowledgements |
| Layout | Long student/parent names, long fee labels, multiple pages, A4 bill/receipt sheets and annual receipts |

Thermal bill samples use the production fitted-roll renderer used by bill
preview/export/printing. Their PNG uses the first fitted printer image. Receipt
and A4 samples use the production data presentation and content-sized RDLC
definitions. For multi-page documents the PNG shows **only page one**; inspect
the PDF to check all children, totals and continuation pages.

The review PDF is uploaded only after the rendering checks pass. Failed runs
report their diagnostics in the workflow log. A successful run verifies the
automated assertions and rendering, while layout appearance and physical printer
margins still need human review. The combined PDF is a review bundle, not a new
format for printing real bills or receipts.

To run the same pack on Windows without a database:

```powershell
dotnet run --project Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -c Release -- --fee-document-review-pack
```
