# FMSC_SMS — Master Audit: Findings, Fixes, Decisions and Remaining Steps

**Consolidation status: DONE — 13 October 2026**  
**Release / live-workflow verification status: NOT YET COMPLETE**  
**Repository:** farhan2166-creator/FMSC-Database  
**Branch and reference HEAD reviewed:** main, ba2a00632ae387f412d384eb407ea3488455ccaf  
**Scope:** Consolidation of all 13 Markdown reports present in Audit at this reference point, cross-checked against the current migration/validation inventory. This is not a fresh full application audit or an assertion that the code has been executed in this session.

> **Interpretation of DONE:** The **document consolidation** is done. A finding marked **IMPLEMENTED / REPORTED VERIFIED** means the source audit describes a committed fix and specified tests that passed; it does **not** mean this consolidation independently reran those tests, completed Windows user acceptance, or cleared the application for production. Old source reports are retained as historical evidence. Do not change an unresolved test to DONE merely because the audit reports were merged.

## 1. At-a-glance disposition

| Category | Disposition |
|---|---|
| Master audit compilation and overlap reconciliation | **DONE** |
| Original BL-01–BL-10 business/schema findings | BL-01/02/04 reported fixed; BL-05–09 resolved by approved policy or explanation; BL-03 deliberately deferred; BL-10 legacy cleanup requires current-baseline confirmation if relevant |
| Original F-01–F-12 end-to-end findings | F-01–06/09 reported corrected or given explicit role policy; F-07 release-side artifact control implemented with known live-DB limitation; F-08 initial SQL-per-keystroke claim corrected and local filtering improved; F-10 Windows validation open; F-11 significant automated tests added and SQL checks executed, but full Windows/school-data tests open; F-12 targeted extraction, broad refactor not prescribed |
| Follow-up audit topics | Connection/About, dashboard, reliability/recovery, performance, photo storage/viewing, lightweight theme, record opening, UI flows and search/report loading reported implemented; native Windows checks and deployed-workload measurements remain open |
| Database upgrade | **Migrations/2.sql** provides the version-1-to-2 whole-rupee migration; proved against disposable SQL Server 2022 tests in the source reports. Must still be validated with the intended release snapshot and target environment |
| Owner-approved business decisions | **KEEP** as specified below; do not silently reverse |
| Production-readiness signoff | **PENDING** until the release-gate checklist in §6 is executed and recorded |

**Status vocabulary:** IMPLEMENTED = documented C#/SQL/test work exists; VERIFIED IN REPORTED TESTS = the historical report records completed checks; ACCEPTED POLICY = behavior is intentional, not a defect; DEFERRED = no change authorized; PENDING VALIDATION = native/operational acceptance not established; HISTORICAL / CHECK CURRENT BASELINE = earlier artifact-version report may not describe current HEAD.

### Important baseline distinction

The 12 October BL report originally describes a different schema inventory and refers to a version-3 legacy-ledger retirement. Later 13 October F-series reports describe the **current version-1-to-version-2 whole-rupee change**, eight constraints and a reviewed 273-module artifact. **Do not combine the old artifact hashes, schema-object counts, optional legacy cleanup path, or schema-version assumptions as if they were all one simultaneous release baseline.** The current main tree contains Migrations/2.sql; the earlier-mentioned Installer/RemoveLegacyAnnualFeeLedger.sql is not in the current Installer folder. The code search at consolidation time found legacy-ledger names only in the old audit report. Verify the exact approved artifact/contract/target database for the release rather than executing an old cleanup instruction.

## 2. Original business/schema findings — BL-01 to BL-10

### BL-01 — Ordinary admission role mismatch | IMPLEMENTED; TEST REPORTED
**Finding/impact:** New-admission numbering required Students.Administer although the admission workflow/procedure expects Students.Admit, blocking legitimate clerks.  
**Fix/disposition:** AdmissionNumberPolicy.LoadAsync now uses Students.Admit; existing-record loading retains Students.Administer. Historical admission's Enrollment.Manage and discount permissions are unchanged. Cross-build and admission-number policy checks were reported passed.  
**Follow-up:** On a disposable Windows/SQL profile, test users with Admit without Administer; with/without Fees.Adjust; historical admission and existing-record editing. Confirm SQL denials remain authoritative.

### BL-02 — Incomplete schema compatibility inventory | IMPLEMENTED; TEST REPORTED
**Finding/impact:** Startup/restore compatibility contract omitted used procedure arguments, objects and integrity safeguards; a mismatched DB could pass structural checks.  
**Fix/disposition:** Contract generator and checker were expanded to procedure parameters, relevant tables, trigger modes/events, foreign keys, checks and other required shapes; validation strengthened for trusted/enabled constraints. Later artifact review includes full approved-model and 273 module fingerprints at release. A native SQL follow-up detected a system-named LegacyAdmissionSetup check whose generated name varied; validation now compares its parent/exact expression while retaining named-check requirements. Weakened expressions were rejected in disposable native checks.  
**Limits/steps:** Startup still checks live structural contract, **not every live SQL procedure/trigger body**. Run the schema artifact verification and tampered disposable-DB startup/restore checks for every release; do not treat a DACPAC hash as proof of what a separate user-supplied MDF/LDF contains.

### BL-03 — Locally supplied SQL UserId trust boundary | DEFERRED — OWNER DECISION
**Finding/impact:** Stored procedures use caller-supplied application UserId. Where another process can use the same local SQL rights, FMSC UI authentication alone does not authenticate that SQL caller; risk is conditional on the actual local Windows/LocalDB trust boundary.  
**Decision:** Preserve the established locally trusted workstation/database architecture for now. No server/security architecture rewrite approved.  
**Steps if threat model changes:** Review Windows account isolation, SQL permissions and file ACLs on a disposable PC. If malicious local SQL clients are in scope, move privileged operations behind a trusted identity-verified service; client-set context alone is insufficient. Do not call this a proven network exploit.

### BL-04 — Chronological admission-number ordering | IMPLEMENTED / ACCEPTED POLICY
**Finding/impact:** C# chronological sequence advice was not an atomic SQL rule.  
**Decision and change:** Chronology is assistance, **not** a mandatory save invariant. Valid manually selected, unused numbers may override the suggestion. Duplicate normalization, number format, academic-year/date checks and SQL uniqueness remain mandatory. C# was adjusted; synthetic tests covered out-of-order records and valid override cases.  
**Follow-up:** Test old-year admissions, new-number/readmission, concurrent unique-number saves and duplicate refusal, without introducing a SQL chronology hard lock.

### BL-05 — Unknown historical withdrawal date | ACCEPTED POLICY
**Decision:** A historical student may be recorded as Left with an unknown/blank withdrawal date; no synthetic date or extra reason is required. Any actual entered date still has validity bounds.  
**Follow-up:** Confirm reporting presents unknown rather than inventing a departure date.

### BL-06 — Nonmonthly discount changes to unpaid charges | ACCEPTED POLICY
**Decision:** Nonmonthly assignment discount edits may update existing noncancelled charges **without completed allocations**, subject to gross cap and closed-enrollment protections. Monthly UI edits use prospective period handling and protect already-billed months. The old example of an unrestricted monthly UI edit was corrected in the source audit.  
**Follow-up:** Check earlier unpaid vs paid/partly paid charges and verify charge/assignment totals with realistic clones. Do not enforce prospective-only changes without a new decision.

### BL-07 — Best-effort bulk application of fee rates | ACCEPTED POLICY; GUIDANCE IMPLEMENTED
**Decision:** Rates take effect from the chosen month. Some eligible assignments may succeed while invalid ones fail; successful rows stay saved. Previously billed periods are not retroactively repriced.  
**Fix:** Page guidance, confirmation and partial-result language were clarified.  
**Follow-up:** Test mixed valid/invalid assignments, inspect succeeded/failed totals and review retries. For bill conflicts, choose a later effective month where appropriate; do not pretend that changing month fixes every validation error.

### BL-08 — Revisable daily collection closing | ACCEPTED POLICY
**Decision:** Approved closing is a snapshot/revision, **not a hard lock on old dates**. Authorized backdated corrections can make a day Needs review, requiring fresh review/approval; existing snapshot checks protect submit, approval and print.  
**Follow-up:** On disposable data test backdated receipt, refund/reversal, changed snapshot and reapproval. Never display the stale prior snapshot as currently reconciled.

### BL-09 — Assignment-level rather than per-charge history | ACCEPTED POLICY
**Decision:** Keep the existing assignment-level audit trail for affected discount/removal operations; do not add per-BillingCharge before/after rows solely to close this finding. Retain payment-time safeguards and existing logs.  
**Follow-up:** Verify the actual recorded operation can be traced using existing assignment history; reconsider only if compliance requirements change.

### BL-10 — Unused annual fee ledger retirement | HISTORICAL / CHECK CURRENT BASELINE
**Earlier disposition:** The BL source report says unused GetStudentAnnualFeeLedger/RDLC references were removed, with an optional existing-DB cleanup script. Its artifact/version assumptions differ from the later October 13 baseline.  
**Current cross-check:** No active code search hits for legacy ledger symbols outside the BL report; the old optional cleanup script is **not present** in the current Installer inventory.  
**Steps:** Do not run, recreate or assume legacy schema cleanup based solely on this historical narrative. If needed, compare current approved DACPAC and deployed database, confirm compatibility and produce a separately reviewed SQL migration. There is no established current reporting defect requiring a new feature.

## 3. End-to-end C#/DACPAC findings — F-01 to F-12

### F-01 — Whole-rupee financial integrity | IMPLEMENTED; SQL TESTS REPORTED
**Finding/impact:** Fractional configuration/discount amounts could produce balances normal whole-rupee Fee Collection could not settle exactly.  
**Owner policy:** Whole Pakistani rupees only; **reject fractional amounts without rounding**.  
**Fix:** Shared validation for UI inputs/TVPs/parameters; eight SQL whole-rupee check constraints added to configurations, assignments, charges, payments, allocations, manual fee posting, clearance and bank-certificate income. Migrations/2.sql preflights historical rows under locks, refuses fractions atomically (reported error 53201), installs trusted constraints and records schema version 2 only after success; rerun is idempotent.  
**Evidence:** Source reports record 75 portable money/quotation checks, artifact regression/provenance checks, native SQL Server 2022 publication, and successful/refused migration paths.  
**Remaining steps:** Check release-specific database copy for old fractional values; correct only with explicit approved financial treatment. Test UI paste, long decimal inputs, negative/boundary values, discount and receipt reconciliation in Windows. Initial database files are for first installation; later changes apply to the existing database through ordered SQL migrations, **not by swapping MDF/LDF or deploying DACPAC**.

### F-02 — Stale Quick Registration (QR) fee quote | IMPLEMENTED; SQL TESTS REPORTED
**Finding/impact:** Admin changing a fee between preview and save could cause a different charge than the clerk saw.  
**Fix:** QR save compares quoted amount, frequency, mandatory and transport flags with current values under a serializable transaction and retains locks through the existing admission procedure; changed quote aborts before insert.  
**Remaining steps:** Two-window test: A previews, B changes rate, A saves and receives a clear conflict; refresh and retry once; verify no student, enrollment, assignment or bill was partially created.

### F-03 — Lost update between two pending-record editors | IMPLEMENTED; SQL TESTS REPORTED
**Finding/impact:** Second clerk's stale form could overwrite first clerk's name/phone/address or discounts.  
**Fix:** Coherent details/assignment/state snapshot at load; snapshot compared under update/range locks in the save transaction. Stale data is rejected and the draft preserved rather than silently overwritten.  
**Remaining steps:** Concurrent A/B edits to basics, parents and discounts, plus payment vs correction. Confirm newer SQL values remain untouched on conflict.

### F-04 — Implicit QR draft discard | IMPLEMENTED; WINDOWS TEST PENDING
**Finding/impact:** Cross-record open and closing Review could reload/erase unsaved QR values.  
**Fix:** Dirty/busy navigation guards and explicit discard decisions; Review close without completed registration no longer reloads; deliberately chosen New/Clear behavior is unchanged.  
**Remaining steps:** Verify Stay/Discard and Save/Cancel paths, same/different pending record, tabs/sign-out and repeated review closing, all on actual Windows.

### F-05 — DACPAC/contract provenance mismatch | IMPLEMENTED; RELEASE CHECK REPORTED
**Finding:** Approved package hash did not match embedded artifact-provenance expectation in original audit.  
**Fix:** Record exact approved artifact SHA-256; reject package/model mismatch in offline and PowerShell publish checks, with full model/module fingerprint inventory. Source reports cite revised approved package SHA-256 45c43055a5a23647dece750dbbecf4bf4849fc56fa55f3929282da420c64f154; check against **current** artifact rather than treating that dated value as permanently authoritative.  
**Step:** Verify exact current bytes, model, contract and publish baseline on every release. Do not use an auto-approval mode to disguise unintended drift.

### F-06 — QR access with Admit but not View | IMPLEMENTED; ROLE TESTS REPORTED
**Finding:** Dashboard exposed QR to Students.Admit, but Pending search separately needed Students.View.  
**Fix/policy:** Require **both Students.Admit and Students.View** for QR entry, pending actions and redirects; retain SQL permission checks. No grants or hidden permission implications added.  
**Remaining steps:** Limited-role matrix in actual app: Admit only must not see/use list; Admit+View may enter subject to other valid permissions; Administer alone must not imply Admit.

### F-07 — Module semantics versus structural compatibility | PARTIALLY MITIGATED; KNOWN LIMIT
**Fix:** Approved release artifact/model and all reviewed SQL module definitions are fingerprinted and checked; native tests verified a weakened safeguard is rejected.  
**Open limit:** Startup structural comparison does not hash bodies in the installed live database or independently supplied MDF.  
**Step:** Independently validate target snapshot/model on a **clone** using approved release checks and representative SQL procedures. Do not promise full semantic proof for arbitrary local databases.

### F-08 — Pending-list search overhead | IMPLEMENTED; LIVE CAPACITY PENDING
**Correction:** The original suggestion that every keystroke re-queried SQL was inaccurate; records were cached.  
**Fix:** Reuse a DataView and binding, apply 200 ms debounce, correctly escape search literals, retain sort/status and flush before opening selection. Synthetic 100/2,000/10,000-row checks passed in source report.  
**Open:** Initial SQL result retrieval and per-student balance work, WinForms painting and school-scale CPU/RAM have not been profiled with production-like data.  
**Step:** Time query plans, cold/warm list load, filtering, paging, redraws and RAM before deciding about server-side paging/index changes.

### F-09 — Non-QR Pending records in QR list | IMPLEMENTED; SQL TESTS REPORTED
**Fix:** Pending list requires Pending status, QuickReference present and official AdmissionNumber absent, aligned to QR completion eligibility. Legacy list unchanged.  
**Step:** Test QR and older non-QR Pending fixtures, completion and duplicate confirmation.

### F-10 — DPI/window accessibility | GEOMETRY IMPROVED; WINDOWS VALIDATION PENDING
**Fix:** Existing window working-area clamping/scrolling was retained and extracted/tested geometrically. Other audit passes add responsive layout and scroll fixes.  
**Step:** Test primary Save/Confirm/Cancel/footer reachability, keyboard focus, nested dialogs and laptop/offset-monitor working areas at 100%, 125%, 150% DPI, including at least 1024×768, 1080×600 and 1366×768 as relevant. Geometry unit checks do **not** prove visible controls never clip.

### F-11 — Scenario / test coverage | SUBSTANTIALLY IMPROVED; RELEASE SUITES PENDING
**Fix/tests reported:** Portable money/role/contract tests, native SqlPackage publication, disposable version-1 migration refusal/success/idempotence, 17 SQL integration checks, limited-role tests, and production writer/compatibility harness compilation/execution.  
**Remaining:** Full restored-school-data script suites, Windows smoke UI, true DPI, real failure/recovery conditions and intended-version/SQL Server environment acceptance.  
**Step:** Execute §6 and preserve logs/screenshots/results; do not infer that compiled Windows tests ran on the Linux builder.

### F-12 — Large mixed-responsibility UI controllers | TARGETED REFACTOR DONE; CONTINUOUS MAINTENANCE
**Finding:** Admission and collection controllers are large, raising regression risk, not demonstrating a runtime defect.  
**Changes:** Extracted QR quote/transaction, permissions, query/search expressions, geometry, loading/cache and other isolated helpers.  
**Step:** Continue small, test-backed extraction only when it eliminates actual duplication or supports a change; avoid a wholesale redesign that could alter money/admission behavior.

## 4. Follow-up hardening — consolidated by workflow

These are reported implemented changes, not fresh recommendations to repeat them. All share the release testing limitations below.

| Workstream / source | Finding and implemented correction | Verification status / follow-up |
|---|---|---|
| Connection compatibility & About | Propagate typed database mismatch/engine failure explanations, distinguish older/newer/missing-version/structural/identity failures, choose correct restart/repair/reconnect actions; show real app, build, schema requirement and runtime in About with Copy Details | 27 compatibility checks reported; Windows recovery/About keyboard, scrolling, older/newer clone matrix still pending |
| Dashboard & navigation | Independent totals/year/readiness loading, cancellation of obsolete reads, proper zero vs unavailable handling, honest year and time wording, Reports.View-aware totals, reliable quick-action route context and sidebar selection, responsive scroll/layout, resource cleanup | 288 portable and 32,176 performance checks reported in latest dashboard pass; Windows fixture compiled but not run; real SQL costs unmeasured |
| Startup/readiness/restore | Validate attached DB before caching readiness, avoid duplicate validation, invalidate readiness and lookups after **any attempted restore** including failed recovery; preserve typed recovery details | Portable/cross-build checks reported; test restore success, incompatible restore with safety backup/recovery and failed safety recovery on disposable Windows DBs |
| Sign-in and guarded writes | Move authentication SQL/derivation off UI thread with captured credentials; log failures, avoid auto-retrying auth; expose busy writes for monthly/additional fee posting and bulk promotion to workspace guards | Build and portable checks reported; test slow SQL, closing/navigation mid-write, ambiguous commit outcomes and permissions on Windows |
| Collection recovery journal | Validate journal IDs/summaries/dates, review incomplete data, idempotent cleanup, prevent stale owner deleting a new intent; never blindly retry an uncertain payment commit | Portable tests reported; interrupted commit/reopen/two-instance tests and manual receipt/history reconciliation still pending |
| Preferences/settings/reports | Atomic flushed replacement of theme, backup and report layout files; unique temp files; no false success after denied backup-setting write; unknown theme values default safely | Tests reported; Windows denied-write/disk-full/settings persistence checks pending |
| Photo lifecycle | Reject oversized source before reading; latest-result/identity guards and draft/busy protection; later consolidate management into Admission/Student Details, with Profile shortcut; proportional 600-pixel-long-side JPEG preparation <=300 KB, no cropping/upscaling, EXIF/PNG handling, and left-click proportional zoom dialog owning a safe image snapshot | Portable/boundary tests and smoke harness compilation reported; real Windows image decoding/orientation/GDI/memory, retry/removal, themes and photo-dialog disposal pending; SQL procedure still does not enforce app-only stored-byte cap for direct SQL clients |
| Existing workflow performance | Read-only operations avoid invalidating lookup caches; cached copies outside lock with generation checks; reusable bulk result buffer; avoid unchanged derived number-key updates; fee type catalog reuse; parallel independent admission/collection reads | Portable workload supports less allocation for one loader case, **not faster timing**; actual SQL and process RAM still unmeasured |
| Profile & collection read paths | True async profile/family/payment reads; cancel superseded A→B→A profiles; fresh balances, no stale render; allocation-light admission-number comparison | Synthetic sorting and cancellation checks reported; actual Windows profile latency/SQL behavior still pending |
| Pickers, reports & paging | 180 ms picker debounce without stale Enter selection; report SQL cancellation for supported async paths; no printing cancelled results; index-based paging; avoid unnecessary full-table duplication; O(n) charge dedup | Synthetic 200k-row paging shows faster isolated last-page construction; SQL queries, UI paint and real memory not measured; test user behavior on Windows |
| Professional Simple theme | Added lightweight flat-control theme with no decorative timers/rounded-region allocations; old theme settings preserved; focus/validation remain | Contrast calculations and cross-build reported; Windows theme-switch smoke, DPI, CPU/RAM comparison pending |
| Shared UI consistency | Location breadcrumb reflects actual page/section, preserve dirty editor when reopening same record, focus first editable field, Enter on final field moves to Save without autosave, account for Payment Method in optional filters, accurate report-scope hints and no decorative warning pulse | Portable/cross-build reported; keyboard/navigation and report filter interaction Windows fixture compiled but not executed |
| Dashboard review overlap | All dashboard-specific changes above supersede earlier generic speculation about chart/totals/navigation responsiveness | Do not reopen resolved dashboard improvements without a reproduced new issue |

**Quantitative evidence caution:** Source reports contain synthetic figures (e.g. DataView filter totals at 10k rows, 20k-row loader allocation reduction, 200k-row last-page paging improvement, 10k-number comparator speedup). These are isolated local microbenchmarks, not deployed end-to-end performance/SLA or low-end-PC RAM results.

## 5. Owner-approved invariants — preserve when making further fixes

1. One application installation starts with its intended local MDF/LDF database. **After initial installation, deliver ordered, transactional SQL migrations to the existing database**, preserving data and history. DACPAC is a reference/build verification artifact, **not** the update delivery mechanism, and replacement MDF/LDF must not be used as an upgrade.
2. New student admissions respect unique official numbers and intended academic-year/old-year business rules; chronological number proposals may be manually overridden when valid.
3. Historical Left can have unknown withdrawal date; do not fabricate one.
4. Fee amounts, discounts and payments use **whole rupees**; reject fractions without silent rounding or silent historic cleanup.
5. Monthly discount changes remain prospective through their current period-specific pathway; nonmonthly unpaid-charge discount updates remain allowed per approved policy.
6. Applying a rate to existing assignments is deliberately partial/best-effort with explicit per-scope results, starting in the chosen effective month. Do not alter already issued bills.
7. Collection closings are revisable snapshots and authorized late transactions trigger Needs review and reapproval.
8. Existing assignment-level audit granularity for the designated changes is accepted.
9. QR requires both Students.Admit and Students.View in current role policy. Do not broaden SQL permissions to make UI tests pass.
10. Preserve busy/dirty navigation protection, including the deliberately different New/Clear policy. Never silently lose drafts due to implicit refresh.
11. Preserve paid-charge protections, uniqueness, transaction boundaries, payment journals and non-idempotent write caution. No automatic retry after ambiguous payment commit.
12. No automatic changes to real school/student records during audit, testing or migration verification; use separately restored disposable copies.

## 6. Remaining steps — prioritized release acceptance checklist

**Mark each checkbox complete only after executing it in the intended environment and recording date, operator, build/commit, test DB identity, outcome and evidence.** Nothing below is automatically DONE merely because the consolidated report is DONE.

### Gate A — Establish exact candidate and offline reproducibility
- [ ] Pin application commit, approved FMSC.dacpac hash, contract/model revision, schema version, migration order and installer inputs for the **actual** release candidate. Record discrepancies rather than editing the baseline silently.
- [ ] Run Windows-targeted build, Tests/AuditFixChecks, Tools/verify_schema_artifact.py --check, Tools/test_audit_schema.py and Installer/VerifySchemaArtifact.ps1; archive full results. Include relevant PerformanceChecks and older policy/contract tests.
- [ ] Confirm that required release files are present **when packaging**, not simply because development workspace lacks a database pair. Initial install and incremental existing-user upgrade have different rules. Do not push a modified school database to resolve an audit mismatch.

### Gate B — Verify migration and database compatibility on clones
- [ ] Restore a copy to an isolated **FMSC_AuditChecks_<suffix>** catalog and separate MDF/LDF paths, never into live school catalog. Confirm source version and sample historical balances.
- [ ] From version 1, exercise Migrations/2.sql fractional-row refusal: source amount/version/constraints remain unchanged. Correct the fixture manually; rerun; confirm eight enabled/trusted whole-rupee checks and version 2; rerun again safely.
- [ ] Validate approved DACPAC/contract against this release's database with SqlPackage/native tool as needed for **testing only**; confirm structural mismatch and weakened critical checks are rejected. Never publish DACPAC to the user's in-place existing database as the release upgrade.
- [ ] Run Tests/QuickRegistrationSqlChecks (production writer, limited-role, stale quote/edit, actor ID, pending eligibility) with guarded clone connection; run Validation/AdmissionWorkflowChecks.ps1 and Validation/StudentCorrectionChecks.ps1 on their disposable fixtures.
- [ ] Repeat target-specific compatibility/restore checks against the actual supported Windows SQL Server/LocalDB version and a representative **copy** of the intended initial snapshot. SQL Server 2022 Developer synthetic pass alone does not prove behavior on the installed environment.

### Gate C — Multi-operator workflow and financial integrity
- [ ] Two independent clerks: stale QR quote/edit, complete versus edit/cancel/payment, new family + sibling, duplicate registration, old academic year and admission number, direct admission, re-admission, transfer and promotion.
- [ ] Test concurrent monthly posting, two collectors on the same charge, family payment, interrupted or lost commit acknowledgement, failed write with journal recovery, partial bulk-rate application, nonmonthly discount edits around paid charges, and late daily-closing revisions.
- [ ] After each test compare student/enrollment identity, no duplicate official numbers/charges, ownership of fees/allocations, gross minus discount minus completed payments = outstanding, correct reversal/refund behavior, permissions and traceable audit/history. No automatic write replay on ambiguous commit.

### Gate D — Native Windows behavior, DPI and usability
- [ ] Run Tests/ThemeSmokeChecks on a disposable Windows profile in Professional Simple and an existing theme. Verify dashboard, About, connection guidance, auth, photo preparation/zoom, shared editor focus, workspace breadcrumbs, report filter state, dirty/busy guards and report/picker cancellation.
- [ ] Inspect at 100/125/150% scaling and 1024×768, 1080×600, 1366×768 plus intended typical monitor, checking all Save/Confirm/Cancel/footer controls, dialogs, keyboard-only tab/Enter/Escape and active/offscreen windows.
- [ ] Try real EXIF photos/transparent PNG/large and detailed JPEGs, slow A→B→A student selections, denied or full preferences storage, disconnection during sign-in, restore and writes. Confirm no stale form values or stuck busy state.
- [ ] Run backup/restore safety paths on throwaway DB only, including cache revalidation, incompatible restore recovery and failure of safety recovery.

### Gate E — Actual workload, resource use and release decision
- [ ] Profile cold/warm startup, dashboard, 100/2,000/10,000+ row search, admission open/save, family/individual collection, reports/export/print, many page switches and sustained use. Record SQL latency/query plans, GUI responsiveness, GC, private bytes/working set and CPU on a minimum-spec school PC.
- [ ] Investigate measured bottlenecks (e.g. pending-list balance aggregation or dashboard's unused finance computations); avoid new complexity without evidence.
- [ ] Record role matrix and OS/LocalDB ACL test under the currently accepted local trust model. Document BL-03 as consciously deferred.
- [ ] Sign off backup/restoration, migration success and rollback/recovery instructions, logs/screenshots, smoke-test results and unresolved exceptions **before** stating production readiness.

## 7. Useful commands and guardrails

From repository root, reproduce the checks as documented in Validation/AuditFixChecks.md. The exact commands there are authoritative for the repository version; examples:

    dotnet build --no-restore -p:EnableWindowsTargeting=true -v minimal
    dotnet run --project Tests/AuditFixChecks
    dotnet run --project Tests/PerformanceChecks/PerformanceChecks.csproj -- --profile
    python Tools/verify_schema_artifact.py --check
    python Tools/test_audit_schema.py

On Windows for native UI checks:

    dotnet run --project Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj

For guarded disposable native SQL checks, use the FMSC_AUDIT_CONNECTION environment variable with the required FMSC_AuditChecks_<suffix> database; see Validation/AuditFixChecks.md. It rejects unsafe production database selection. **Never run destructive tests on live student data.**

**Release update approach:** Existing user's original MDF/LDF remain in place. Deliver the required sequential SQL migration(s), with preflight, backup and version checks, never a DACPAC replacement or overwritten database files. Artifact fingerprint review remains a release/build control.

## 8. Source-to-master traceability (13 reports)

All 13 original audit reports were consolidated and removed from the current main branch's Audit folder. Their original contents remain retrievable from the pinned historical Git commit linked below. This master is the only active report in Audit; the historical reports retain supporting details and test evidence.

1. [12 Oct business logic / schema / C# cross-audit](https://github.com/farhan2166-creator/FMSC-Database/blob/ba2a00632ae387f412d384eb407ea3488455ccaf/Audit/2026-10-12_Business_Logic_and_Schema_CSharp_Cross_Audit.md) — BL-01..BL-10 and owner policy decisions.
2. [13 Oct confirmed findings and fix verification](https://github.com/farhan2166-creator/FMSC-Database/blob/ba2a00632ae387f412d384eb407ea3488455ccaf/Audit/2026-10-13_Confirmed_Findings_Fix_Verification.md) — F-01..F-12 correction, whole-rupee plan, initial testing.
3. [13 Oct remaining findings and native verification](https://github.com/farhan2166-creator/FMSC-Database/blob/ba2a00632ae387f412d384eb407ea3488455ccaf/Audit/2026-10-13_Remaining_Findings_and_Runtime_Verification.md) — native SQL, role/list checks and fixed test-discovered defects.
4. [Connection guidance and About](https://github.com/farhan2166-creator/FMSC-Database/blob/ba2a00632ae387f412d384eb407ea3488455ccaf/Audit/FMSC_Connection_About_2026-10-13.md).
5. [Dashboard review](https://github.com/farhan2166-creator/FMSC-Database/blob/ba2a00632ae387f412d384eb407ea3488455ccaf/Audit/FMSC_Dashboard_Review_2026-10-13.md).
6. [Cross-application reliability / deployment hardening](https://github.com/farhan2166-creator/FMSC-Database/blob/ba2a00632ae387f412d384eb407ea3488455ccaf/Audit/FMSC_Deployment_Hardening_2026-10-13.md).
7. [Existing workflow performance hardening](https://github.com/farhan2166-creator/FMSC-Database/blob/ba2a00632ae387f412d384eb407ea3488455ccaf/Audit/FMSC_Performance_Hardening_2026-10-13.md).
8. [Photo storage, proportional preparation and photo zoom](https://github.com/farhan2166-creator/FMSC-Database/blob/ba2a00632ae387f412d384eb407ea3488455ccaf/Audit/FMSC_Photo_Storage_2026-10-13.md).
9. [Professional Simple theme](https://github.com/farhan2166-creator/FMSC-Database/blob/ba2a00632ae387f412d384eb407ea3488455ccaf/Audit/FMSC_Professional_Simple_Theme_2026-10-13.md).
10. [Profile opening, collection reads and sorting](https://github.com/farhan2166-creator/FMSC-Database/blob/ba2a00632ae387f412d384eb407ea3488455ccaf/Audit/FMSC_Record_Opening_Hardening_2026-10-13.md).
11. [End-to-end read-only C#/DACPAC audit](https://github.com/farhan2166-creator/FMSC-Database/blob/ba2a00632ae387f412d384eb407ea3488455ccaf/Audit/FMSC_SMS_End_to_End_ReadOnly_Audit_2026-10-13.md) — original F-01..F-12 and acceptance runbook.
12. [Existing UI flow and consistency](https://github.com/farhan2166-creator/FMSC-Database/blob/ba2a00632ae387f412d384eb407ea3488455ccaf/Audit/FMSC_UI_Flow_Consistency_2026-10-13.md).
13. [Workflow/search/collection/report loading](https://github.com/farhan2166-creator/FMSC-Database/blob/ba2a00632ae387f412d384eb407ea3488455ccaf/Audit/FMSC_Workflow_Loading_Hardening_2026-10-13.md).

Supplementary current operational reference: [Validation/AuditFixChecks.md](../Validation/AuditFixChecks.md), [Migrations/2.sql](../Migrations/2.sql) and [AGENTS.md](../AGENTS.md). Note that AGENTS.md is release-packaging context, not authority to overwrite installed user data during updates.

---

**FINAL STATUS — DOCUMENT CONSOLIDATION: DONE.** All thirteen reports were combined into this prioritized, traceable master with explicit findings, existing remedies, accepted policies and actionable remaining tests. The 13 superseded reports have been removed from the current Audit folder and remain available in Git history. **FIX DEPLOYMENT / WINDOWS & RELEASE ACCEPTANCE: NOT CLAIMED DONE.** No application code, SQL, DACPAC, migration or installer artifact was changed by this consolidation; only audit documentation was updated, with the superseded individual reports deleted from the active branch.
