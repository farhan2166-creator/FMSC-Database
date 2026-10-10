# Audit regression checks

Apply migration 18 to a disposable database copy, then run these scripts with `sqlcmd -b`:

- `Tests/AuditChecks/AgreedRules.sql`: selected-year discovery/preview/posting, idempotency, atomic mixed-year rejection, occupied placement protection, allowed retirement, reversal eligibility and all three charge kinds, and historical spans of 12, 99, 101 and 130 months with the existing future cap.
- `Tests/StudentCorrections/LeavingDateChecks.sql`: 14 lifecycle checks, including successful missing-enrollment creation with valid existing audit logging.
- `Tests/StudentCorrections/EnrollmentDateChecks.sql`: five existing date/financial reconciliation checks.

Fixtures require existing catalog rows and an administrator account. Synthetic records are rolled back, including expected failures. Do not use the live school database. Preview result sets are also printed for inspection; their displayed rows are not all independently asserted by the script.

Run `dotnet run --project Tests/FeeRateApplyChecks/FeeRateApplyChecks.csproj` for ten portable checks of confirmed counts, an interruption after one completed group, unknown pending outcomes, groups not started, required review before retry, refresh failure, and invalid summaries. These test the shared result/progress helper, not a network disconnect in the Windows interface.

Build the WinForms project with `-p:EnableWindowsTargeting=true`. On Windows, verify both posting entry points, placement refusal, reversal charge types and guidance, report headings, and persistent fee-rate outcomes after an interrupted batch. Confirm normal/increased display scaling and the fresh-review requirement. Concurrent administrator/placement changes require a separate multi-session test.
