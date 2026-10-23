# Monthly reconciliation and certificate register

Follow-up to report improvements at `9fcc7fa`. These two reports use read-only
queries against the existing DACPAC contract and the existing grid, pagination,
CSV, PDF export and generated RDLC preview. No schema changes, migration, posting,
allocation, certificate creation/collection, receipt snapshot or installer changes
are required.

## Monthly Fee Reconciliation

- Requires Reports.View, matching existing finance reports. The query checks it
  on the server before returning rows.
- Selects non-cancelled charges, scoped by the charge's unique enrollment and its
  academic year/campus/class/section. It never joins all enrollments for a student.
- Month uses BillingMonth, or ChargeDate when BillingMonth is null. From/To
  include complete months. Groups use IDs internally so equal names in different
  locations do not merge. IDs are omitted from output.
- Preaggregates completed allocations per charge before joining. Money and
  credit applications are separate; negative refunds/corrections remain signed;
  reversed payments contribute nothing. Unallocated credit deposits are excluded.
- Uses the DACPAC GetFeeChargeBalance rule: max(net fee minus completed signed
  allocations, zero). Excess Settlement preserves the part clamped off by this
  rule, making the reconciliation identity explicit at charge and group levels.
- Reports current state of each billing-month cohort, including later payments.
  It does not reconstruct historical monthly closing balances or cash movement.
  Fully waived fees remain charge entries with zero net/outstanding.

## Certificate Issue Register

- Requires Reports.View plus the same source permission as the existing record
  lists: Certificates.Create for general/student/staff records; Students.Leave
  for leaving records. Limited users query only their source set. Every included
  permission is rechecked by RequirePermission on the server.
- UNION ALL retains all recorded issues, including repeated issues on one
  certificate. Character, Date of Birth and Leaving sources have issue tables.
  Left joins retain a saved record without any issue, labelled Saved record only.
- Regular Student, Sports, No Objection, Provisional, Regular Teacher/Lecturer and
  Bank Account Opening have saved certificate records without issue/collection
  logs. They are never counted as confirmed issues or awaiting collection.
- School/College Leaving and original/duplicate issue types remain distinguishable.
  Missing certificate numbers use an explicit Record # reference, never a newly
  invented certificate number.
- Date filters use the recorded issue timestamp, falling back to record creation
  for saved-only entries. Dates are inclusive; name/reference search is literal
  CHARINDEX with parameters, not user-composed SQL or LIKE wildcards.
- Uses saved recipient names; the student reference is the current admission/QR
  number. Staff references are labelled separately. No bank income, CNIC, remarks,
  internal keys, or other certificate contents are exported.
- Timestamps are formatted once before binding so time survives CSV/PDF/RDLC.
  Filter/session signatures prevent stale results from being printed/exported.
  Printing the register does not print or issue individual certificates.

## Focused verification

Cloud verification: **281 focused checks passed**. The application and focused
Windows test project compile in Release with **0 warnings and 0 errors**.
The Windows runner is compiled, not executed in this Linux environment.

Portable checks (SQL Server grammar, DACPAC columns including computed columns,
read-only statement whitelist, grouping, reconciliation, empty results, access
source selection, issue counts and date/time projection):

```sh
dotnet run --project Tests/ReportReadChecks/ReportReadChecks.csproj -c Release
```

Optional integration coverage is part of that runner's `--sql` mode. It creates
only a uniquely named disposable synthetic catalog on a local SQL Server, then
drops it; it refuses a school catalog/non-local server. It covers cancelled fees,
signed refunds/corrections, reversals, credit applications, later settlements,
null billing months, scoped enrollments, all ten certificate types, repeated
issues, collection/date/search filters and source-specific permission denials.
Supply FMSC_REPORT_TEST_CONNECTION for local master only. No school data is used.

Windows-only focused controls and actual RDLC/PDF rendering with synthetic data:

```powershell
dotnet run --project Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -c Release -- --new-reports
```

It writes synthetic PDFs/CSVs to artifacts/new-reports. Run on a disposable
Windows test profile. This is separate from the broad theme/regression suite.
Linux can compile these checks, but cannot run WinForms or validate the Windows
print layout. Live SQL integration remains unexecuted here because the SQL Server
container image download was blocked by the environment network policy. Do not
treat compilation/static checks as Windows visual or live-database signoff.
