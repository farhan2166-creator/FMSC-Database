# FMSC_SMS — Independent Post-Merge Project Audit

**Date:** 14 October 2026  
**Reviewed immutable source:** [4c25103f4930d6cbd23e2c41100f474429d1c7e9](https://github.com/farhan2166-creator/FMSC-Database/commit/4c25103f4930d6cbd23e2c41100f474429d1c7e9) (main after PR #11 and PR #10)  
**Method:** Read-only GitHub repository/static-source review, full-workflow and published CI-log inspection, delta review against audit freeze 14aba80a, comparison with current master audit and release requirements. This review did **not** run the application, installer, SQL transactions, UI, DPI scenarios or a fresh release build locally.  
**No source, database, migration, business-rule or installation change is authorized by this document.**

## Executive decision

**Status: CODE/CI GREEN; PRODUCTION RELEASE ACCEPTANCE STILL PENDING (NO-GO until release gates have evidence).**

The current branch compiles and passes substantial portable regression, synthetic performance, source-based schema verification and secret checks. The end-user-flow fix PR #8 was merged before the dependency updates, and relevant regression fixtures have passed. This is positive evidence against simple compile/regression defects **but cannot prove** installer behavior on a real Windows operator account, compatibility of the owner's detached MDF/LDF, live SQL concurrency/financial recovery, interactive UI, physical printing or deployment rollback.

### New snapshot findings / carry-forward register

| ID | Severity | Kind | Disposition |
|---|---|---|---|
| PMA-01 | **High** | Confirmed deployment policy / implementation mismatch; already identified in master audit | **Open**: current installer can still upgrade an older schema |
| PMA-02 | **High** | Confirmed pre-activation/final-check coverage gap; corroborates RR-04 | **Open**: installer success check is materially weaker than application startup compatibility |
| PMA-03 | **High** | Known Windows operator-account deployment risk; corroborates RR-03 | **Acceptance open**: installer configures LocalDB/ACLs for elevation identity |
| PMA-04 | **Medium** | Confirmed release-identification weakness | **Open**: constant version numbers do not distinguish new application builds |
| PMA-05 | **Medium** | Native UI/DPI acceptance and per-monitor scaling risk | **Unverified**: no real multi-screen rendering evidence |
| PMA-06 | **Medium** | CI coverage gap | **Unverified**: disposable-SQL integration suites exist but are not run in standard Actions |
| PMA-07 | **Medium** | Release-artifact freshness / evidence gap | **Open**: successful full installer artifact predates this HEAD and Action major-version merges |
| PMA-08 | **Medium** | Dependency-acceptance gate, not current-main defect | **Deferred**: PR #12 SQL Client/PDFsharp update still open |

Findings are **not eight newly discovered software bugs**. Three are confirmed source/design mismatches, one is a confirmed release-metadata weakness, and the rest require release or testing evidence. Existing numbered historic FQA/RFA/RVA/UFA/RR findings are not re-opened wholesale by this audit.

## Evidence: current source and GitHub checks

- **Latest main:** 4c25103f4930d6cbd23e2c41100f474429d1c7e9. PR [#11](https://github.com/farhan2166-creator/FMSC-Database/pull/11) (setup-dotnet v6) and [#10](https://github.com/farhan2166-creator/FMSC-Database/pull/10) (upload-artifact v7) are merged. PR [#12](https://github.com/farhan2166-creator/FMSC-Database/pull/12) remains open, so its NuGet changes are **not** part of this source freeze.
- **Desktop CI success at this exact SHA:** [run 38562380878](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38562380878). Windows Release solution build: **0 warnings, 0 errors**. WinForms test fixture built with 0 warnings/errors and ran **148** post-fix UI contract assertions; portable AuditFixChecks reported **657** checks; synthetic performance/state checks reported **32,183** checks. These counts come from the run's job logs, not an independent execution here.
- The same CI reported approved DACPAC ZIP/model/provenance, whole-rupee checks, and **273 SQL module fingerprints** passing; Windows PowerShell provenance check and disposable publish regression (including deliberately tampered DACPAC rejection) passed.
- **Security checks success at this exact SHA:** [run 38562380944](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38562380944). Gitleaks/forbidden filename scan reported no detections in the fetched history/staged change scope. This is not proof that all secrets or personal data are absent.
- **Previous complete installer run:** [run 38528371049](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38528371049), built at **08fa039080a7…**, succeeded and uploaded the private **FMSC_SMS-Complete-Windows-x64-Add-Database-Files** artifact: 633 uploaded files, approximately 161.3 MB reported artifact bytes. It predates the latest code and Action version updates; it is not the current release candidate. The MSI was verified in that older run; the current CI uses an intentional dummy MSI *only for its isolated publish test*.
- [Current complete installer workflow](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/.github/workflows/fmsc-complete-installer.yml#L1-L220) is manual, private, win-x64, and configured to exclude MDF/LDF while bundling tools and migration scripts. [Desktop CI](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/.github/workflows/fmsc-desktop-ci.yml#L1-L78) deliberately avoids live SQL and interactive GUI. A successful workflow **does not** equate to a tested installation.
- [Prior master audit](FMSC_MASTER_AUDIT.md), [historical archive](FMSC_HISTORICAL_AUDITS.md) and [end-user flow audit](FMSC_END_USER_FLOW_AUDIT_2026-10-14.md) remain the evidence trail, with original revision-specific claims retained.

## Findings with operator scenarios and safe follow-up

### PMA-01 — Older-schema migration remains enabled despite approved no-upgrade target policy (High; CONFIRMED)

**Source:** [Installer/core.ps1 database gate and migration branch](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/Installer/core.ps1#L1382-L1645); [master audit owner-approved policy](FMSC_MASTER_AUDIT.md#owner-policy-and-migration-retirement-verification--14-october-2026); [migration files](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/Migrations/6.sql#L1-L20).

**Reproduction by code path:** For an existing schema below required version 6, the installer selects *.sql migration files, checks a contiguous chain, requests operator confirmation, takes a pre-migration backup and invokes the migrations. This branch is executable today. The approved target policy documented in the master audit, by contrast, requires older/newer/structurally incompatible databases to be rejected without upgrade or reseed. Therefore code and target deployment policy differ.

**Risk:** An operator may approve an in-place migration when the desired release mode forbids it. Existing backup/rollback logic reduces risk, but does not make policy compliance automatic.

**Next action:** **Do not just delete Migrations/2–6 or disable validation.** First verify a disposable copy of the owner's final detached pair, including actual schema, version, roles, whole-rupee constraints, recovery GUID SQL_VARIANT base type and structural manifest. Only after that evidence and explicit deployment acceptance should a dedicated installer PR implement a *pre-change* exact compatibility rejection and retire no-longer-needed migration paths. Keep the original data untouched.

### PMA-02 — Installer claims compatible after a weaker post-activation check (High; CONFIRMED)

**Source:** [installer activation/version/limited final object check](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/Installer/core.ps1#L1398-L1683) versus [DatabaseCompatibility.Validate structural contract](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/Core/DatabaseCompatibility.cs#L38-L187).

**Source observation:** For a database already at version 6, the installer activates the staged EXE. Its final compatibility step subsequently checks the version and a few named tables/procedures, then prints that the database is compatible. Application startup validates substantially more: columns, foreign keys, trusted constraints, triggers, unique indexes, typed extended-property identity and other contract details. Equal version and four objects do **not** guarantee that contract passes.

**Impact:** Installation can report success and activate a new application while its subsequent startup may refuse the database. This is a source-proven **validation parity gap**; no actual failed school installation was performed here.

**Next action:** In a focused installer-only change, run an equivalent full compatibility/preflight before activation or irreversible operations, or reuse a formally synchronized manifest validator. Test missing trigger/FK/check/extended-property and structurally corrupt schema-6 disposable clones. Never repair an owner's school database silently.

### PMA-03 — Elevated installer identity versus intended Windows operator (High; ACCEPTANCE OPEN)

**Source:** [installer requires elevation and starts MSSQLLocalDB](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/Installer/core.ps1#L945-L961), [local database start](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/Installer/core.ps1#L1125-L1160), [ACL assignment](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/Installer/core.ps1#L1209-L1230), [runtime LocalDB resolution](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/Core/LocalDatabaseEngine.cs#L1-L170).

**Observation:** Database-folder ACLs remove inheritance and grant full access to SYSTEM, Administrators and **the account running the elevated installer**. The installer starts the **per-user LocalDB automatic instance** in that identity. If the day-to-day operator runs under a different standard Windows account, that account's LocalDB instance and file permissions may differ from installation's. This possibility is explicitly tracked as RR-03 and is **not an observed failure in this review**.

**Next action:** On an isolated Windows test machine, install elevated as intended administrator, then launch under the actual standard operator account. Confirm DB attach, login, backup/restore, write/delete permissions, file and SQL identities, and restart. Adjust the least-privilege deployment model only if testing shows a problem; never grant broad Everyone write permissions as a shortcut.

### PMA-04 — Application/release version remains 1.0.0 across source changes (Medium; CONFIRMED)

**Source:** [FMSC_SMS.csproj](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/FMSC_SMS.csproj#L10-L16) and [Installer/core.ps1](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/Installer/core.ps1#L1-L5); [installed EXE ProductVersion detection](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/Installer/core.ps1#L425-L452) and [same-version refresh logic](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/Installer/core.ps1#L984-L1016).

Both project metadata and installer application version are fixed to **1.0.0** despite later code/flow/workflow commits. The installer allows reinstall/refresh at the same version and only refuses versions it can prove are *newer*. Consequently two different code revisions may be indistinguishable to the version-based downgrade gate even though the Actions manifest records the source SHA.

**Next action:** Coordinate app/ProductVersion, assembly/file release identity and installer comparison from one reviewed release-version source; make it monotonic at intentional release creation, not on every debug build. Test same-version reinstall, upgrade, older artifact rollback rejection and recovery. Preserve database-schema version as a separate field.

### PMA-05 — DPI / responsive layout remains a manual acceptance risk (Medium; UNVERIFIED)

**Source:** [global AppScale factor and DPI thresholds](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/Core/AppScale.cs#L1-L63); [MainDashboardForm sizing and DPI refresh](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/MainDashboardForm.cs#L68-L200); [WorkingAreaLayout](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/Core/WorkingAreaLayout.cs#L1-L18); [dialog monitor fitting](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/Core/ApplicationWindowForm.cs#L58-L115); [WinForms smoke test mode](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/Tests/ThemeSmokeChecks/Program.cs#L1-L51).

The application intentionally uses custom adaptive geometry and AutoScaleMode.None, a shared global AppScale factor, dashboard resize/DPI handling and working-area fitting. This avoids some default scaling conflicts but also makes multi-monitor and dialog behavior important to exercise. No actual 100/125/150/200% native interactive screenshot or 1280×720/1366×768/1920×1080/2560×1440 monitor matrix was executed in this review or standard CI. A global factor does not itself prove controls are clipped; **do not record a visual defect without reproduction**.

**Next action:** Run a UI-only two-pass assessment: DPI correctness first, then responsive layouts. Check startup/login, large admission/collection pages, all modal dialogs, grids, report preview, dropdowns, Windows taskbar, keyboard navigation, moving the app between monitors and changing scaling. Record screenshots, measurements and actual repros. Do not change SQL/business logic while adjusting layout.

### PMA-06 — Live SQL integration suites are outside standard CI (Medium; COVERAGE GAP)

**Source:** [FMSC_SMS.sln](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/FMSC_SMS.sln#L1-L30) (only the application project); [desktop workflow](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/.github/workflows/fmsc-desktop-ci.yml#L35-L78) (WinForms post-fix contract, portable regression and synthetic checks); [SQL integration test projects](../Tests/MutationSqlChecks/MutationSqlChecks.csproj), [Quick Registration SQL](../Tests/QuickRegistrationSqlChecks/QuickRegistrationSqlChecks.csproj) and [Administration SQL](../Tests/AdministrationSqlChecks/AdministrationSqlChecks.csproj).

Current CI does **not** spin up a disposable SQL Server database or run these integration suites. Prior audits record historical disposable SQL work, but that work does not prove every later commit or the owner's detached snapshot.

**Next action:** Run SQL suites deliberately against a newly created, explicitly disposable, compatible test database; test fee posting and replay, quick registration/recovery, authorization, schema baseline and upgrade/rollback (if migration mode is still intentionally supported). Do not point CI or tests to the school database. Add an isolated opt-in integration job only if its safe setup/teardown can be independently demonstrated.

### PMA-07 — No complete installer artifact is proven for the latest HEAD (Medium; ACCEPTANCE OPEN)

**Source:** [old successful full installer run](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38528371049), [newest main desktop CI](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38562380878), [updated upload-artifact@v7](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/.github/workflows/fmsc-complete-installer.yml#L211-L219), [release checklist](../Docs/RELEASE_CHECKLIST.md).

The prior verified full ZIP was at **08fa039…**, while the present HEAD is **4c25103…** after workflow action major updates. The latest desktop CI passed the *disposable publish fixture*, which is deliberately **not** a signed-LocalDB-MSI complete installer test and does not upload a distributable ZIP. Therefore no current-sha complete private installer artifact or end-to-end install is proven.

**Next action:** On a frozen candidate, manually dispatch the private complete-installer workflow at the exact SHA; confirm Microsoft MSI signature, pinned go-sqlcmd SHA, migrations, FMSC_SMS application files, artifact upload/download and hashes, no MDF/LDF/BAK, then install on a disposable Windows machine with the verified detached pair. A successful artifact alone is **not** production acceptance.

### PMA-08 — Pending SQL/PDF NuGet changes are intentionally not merged (Medium; CHANGE GATE)

**Source:** [open PR #12](https://github.com/farhan2166-creator/FMSC-Database/pull/12); current main [project package references](https://github.com/farhan2166-creator/FMSC-Database/blob/4c25103f4930d6cbd23e2c41100f474429d1c7e9/FMSC_SMS.csproj#L68-L76).

Dependabot proposes **Microsoft.Data.SqlClient 7.0.2 → 7.1.1** and **PDFsharp-MigraDoc-GDI 6.2.4 → 6.5.0** across app and some SQL test projects. Its individual CI/security checks pass, but SQL provider and PDF output changes need functional evaluation of authentication/connection, timeouts, fees, reports, certificates and PDF content/printing. **No current-main defect is asserted**, because #12 is open.

**Next action:** Review changelogs/transitives, run applicable disposable SQL suites and PDF visual/print acceptance, then merge if safe. Keep it separate from installer-policy or DPI PRs.

## Area-by-area audit matrix

| Area | Inspected evidence | Current conclusion |
|---|---|---|
| Repository/PR hygiene | main/PR states, 26-commit delta after prior source freeze, Dependabot rules | #10/#11 merged; #12 open; no unapproved SQL/PDF package change in main |
| Build and portable code tests | Main SHA Actions logs, portable / WinForms fixture execution | **PASS at HEAD**; 0 compile warnings/errors, 148 UI contract, 657 portable, 32,183 synthetic/state checks |
| Security | Current security workflow and logs, gitignore/configuration policy | **PASS at HEAD** within the scanner's defined coverage; not a complete secrets/person-data proof |
| Installer / prereqs | PortableRelease targets, PowerShell scripts, full workflow and historical artifact | **Build fixture PASS; release acceptance OPEN**; policy/compatibility/Windows identity items PMA-01–03 |
| Database schema | manifest, DACPAC provenance and approved fingerprints | **Static/provenance PASS**; owner-specific MDF/LDF and SQL_VARIANT recovery identity not verified here |
| Fees and financial recovery | Relevant code/portable tests; historical SQL tests in master | **No current regression demonstrated**; live disposable SQL/injected failures must still be exercised |
| Admission / lifecycle / certificates | Merged UFA corrections and 148 focused UI contract checks | **Automated contract PASS**; actual multi-dialog, delayed SQL and operator workflow acceptance open |
| Authentication/permissions | AuthenticationService, permissions/role fixtures, historical master | **Existing safeguards observed**; real operator/user account, negative permission and lockout tests remain manual/integration gates |
| Backup, restore and rollback | DatabaseBackupManager safety backup + rollback paths, master audit | **Source safeguards exist**; exact school-like backup and restore, interrupted-failure recovery unverified |
| Reports/printing | Report UFA fixes, RDLC/report architecture, PDF package state | **Portable checks PASS**, but PDFs, physical print, long text, fonts, paper and screen scaling require native validation |
| UI / DPI | AppScale, form sizing, custom layout, theme smoke fixtures | **Structural helpers exist**, no actual matrix of monitors/DPI or full interactive screenshot evidence |
| Performance/RAM | Synthetic tests and source loading/cache helpers | **32,183 synthetic/state assertions pass**; low-end PC profiling, decoded photo RAM and long-session leaks not measured |

## Suggested remediation and verification order

1. **Freeze release scope.** Retain passed CI at SHA 4c25103…, postpone PR #12 and unrelated UI/data changes until a target-release plan is approved.
2. **Decide and enforce the approved installer policy** only after the exact owner-provided detached database *clone* is independently verified; do not remove migrations early (PMA-01). Align full compatibility checks pre-activation (PMA-02).
3. **Test the actual Windows account model and restoration safety** on a disposable test PC (PMA-03, RR-05). Prove app startup and data preservation, including negative tests.
4. **Fix coordinated release version metadata** on a narrowly scoped release-engineering PR, with upgrade/downgrade checks (PMA-04).
5. **Run UI-only DPI/responsive pass** as a separate PR and record a small/large/multi-monitor acceptance matrix (PMA-05), without touching business logic.
6. **Exercise disposable SQL and financial recovery scenarios** (PMA-06) and full physical report/print workflow.
7. **Trigger and independently verify a fresh full private installer ZIP** at the final accepted SHA (PMA-07); do not upload school database files.
8. **Review the remaining dependency PR** independently (PMA-08). Promote to GO only after the [release checklist](../Docs/RELEASE_CHECKLIST.md) has actual test evidence and owner approval.

## Guardrails / explicit non-findings

- Missing MDF/LDF files in GitHub is **intentional and correct**. Their absence is not a source-code defect or normal CI blocker. The owner supplies a verified matching *detached* pair only after downloading the ZIP for fresh-install acceptance.
- Do not use DACPAC publication to replace an attached school database or silently fix/seed student, role or finance data. Do not retire migrations until the compatibility/preservation gate is passed.
- The previously recorded nine end-user-flow findings were followed by merged targeted corrections and new native UI contract tests. Their old descriptions are **historical**, not independently established active defects today.
- Passing compilation, checksums, historical installer ZIP creation or test fixture publishing does **not** authorize production deployment. Conversely, absent interactive/manual evidence is **not** proof that a specific screen is broken.
- No application files, SQL, migrations, installer scripts, NuGet versions or workflow execution were changed by this audit. Only audit documentation and an index reference may be added.

## Evidence links

- [Head commit](https://github.com/farhan2166-creator/FMSC-Database/commit/4c25103f4930d6cbd23e2c41100f474429d1c7e9)
- [Windows desktop CI run at head](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38562380878)
- [Security run at head](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38562380944)
- [Prior successful complete-installer artifact run](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38528371049)
- [Merged PR #11](https://github.com/farhan2166-creator/FMSC-Database/pull/11), [merged PR #10](https://github.com/farhan2166-creator/FMSC-Database/pull/10), [pending PR #12](https://github.com/farhan2166-creator/FMSC-Database/pull/12)
- [Master audit](FMSC_MASTER_AUDIT.md) and [release acceptance checklist](../Docs/RELEASE_CHECKLIST.md)

**Final audit judgement:** Current source passes automated checks. The code is **not yet independently signed off for production deployment** because installer contract/policy alignment and user-account, database-clone, DPI/printing and current-artifact acceptance are incomplete.