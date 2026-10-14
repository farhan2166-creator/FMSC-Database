# FMSC_SMS — Independent Release-Readiness and Production Deployment Audit

**Review date:** 14 October 2026  
**Repository:** `farhan2166-creator/FMSC-Database`  
**Pinned code candidate:** `fa44111b35ff9534ddaeae2ac865bbdd10d5df2a` (includes **RFA-01 Quick Registration internal tab guard**).  
**Scope:** Release packaging and installation, deployed database compatibility and migrations, financial integrity/recovery, access control, prior functional findings, test coverage, rollback, operator usability, and release evidence.  
**Method:** GitHub source/configuration/documentation review, source-path tracing, reported test evidence cross-check, and GitHub CI/status inspection. **No Windows application, installer, physical printer, live school SQL or restored school-like database was run in this audit.**  
**Modifications:** This audit Markdown file only; no source code, installer, SQL, migrations, tests, permissions or school database is modified.

**Subsequent implementation evidence:** [Post-fix progress](FMSC_POST_FIX_IMPLEMENTATION_PROGRESS_2026-10-14.md) records the committed RFA-02/03 corrections, ordered snapshot upgrades/restore, additional-fee SQL replay checks and successful focused Windows CI. This report below retains its original `fa44111` source freeze and deployment acceptance scope.

> **RELEASE DECISION: NO-GO for an unqualified school-wide rollout at this review baseline.** This is an evidence- and workflow-based decision, **not** a claim that the reviewed DACPAC is corrupt or that the 25 earlier fixes failed. The necessary build/schema foundations are strong; the final acceptance evidence and several user-input/installer guards are still outstanding. A supervised test installation using a **disposable or verified restored clone** is appropriate once the exact package is frozen.

## A. Executive release gate

| Area | Status | Release consequence |
|---|---|---|
| Source builds and native SQL/DACPAC consistency | **REPORTED PASS** | The schema-6 artifact report claims clean builds, 72 QR SQL checks, 32 mutation checks, 16 Python checks, backup/restore validation; not independently rerun here |
| Approved schema 6 baseline, numbered migration chain | **SOURCE REVIEWED; TARGET TEST PENDING** | The schema-6 DACPAC matches the updated model and migrations 4–6 in reported disposable tests; the target deployment MDF/LDF has not been tested in this session |
| Existing financial request recovery | **SOURCE PRESENT; FAILURE-INJECTION ACCEPTANCE PENDING** | Quick Registration, adjustments and bank certificate creation have exact-request/reconciliation protections; physical outage/restart and target permissions untested |
| Earlier FQA-01–25 code fixes | **IMPLEMENTED / TESTED AS REPORTED** | Their reported portable/SQL coverage is not a substitute for WinForms or school-clone acceptance |
| New RFA-01 | **SOURCE-FIXED at `fa44111`** | Quick Registration now blocks internal tab switching while busy or holding an unresolved request; fixture added but not run here |
| New RFA-02, RFA-03 | **OPEN in pinned source** | Certificate drafts/modified certificate data can be discarded by certain user actions |
| Fresh install, existing-data upgrade and rollback | **NOT END-TO-END EXECUTED** | Release package must be run on a clean Windows PC and on a restored database clone, including failure recovery |
| Permission/login and standard Windows account | **CONDITIONAL OPERATIONAL RISK** | The installer ACL and per-user LocalDB identity must be accepted/tested for the actual operator account model |
| Windows GUI, reports, printer, accessibility | **NOT EXECUTED HERE** | Cannot issue end-user usability and physical print sign-off |
| Automated CI for pinned commit | **NO RUN/STATUS FOUND** | GitHub Actions reported zero workflow runs; GitHub combined commit status reported no checks. This does not invalidate manually reported checks, but release proof is not independently archived in CI |

**Classification:** “Confirmed code-level gap” proves missing logic in the pinned code; “conditional deployment risk” requires a specified machine/account arrangement; “release gate” means evidence still needed, not necessarily a source defect.

## B. Must-close or explicitly scope before production

### RR-01 — Unsaved *new* Regular Student Certificate draft can be lost [P2/P3, source-confirmed]

**Evidence:** [Pages/Students/Certificates/RegularStudentCertificatePage.cs L1–17](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L1-L17), [Pages/Students/Certificates/RegularStudentCertificatePage.cs L107–116](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L107-L116), [Pages/Students/Certificates/RegularStudentCertificatePage.cs L191–233](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L191-L233), [Pages/Students/Certificates/RegularStudentCertificatePage.cs L691–745](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L691-L745).

`HasUnsavedChanges` checks `HasCertificateEdits`, which only becomes true when `selectedRegularStudentCertificateId > 0`. An operator choosing a student **without** an existing certificate can change the new certificate date and then change students, click Next Student, refresh or leave; this local date draft has no dirty baseline and can be discarded without warning. This is the still-open **RFA-02**, not a new database-write defect.

**Required fix/test:** Track the pre-create date baseline separately from an existing-certificate editor snapshot; reuse it in the student picker, Next, Refresh and shell leave. Test dirty A→B (Stay/Discard), new date reverted to default, Save/Create success, existing certificate edit unchanged. Test the actual Windows controls, not only `HasUnsavedChanges` via reflection.

### RR-02 — “New” bypasses unsaved edits on existing certificates [P2/P3, source-confirmed]

**Evidence:** [Pages/Students/Certificates/RegularStudentCertificatePage.cs L629–637](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L629-L637), [Pages/Students/Certificates/RegularStudentCertificatePage.cs L724–745](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L724-L745), [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L660–680](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L660-L680), [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L790–808](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L790-L808).

In Regular Student Certificate, `StartNewCertificate()` calls `ClearForm(confirmDiscard:false)`; in Bank Account Opening Certificate, `StartNewCertificate()` clears the selection and fields without `ConfirmDiscard()`. In both, the operator may edit an existing record and click New, losing the unsubmitted changes. Other actions already have discard checks. This is the still-open **RFA-03**.

**Required fix/test:** Apply the existing dirty/discard contract to New without adding extra prompts for clean records. Test modified existing certificate→New→Stay and Discard, success→New, active save and unresolved bank-certificate recovery.

### RR-03 — Installer Windows account and LocalDB identity model [P1 if different operator account; conditional]

**Evidence:** [Installer/core.ps1 L940–950](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Installer/core.ps1#L940-L950), [Installer/core.ps1 L1209–1230](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Installer/core.ps1#L1209-L1230), [DatabaseHelper.cs L6–23](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/DatabaseHelper.cs#L6-L23), [Core/LocalDatabaseEngine.cs L12–56](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Core/LocalDatabaseEngine.cs#L12-L56).

Installation requires Administrator rights. The installer removes inherited access on `C:\ProgramData\FMSC_Database` and grants directory access only to **SYSTEM**, **Administrators**, and **the Windows identity running the installer**. LocalDB's `MSSQLLocalDB` automatic instance is per Windows user. If an administrator installs using one Windows account and a **different standard Windows account** subsequently runs FMSC, the standard account may lack database-file permission and will have a distinct LocalDB database attachment. This is a concrete trust/deployment mismatch for that scenario, not proof of failure if the application is always installed and run by one authorized Windows account.

**Acceptance:** Choose and document the approved Windows account model, then actually install elevated and run as the intended operator. Check file ACLs, LocalDB instance identity, attach, startup compatibility, SQL permissions and backup/restore. If cross-account operation is required, design access with a deliberate least-privilege ACL and verified DB instance/identity strategy; **do not simply grant Everyone full access to school data**.

### RR-04 — Installer's final “database compatible” decision is weaker than application startup [P2, confirmed installer validation gap]

**Evidence:** [Installer/core.ps1 L1627–1673](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Installer/core.ps1#L1627-L1673), [Core/DatabaseCompatibility.cs L48–95](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Core/DatabaseCompatibility.cs#L48-L95), [Core/DatabaseCompatibility.cs L105–184](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Core/DatabaseCompatibility.cs#L105-L184).

At install completion, the installer checks the recorded version and a small set of older mandatory procedure/column probes; it then prints **“DATABASE IS COMPATIBLE”**. The application itself checks a much broader inventory: constraints, trusted foreign keys, procedure parameters, uniqueness safeguards, database recovery identity and other required schema. A malformed database could record version **6** and contain the few legacy probes, yet fail application startup because a required v6 index, property, constraint or procedure is missing/disabled. This creates a false installer-success condition.

**Required fix/test:** Make installation success dependent on the same production compatibility contract (e.g. a supported offline SQL validator operating on the actual attached database, not a version-only duplicate). At minimum, run a post-install application startup probe before proclaiming full compatibility, without auto-modifying an incompatible school database. Negative-test a disposable version-6 copy with a missing `MutationRequests` FK and wrong `FMSC_QuickRegistrationScope` property type. Ensure both installer and runtime refuse with helpful actionable diagnostics.

### RR-05 — Exact detached database snapshot has not been deployment-validated [P1 release gate, not a missing development file defect]

**Evidence:** [Schema-6 DACPAC verification](FMSC_DACPAC_SCHEMA6_VERIFICATION_2026-10-14.md), [Core/DatabaseCompatibility.cs L110–130](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Core/DatabaseCompatibility.cs#L110-L130), [owner release-packaging policy](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/AGENTS.md).

The schema-6 DACPAC was independently compared in an earlier reported verification, with a disposable publish and SQL integration tests. **However, that report explicitly notes that DacFx publishes the recovery GUID property as an `nvarchar` SQL variant**, while application compatibility requires **`uniqueidentifier`**. Its disposable DB had the GUID's variant type normalized as a test-only step. Therefore **direct DACPAC publication should not be assumed to produce a compliant school deployment snapshot**. This matters only if the release MDF/LDF is made that way, not necessarily if the owner's detached snapshot was migrated normally.

The owner-established workflow is to supply a detached MDF/LDF pair **when preparing the package**. Their absence from the development repository is **expected and not a readiness bug**. The release gate is to validate the **exact pair selected for shipment**, check schema version 6, database-property variant type, valid school data, ACLs and direct launch. Avoid replacing existing school data or silently patching the detached pair from the DACPAC.

### RR-06 — User-facing Windows and financial-failure acceptance [P1 release gate; not a confirmed new source defect]

The available portable, SQL and WinForms build reports are substantial, but **the compiled native Windows GUI fixtures, physical print paths and real user interaction have not been independently executed in this review**. For release, run the executable with the exact shipped self-contained files and a disposable target-compatible database. See the acceptance matrix below.

**Specific pending case:** **RVA-01** remains a verification item: after `PostAdditionalFee` commits and loses its reply, attempt the same request with unchanged/changed fee amount/selection and after a restart. The C# page retains the request ID after an exception and changes it after an acknowledged success; do **not** infer safe or unsafe SQL replay without testing the stored-procedure/request-marker behavior. Also complete **RVA-02**: actual ordered v5→v6 upgrade and rollback from a restored school-like v4/v5 clone.

## C. Positive controls and verified-in-source safeguards to preserve

1. **Packaging profile exists:** [PortableRelease.pubxml](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Properties/PublishProfiles/PortableRelease.pubxml) targets Windows x64, self-contained .NET Desktop Runtime, no trimming/single-file, and output folder matching the installer's `FMSC_SMS` directory. Do not mistakenly claim the publish path is missing when using this profile.
2. **Optional snapshot is intentional:** `ValidateReleaseAssets.ps1` checks pair completeness, nonempty/lock-exclusive files and LocalDB prerequisite. App-only updates are valid. **A fresh install**, in contrast, explicitly requires a detached compatible snapshot.
3. **Staged activation:** `Installer/core.ps1` copies new application files into a staging directory and checks file inventory and sizes; activation uses directory moves with a retained rollback folder. A failed activation attempts to reattach the previous directory.
4. **Migration chain safety:** Installer requires numbered contiguous migration files, creates `BACKUP DATABASE ... WITH CHECKSUM`, runs `RESTORE VERIFYONLY WITH CHECKSUM`, and checks each recorded version. Schema-change scripts use transactions and `XACT_ABORT`.
5. **Existing user data:** The installer recognizes the existing MDF/LDF and does not copy the release template on top of those files. It refuses inconsistent registered MDF/LDF paths.
6. **Current schema:** Application, installer and migration requirements target version **6**. The new [DACPAC schema-6 verification](FMSC_DACPAC_SCHEMA6_VERIFICATION_2026-10-14.md) reports 60 tables, 232 procedures, 34 triggers, 273 SQL-module fingerprints, 72 QR SQL checks and 32 mutation-recovery checks, as well as backup/restore. This is reported evidence, not independently rerun by this audit.
7. **Money/write durability:** Confirmed write requests and unknown-outcome recovery use transaction-backed request records in QR and selected financial/certificate operations. Report/CSV/PDF exports use atomic staged output in the previously audited paths. Continue protecting no-rounding whole-rupee invariants and no-blind-retry behavior.
8. **Functional correction status:** Earlier **FQA-01–FQA-25** have committed source changes. **RFA-01** is fixed at this candidate baseline; two certificate draft issues remain. Owner-approved two-role model is Administrator + Accountant; no unapproved role or permission widening is proposed.
9. **No automated CI status:** GitHub's workflow-run query returned **0 runs**, and the commit-status query returned an empty status list for the previous commit; no check-run evidence was surfaced. This is a release evidence gap, **not** a claim that builds/test commands were never run locally.

## D. Mandatory release acceptance matrix (use disposable copies)

| Gate | Minimal test / proof required | Pass condition |
|---|---|---|
| G1 Source freeze | Record exact Git SHA, .NET SDK, NuGet restore log, `PortableRelease` publish command, final EXE/hash and all files | Same inputs produce a complete, runnable release, no source drift |
| G2 Release package | Publish using `PortableRelease`; inspect `FMSC_SMS_Release/FMSC_SMS`, `Tools/SqlLocalDB.msi`, scripts and numbered migrations; verify checksum/permissions | Every referenced file present; no wrong/stale DLL, report, migration or resource |
| G3 Fresh installation | Clean Win10/11 x64 VM; **exact owner-supplied detached test MDF/LDF**; execute `Install FMSC_SMS.bat` elevated | App launches under intended operator account; login and schema-6 compatibility succeed; existing data not silently replaced |
| G4 In-place update | Restore representative school version-4 and -5 clones; start app before update, close sessions; run 5→6 or 4→5→6 | Verified backup, exact ordered migrations, preserved students/payments/roles, correct v6 startup |
| G5 Failure and rollback | Refuse migration permission; fail a numbered migration; drop database connectivity after commit; fail app activation; restore backup/previous app on test VM | No ambiguous success; database and application return to a **mutually compatible** usable state, with instructions and logs |
| G6 Security / access | Administrator, Accountant, denied-permission tests; installer admin vs standard Windows user test; revocation and sign-in again | Exact permitted menus/procedures; no unauthorized writes; correct LocalDB identity and data-file ACL |
| G7 Financial and admission workflows | New QR and sibling → review/confirm → first invoice → fee posting → collection → receipt → statement; refund/reversal/closing; replay/timeout | Exact student/charge/payment identities and totals, one write per authorized request, no double payment on retry |
| G8 UI and certificates | Execute native WinForms smoke fixtures; test certificate RFA-02/03 before sign-off; use 100/125/150% DPI and narrow laptop window | All buttons, focus paths, edits, discard/close guards, scrolling, form states and keyboard accessible |
| G9 Printing/exports | Physical A4 / thermal receipts, RDLC report preview/PDF and CSV, missing logo/template/printer, overwrites and locked files | Legible correct fields/students, status truthful, existing exports protected after failure |
| G10 Load/continuity | Realistic 100/1k/10k record searches; multi-student statement, concurrent two-operator actions, backup/restore and cold restart | Acceptable measured latency/RAM, no frozen UI, clean recovery, no silent data loss |
| G11 Sign-off archive | Attach timestamped logs, test database IDs, report samples, SHA/manifest, installer screenshots, negative-test results and owner approvals | Traceable release evidence recorded; each remaining gate marked pass/known accepted exception |

**Suggested commands on the approved Windows build machine** (adapt paths/connection only for the isolated test environment):

```powershell
dotnet publish FMSC_SMS.csproj -p:PublishProfile=PortableRelease -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File Installer/VerifySchemaArtifact.ps1 -ProjectDirectory .
python Tools/verify_schema_artifact.py --check
dotnet run --project Tests/AuditFixChecks
dotnet run --project Tests/PerformanceChecks
dotnet run --project Tests/ThemeSmokeChecks
```

For SQL harnesses, configure their **documented disposable-database connection arguments/environment** and review the fixture guards first. Do **not** point SQL integration scripts, migration tests or fault-injection harnesses at the school database. Build success alone is not a Windows UI test; test commands must run to completion and leave saved logs.

## E. Versioning, packaging and process improvements (non-blocker unless release policy demands them)

- The project/installer currently advertise **application version 1.0.0** ([FMSC_SMS.csproj L5–17](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/FMSC_SMS.csproj#L5-L17); [Installer/core.ps1 L1–8](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Installer/core.ps1#L1-L8)). The installer blocks a numerically older release but accepts a different binary with the **same** version. Before shipping successive versions to schools, assign an explicit unique release version/build identifier and verify it in About, installer log and rollback manifest.
- Staging inventory currently compares **paths and lengths**, not file hashes ([Installer/core.ps1 L731–805](https://github.com/farhan2166-creator/FMSC-Database/blob/fa44111b35ff9534ddaeae2ac865bbdd10d5df2a/Installer/core.ps1#L731-L805)). For a tamper- or corruption-resistant release manifest, record SHA-256 per shipped application file and verify the staged bytes, not only length. This is hardening, not proof of current corruption.
- Add **repeatable Windows CI** or a signed/manual release evidence archive. There was no current GitHub Actions execution/status in the API results. Build/test logs from the prior DACPAC review should be attached to this exact candidate and rerun after each code change.

## F. Decision and remediation order

**NO-GO for broad operational deployment until G1–G9 have documented pass results or explicitly scoped owner-approved exceptions.** This is especially important for financial transactions, data-file permissions, upgrade rollback and user-visible data-entry loss. G10/11 provide operating evidence and formal sign-off.

Recommended order:

1. **Finish the two open C# certificate guards:** RFA-02 and RFA-03, add real WinForms behavior tests and source-verify them.
2. **Resolve installer-compatibility truthfulness:** RR-04; validate full schema-6 contract before declaring success and test the specific real Windows account model (RR-03).
3. **Freeze release candidate & publish the exact supported profile**; validate the shipped **owner-provided** detached MDF/LDF snapshot. Do not require snapshot files in the development repository.
4. **Run Windows native GUI, isolated SQL replay/migration, real package/install/restore and printing tests** with persisted logs. Include failure conditions and two-operator/permission cases.
5. **Make the GO decision only from that evidence**. If another Codex commit lands, re-baseline the source and rerun tests affected by it. Do not silently carry forward a prior candidate's release approval.

**Final limitations:** Not all screens or buttons were operated; no production database or Windows installer was run here; no fresh source build/test commands were executed in this environment. The evidence and code-gap classifications above are grounded in source and previously reported tests. Git commit `fa44111b35ff9534ddaeae2ac865bbdd10d5df2a` is the immutable baseline for this decision.
