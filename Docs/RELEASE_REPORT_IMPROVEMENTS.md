# Release report improvements

Scope: existing report fixes and the Student Credit Balance Register, based on
`main` at `17fef27`. Monthly Fee Reconciliation and the Certificate Issue Register
are deferred. Financial writers, permissions, database versions, numbered
migrations, saved bills and receipt snapshots are unchanged.

## Operator behavior

- **Collections & Receipts:** Account Credit is available in Payment Method.
  CSV exports contain data rows only, without headings, subtotals or grand totals.
  PDF and print retain formatted groups. Screen pagination counts receipt entries,
  repeats headings on continuation pages and keeps a subtotal with the group's
  final receipt. Printed group headings repeat across pages; totals are calculated
  from original numeric receipt records, never from displayed subtotal rows.
- **Outstanding & Aging:** Overdue Only, Minimum Outstanding and optional inclusive
  Days Overdue From/To apply to student, charge and family rows. Blank day limits
  mean unrestricted; zero means zero days. Due-today and future charges remain at
  zero days and are excluded by Overdue Only. The Aging bucket view disables
  exact-row filters. When these filters are active, totals explicitly describe
  the displayed rows; the report does not pretend to recalculate overdue amounts
  charge-by-charge from aggregated student rows.
- **Open Statement:** available for a selected student/charge row with
  Fees.Statement.View permission. Superseded selections, changed filters or changed
  sessions cannot open an old response. The account uses the existing statement
  workflow and its permissions.
- **Class / Section A4:** still prints all unpaid charges for the location filters.
  It is disabled while exact-day/minimum-balance filters are applied; use Print
  for the filtered report instead.
- **Billing & Discounts:** Concession Summary groups assigned discounts by campus,
  class, fee type, frequency and concession. Monthly and yearly discount rates
  remain separate. These are assigned discounts per billing cycle, not discounts
  already posted to bills; no misleading mixed-cycle money grand total is shown.
- **Student Credit Balance Register:** appears in Reports > Finance for users with
  both Reports.View and Fees.View. It sums signed credit entries, showing current
  positive credit by student or family, with latest ledger activity. Location
  filters use the current placement, or latest placement when none is active.
  Unlinked students stay separate. Minimum credit applies per student before
  family grouping, as labelled. Credit held is not new income or unpaid school fees.
- Finance reports capture Report Generated time when the run starts. Outstanding
  reports also state their Aging Date. Printed time is identified separately.
  These are current-balance reports; no historical-balance feature has been added.

## Query and data safety

The new queries execute only permission checks and SELECT statements. Required
columns and the unique FamilyMembers student constraint are verified against the
DACPAC. The credit query checks Reports.View and Fees.View; the concession query
uses the existing Reports.View boundary. Enrollment history cannot multiply a
student's ledger balance. Explicit projection removes internal and admission-sort
columns before display/export. No messaging compatibility work is included.

## Focused verification

From the repository root:

```powershell
dotnet run --project Tests/ReportReadChecks/ReportReadChecks.csproj -c Release
dotnet run --project Tests/ReportRdlcChecks/ReportRdlcChecks.csproj -c Release
dotnet build Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -c Release -p:EnableWindowsTargeting=true
```

The first checks receipt grouping/pagination/export totals, overdue boundaries,
credit/family reconciliation, discount frequencies, SQL Server syntax and DACPAC
contracts. The second compiles the grouped RDLC in the reporting engine and
processes it to XML, asserting signed group and grand totals without a printer.

Windows-only functional check (no database connection):

```powershell
dotnet run --project Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -c Release -- --release-reports
```

This checks form choices and permission gates, CSV projection and a 1,200-receipt
multipage PDF. It writes a synthetic PDF under artifacts/release-reports.

Optional disposable SQL integration:

```powershell
dotnet run --project Tests/ReportReadChecks/ReportReadChecks.csproj -c Release -- --sql
```

Supply FMSC_REPORT_TEST_CONNECTION securely for a local SQL Server with Initial
Catalog=master and permission to create/drop a temporary test catalog. The runner
refuses non-local targets, creates its own uniquely named database and cleans it
up. It never points at a school catalog.

## Verification limits

Data/contract and reporting-engine XML checks were executed in Linux. Application
and focused Windows checks compile; the Windows checks and visual/printer review
were not executed here. SQL Server integration was not executed: the image registry
redirect to westcentralus.data.mcr.microsoft.com was denied by environment network
policy. The SQL fixture remains available above. These results are not installer
or school-deployment sign-off.
