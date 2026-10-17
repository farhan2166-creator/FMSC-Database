# Audit fix verification — 13 October 2026

**Archived historical record:** References below to DACPAC approval commands and removed utilities document old verification and are not current release instructions. See `Docs/RELEASE_CHECKLIST.md` for current validation.

**Historical verification record.** Applied migrations 2–7 and their upgrade-only test modes were retired on 15 October 2026. The commands below that apply those scripts describe earlier releases. Current SQL suites require a disposable copy of the schema-6 baseline; the deployment migrations folder is currently empty. See the current [installation guide](../Docs/INSTALLATION.md).

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

The portable tests compile production money, TVP, quotation, registration/photo
file-persistence and administrative draft/transaction-outcome helpers. The
schema verifier checks ZIP CRC, Origin.xml's model checksum, the exact package
hash, contract inventory, money check expressions, the reviewed full model, and
273 SQL-module fingerprints. These checks do not execute SQL Server or WinForms.
Publishing also runs `Installer/VerifySchemaArtifact.ps1`, which rejects an
unreviewed package/model. Use `--approve` only after independently reviewing an
intentional artifact change; it records a baseline rather than proving correctness.

The owner's data-bearing re-extraction at `ad60bbb` was independently reviewed
and published into a disposable SQL Server 2022 copy. All 273 SQL modules are
unchanged; reviewed model differences are serialization, `FLOOR` keyword casing
and identity last-values. Approved package/model provenance is synchronized.
Whole-rupee expression validation accepts only the function keyword's casing
difference; exact fingerprints and policy columns/operators remain checked.
The twelve artifact regression tests include casing acceptance and refusal of
changed columns/operators/additional SQL. See the independent audit's section 10
for the actual four-role catalog, 39 rerun production SQL checks and twelve
separate role-boundary assertions. Native Windows acceptance remains pending.

`Installer/VerifyBaseline.ps1` is retired: its old source-freeze manifest is no
longer maintained. Invoking it fails explicitly with the supported verifier and
validation instructions; it does not claim a successful source-freeze check.
For the supported PowerShell artifact check, run:

```text
pwsh -NoProfile -File Installer/VerifySchemaArtifact.ps1 -ProjectDirectory .
```

## Disposable SQL checks

1. Restore a COPY_ONLY backup to a new database named `FMSC_AuditChecks_<suffix>`.
   Give the clone separate MDF/LDF paths. Never rename the school database to
   satisfy the test's name guard. Keep the source database unchanged.
2. Run `Migrations/2.sql` on the clone. It locks/preflights monetary tables and
   aborts atomically if any stored amount is fractional. It does not round or
   clean existing data. Only a successful complete migration records version 2.
   Then apply `Migrations/3.sql` and `Migrations/4.sql`; the current harness requires
   version 4, the consistent family-maintenance guard and save-request recovery.
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
drift refusal, and database-side fractional-rate refusal. It also checks pending
QR records in the parent picker query; family creation before confirmation;
later link/unlink/relink; sibling-change snapshot refusal; cross-family refusal;
required maintenance access; preservation of student and financial records; and
parent-link management after actual `CompleteQuickAdmission`. It deliberately leaves
fixtures in the clone for inspection. Drop that clone after inspecting results.

On a separate version-1 clone, verify the migration's refusal path: inject a
fractional configuration, record its exact amount and the schema version, run
the migration and expect error 53201. Confirm the amount and version are unchanged
and no new constraints remain. Restore the recorded original amount explicitly,
then apply the migration successfully. No automated rounding is an acceptable fix.

The existing `AdmissionWorkflowChecks.ps1` and `StudentCorrectionChecks.ps1`
restore their own disposable copies and now apply migration 2 on those copies
before their existing scenarios.

### Administration transaction checks

The unified Users/Roles writer reuses the existing procedures in one enclosing
transaction; no new schema migration is needed. On a separately restored,
**disposable** `FMSC_AuditChecks_<suffix>` clone, the administration harness links
the production `AdministrationStore` and tests typed outcomes, stale state,
zero access, password isolation, protected accounts/roles and captured actors.
The current synthetic fixture expects `Students.View`, `Students.Admit`,
`Fees.View`, `System.Users`, `System.Users.Manage`, `System.Roles` and
`System.Roles.Manage` permission definitions, an Administrator, and at least two
non-Administrator roles. It creates synthetic accounts/roles and temporarily
creates a failure-injection trigger; it must not run against real school data.

```powershell
$env:FMSC_AUDIT_CONNECTION='Server=(localdb)\MSSQLLocalDB;Database=FMSC_AuditChecks_admin;Integrated Security=true;TrustServerCertificate=true'
dotnet run --project Tests/AdministrationSqlChecks
Remove-Item Env:FMSC_AUDIT_CONNECTION
```

The **39 executed SQL assertions** on the isolated SQL Server 2022 Developer
fixture include genuine second-procedure failure with all first-step data/audit
effects rolled back, two simultaneous user editors, and unchanged snapshots for
unrelated login-counter activity. The portable suite now has **522 assertions**.
Windows editor fixtures are compiled but not executed on the Linux host. Existing
role/grant data and restricted maintenance navigation are preserved; business
role consolidation is a separate review of live catalog/usage, not an automatic
consequence of this UI restructuring.

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

## Two-role migration and permission consistency — current update

The application and installer now require version **3**. Apply **Migrations/3.sql**
after version 2, using the installer's verified recovery backup and closing all
application sessions before migration. Administrator is always full authority;
Accountant keeps its current six grants, including student and finance reports.
Every other role is removed. User accounts/credentials remain; removed-role-only
accounts gain no replacement membership and retain zero access. Existing Accountant
and supported permission IDs are preserved.

The submitted DACPAC still represents version 2. Provide the newly extracted
version-3 artifact after applying this migration; review its seven changed SQL
modules and data before updating approved fingerprints. The current artifact
verifier passing describes the supplied baseline, not completed version-3 release
packaging. No production or detached school files were changed during these tests.

```text
python -m unittest discover -s Tools -p test_permission_policy.py
```

For **a fresh disposable clone of the supplied data-bearing version-2 DACPAC**,
set `FMSC_AUDIT_CONNECTION` as described above and also set `FMSC_MIGRATION_FILE`
to the absolute path of `Migrations/3.sql`, then run:

```text
dotnet run --project Tests/AdministrationSqlChecks
```

The optional migration suite intentionally injects a final version-insert failure
and verifies complete data/audit/procedure rollback, refuses an older version,
adds legacy/empty-membership fixtures, migrates, confirms exact catalog and
Accountant grants and verifies repeat execution is a no-op. It requires an
unmodified version-2 fixture with the original four roles. Without
`FMSC_MIGRATION_FILE`, the production-writer suite requires an already migrated
copy. Both modes accept only an explicitly disposable database name; use neither
against the school's installed database. The version-3 writer fixture changes
Accountant temporarily to test atomic saves and readonly boundaries, then restores
its original description and grants.

Current results: **530 portable assertions**, **67 production SQL checks**,
**15 migration durability assertions**, **four cross-layer catalog tests** and
**12 artifact tests** passed. Windows-targeted application/smoke-harness build
passed with zero warnings/errors. Native Windows interaction remains pending.

`Tests/AdministrationSqlChecks/FirstRunChecks.sql` additionally passed three
first-run checks on an empty, native schema-only version-3 database seeded with
Administrator, Accountant and the 28 canonical permission definitions. It verifies
Administrator-only assignment, full authority and refusal of repeat setup. Do not
run it against the data-bearing fixture: it explicitly requires an empty Users
table and a disposable database name.

## Colored lightweight-theme verification

Portable count at the theme change: **585**, including 55 production appearance-policy checks.
The five lightweight options preserve saved IDs/names, provide distinct palettes,
meet tested primary/hover/text contrast and visible input-boundary contrast, and
share the existing simple-rendering policy. Theme refresh dispatch continues after
a failed listener. Run the normal AuditFixChecks command above.

The expanded `ThemeSmokeChecks` harness compiles with zero warnings/errors. On a
disposable Windows profile, run it to exercise all nine appearances forward and
back, verify preference files, preserve draft/focus/geometry, release clipping and
animation resources, replace/cancel status attention and contain failed refresh/
recursive callbacks. The suite also tests compact/wide administration layouts for
each appearance. Native GUI execution and DPI/readability acceptance remain unrun
on Linux. Measure RAM/CPU on a representative low-end Windows PC before claiming
a deployed speed improvement. No additional SQL migration is needed for themes.

## Quick Registration family and entry-flow verification

Portable count at the family-flow change: **602**, including nine additional checks for verified
family IDs/counts and rejection of missing/inconsistent family context, plus
eight student/family bill identity, grouping and monetary-preservation checks.
The production QuickRegistration SQL harness passes **34 checks** against an
isolated SQL Server 2022 publication of the supplied DACPAC plus migration 3.
No school database is modified. No additional SQL migration is needed.

First-time sibling flow: save the first child, choose **Add Another Child**, then
save the second child with the retained parent details. Saving creates the family;
neither child needs confirmed admission. **Link Existing Parent** searches pending
QR and admitted students and also works on saved registrations. **Unlink Parent**
removes only this child's membership and retains the student/contact/financial
records. Existing parent information is stored per student; linking a saved record
does not overwrite its parent/contact details. Correct details in Admission / Review.

On Windows, run the expanded smoke harness for draft unlink retention, pending/
active/cancelled family actions, same-parent next-child/new-family resets, dirty/
busy/stale guards. The portable harness executes student versus family bill
heading, same-name sibling grouping and monetary-preservation checks.
It compiles cleanly here; native GUI execution remains unrun on Linux. Manually
verify the unsaved-discard confirmation keeps the draft when No is selected and
that parent actions/search remain visible at supported window sizes and DPI.


## Quick Registration lost-reply recovery (schema version 4)

Current results: **636 portable checks**, **51 production Quick Registration SQL
checks**, **9 migration-4 checks**, and **16 artifact/catalog tests**. App and
Windows smoke builds have no warnings/errors; native GUI checks remain unrun here.

Apply `Migrations/4.sql` after version 3, with the established verified backup and
closed application sessions. Application and installer now require version 4.
The latest DACPAC at `51abb13` is now independently reviewed as version 3. Startup merges its
base inventory with `Core/DatabaseMigrationRequirements.json`, checking the new
table/columns, both uniqueness safeguards, trusted foreign keys and a persistent
database recovery GUID. This GUID survives reconnects/restarts and avoids treating
a different installation with the same connection name as the original database. A version-4
label alone cannot satisfy this new structural contract. The artifact verifier
continues checking the actual supplied artifact without inventing a new baseline.

Each new save has an exact request GUID and normalized payload hash. A tiny local
intent is flushed before submission; SQL commits student, fees and request marker
in one transaction. Recovery reads that exact request; it never repeats a write
or searches by possibly duplicated student names. A transaction-scoped request
lock prevents a check from reporting absence while that request is still running.
A confirmed absence unlocks a deliberate new save. Offline/timeout/permission/
corrupt-journal cases remain blocked and retain recovery. The form keeps the
intent until the saved record is loaded, including after app/page restart.
It retains current input on failure; the journal stores a short summary, not a
complete draft or photos. Restart after confirmed absence may require re-entry.

Normal use needs no extra click. **Check Save Outcome** appears during recovery.
Save, new/sibling drafts, linking, payment and printing wait for resolution.
The originating operator can check their request; Administrator can check another
operator's request. A different database scope or payload cannot silently reuse
an intent. Do not delete `PendingQuickRegistration.json` or SQL request markers
as a routine way to enable retry; unresolved/corrupt intents require administrator
review. No recurring timers or background polling are added.

For migration refusal/rollback/idempotence, use a separate disposable version-3
copy (request table absent) and run:

```powershell
$env:FMSC_AUDIT_CONNECTION='Server=(localdb)\MSSQLLocalDB;Database=FMSC_AuditChecks_qr_migration;Integrated Security=true;TrustServerCertificate=true'
dotnet run --project Tests/QuickRegistrationSqlChecks -- --migration-4
Remove-Item Env:FMSC_AUDIT_CONNECTION
```

The embedded migration test intentionally changes this guarded disposable clone,
uses a final-version trigger failure, checks existing record preservation and
validates the final application contract. The normal SQL harness additionally
injects a lost reply after an actual commit, runs two concurrent identical
requests, fails marker insertion and tests recovery while a request lock is held.
It checks typed discounts, Administrator recovery and refusal of a database with
a missing recovery uniqueness safeguard. All fixtures stay in the disposable
clone; remove it after inspection. Historical migration-3 administration tests
retain their separately scoped version-3 compatibility check.

On Windows, verify automatic recovery on reopening, Check Save Outcome keyboard
access, close/reopen with offline SQL, successful save followed by refresh failure,
locked/corrupt recovery storage, sign-out/session changes and a real disconnect
around commit. Run the smoke suite in a disposable Windows profile.

### Quick Registration fee controls and pending-grid flow — 13 October 2026

- Added explicit + Add Fee and cross controls for configured opening fees, before
  saving and on saved pending registrations. Required automatic fees can be
  explicitly waived with Fees.Adjust; the waiver and recovery marker commit in
  the original registration transaction. Paid fees cannot be removed.
- Saved fee changes require Students.Administer and Fees.Adjust, verify the
  registration snapshot, placement, assignment ownership and current fee quote,
  and reload the screen. Failed or uncertain changes require a deliberate reload.
- Phone is optional in QR and full review/confirmation. A supplied phone retains
  the existing format validation.
- Fixed shared toolbar collapse caused by measuring effective button visibility
  while a page's tab was hidden; toolbar fitting resumes when the page returns.
- Pending grid shows shared family groups with member tooltips and combined
  Campus / Class / Section. The helper-generated Admission Number column is
  hidden, leaving the genuine QR temporary reference. Refresh, changed filters,
  empty-grid clicks and clicks elsewhere clear the confirmation target; actions
  use selected rows instead of stale CurrentRow values.
- Apply Migrations/5.sql after migration 4. Application and installer now require
  schema 5. Migration 5 changes three existing procedure bodies: optional phone
  confirmation, safe same-month restoration of unpaid pending fees, and pending
  As Needed fees on the opening bill. The source DACPAC remains the reviewed
  version-3 artifact; migrations 4 and 5 are still required with that artifact.
- Verification: 72 production SQL checks on a disposable restored DACPAC,
  migration-4/5 rollback, rerun and record-preservation checks, 636 portable
  checks, 16 Python artifact checks and all 273 approved module fingerprints.
  App and Windows smoke-check project build with zero warnings/errors. Added
  Windows regression checks for hidden-tab toolbar recovery and stale-row
  selection; Windows GUI execution remains outstanding in this Linux environment.
