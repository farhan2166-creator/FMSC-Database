# Search, collection and report loading hardening — 2026-10-13

This pass changes existing loading paths. It adds no database schema, SQL module,
product feature or financial mutation.

## Confirmed gaps addressed

- Cached student/staff picker matches were rebuilt on every keystroke. Typing
  now waits for a 180 ms pause. Enter and dropdown actions remain immediate.
  Editing hides the prior matches before the delay, so Enter cannot commit an
  old highlighted student. Clear, Escape, selection, focus loss, hiding and
  disposal stop pending work; dismissed asynchronous replies cannot reopen it.
- Report generation previously rejected obsolete results without passing
  cancellation into its SQL read. The shared coordinator now cancels queued
  waiters immediately and exposes a safely captured token to active readers.
  Canonical finance reports, billing status, unposted fees and student report
  loading pass that token through the existing asynchronous database helpers.
  Filters, superseding runs and page disposal cancel obsolete reads. Legacy
  synchronous delegates still reject stale results; their already-running SQL
  commands cannot be cancelled through this adapter.
- Cancelled reports cannot mark rows as freshly loaded or enable printing/export.
  Cancellation clears the loading overlay/cursor and leaves a filters-changed
  prompt. Existing report-specific policies for retaining old rows remain.
- Paging used LINQ Skip, enumerating all rows before a late page. It now accesses
  only the requested rows by index and retains detached page tables, metadata,
  ordering and row state. The full source remains available for exports.
- Canonical finance pages no longer duplicate their entire result table solely
  to check whether printing is available. Grid pages remain detached copies.
- Family collection checks charge IDs with one local set while appending, rather
  than scanning the grid for every row. Existing rows and repeated input charges
  still cannot be appended twice; payment and allocation rules are unchanged.

## Existing protections retained

The picker already shares short-lived student-list reads and rejects superseded
generations. Collection already guards asynchronous results by selected identity
and load version. RDLC preview windows already construct their viewer on request
and dispose it and related resources on close. These paths were inspected before
selecting changes. Record opening remains within the existing workflow; no new
progressive-loading UI was introduced without timing evidence.

## Verification

The application and Windows smoke-check harness cross-build with zero warnings
and errors. Existing portable money/quotation and role/search/geometry checks
pass. The production loading/cache tests and new workflow checks cover empty,
partial and late report pages, ordering, row state, detached edits, oversized
page requests, invalid page sizes, active/queued cancellation, superseded ticket
tokens, disposal while queued, and stale picker completions with a controlled
synchronization context.

A warmed synthetic test for the final 500 rows of a 200,000-row table measured
median page construction of 9.59 ms before and 0.55 ms after across nine runs.
This isolates paging work, excluding SQL, WinForms and school workloads. Run:

```sh
dotnet run --project Tests/PerformanceChecks/PerformanceChecks.csproj -- --profile
```

The Windows smoke harness also covers debounced typing, immediate Enter,
replacing a selected identity, rejecting old highlighted choices, Escape and
clearing input using a fixed in-memory student source. It is compiled but not
executed in this Linux environment. Interactive Windows checks and live SQL
cancellation/latency, process RAM and CPU measurements remain outstanding. No
school database was accessed and no new SQL integration run is claimed.
