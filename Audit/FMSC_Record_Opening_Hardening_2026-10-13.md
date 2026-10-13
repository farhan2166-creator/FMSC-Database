# Profile opening, collection reads and register sorting — 2026-10-13

This pass improves existing read paths without changing layouts, SQL definitions,
schema versions, permissions or financial transactions.

## Changes

- Student profile details, photo, family, timeline and balance now start through
  true asynchronous database APIs. The five reads remain fresh; balance retrieval
  no longer waits for the other four reads to finish first. This changes read
  scheduling, not the number of SQL queries or the balance calculation.
  Identity/context still render before waiting for a slower balance calculation.
- A latest-read scope cancels superseded profile requests, including a quick
  student A → B → A sequence that an identity-only guard cannot distinguish.
  Clearing the selected student and disposing the page cancel outstanding reads.
  Only the current generation may render values or show a failure.
- Profile balance still uses the Students.View-specific procedure. Missing-module
  fallback and ordinary error handling remain separate from a legitimate zero
  balance, and no financial balance is cached.
- Family eligibility and recorded-payment summary reads use asynchronous text
  querying rather than blocked thread-pool workers. SQL text, explicit Fees.View
  checks, selected-enrollment scope and post-await version guards are preserved.
- Admission-number comparisons and derived report sort keys use spans instead
  of regex match/group allocations. Numeric suffix order, lexical fallback,
  overflow, leading zeros, case and the old final-newline behavior are preserved.

## Verification

Application/Windows smoke-harness cross-build: zero warnings and errors.
Existing portable audit and expanded performance checks pass. New checks compare
10,000 pairs against the original comparator, verify derived-key equivalence and
unchanged-row behavior, and exercise latest-read replacement, cancellation of five
concurrent fake reads, selection clearing and disposal.

A warmed synthetic 10,000-number sort measured these medians across five runs:

| Path | Time | Allocated during sorting |
|---|---:|---:|
| Original regex comparator | 93.10 ms | 144,304 KiB |
| Span comparator | 23.74 ms | Below 1 KiB |

The test also checks identical sorted output. Input-array copying, SQL, WinForms
rendering and overall process memory are excluded. Reproduce with:

```sh
dotnet run --project Tests/PerformanceChecks/PerformanceChecks.csproj -- --profile
```

Interactive Windows profile loading and live SQL latency/cancellation have not
been exercised in this Linux environment. Asynchronous loading should reduce
blocked worker threads, but this report makes no numerical claim about end-to-end
record opening, SQL CPU or application working set. No school database was used.
