# Academic-year date lock — 31 October 2026

The owner approved allowing Name/Remarks corrections and changing Current while
locking Start/End dates once an academic year has student or financial usage.
Any enrolment, including historical/left/legacy records, counts as usage. Promotion
history locks both its source and destination years. Financial activity retains
its connection through enrolment. Age alone does not lock an unused year.

Migration 63 adds `DatesLocked` to `GetAcademicYears` and enforces the same boundary
transactionally in `SaveAcademicYear`, including saves from stale or older forms.
The native page disables both date controls for used years and leaves Name,
Remarks and Current editable. New Record and unused-year selection unlock dates.
Names must contain consecutive four-digit years, such as `2021-2022`. The name
validator and date autofill use the same parser. No saved year or history is
automatically corrected or removed.

Verification on the working tree based on
`2db45ddc870fefc4034474034a52327ea9497735`:

- 25 portable name checks passed, including `2021-20224444` rejection.
- Eight native control checks passed without a database connection.
- `Tools/TestAcademicYearDateLock.ps1` passed against session-local synthetic
  tables: historical enrolment, promotion source/destination, used-date refusals,
  allowed corrections, unused-date edits, creation, newly added usage/stale saves,
  malformed names, permissions, duplicate names and overlaps.
- Application Release build passed with zero warnings/errors. The native test
  project build reported NU1900 because NuGet vulnerability data was unavailable;
  compilation and the focused native checks succeeded.
- `Tools/ApplyAcademicYearDateLock.ps1 -Apply` applied migration 63 to the authorized
  live schema-62 database after COPY_ONLY/CHECKSUM backup and RESTORE VERIFYONLY.
  Before/after hashes matched across all 79 existing business tables (excluding
  the version ledger and rowversion metadata). Final read-only verification found
  schema 63 and both installed guards. Backup and execution evidence remain in
  the ignored `artifacts/academic-year-upgrade/7839923b13e54ca3a4eae318fe5f7617/`.

The first apply attempt was rolled back because the preservation checker treated
an empty table's null hash as a mismatch. A read-only rolled-back diagnostic
confirmed this without running the migration. Explicit empty-array hashing fixed
the checker; all 79-table checks then passed before the successful retry.

Joint operator acceptance in the updated application remains pending. This is
focused evidence, not full application or release certification.
