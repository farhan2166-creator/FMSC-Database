# Global record search

The dashboard text box retains text-only feature/page navigation. **Ctrl+K** and
its labelled button open a separate record search for student names, official or
pending admission references, saved bill/receipt numbers and payment references.
No form-navigation rows are mixed into that dialog.

The optional **Search in** selector starts at **All**. Choose **Students**,
**Bills**, or **Receipts** to narrow the same text without retyping it. Only
permitted types are offered; Receipts includes payment references. Filtering
applies in SQL before the 20-result limit, so other types cannot crowd out the
selected type. Changing the type cancels the previous lookup.

Results show record type, reference, student and distinguishing context. Exact
references rank ahead of partial matches. Student/father words can match in any
order; former/pending records remain searchable. Numeric fragments of at least
three digits may match document numbers. Results are limited to 20; type more
of the name/reference to narrow them. LIKE punctuation is literal input.

Queries start after a short typing delay. New input and closing cancel pending
work; late responses and responses belonging to another operator/endpoint are
ignored. Search errors remain inline. Blank or broad one-letter queries do not
read the database.

Student results require Students.View and open the selected student's existing
profile after workspace restoration. Financial results require Fees.View and
open a read-only saved-record page; permissions and actor are checked again.
The shell retains its existing busy/unsaved-work navigation guards. Opening the
student account retains that destination's independent access requirements.

Bills read the original number/date/XML snapshot; search never calls IssueFeeBill.
Receipts use the existing snapshot-aware reader and preview. A payment reference
may link to its original family receipt. An older standalone payment without a
saved collection document remains labelled as a payment entry, with account
history available separately; no replacement receipt or payment is manufactured.

This uses parameterized SELECT queries and a local SQL result-table variable on
the existing schema. No schema change, stored-procedure revision, migration or
role grant is needed. SQL independently enforces the requested lookup permissions.
The lookup contains no operational INSERT/UPDATE/DELETE or financial writer call.

`Tests/GlobalSearchChecks` covers input, query syntax/read-only contracts, actor,
permission and endpoint changes. `--sql` requires an explicitly supplied local
master connection, creates/drops only a GUID-named fixture database and uses
fictional records. The focused Windows workflow owns a separate LocalDB instance;
it never attaches school MDF/LDF files. Native `--global-search` checks the split
UI behavior, digit input, cancellation, stale results and destination permission
rechecks without SQL.
