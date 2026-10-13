# Remaining findings and native verification — 13 October 2026

The original confirmed fixes were committed as `cb229fa`. This follow-up addresses
the remaining actionable findings and two issues exposed by native execution.
No live school database was connected or changed.

## Changes

- **F-06, permission consistency:** Dashboard visibility, direct QR entry, pending
  Open/Review actions and admission-page redirects now require both existing
  `Students.Admit` and `Students.View` permissions. SQL still independently checks
  those rights. No permission implication or grant was added. Native limited-role
  tests confirm that Admit alone cannot read the list, and adding View permits it.
- **F-08, cached search:** A single DataView retains source rows and grid binding.
  Search is debounced for 200 ms and flushed before opening/reviewing a selection.
  Literal apostrophes, brackets, percent and asterisks are escaped; case-insensitive
  and Unicode search and status filters are tested. Summary counts are computed
  once per refresh. Sorting is retained. The audit's per-keystroke SQL-reload claim
  was incorrect; full SQL loading/balance aggregation remains a separate capacity
  question, not an established defect.
- **F-09, pending eligibility:** The production list now requires Pending status,
  a non-null QuickReference and a null official AdmissionNumber, matching the
  completion procedure's student discriminator. The legacy-list branch is unchanged.
  Native tests include/exclude eligible and non-QR Pending fixtures.
- **F-10, working-area review:** ApplicationWindowForm already clamped outer bounds
  and minimum size to the monitor working area; admission content already scrolls.
  That behavior was retained and its geometry extracted for testing across small,
  scaled and offset monitors. Interactive DPI/footer accessibility still needs Windows.
- **F-11, execution coverage:** Native SqlPackage, migration and production SQL
  integration tests now ran. The harness compiles the actual writer, list query
  and startup/restore checker, with connection selection restricted to disposable
  test catalog names. Windows UI and the full school-data suites remain outstanding.
- **F-12, maintainability:** Access policy, list SQL, search expressions and window
  geometry were extracted into focused helpers. No broad controller rewrite was made.

## Issues exposed by execution

1. The QR UI already passed UserId while the new writer added another. The writer
   now binds the captured signed-in actor exactly once, disregarding any supplied
   duplicate actor parameter. The SQL harness supplies an invalid incoming actor
   value and proves the production writer uses the current actor successfully.
2. The compatibility contract depended on the export-time name of an automatically
   named LegacyAdmissionSetup check. Native deployment generated a different name.
   That system-named safeguard now matches its parent and exact expression, with
   enabled/trusted status still mandatory. Named safeguards retain their name
   checks. Native tests weaken the expression, verify refusal, restore the exact
   safeguard and verify acceptance. Offline verification also checks this metadata.

These changes need no additional database migration. `Migrations/2.sql` remains
the required version-1-to-version-2 whole-rupee update. The revised DACPAC and its
approved package/model/module fingerprints are unchanged in this follow-up.

## Results

- Application cross-build: zero warnings and errors.
- Production portable regressions: 75 money/TVP/quotation checks plus 102
  permission, literal-search and working-area checks passed.
- Artifact regressions: nine passed; ZIP/model checksum, provenance, contract
  inventory, whole-rupee checks and 273 module fingerprints passed.
- Native SqlPackage 170.5.96 publication of the revised DACPAC to SQL Server 2022
  Developer: passed. Source version-1 DACPAC publication also passed for upgrade tests.
- Migration 2: fractional-data refusal was atomic, successful upgrade installed
  eight trusted/enabled constraints, and re-execution was safe.
- Production SQL harness: 17 checks passed, including startup compatibility and
  weakened-check refusal, competing rate locks, stale saves, retained newer values,
  actual role permissions, eligibility and fractional-rate refusal.
- PowerShell artifact check: approved package accepted, modified package rejected.
- Cached-list profile: seven filter changes at 100, 2,000 and 10,000 synthetic rows
  completed with expected counts. One run measured 0.7, 17.4 and 73.4 ms total,
  respectively. These figures exclude database queries and WinForms painting;
  they are not a production performance guarantee.

Native tests used synthetic data in a dedicated disposable container. They do not
establish behavior on the user's SQL Server 2019/LocalDB snapshot, all historical
school records, physical Windows monitors or every application workflow. See
`Validation/AuditFixChecks.md` for the remaining acceptance scenarios. Startup
still validates live structure; approved artifact fingerprints do not fingerprint
SQL bodies inside an independently supplied MDF or live database.
