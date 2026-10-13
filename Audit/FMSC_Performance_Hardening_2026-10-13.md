# Existing workflow performance hardening — 2026-10-13

This pass improves shared loading paths and existing admissions/fee collection.
It adds no product module and changes no database schema or financial transaction.

## Changes

- Verified read-only previews and consistency checks no longer invalidate lookup
  and student search caches. Writes and unknown operations still invalidate them.
- Cached lookup copies are made outside the shared lock, with generation checks
  before and after copying. Callers still receive independently editable tables;
  concurrent saves cannot publish an older generation as the refreshed result.
- Async result loading reuses one transfer buffer per result and uses bulk-load
  mode. Result column types, nulls, row states, cancellation and multiple results
  retain the existing contract.
- Repeated admission-number sorting only updates derived keys when values change,
  avoiding unnecessary row-change events and index maintenance.
- Fee collection uses the existing user-scoped fee-type catalog cache rather than
  querying its order separately for every student/sibling. The permission remains
  `Fees.View`. Outstanding charges and assignment details still load fresh.
- Independent fee collection reads and admission configuration/catalog reads run
  concurrently, without modifying save order or transaction rules.

## Validation

- Windows-targeted application cross-build: zero warnings and errors.
- Existing portable audit checks: 75 money/TVP/quotation checks and 102 role,
  literal search and working-area checks pass.
- `Tests/PerformanceChecks` exercises production loader, cache and sorting code:
  row integrity across 20,000 rows, nulls, row states, empty/multiple results,
  cancellation, independent cache copies, user separation, concurrent load sharing,
  write invalidation, invalidation during loading, failure/retry and sort refresh.
  SQL dependency stubs fail on accidental database access; these are SQL-free checks.
- One warmed synthetic loader comparison (five loads of 20,000 four-column rows)
  allocated 100,299 KiB before and 94,828 KiB after, about 5.5% less allocation.
  Elapsed times were 211.2 ms and 242.8 ms respectively. This measurement supports
  reduced allocation, not a speed claim. Run `--profile` to reproduce locally.

## Limits and next measurements

These checks do not measure live SQL latency, WinForms responsiveness, process
working set, CPU use on school hardware, or interactive DPI behaviour. No new live
database integration run was needed for this pass because SQL definitions and
financial mutations are unchanged. Permission equivalence of the fee-type query
was checked against the DACPAC's `GetFeeTypes` definition.

Use a representative Windows test copy to record startup/navigation times,
admission search/open/save, student and family collection/open/pay/receipt,
report load/export, and repeated page navigation. Capture median and slower-case
times, SQL round trips, private memory and CPU, including a large student list.
Revisit database aggregation/indexes or rendering only when those measurements
identify the next bottleneck. Preserve whole-rupee rules and transaction safety.
