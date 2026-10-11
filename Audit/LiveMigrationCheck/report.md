# Live migration comparison — 11 October 2026

Database: FMSC_DB, C:\ProgramData\FMSC_Database\FMSC_DB.mdf.
Read-only inspection; no migrations or database changes executed.

Compared migrations 2–18 in numeric order, using the latest replacement of each module and accounting for the migration 8 amendment to CreateReviewedLeaveCertificate.

Results:
- 25 of 26 final procedure/trigger definitions match after ignoring comments, whitespace, and CREATE versus ALTER syntax.
- PreviewMonthlyFees differs only in a user-facing string: migration 18 contains “Previously reversed â€” selecting this creates a new charge”; the live definition contains “Previously reversed — selecting this creates a new charge”. Logic matches, but this is not an exact match of string content.
- 21 additional checks of dynamic patch fragments, parameters, and added column definitions passed. These verify resulting features rather than reconstructing the complete pre-migration database.
- The two financial guard triggers are enabled.
- CampusClassSections.IsActive has the expected default ((1)).
- Latest DatabaseVersion is 3. Its latest note explicitly records migrations 4–18 applied in sequence while preserving compatibility baseline 3. This corroborates the definitions but is not independent proof of execution of every historical migration.
- Changes superseded by later migrations are assessed against their final state.

Conclusion: no missing schema or logic changes were found in this comparison. The saved migration 18 has one text-encoding discrepancy; therefore an exact all-definitions match cannot be claimed. No behavioral execution tests or historical data backfill verification were performed.

Evidence: live.json (live metadata), individual dbo.*.sql files (live definitions), module-comparison.csv, patch-comparison.csv, schema-checks.txt, and compare.ps1.
