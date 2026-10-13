# FMSC_SMS confirmed audit fixes — 13 October 2026

The confirmed behavior defects were fixed in the workspace. No live database was connected or modified, and nothing was pushed to GitHub.

## Findings and disposition

| Finding | Verification and result |
|---|---|
| F-01: fractional money | Confirmed. The owner selected whole rupees. Money inputs, scalar write parameters, and monetary TVPs reject fractions without rounding. Eight database check constraints enforce integral stored amounts. |
| F-02: stale fee quotation | Confirmed window. QR save now rereads amount, frequency, mandatory and transport flags under a serializable transaction, compares them with the displayed quotation, and calls the existing admission procedure before releasing the locks. Changes abort before insertion. |
| F-03: stale pending edits | Confirmed window. Details, assignments and a state snapshot are loaded coherently. Save compares that snapshot under SQL update/range locks in the same transaction as the existing procedure. Changed basics, enrollment state or assignments cause a conflict; the draft is retained. |
| F-04: unsaved reload | Confirmed. Opening/reloading records requires an explicit discard decision when the form is dirty. Closing Review without completing registration no longer triggers a reload. Workspace navigation respects dirty/busy state. Deliberate New/Clear behavior remains as chosen previously. |
| F-05: artifact provenance | Confirmed. The compatibility contract now records the revised DACPAC's exact SHA-256. Offline and publish checks reject package/model mismatch. |
| F-06: admission-only role | Conditional on configured permissions. No permission was broadened or role policy changed. A limited-role acceptance check remains in the runbook. |
| F-07: body semantics | Confirmed limitation. A reviewed artifact manifest and offline release check now fingerprint the full model and all 273 SQL modules. Publishing rejects an unreviewed model. Live startup validation remains structural; this does not fingerprint module bodies inside a supplied MDF or live database. |
| F-08: search cost | Audit correction: keystrokes filter/copy cached records; they do not reload SQL each time. The remaining performance concern needs profiling and was not treated as a demonstrated defect. |
| F-09: pending eligibility | Conditional on actual records and intended list policy. No speculative filter change. |
| F-10: DPI | Windows verification still needed; static source does not establish clipping. |
| F-11: test coverage | Added production money/quotation tests, artifact regression tests, and a disposable SQL harness that compiles the production QR writer. Updated existing SQL scripts to apply migration 2 only to their restored clones. SQL/Windows execution remains outstanding. |
| F-12: controller size | Maintainability concern, not a demonstrated runtime defect. QR transaction and quotation logic were extracted for targeted testing; no broad rewrite. |

## Database change

The application and installer now require schema version 2. `Migrations/2.sql` supplies the upgrade for existing version-1 databases.

The migration preflights monetary tables under locks and aborts atomically if it finds fractional stored values. It never rounds or cleans historical data automatically. Only successful constraint installation records version 2. Review any refusal against a disposable copy and make explicit corrections before applying the upgrade to school data.

The revised DACPAC contains eight additional whole-rupee constraints covering fee configurations, assignments, charges, payments, allocations, manual posting requests, clearance balances and bank-certificate income. Every earlier model element and all 273 procedure/trigger/function definitions and signatures were compared and remain unchanged. Existing compatibility signatures and requirements were preserved.

**Revised DACPAC SHA-256:** `45c43055a5a23647dece750dbbecf4bf4849fc56fa55f3929282da420c64f154`

The artifact was edited offline using the supplied reproducible generator. Native DacFx loading/deployment has not been verified in this environment.

## Verification completed

- Windows-targeted application cross-build: **passed, zero warnings/errors**.
- Portable tests compiling production money, TVP, parser and quotation helpers: **75 checks passed**.
- Artifact regression tests: **8 passed**.
- ZIP CRC, model checksum, exact provenance hash, contract inventory, money expressions and **273 module fingerprints**: **passed**.
- Additive model comparison and compatibility-requirement preservation: **passed**.
- Whitespace checks: **passed**.
- Disposable SQL harness: **compiled**. Its restore reported NU1900 because NuGet vulnerability data was unreachable; no compilation errors occurred.

## Runtime verification remaining

SQL Server execution, native DacFx/SqlPackage validation, PowerShell release-check execution and interactive Windows testing were unavailable here. The Docker daemon was inaccessible.

`Validation/AuditFixChecks.md` provides the commands and acceptance scenarios. `Tests/QuickRegistrationSqlChecks` rejects production catalog names and non-integrated authentication; it requires a disposable restored database named `FMSC_AuditChecks_<suffix>`. It checks competing rate locks, stale quote refusal, successful QR creation, stale detail/assignment refusal and SQL fractional-rate refusal.

Apply migration 2 and run the clone/native/Windows checks before deploying this application. The user's established snapshot-supply process remains unchanged; absence of an MDF/LDF pair in this development workspace was not treated as a release blocker.
