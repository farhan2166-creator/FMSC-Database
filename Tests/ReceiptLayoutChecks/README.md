# Annual receipt layout checks

Run `dotnet run --project Tests/ReceiptLayoutChecks/ReceiptLayoutChecks.csproj`.
The checks process the real shared receipt template through the annual layout helper, then render short, mixed-length and oversized receipts with RDLC HTML. They check receipt grouping, headers, every charge line, three-column A4 and preservation of the original thermal template.

The annual layout wraps each receipt in a rectangle with KeepTogether enabled inside a separate receipt group. Receipts move intact to the next column when they fit a full column; a receipt taller than the printable column must continue. The outer table can paginate between receipt blocks. No accounting or data selection rules change.

Windows manual verification remains required for hard-page pagination: preview and print a set of receipts that nearly fills a column, followed by a long receipt that fits a fresh column. Confirm its header, lines, total and footer move together. Check three columns per A4 page, several pages, long student/father/school names and a receipt taller than a full column without missing lines. Compare a normal thermal receipt and family receipts to confirm their paper dimensions and separate pages stay unchanged. Linux HTML processing does not verify column boundaries in PDF/EMF or physical printing.
