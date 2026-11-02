# Saved report views

Uses the existing ReportLayoutStore and ReportSavedView format in
`%AppData%/FMSC_SMS/ReportLayouts/user-{UserId}.json`. There is no new database
table, migration or permission grant. Views remain local to the signed-in user
and computer. Existing layouts, last-location preferences and student views are
preserved. Normal report queries still enforce their existing permissions.

## Coverage

| Report | Saved settings |
| --- | --- |
| Collections & Receipts | View, grouping, dates, location, payment method, columns/order |
| Outstanding & Aging | View, academic year/location, overdue/minimum balance/day limits, columns/order |
| Billing & Discounts | View, grouping, dates/location, columns/order |
| Credit Balance Register | Student/family view, location, minimum per-student credit, columns/order |
| Monthly Fee Reconciliation | Grouping, whole billing months, academic year/location, columns/order |
| Certificate Issue Register | Type, recipient, recorded dates, entry, collection, search, columns/order |
| Class Marks & Attendance Sheets | Scope, sheet type, attendance month, exam/subject/date/teacher text, search |
| Student Reports | Existing modes/grouping/split/sort/filters/columns, plus rename and relative date periods |

Selected result rows, class-sheet Include checkboxes, individual student account
selections, printed documents and transaction drafts are not stored in views.
Statements and financial transaction screens keep their current behavior.

Finance and certificate views also save **Order by**. Applying an older view
without that setting selects Default. Choices depend on the report view and
grouping; an unavailable order is refused rather than silently substituted.
Run Report applies the chosen order before pagination, print and export. Receipt
details sort within their existing groups, with headings and totals retained.

## Operator flow

1. Configure a report, run it and choose Columns where available. Dynamic report
   columns become available after a successful run and are specific to the
   report view/grouping, so a receipt layout does not hide summary fields.
2. Save View with a descriptive name. Saving to an existing name asks before
   replacing it. Names are limited to 60 characters.
3. Select a name and Apply View. This restores settings and clears old results;
   review the scope and press Run Report / Load Students. It does not run SQL,
   post fees, choose students, print or export automatically.
4. Rename View preserves the saved state. A conflicting name is refused, not
   overwritten. Delete View asks before deleting only that local preset.

Examples:

- Collections: Receipts / Cash / Today / Campus Boys, save as "Today's cash".
- Outstanding: Students / Campus Boys / overdue only / minimum Rs. 5,000,
  save as "Boys fee follow-up". This saves the report selection, not a follow-up log.
- Billing: Billing Summary / Campus / This month, save as "Monthly campus billing".

Dates can be fixed, unrestricted, today, this month, previous month or this
calendar year. Relative periods are resolved again on Apply and Run, including
when a page remains open overnight. Manual date edits select Fixed dates; optional
unchecked dates are retained for fixed-date views. Whole-month reconciliation
continues normalizing its dates to month boundaries. Calendar year is not the
academic-year filter. Attendance sheets retain their explicit attendance month.

Student Reports retains its ready-made views and automatic load when a view is
chosen. Earlier student views without a datePeriod key use their original
limit/from/to values. A single Date period selector replaces the separate limit
checkbox in the same filter slot, retaining the existing filter area footprint.

## Safeguards and verification

- New report filter keys are explicit. Dropdown choices are restored by value or
  text, never by a positional index that could refer to another option later.
- Applying is unavailable until lookups load and during the report's own read.
  It is also refused after the signed-in user changes.
- A missing saved lookup or permission-limited option refuses the view and rolls
  back earlier changes. It never substitutes All for an unavailable saved campus.
  Student Reports also checks saved locations before its existing automatic load.
- Column catalogues reuse the shared raw-field exclusion. Internal IDs and sort
  helpers cannot be selected. Required receipt/total references stay visible;
  receipt grouping metadata remains attached to print/export projections.
- Save/rename/delete use the existing atomic settings writer and damaged-file
  recovery. Failures retain the original file and display a warning.
- No financial queries/writers, certificate issuance, saved snapshots or RDLC
  templates are changed. Selected columns feed the established grid/print/export.

Focused portable checks:

Local verification after integration with current report/PDF improvements:
**324 focused checks passed**. The application and native
Windows test project compile in Release with **0 warnings and 0 errors**.
The WinForms checks are compiled, not executed in the Linux environment.
Portable persistence checks use the settings writer's injected publisher because
the production Windows session mutex is unavailable here. Windows checks retain
the production publisher. Existing financial readers, PDF summaries and date
validation for the separate concession as-of filter are preserved.

```sh
dotnet run --project Tests/ReportReadChecks/ReportReadChecks.csproj -c Release
```

Focused native Windows checks, without a database connection:

```powershell
dotnet run --project Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -c Release -- --saved-report-views
```

The Windows runner exercises dependent choices, numeric/checkbox/optional dates,
unavailable-choice rollback, rolling dates and manual edits, busy/session gates,
required/raw columns, receipt metadata, and the report integrations. It uses a
random synthetic user settings file and removes that file afterward. Run it on
a disposable Windows test profile. Linux can compile, but cannot execute the
WinForms runner or confirm visual wrapping, scaling and physical print layout.
