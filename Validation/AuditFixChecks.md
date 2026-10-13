# Audit fix verification — 13 October 2026

The approved policy is whole rupees for fees, discounts and payments. Fractions
must be rejected without silently rounding a draft or historical record.

## Offline checks

From the project directory:

```text
dotnet build --no-restore -p:EnableWindowsTargeting=true -v minimal
dotnet run --project Tests/AuditFixChecks
python Tools/verify_schema_artifact.py --check
python Tools/test_audit_schema.py
```

The portable tests compile the production money, TVP and quotation helpers. The
schema verifier checks ZIP CRC, Origin.xml's model checksum, the exact package
hash, contract inventory, money check expressions, the reviewed full model, and
273 SQL-module fingerprints. These checks do not execute SQL Server or WinForms.
Publishing also runs `Installer/VerifySchemaArtifact.ps1`, which rejects an
unreviewed package/model. Use `--approve` only after independently reviewing an
intentional artifact change; it records a baseline rather than proving correctness.

## Disposable SQL checks

1. Restore a COPY_ONLY backup to a new database named `FMSC_AuditChecks_<suffix>`.
   Give the clone separate MDF/LDF paths. Never rename the school database to
   satisfy the test's name guard. Keep the source database unchanged.
2. Run `Migrations/2.sql` on the clone. It locks/preflights monetary tables and
   aborts atomically if any stored amount is fractional. It does not round or
   clean existing data. Only a successful complete migration records version 2.
3. Confirm the clone has a current academic year, a usable class placement,
   complete mandatory fee configurations and an Administrator user.
4. In Windows PowerShell, set a connection to the clone and run the harness:

```powershell
$env:FMSC_AUDIT_CONNECTION='Server=(localdb)\MSSQLLocalDB;Database=FMSC_AuditChecks_example;Integrated Security=true;TrustServerCertificate=true'
dotnet run --project Tests/QuickRegistrationSqlChecks
Remove-Item Env:FMSC_AUDIT_CONNECTION
```

The harness compiles the production `QuickRegistrationWriter` and connects only
to the supplied clone. It rejects a non-test catalog and non-integrated security except SQL authentication to a guarded loopback test instance.
It checks the SQL version, competing rate updates under serializable quotation
locks, stale quotation refusal before student insertion, successful QR save,
two-clerk stale detail refusal, retention of the newer phone number, assignment
drift refusal, and database-side fractional-rate refusal. It deliberately leaves
fixtures in the clone for inspection. Drop that clone after inspecting results.

On a separate version-1 clone, verify the migration's refusal path: inject a
fractional configuration, record its exact amount and the schema version, run
the migration and expect error 53201. Confirm the amount and version are unchanged
and no new constraints remain. Restore the recorded original amount explicitly,
then apply the migration successfully. No automated rounding is an acceptable fix.

The existing `AdmissionWorkflowChecks.ps1` and `StudentCorrectionChecks.ps1`
restore their own disposable copies and now apply migration 2 on those copies
before their existing scenarios.

## Native validation and remaining Windows checks

- Load the revised DACPAC through native DacFx/SqlPackage and generate a deployment
  script against a disposable clone. Compare the eight additive constraints with
  `Migrations/2.sql`, then execute the clone checks. The artifact was edited
  offline and has now passed native SqlPackage 170.5.96 publication against a
  disposable SQL Server 2022 Developer instance. Repeat release-specific checks
  against the intended snapshot; the source school database was never connected.
- Open a pending QR, edit its phone/discount, open another QR and choose Stay at
  the discard prompt. Verify every draft value is retained. Choose Discard and
  verify the requested record loads. Close Review without completion and verify
  no reload or draft loss. Complete Review and verify dirty drafts still require
  an explicit discard decision. Test workspace/tab closure while a save is busy.
- Type and paste fractional values, including `999.999`, into QR/admission
  discounts, rate details/matrix, bulk adjustments, additional fees, collection
  discounts and assignment corrections. They must remain visible and be rejected,
  never become a rounded whole amount. Valid whole values must still save.
- Check existing role combinations and 100/125/150% DPI as described in the audit.
  No general student-view permission was broadened by these fixes.

Startup continues to validate live schema structure; the new release check
fingerprints the approved artifact. It does not assert that a supplied MDF or a
live database's module bodies match that artifact. Clone/runtime validation remains
a separate verification step.

## Follow-up results

- 75 production money/TVP/quotation checks and 102 role/search/geometry checks passed.
- Nine artifact regression tests and both PowerShell release-check success/refusal paths passed.
- Native publication of both the source version-1 schema and revised DACPAC passed.
- Migration refusal retained fractional data, version 1 and the prior constraints; valid
  data upgraded to version 2 with eight enabled/trusted constraints. Re-execution was safe.
- Seventeen production SQL checks passed, including startup/restore compatibility,
  rejection of a weakened generated check, current-actor parameter binding, rate locks,
  stale saves, real limited-role permission checks and pending eligibility.
- Synthetic cached-list filtering was exercised at 100, 2,000 and 10,000 rows. It does
  not measure SQL balance aggregation, WinForms rendering or a school workload.

Run `dotnet run --project Tests/AuditFixChecks -- --profile-search` to repeat the
UI-side profile. Search uses a single DataView with a 200 ms debounce, literal
wildcard escaping, and immediate flushing before opening/reviewing a selection.

Interactive WinForms, physical DPI, the data-specific correction script and the
full restored-school admission suite remain separate Windows acceptance checks.
