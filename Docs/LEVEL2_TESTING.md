# Level 2: interactive Windows operator testing

Level 2 launches the real FMSC_SMS WinForms application as a separate process and
uses FlaUI/UI Automation, mouse input and keyboard input on an unlocked Windows
desktop. It never calls form event handlers or seeds a student/payment/promotion
and counts that as UI success. The reference fixture creates only fictional
configuration, staff and two accounts. The 48-row scenario catalog is a coverage
inventory: **a row is successful only when its recipe executes and its assertions
pass**. Unimplemented coverage remains `Not Run`; failed prerequisites are
`Blocked`. This is an incremental suite, not a declaration of full Level 2 acceptance.

## Rerun on this PC

Requirements: Windows, an unlocked interactive desktop, .NET 8 Desktop Runtime
and .NET SDK 8 or newer, SQL Server LocalDB 2019 or newer, `sqlcmd`, Python 3.11+,
and NuGet access. No administrator elevation or school database files are needed.
LocalDB must be available to the same Windows account that runs the application.
The approved test model uses Sql150. SQLPackage is pinned by the existing Level 1
tool manifest to 170.5.96; no incompatible-platform override is used.

In PowerShell from the repository:

```powershell
.\Tools\Run-Level2.ps1
# If Python is installed somewhere else:
.\Tools\Run-Level2.ps1 -PythonPath 'C:\path\to\python.exe'
# Build and provenance checks only; scenario results are deliberately blocked:
.\Tools\Run-Level2.ps1 -PreflightOnly
```

Python is resolved from a real `python` installation or the bundled Codex runtime
on this PC. The Windows Store execution alias is deliberately avoided. Do not
lock the desktop or operate another application during input injection. The
driver checks the interactive desktop before inputs; it does not unlock Windows
or change display settings. Exit zero requires every catalog row to pass; the
current incremental suite will return nonzero while coverage remains incomplete.

Reports are written to:

```text
artifacts/level2/evidence/FMSC_Level2_<run GUID without hyphens>/
    report.json
    summary.md
    environment.json
    L2-*.png
    L2-*-controls.txt
```

Only this evidence directory may be shared after validation:

```powershell
python Tests/Level2UiChecks/validate_report.py <exact evidence directory>
```

Private state lives in `artifacts/level2/private/FMSC_Level2_<GUID>`. Never upload
that directory, its credentials/recovery files, raw console logs, database files,
NuGet output or the data-bearing repository DACPAC. Git ignores generated output.
Screenshots render only the owned application window with `PrintWindow`; they do
not capture the desktop or unrelated windows. Control inventories contain names,
IDs and roles, excluding password values and general ValuePattern contents.

## Isolation and cleanup

Each run creates a unique LocalDB **instance and catalog**, both named
`FMSC_Level2_<GUID>`. The harness accepts no SQL endpoint, connection string,
existing catalog, or MDF/LDF input. It verifies the unchanged schema-only
DACPAC/package/model/module provenance using the Level 1 verifier and publishes
only `Tests/LifecycleSqlChecks/Baseline/schema.dacpac`.

SQLPackage must preserve the `FMSC_Level2Owner` extended property using
`DropExtendedPropertiesNotInSource=False`. Default SQLPackage behavior otherwise
removes it even with `DropObjectsNotInSource=False`; the first implementation run
demonstrated that the subsequent guard refuses to proceed.

The reference fixture uses the production setup procedures and runs the unchanged
startup schema compatibility validator. As in Level 1, DacFx's recovery-scope
property is converted to a fresh typed uniqueidentifier only in the disposable
fixture. Approved schema artifacts, fingerprints and migration requirements are
not rewritten. Three fictional years support current-year admission and next-year
promotion. No student, collection, allocation or promotion is seeded.

The application is built with the explicit `FmscLevel2=true` property into the
private run folder. That compile-time symbol adds the guarded connection route,
isolated local preferences/recovery/log/report-layout/communication files, stable
input IDs and a fictional-environment title. Normal builds do not contain that
route and ignore test environment variables. Publishing a Level 2 binary is
explicitly rejected. The offline guard probe checks that operational instance,
operational catalog, attach-file and SQL-credential inputs fail **before** opening
a SQL connection; a normal binary rejects that probe before normal startup.

Before each modifying driver operation and application connection request the
instance/catalog identity, exact owner marker and private database path are
verified. Cleanup also refuses unexpected other catalogs. Live messaging providers
are disabled in the test build; normal gateway credentials are never read. The
suite does not visit installer/restore/repair operations, send messages, print to a
physical printer, change Windows settings or attach the school's files.

The driver terminates only its own launched process. Normal completion verifies
ownership, stops/deletes only its own LocalDB instance, and removes only its
resolved private run directory. Evidence survives. A failed ownership check retains
private state rather than deleting an uncertain target. Cleanup failure is recorded
and returns nonzero. For an interrupted run with intact ownership:

```powershell
.\Tools\Clear-Level2.ps1 -RunId '<exact GUID from report.json>'
```

If the ownership marker, file paths or catalog differ, cleanup stops. Inspect the
original local run first; do not remove databases by name prefix or recreate a
marker just to bypass a failed check.

## Implemented recipes and remaining acceptance

Implemented recipes cover visible startup; empty/incorrect/admin/restricted login;
Tab focus; sign-out and fresh authentication; admission navigation/required fields;
no-match student search; placement choices; invalid phone/date-year combinations;
UI admission and opening-fee verification; repeated Save; search/reopen; allowed
field edit and discard; unsaved navigation cancellation; partial/full collection;
cancelled and settled collection safeguards; account-statement opening; independent
allocation reconciliation; promotion selection/cancellation/commit/history;
student profile and related admission navigation. See the actual report for which
recipes completed in any particular run; an implemented recipe can still fail.

Coverage still requiring additional recipes and/or operator acceptance includes
fee-rate configuration and monthly posting through UI, additional-charge entry,
receipt rendering/export, deliberate duplicate-identity handling, invalid placement
combinations across additional campuses, sibling linking and student status,
confirmed next-year admission/fees, promotion replay refusals, connection-interruption
recovery, switching records during edits, comprehensive layout/keyboard inspection,
and a real resolution/DPI matrix. The scenario inventory retains these as `Not Run`
until executed. A single monitor or headless controls test does not prove the matrix.

Financial scenarios independently query committed payments, allocations, charge
totals and relationship ownership. The current fixture's opening charges are
5000 tuition + 2000 admission. No partial/full payment may pass merely because a
success dialog appeared. Statement opening is tracked separately from exhaustive
statement rendering and receipt acceptance.

## Maintenance and CI

The first visual defect found was a long inline validation message painting over
the phone field's caption. `FormValidator.FitBadge` now reserves the measured
caption width. A focused Windows control regression checks non-overlap, preserved
validation state and the complete accessible message at three field widths (nine
assertions). It also runs in the existing desktop `--post-fix` checks:

```powershell
dotnet run --project Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -c Release -- --validation-badges
```

`Tests/Level2UiChecks/Desktop.cs` owns process-scoped discovery, bounded retries,
input, known startup dialogs and screenshots. Inputs use semantic accessible names
with stable test-build IDs as fallback. Dropdowns click exact visible item labels,
including academic-program qualifiers; native `Select` or shared-prefix keyboard
search must not be assumed to have selected a value. Native ValuePattern verifies
the result. `Identity.cs` rejects unowned targets; `Evidence.cs` writes structured
results; `scenarios.json` is the coverage inventory. `Level2Fixture` links the
existing reference-data factory without changing Level 1's runner or target guard.

Run the offline safety/report regressions after building the test binary:

```powershell
dotnet build FMSC_SMS.csproj -c Release -p:FmscLevel2=true
python -m unittest discover -s Tests/Level2UiChecks -p 'test_*.py' -v
```

`.github/workflows/fmsc-level2-ui.yml` compiles and checks report/isolation contracts
on hosted Windows. It explicitly makes **no interactive UI claim**. Its optional
manual desktop job requires a dedicated self-hosted Windows X64 runner labeled
`fmsc-level2-interactive`, running interactively under the logged-in user rather
than as a service. Do not attach this label to a school workstation. Only manual
dispatch can schedule that job; untrusted PRs never automatically run on it.
Validated fictional evidence is uploaded as a short-lived Actions artifact.

Keep the existing Level 1 SQL and desktop control suites independent. At the
inspected main commit `bf37dfec784ac48f76960afc136d37a3794722bc`, GitHub Level 1
reported 45 passing scenarios; Security and Desktop CI also succeeded. The eight
Level 1 safety tests and schema-only provenance checks passed locally. A Windows
sandbox temporary-directory restriction initially prevented five Python tests;
rerunning with a writable temporary directory passed all eight. This was an
environment error, not a reason to relax SQL/schema checks.
