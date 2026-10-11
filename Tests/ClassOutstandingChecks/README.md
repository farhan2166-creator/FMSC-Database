# Class / section A4 outstanding print

Open Reports → Outstanding & Aging and choose **Class / Section A4**. It reads current balances using the existing campus, academic-year, class and section filters. All is supported for each filter. The button is a dedicated print layout, independent of the selected Students/Charges/Aging/Families view; it does not replace those views or their grid, export and existing print options.

The full-width portrait A4 RDLC prints Sr. No., admission number, student name, father name, total outstanding and blank Remarks. Every campus/class/section group starts on a new page, with students sorted by student name then father name, numbering reset per group and one group subtotal. Larger groups continue with repeated school, placement and column headings. No students are dropped to force a group onto a single physical page. No groups with zero outstanding are generated.

The read-only query uses `GetFeeChargeBalance`, the same non-cancelled positive-balance eligibility and enrollment placement as `GetOutstandingAndAgingReport`. Completed signed allocations affect balances; reversed payments do not. All charge kinds are included, including charges not yet due, as in the existing report. Academic year filters the enrollments attached to the charges. Closed enrollments with debts remain visible. If a student's debts belong to several placements, that student can appear once in each placement group with the applicable subtotal, rather than assigning all historical debts to their current class. Neither the balance function nor stored procedures are changed; no migration is required.

Run `dotnet run --project Tests/ClassOutstandingChecks/ClassOutstandingChecks.csproj` for grouping, ordering, numbering, exact amounts, blank remarks, A4/group boundaries, repeated headers and actual RDLC HTML processing. Pass a filename to save the HTML for visual review.

For a disposable SQL database copy, generate the exact application query as a temporary procedure:

```
dotnet run --project Tests/ClassOutstandingChecks/ClassOutstandingChecks.csproj -- --write-sql /tmp/class-outstanding-query.sql
```

Run that generated file and `BalanceChecks.sql` in the same `sqlcmd -b` session. The script creates synthetic fixtures inside a transaction, verifies discounts, partial collections/refunds, reversed payments, cancelled charges and closed enrollments, and compares seven filter combinations against independent authoritative charge sums. All synthetic records roll back. Never use the live school database.

The cloud build, SQL checks and RDLC HTML checks do not run the Windows interface or physical printer. On Windows, verify the new button beside existing actions, one group per new page, repeated headings for a 61-student group, long student/father names, correctly formatted amounts, blank Remarks space and no trailing blank pages. Change filters while loading: the old scope must not open. Confirm empty results and school-profile failures remain inline, and that Students/Charges/Aging/Families, existing printing and CSV export still work.
