# FMSC_SMS — Consolidated Historical Audit, Fixes, Decisions and Release Requirements

**Consolidated:** 14 October 2026  
**Repository:** `farhan2166-creator/FMSC-Database`  
**Original-source snapshot:** `b5cb9342968eebe76f919538ebdbf2dc4662a187` (the last commit retaining all five original reports)  
**Last functional source-fix baseline:** `6e4fca3650a429e9b6cc213cbfa0cd09383756a6`. A later DACPAC commit is not itself a Windows or database acceptance sign-off.  
**Scope:** One indexed record superseding five old findings/fix-status reports. This is a documentation consolidation; it does not rerun tests or modify any C#, SQL or user data.

> **Do not mistake historical wording for current status.** Original reports were authored at different commits and sometimes describe problems as "NOT FIXED"; subsequent fix records below take precedence. Similarly their schema versions, photographed image-byte limits, SQL artifact hashes and counts belong to their original dates. The current application requires **schema version 6**. **Native Windows, target LocalDB, representative-data upgrade and failure-injection acceptance remain pending.**

## 1. Status index and separation from the new active audit

This file combines **64 historical finding IDs** across BL-01–10 (10), F-01–12 (12), FR-01–09 (9), IA-01–03 (3), DP-01–04 (4), RT-01 (1), and FQA-01–25 (25). Items may be **source-fixed**, **owner-accepted**, **deferred**, **historical**, or **pending release validation**; those statuses are intentionally kept distinct.

| Series | Consolidated disposition |
|---|---|
| BL-01–10 | BL-01/02/04 fixed or owner-policy aligned; BL-05–09 accepted policies; **BL-03 deliberately deferred**; BL-10 historical and requires the current release baseline if relevant |
| F-01–12 | Main issues source-addressed; **F-07** has known live-procedure semantic-check limitations; **F-08** needs actual workload measurement; **F-10/11** need native Windows/release testing; F-12 is ongoing small refactor work |
| FR-01–09 | FR-01–05 source-addressed; FR-06 explicitly accepted by owner; FR-07 decoded-image memory and FR-08 SQL photo-cap policy are not proven closed; FR-09 release checks remain |
| IA-01–03 | Implemented in the later owner-approved Users/Roles editor overhaul, after initial deferral |
| DP-01–04 and RT-01 | Implemented in source; RT-01 retired safely with an explicit verifier failure and supported replacement |
| **FQA-01–25** | **All 25 source-fixed or resolved by owner decision** in independent fix commits and documented test reports; *actual Windows interaction remains unverified in this consolidation* |

**Separate current open report:** [Independent post-fix regression audit (RFA-01–03 and RVA-01–02)](FMSC_POST_FIX_INDEPENDENT_REGRESSION_AUDIT_2026-10-14.md). **Do not consider its new findings fixed or absorbed here.**

**Meaning of statuses:** "Source-fixed" = a committed implementation exists; "reported tests passed" = the fix report records checks, not that this consolidation reran them; "owner-accepted" = intended behavior; "deferred" = change not approved; "pending" = no native deployed acceptance. In particular, older photo/migration/role discussions are *not* current instructions to change SQL or school records.

## 2. Original five audit files — immutable historical access

These five files will be deleted from **current `main` only after this consolidated file is checked**. Git history preserves their full contents and edits, including all original line evidence and prior baselines. Their permanent pre-deletion versions are:

1. [FMSC_FOLLOWUP_CODE_REVIEW_FINDINGS_2026-10-13.md](https://github.com/farhan2166-creator/FMSC-Database/blob/b5cb9342968eebe76f919538ebdbf2dc4662a187/Audit/FMSC_FOLLOWUP_CODE_REVIEW_FINDINGS_2026-10-13.md)
2. [FMSC_FUNCTIONAL_WORKFLOW_DEEP_AUDIT_2026-10-13.md](https://github.com/farhan2166-creator/FMSC-Database/blob/b5cb9342968eebe76f919538ebdbf2dc4662a187/Audit/FMSC_FUNCTIONAL_WORKFLOW_DEEP_AUDIT_2026-10-13.md)
3. [FMSC_FUNCTIONAL_WORKFLOW_FIX_PROGRESS_2026-10-13.md](https://github.com/farhan2166-creator/FMSC-Database/blob/b5cb9342968eebe76f919538ebdbf2dc4662a187/Audit/FMSC_FUNCTIONAL_WORKFLOW_FIX_PROGRESS_2026-10-13.md)
4. [FMSC_INDEPENDENT_SECURITY_DURABILITY_RELEASE_AUDIT_2026-10-13.md](https://github.com/farhan2166-creator/FMSC-Database/blob/b5cb9342968eebe76f919538ebdbf2dc4662a187/Audit/FMSC_INDEPENDENT_SECURITY_DURABILITY_RELEASE_AUDIT_2026-10-13.md)
5. [MASTER_AUDIT_FINDINGS_FIXES_STEPS_DONE_2026-10-13.md](https://github.com/farhan2166-creator/FMSC-Database/blob/b5cb9342968eebe76f919538ebdbf2dc4662a187/Audit/MASTER_AUDIT_FINDINGS_FIXES_STEPS_DONE_2026-10-13.md)

**Current operational invariants:** No replacement of existing school MDF/LDF with DACPAC. Require verified backups and closed app sessions before applying ordered SQL migration scripts. The current code requires version **6**, including version **5 → 6** upgrades where applicable. Use a restored disposable data copy for failure/migration tests. Preserve optional historical withdrawal dates, manual valid admission numbers even when nonchronological, whole-rupee no-rounding rules, paid-charge protections, assignment-level history, revisable daily closing and best-effort effective-month bulk rates. The local workstation SQL trust boundary remains a deliberate deferred architecture decision. New prepared student photos are now capped at **80 KB in C#**; FR-08's SQL-side cap was not automatically introduced. Fresh admission/registration write acknowledgments require reconciliation, not blind retry.

---

## A. Original business/schema and end-to-end implementation decisions (BL and F)

These are master-audit descriptions with date-pinned statuses, preserved verbatim in substance.

### 1. At-a-glance disposition

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

#### Important baseline distinction

The 12 October BL report originally describes a different schema inventory and refers to a version-3 legacy-ledger retirement. Later 13 October F-series reports describe the **current version-1-to-version-2 whole-rupee change**, eight constraints and a reviewed 273-module artifact. **Do not combine the old artifact hashes, schema-object counts, optional legacy cleanup path, or schema-version assumptions as if they were all one simultaneous release baseline.** The current main tree contains Migrations/2.sql; the earlier-mentioned Installer/RemoveLegacyAnnualFeeLedger.sql is not in the current Installer folder. The code search at consolidation time found legacy-ledger names only in the old audit report. Verify the exact approved artifact/contract/target database for the release rather than executing an old cleanup instruction.

### 2. Original business/schema findings — BL-01 to BL-10

#### BL-01 — Ordinary admission role mismatch | IMPLEMENTED; TEST REPORTED
**Finding/impact:** New-admission numbering required Students.Administer although the admission workflow/procedure expects Students.Admit, blocking legitimate clerks.  
**Fix/disposition:** AdmissionNumberPolicy.LoadAsync now uses Students.Admit; existing-record loading retains Students.Administer. Historical admission's Enrollment.Manage and discount permissions are unchanged. Cross-build and admission-number policy checks were reported passed.  
**Follow-up:** On a disposable Windows/SQL profile, test users with Admit without Administer; with/without Fees.Adjust; historical admission and existing-record editing. Confirm SQL denials remain authoritative.

#### BL-02 — Incomplete schema compatibility inventory | IMPLEMENTED; TEST REPORTED
**Finding/impact:** Startup/restore compatibility contract omitted used procedure arguments, objects and integrity safeguards; a mismatched DB could pass structural checks.  
**Fix/disposition:** Contract generator and checker were expanded to procedure parameters, relevant tables, trigger modes/events, foreign keys, checks and other required shapes; validation strengthened for trusted/enabled constraints. Later artifact review includes full approved-model and 273 module fingerprints at release. A native SQL follow-up detected a system-named LegacyAdmissionSetup check whose generated name varied; validation now compares its parent/exact expression while retaining named-check requirements. Weakened expressions were rejected in disposable native checks.  
**Limits/steps:** Startup still checks live structural contract, **not every live SQL procedure/trigger body**. Run the schema artifact verification and tampered disposable-DB startup/restore checks for every release; do not treat a DACPAC hash as proof of what a separate user-supplied MDF/LDF contains.

#### BL-03 — Locally supplied SQL UserId trust boundary | DEFERRED — OWNER DECISION
**Finding/impact:** Stored procedures use caller-supplied application UserId. Where another process can use the same local SQL rights, FMSC UI authentication alone does not authenticate that SQL caller; risk is conditional on the actual local Windows/LocalDB trust boundary.  
**Decision:** Preserve the established locally trusted workstation/database architecture for now. No server/security architecture rewrite approved.  
**Steps if threat model changes:** Review Windows account isolation, SQL permissions and file ACLs on a disposable PC. If malicious local SQL clients are in scope, move privileged operations behind a trusted identity-verified service; client-set context alone is insufficient. Do not call this a proven network exploit.

#### BL-04 — Chronological admission-number ordering | IMPLEMENTED / ACCEPTED POLICY
**Finding/impact:** C# chronological sequence advice was not an atomic SQL rule.  
**Decision and change:** Chronology is assistance, **not** a mandatory save invariant. Valid manually selected, unused numbers may override the suggestion. Duplicate normalization, number format, academic-year/date checks and SQL uniqueness remain mandatory. C# was adjusted; synthetic tests covered out-of-order records and valid override cases.  
**Follow-up:** Test old-year admissions, new-number/readmission, concurrent unique-number saves and duplicate refusal, without introducing a SQL chronology hard lock.

#### BL-05 — Unknown historical withdrawal date | ACCEPTED POLICY
**Decision:** A historical student may be recorded as Left with an unknown/blank withdrawal date; no synthetic date or extra reason is required. Any actual entered date still has validity bounds.  
**Follow-up:** Confirm reporting presents unknown rather than inventing a departure date.

#### BL-06 — Nonmonthly discount changes to unpaid charges | ACCEPTED POLICY
**Decision:** Nonmonthly assignment discount edits may update existing noncancelled charges **without completed allocations**, subject to gross cap and closed-enrollment protections. Monthly UI edits use prospective period handling and protect already-billed months. The old example of an unrestricted monthly UI edit was corrected in the source audit.  
**Follow-up:** Check earlier unpaid vs paid/partly paid charges and verify charge/assignment totals with realistic clones. Do not enforce prospective-only changes without a new decision.

#### BL-07 — Best-effort bulk application of fee rates | ACCEPTED POLICY; GUIDANCE IMPLEMENTED
**Decision:** Rates take effect from the chosen month. Some eligible assignments may succeed while invalid ones fail; successful rows stay saved. Previously billed periods are not retroactively repriced.  
**Fix:** Page guidance, confirmation and partial-result language were clarified.  
**Follow-up:** Test mixed valid/invalid assignments, inspect succeeded/failed totals and review retries. For bill conflicts, choose a later effective month where appropriate; do not pretend that changing month fixes every validation error.

#### BL-08 — Revisable daily collection closing | ACCEPTED POLICY
**Decision:** Approved closing is a snapshot/revision, **not a hard lock on old dates**. Authorized backdated corrections can make a day Needs review, requiring fresh review/approval; existing snapshot checks protect submit, approval and print.  
**Follow-up:** On disposable data test backdated receipt, refund/reversal, changed snapshot and reapproval. Never display the stale prior snapshot as currently reconciled.

#### BL-09 — Assignment-level rather than per-charge history | ACCEPTED POLICY
**Decision:** Keep the existing assignment-level audit trail for affected discount/removal operations; do not add per-BillingCharge before/after rows solely to close this finding. Retain payment-time safeguards and existing logs.  
**Follow-up:** Verify the actual recorded operation can be traced using existing assignment history; reconsider only if compliance requirements change.

#### BL-10 — Unused annual fee ledger retirement | HISTORICAL / CHECK CURRENT BASELINE
**Earlier disposition:** The BL source report says unused GetStudentAnnualFeeLedger/RDLC references were removed, with an optional existing-DB cleanup script. Its artifact/version assumptions differ from the later October 13 baseline.  
**Current cross-check:** No active code search hits for legacy ledger symbols outside the BL report; the old optional cleanup script is **not present** in the current Installer inventory.  
**Steps:** Do not run, recreate or assume legacy schema cleanup based solely on this historical narrative. If needed, compare current approved DACPAC and deployed database, confirm compatibility and produce a separately reviewed SQL migration. There is no established current reporting defect requiring a new feature.

### 3. End-to-end C#/DACPAC findings — F-01 to F-12

#### F-01 — Whole-rupee financial integrity | IMPLEMENTED; SQL TESTS REPORTED
**Finding/impact:** Fractional configuration/discount amounts could produce balances normal whole-rupee Fee Collection could not settle exactly.  
**Owner policy:** Whole Pakistani rupees only; **reject fractional amounts without rounding**.  
**Fix:** Shared validation for UI inputs/TVPs/parameters; eight SQL whole-rupee check constraints added to configurations, assignments, charges, payments, allocations, manual fee posting, clearance and bank-certificate income. Migrations/2.sql preflights historical rows under locks, refuses fractions atomically (reported error 53201), installs trusted constraints and records schema version 2 only after success; rerun is idempotent.  
**Evidence:** Source reports record 75 portable money/quotation checks, artifact regression/provenance checks, native SQL Server 2022 publication, and successful/refused migration paths.  
**Remaining steps:** Check release-specific database copy for old fractional values; correct only with explicit approved financial treatment. Test UI paste, long decimal inputs, negative/boundary values, discount and receipt reconciliation in Windows. Initial database files are for first installation; later changes apply to the existing database through ordered SQL migrations, **not by swapping MDF/LDF or deploying DACPAC**.

#### F-02 — Stale Quick Registration (QR) fee quote | IMPLEMENTED; SQL TESTS REPORTED
**Finding/impact:** Admin changing a fee between preview and save could cause a different charge than the clerk saw.  
**Fix:** QR save compares quoted amount, frequency, mandatory and transport flags with current values under a serializable transaction and retains locks through the existing admission procedure; changed quote aborts before insert.  
**Remaining steps:** Two-window test: A previews, B changes rate, A saves and receives a clear conflict; refresh and retry once; verify no student, enrollment, assignment or bill was partially created.

#### F-03 — Lost update between two pending-record editors | IMPLEMENTED; SQL TESTS REPORTED
**Finding/impact:** Second clerk's stale form could overwrite first clerk's name/phone/address or discounts.  
**Fix:** Coherent details/assignment/state snapshot at load; snapshot compared under update/range locks in the save transaction. Stale data is rejected and the draft preserved rather than silently overwritten.  
**Remaining steps:** Concurrent A/B edits to basics, parents and discounts, plus payment vs correction. Confirm newer SQL values remain untouched on conflict.

#### F-04 — Implicit QR draft discard | IMPLEMENTED; WINDOWS TEST PENDING
**Finding/impact:** Cross-record open and closing Review could reload/erase unsaved QR values.  
**Fix:** Dirty/busy navigation guards and explicit discard decisions; Review close without completed registration no longer reloads; deliberately chosen New/Clear behavior is unchanged.  
**Remaining steps:** Verify Stay/Discard and Save/Cancel paths, same/different pending record, tabs/sign-out and repeated review closing, all on actual Windows.

#### F-05 — DACPAC/contract provenance mismatch | IMPLEMENTED; RELEASE CHECK REPORTED
**Finding:** Approved package hash did not match embedded artifact-provenance expectation in original audit.  
**Fix:** Record exact approved artifact SHA-256; reject package/model mismatch in offline and PowerShell publish checks, with full model/module fingerprint inventory. Source reports cite revised approved package SHA-256 45c43055a5a23647dece750dbbecf4bf4849fc56fa55f3929282da420c64f154; check against **current** artifact rather than treating that dated value as permanently authoritative.  
**Step:** Verify exact current bytes, model, contract and publish baseline on every release. Do not use an auto-approval mode to disguise unintended drift.

#### F-06 — QR access with Admit but not View | IMPLEMENTED; ROLE TESTS REPORTED
**Finding:** Dashboard exposed QR to Students.Admit, but Pending search separately needed Students.View.  
**Fix/policy:** Require **both Students.Admit and Students.View** for QR entry, pending actions and redirects; retain SQL permission checks. No grants or hidden permission implications added.  
**Remaining steps:** Limited-role matrix in actual app: Admit only must not see/use list; Admit+View may enter subject to other valid permissions; Administer alone must not imply Admit.

#### F-07 — Module semantics versus structural compatibility | PARTIALLY MITIGATED; KNOWN LIMIT
**Fix:** Approved release artifact/model and all reviewed SQL module definitions are fingerprinted and checked; native tests verified a weakened safeguard is rejected.  
**Open limit:** Startup structural comparison does not hash bodies in the installed live database or independently supplied MDF.  
**Step:** Independently validate target snapshot/model on a **clone** using approved release checks and representative SQL procedures. Do not promise full semantic proof for arbitrary local databases.

#### F-08 — Pending-list search overhead | IMPLEMENTED; LIVE CAPACITY PENDING
**Correction:** The original suggestion that every keystroke re-queried SQL was inaccurate; records were cached.  
**Fix:** Reuse a DataView and binding, apply 200 ms debounce, correctly escape search literals, retain sort/status and flush before opening selection. Synthetic 100/2,000/10,000-row checks passed in source report.  
**Open:** Initial SQL result retrieval and per-student balance work, WinForms painting and school-scale CPU/RAM have not been profiled with production-like data.  
**Step:** Time query plans, cold/warm list load, filtering, paging, redraws and RAM before deciding about server-side paging/index changes.

#### F-09 — Non-QR Pending records in QR list | IMPLEMENTED; SQL TESTS REPORTED
**Fix:** Pending list requires Pending status, QuickReference present and official AdmissionNumber absent, aligned to QR completion eligibility. Legacy list unchanged.  
**Step:** Test QR and older non-QR Pending fixtures, completion and duplicate confirmation.

#### F-10 — DPI/window accessibility | GEOMETRY IMPROVED; WINDOWS VALIDATION PENDING
**Fix:** Existing window working-area clamping/scrolling was retained and extracted/tested geometrically. Other audit passes add responsive layout and scroll fixes.  
**Step:** Test primary Save/Confirm/Cancel/footer reachability, keyboard focus, nested dialogs and laptop/offset-monitor working areas at 100%, 125%, 150% DPI, including at least 1024×768, 1080×600 and 1366×768 as relevant. Geometry unit checks do **not** prove visible controls never clip.

#### F-11 — Scenario / test coverage | SUBSTANTIALLY IMPROVED; RELEASE SUITES PENDING
**Fix/tests reported:** Portable money/role/contract tests, native SqlPackage publication, disposable version-1 migration refusal/success/idempotence, 17 SQL integration checks, limited-role tests, and production writer/compatibility harness compilation/execution.  
**Remaining:** Full restored-school-data script suites, Windows smoke UI, true DPI, real failure/recovery conditions and intended-version/SQL Server environment acceptance.  
**Step:** Execute §6 and preserve logs/screenshots/results; do not infer that compiled Windows tests ran on the Linux builder.

#### F-12 — Large mixed-responsibility UI controllers | TARGETED REFACTOR DONE; CONTINUOUS MAINTENANCE
**Finding:** Admission and collection controllers are large, raising regression risk, not demonstrating a runtime defect.  
**Changes:** Extracted QR quote/transaction, permissions, query/search expressions, geometry, loading/cache and other isolated helpers.  
**Step:** Continue small, test-backed extraction only when it eliminates actual duplication or supports a change; avoid a wholesale redesign that could alter money/admission behavior.

---

## B. Master follow-up, owner decisions, release gates and traceability

### 4. Follow-up hardening — consolidated by workflow

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

### 5. Owner-approved invariants — preserve when making further fixes

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
13. On an otherwise blank new Quick Registration, changing only the date or academic year does not require a discard warning. Student-detail/placement drafts and existing pending-record edits keep their normal warnings.
14. New photo selections must prepare to <=80 KB while preserving full-image proportions, avoiding upscaling and retaining acceptable JPEG quality. Existing stored photos are not automatically rewritten. Admission success requires a confirmed positive student ID; uncertain saves are checked read-only and never automatically resubmitted.

### 6. Remaining steps — prioritized release acceptance checklist

**Mark each checkbox complete only after executing it in the intended environment and recording date, operator, build/commit, test DB identity, outcome and evidence.** Nothing below is automatically DONE merely because the consolidated report is DONE.

#### Gate A — Establish exact candidate and offline reproducibility
- [ ] Pin application commit, approved FMSC.dacpac hash, contract/model revision, schema version, migration order and installer inputs for the **actual** release candidate. Record discrepancies rather than editing the baseline silently.
- [ ] Run Windows-targeted build, Tests/AuditFixChecks, Tools/verify_schema_artifact.py --check, Tools/test_audit_schema.py and Installer/VerifySchemaArtifact.ps1; archive full results. Include relevant PerformanceChecks and older policy/contract tests.
- [ ] Confirm that required release files are present **when packaging**, not simply because development workspace lacks a database pair. Initial install and incremental existing-user upgrade have different rules. Do not push a modified school database to resolve an audit mismatch.

#### Gate B — Verify migration and database compatibility on clones
- [ ] Restore a copy to an isolated **FMSC_AuditChecks_<suffix>** catalog and separate MDF/LDF paths, never into live school catalog. Confirm source version and sample historical balances.
- [ ] From version 1, exercise Migrations/2.sql fractional-row refusal: source amount/version/constraints remain unchanged. Correct the fixture manually; rerun; confirm eight enabled/trusted whole-rupee checks and version 2; rerun again safely.
- [ ] Validate approved DACPAC/contract against this release's database with SqlPackage/native tool as needed for **testing only**; confirm structural mismatch and weakened critical checks are rejected. Never publish DACPAC to the user's in-place existing database as the release upgrade.
- [ ] Run Tests/QuickRegistrationSqlChecks (production writer, limited-role, stale quote/edit, actor ID, pending eligibility) with guarded clone connection; run Validation/AdmissionWorkflowChecks.ps1 and Validation/StudentCorrectionChecks.ps1 on their disposable fixtures.
- [ ] Repeat target-specific compatibility/restore checks against the actual supported Windows SQL Server/LocalDB version and a representative **copy** of the intended initial snapshot. SQL Server 2022 Developer synthetic pass alone does not prove behavior on the installed environment.

#### Gate C — Multi-operator workflow and financial integrity
- [ ] Two independent clerks: stale QR quote/edit, complete versus edit/cancel/payment, new family + sibling, duplicate registration, old academic year and admission number, direct admission, re-admission, transfer and promotion.
- [ ] Test concurrent monthly posting, two collectors on the same charge, family payment, interrupted or lost commit acknowledgement, failed write with journal recovery, partial bulk-rate application, nonmonthly discount edits around paid charges, and late daily-closing revisions.
- [ ] After each test compare student/enrollment identity, no duplicate official numbers/charges, ownership of fees/allocations, gross minus discount minus completed payments = outstanding, correct reversal/refund behavior, permissions and traceable audit/history. No automatic write replay on ambiguous commit.

#### Gate D — Native Windows behavior, DPI and usability
- [ ] Run Tests/ThemeSmokeChecks on a disposable Windows profile in Professional Simple and an existing theme. Verify dashboard, About, connection guidance, auth, photo preparation/zoom, shared editor focus, workspace breadcrumbs, report filter state, dirty/busy guards and report/picker cancellation.
- [ ] Inspect at 100/125/150% scaling and 1024×768, 1080×600, 1366×768 plus intended typical monitor, checking all Save/Confirm/Cancel/footer controls, dialogs, keyboard-only tab/Enter/Escape and active/offscreen windows.
- [ ] Try real EXIF photos/transparent PNG/large and detailed JPEGs, slow A→B→A student selections, denied or full preferences storage, disconnection during sign-in, restore and writes. Confirm no stale form values or stuck busy state.
- [ ] Run backup/restore safety paths on throwaway DB only, including cache revalidation, incompatible restore recovery and failure of safety recovery.

#### Gate E — Actual workload, resource use and release decision
- [ ] Profile cold/warm startup, dashboard, 100/2,000/10,000+ row search, admission open/save, family/individual collection, reports/export/print, many page switches and sustained use. Record SQL latency/query plans, GUI responsiveness, GC, private bytes/working set and CPU on a minimum-spec school PC.
- [ ] Investigate measured bottlenecks (e.g. pending-list balance aggregation or dashboard's unused finance computations); avoid new complexity without evidence.
- [ ] Record role matrix and OS/LocalDB ACL test under the currently accepted local trust model. Document BL-03 as consciously deferred.
- [ ] Sign off backup/restoration, migration success and rollback/recovery instructions, logs/screenshots, smoke-test results and unresolved exceptions **before** stating production readiness.

### 7. Useful commands and guardrails

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

### 8. Source-to-master traceability (13 reports)

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

#### Quick Registration owner-reported flow fixes — 13 October 2026

Implemented configured fee add/remove controls, optional phone through admission
confirmation, hidden-tab action-bar recovery, visible pending family groups,
combined placement, genuine temporary-reference display and explicit selection
for review/confirmation. Fee removals preserve history and protect paid charges;
required-fee waivers require Fees.Adjust. Migration 5 supports optional phone,
pending fee restoration and As Needed opening charges. Application/installer
require schema 5; the current v3 DACPAC requires migrations 4 and 5.

Verified 72 native SQL registration checks, migration rollback/rerun preservation,
636 portable checks, 16 artifact tests and the Windows-targeted build. New GUI
regression checks compile; execute them on Windows before deployment.

---

## C. Follow-up code findings (FR) and successive implementation results

**Chronology:** Original FR findings precede later fixes in the implementation addenda. The addenda take precedence where they establish a new fix or an owner-approved decision.

### Executive summary

The recent work materially improves Quick Registration concurrency, financial constraints, collection recovery, active-write navigation guards, cancellation of stale reads, database compatibility, and dashboard usability. The follow-up review did **not** demonstrate a new P1 data-corruption regression. It did identify several code-level error/feedback gaps and a changed photo-management user experience worth addressing before end-user release.

#### Prioritized register

| ID | Priority | Status | Area | Finding | Age / attribution |
|---|---|---|---|---|---|
| FR-01 | P2 | Confirmed C# control-flow gap; SQL-response edge case not reproduced | Admissions | Successful admission wording can be shown without a positive returned AdmissionId | Pre-existing in older audited code |
| FR-02 | P2 | Confirmed C# control-flow gap; simulated load failure needed | Quick Registration | A committed save can be followed by failed refresh, yet final status still implies refreshed success | Pre-existing |
| FR-03 | P2 | Confirmed recovery-path gap; exception scenario not reproduced | Main dashboard navigation | Failed destination initialization can leave old page detached and workspace blank/inconsistent | Pre-existing |
| FR-04 | P2 | Confirmed workflow behavior; desired UX is a product decision | Profile → Manage Photo | Choosing a photo on an existing student immediately persists it; no separate Save confirmation | Impact of consolidating the formerly explicit-save Photo page |
| FR-05 | P2 | Confirmed C# mismatch | Student photo recovery | Main Admission Save does not retry a pending photo that failed to persist | Pre-existing handling; still present after consolidation |
| FR-06 | P3 | Confirmed narrow dirty-state omission; user intent-dependent | New Quick Registration | Changing joining date or academic year alone is not counted as unsaved input | Gap in current draft-detection rule |
| FR-07 | P3 / release test | Potential resource risk, not a reproduced leak | Image preparation | 25 MB file limit does not bound decoded image/GDI memory | Needs Windows stress measurement |
| FR-08 | P3 / architecture boundary | Confirmed scope limit; not necessarily a defect | SQL photo storage | 300 KB cap is application-enforced, not guaranteed against independent direct SQL writes | Intentional/known boundary; SQL change only if approved |
| FR-09 | Release gate | Testing incomplete | Windows, SQL, migration, restore | Portable tests and cross-builds do not prove all native UI and school-data workflows | Known verification gap |

**P2** means fix/reconcile before release if the corresponding workflow is in scope. **P3** means improve or explicitly accept, with tests. No SQL migration is proposed for FR-01 through FR-07.

### FR-01 — Admission success needs an authoritative returned record ID

**Evidence:** [`StudentAdmissionPage.cs`, admission result handler](https://github.com/farhan2166-creator/FMSC-Database/blob/4d714a9a8956312678a2a389413225570355e971/Pages/Students/Admissions/StudentAdmissionPage.cs#L4332-L4399), methods `SaveNewAdmissionAsync` and subsequent success message.

**Problem / exact behavior**

1. The form calls `AdmitStudent` or `AdmitHistoricalStudent` through `DatabaseHelper.QueryUserAsync`.
2. It initializes `newAdmissionId = 0` and only assigns an ID if the first returned table has an `AdmissionId` column and a row.
3. Photo persistence and record reload occur only if `newAdmissionId > 0`.
4. The normal `if (photoSaved) Info(...)` branch does not itself require a positive ID, so an empty/unexpected result can still produce “Student admitted...” or historical-save confirmation, accompanied by an unsuccessful-refresh explanation.

**Illustrative failure:** SQL performs or appears to perform the write but returns an unexpected result shape, zero ID, or missing row. The operator sees a success-looking message without an identifiable committed record and may attempt to save again. This is a **conditional client-response contract weakness**, not proof that current stored procedures omit IDs.

**Historical comparison:** The same ID defaulting and success-condition sequence exists at the older `c63bb90` baseline. **Do not call this a regression from the recent dashboard/photo work.**

**Recommended fix (C#; no schema change):**
- Extract a small typed admission-result parser that requires one valid positive `AdmissionId` from the documented procedure response. Check its result table explicitly; do not silently treat a missing ID as zero success.
- Track `writeAttempted` versus `confirmedId` versus `reloadSucceeded`. Treat missing/malformed output after a submitted write as **outcome not confirmed**, not as a routine failed write or a confirmed admission.
- Preserve the entered form and show targeted reconciliation guidance: find by the identifying details/number and check whether the record was created; **do not blindly retry**.
- Keep the existing distinction: a confirmed admission remains committed even if its separate photo save or UI reload fails.

**Acceptance tests:** Valid new/historical result; missing/empty result; ID zero or `DBNull`; valid ID with a failed record reload; SQL connection loss after server commit. Verify that wording never claims success without confirmed result and never encourages duplicate admission.

### FR-02 — Quick Registration save/refresh status can hide a failed reload

**Evidence:** [`QuickRegistrationPage.cs`](https://github.com/farhan2166-creator/FMSC-Database/blob/4d714a9a8956312678a2a389413225570355e971/Pages/Students/Admissions/QuickRegistrationPage.cs#L249-L303): `SaveAsync`, `LoadRegistrationAsync`, `SavePendingAsync`, `OpenRegistrationAsync`.

**Problem / exact behavior**

- After a successful **new** save, the form temporarily clears `busy`, calls `LoadRegistrationAsync(savedStudent)` and then shows “Saved [reference]...” regardless of whether the reload completed.
- After a successful **pending edit** save, `SavePendingAsync` does `await LoadRegistrationAsync(savedStudent)` followed by “Registration changes saved...”.
- `LoadRegistrationAsync` catches errors via `Fail(ex)` and does **not return a success/failure result**. It may partially update in-memory fields before a failure.
- `OpenRegistrationAsync` returns `savedStudent == id` after the same loader; for a previously selected same ID, that check can be true even when the attempted reload failed.

**Impact:** The database transaction can be committed, but the form may remain stale/partial while the final message suggests an ordinary successful save or successful record open. This increases risk of incorrect follow-up decisions.

**Historical comparison:** The same pattern is present in earlier Quick Registration code. The newer snapshot/transaction checks are valid improvements but do not fix the post-save refresh contract.

**Recommended fix (C#):**
- Make the loader return a typed outcome (`Loaded`, `NotFound`, `Failed`, `Superseded`) or `bool` plus diagnostic detail. Do not consume an exception and then implicitly signal success to callers.
- Where possible, construct the new view state off-screen and apply it atomically only after all required reads validate. Retain a previously valid form when refresh fails.
- Separate “Database save succeeded” from “Screen refreshed successfully”; if the latter fails, show explicit guidance to reopen the record before additional operations.
- For `OpenRegistrationAsync`, return success based on the current *load attempt*, not only equality with a previously stored ID.
- Ensure save/write operations are never automatically repeated because a subsequent **read** failed.

**Acceptance tests:** Inject a failing loader immediately after successful new and pending saves; fail halfway through bindings; reopen same ID after a failed load; navigate among two IDs with delayed callbacks. Confirm durable DB write is reported accurately while stale UI is not misrepresented as refreshed.

### FR-03 — Recover the previous workspace if destination initialization fails

**Evidence:** [`MainDashboardForm.cs`](https://github.com/farhan2166-creator/FMSC-Database/blob/4d714a9a8956312678a2a389413225570355e971/MainDashboardForm.cs#L1396-L1515), `NavigateTo`.

**Problem / exact behavior**

The destination page is created, but the existing page controls are removed **before** `content.Controls.Add(page)`, `RestoreWorkspaceState(page, destinationKey)` and the rest of activation complete. If one of these later steps throws, the exception handler reports an error and synchronizes the sidebar; it does not reliably reattach the original page controls to the content panel.

**Example:** A workspace state host throws from `RestoreWorkspaceState` after the old page has been removed. The operator can be left on a blank or inconsistent workspace, even though old controls still exist in memory.

**Historical comparison:** The detach-before-restore sequence existed in the older `c63bb90` revision. The latest commit correctly defers navigation metadata/highlight changes, but does not establish a transactional rollback of the old page.

**Recommended fix (C# only):**
- Prefer preparing/validating and restoring state on the destination *before* retiring the old page, where WinForms control lifetimes permit.
- Otherwise capture old controls and metadata and reattach/restore them inside the exception handler if destination activation fails. Dispose a failed destination; dispose the previous page **only** after confirmed success.
- Keep the existing unsaved-changes and `IBusyPage` leave guard before any state mutation.
- Synchronize top bar, sidebar and `_currentPageControl` as one successful activation outcome; make compensation safe if `Controls.Add` or state restoration throws.

**Acceptance tests:** Destination factory throws; factory returns null; `RestoreWorkspaceState` throws; handle creation/layout throws; user cancels dirty-page prompt; active financial write blocks navigation. Verify previous page remains functional and no stale event subscriptions/leaked controls remain.

### FR-04 — Photo management now has different commit semantics from the retired Photo page

**Evidence:** [Current `StudentAdmissionPage.ChoosePhotoAsync`](https://github.com/farhan2166-creator/FMSC-Database/blob/4d714a9a8956312678a2a389413225570355e971/Pages/Students/Admissions/StudentAdmissionPage.cs#L3958-L4006), [`StudentProfilePage` shortcut](https://github.com/farhan2166-creator/FMSC-Database/blob/4d714a9a8956312678a2a389413225570355e971/Pages/Students/Profile/StudentProfilePage.cs#L247-L254); older deleted `Pages/Students/Profile/StudentPhotoPage.cs` in [historical commit `6e5b584`](https://github.com/farhan2166-creator/FMSC-Database/blob/6e5b584a625d8a936be666689c94c8cab6976b33/Pages/Students/Profile/StudentPhotoPage.cs).

**Problem / behavior change**

- The **former standalone Student Photo page** offered `Choose Photo` and a separate explicit `Save Photo`.
- Its replacement path, **Profile → Manage Photo → Student Admission**, invokes `PersistPendingPhotoIfAnyAsync` immediately when `currentAdmissionId > 0`.
- Therefore, the act of choosing a file can overwrite the stored photo before the operator separately presses Admission Save or decides to leave the page.
- **Important qualification:** The old **Admission** screen itself already auto-saved chosen photos for existing students. The changed expectation arises because the previously explicit-save **Profile Photo workflow was removed**, not because the recent change newly introduced auto-save into Admission.

**Impact:** A user familiar with Profile → Photo may expect a preview before committing the replacement. Choosing the wrong image is immediately persistent. Photo removal already requires an explicit confirmation, but replacement does not have an equivalent second step.

**Recommended decision and fix (C# only):**
- Product owner should choose either: (A) **explicit Save Photo** / confirmation before an existing photo is replaced, or (B) retain immediate save but display this action clearly as “Choose and Save Photo…” and explain that replacement is immediate.
- Prefer option A for consistency with irreversible photo replacement and the old profile workflow. Keep unsaved-photo checks, identity/version guards and cancellation protections.
- If immediate saving is retained, treat it as an accepted UX policy, not an independently proven functional bug.

**Acceptance tests:** Preview and cancel; replacing a stored image; navigation with a pending selection; restricted `Students.View` versus `Students.Administer`; successful and failed saves; photo-dialog enlargement.

### FR-05 — Main Admission Save cannot retry a failed pending photo write

**Evidence:** [Photo persistence](https://github.com/farhan2166-creator/FMSC-Database/blob/4d714a9a8956312678a2a389413225570355e971/Pages/Students/Admissions/StudentAdmissionPage.cs#L3958-L4083) and [existing-student Save branch](https://github.com/farhan2166-creator/FMSC-Database/blob/4d714a9a8956312678a2a389413225570355e971/Pages/Students/Admissions/StudentAdmissionPage.cs#L4489-L4538).

**Problem / exact behavior**

If an existing student's immediate photo upload fails, `PersistPendingPhotoIfAnyAsync` keeps the prepared bytes as `_pendingPhotoData` and warns the operator. But `SaveExistingAsync` only calls `SaveDetailsAndDiscountsAsync` and reloads the student; it does not retry `PersistPendingPhotoIfAnyAsync`. Moreover, pending photo state is included in `HasAdmissionEdits`, so pressing main Save can run unrelated details-save logic while leaving the photo unsaved.

**Impact:** The form's principal Save action does not complete all dirty work represented by its dirty-state check; operator must re-choose the file despite the already prepared bytes still being in memory. This is misleading and may cause repeated database work.

**Proposed fix (C# only):**
- Provide a dedicated **Retry Photo Save** action using the existing `_pendingPhotoData` without reopening the file picker; alternatively, make main Save explicitly process the pending photo after a successful details save.
- Ensure a photo-only dirty state does not trigger an unnecessary full student-details update.
- Show independent details-save and photo-save outcomes. A successful details transaction must never be presented as photo success.
- Preserve the pending bytes on failure and protect the selected student ID/generation throughout retries.

**Acceptance tests:** Force `SaveStudentPhoto` failure; click main Save with photo-only changes; change other details and save with photo failure; retry; navigate A→B→A; verify nothing is silently lost or written against the wrong student.

### FR-06 — Fresh Quick Registration dirty tracking misses year/date-only edits

**Evidence:** [`QuickRegistrationPage.cs`, `HasUnsavedChanges`](https://github.com/farhan2166-creator/FMSC-Database/blob/4d714a9a8956312678a2a389413225570355e971/Pages/Students/Admissions/QuickRegistrationPage.cs#L49-L56).

**Problem:** The new-record dirty check accounts for name, parent, phone, address, or placement, but not a manually changed joining date or academic year when these other inputs remain empty.

**Example:** Start a blank Quick Registration form, change only Joining Date or Academic Year, then navigate away. The form can report clean. Whether a date-only change *ought* to prompt is a product/UX policy decision, but the dirty tracking is inconsistent with other editable inputs.

**Proposed fix (C#):** Capture a clean baseline *after* asynchronous default-year/date initialization. Compare all user-editable fields (including year/date) against that baseline. Distinguish user input from programmatic population to avoid false unsaved prompts. Preserve any explicitly intended New/Clear behavior.

**Tests:** Blank→change year; blank→change date; leave default values untouched; async default loading; reset to original values; New/Clear actions; Navigate/Sign Out.

### FR-07 — Test decoded-photo memory, not only compressed file sizes

**Evidence:** [`StudentPhotoInput.cs`](https://github.com/farhan2166-creator/FMSC-Database/blob/4d714a9a8956312678a2a389413225570355e971/Core/StudentPhotoInput.cs) and [`StudentPhotoProcessor.cs`](https://github.com/farhan2166-creator/FMSC-Database/blob/4d714a9a8956312678a2a389413225570355e971/Core/StudentPhotoProcessor.cs).

**Current improvement:** Source file limit is 25,000,000 bytes; output is a proportional, <=600-pixel-long-side JPEG of <=300×1024 bytes; decoding/compression is off the UI thread, with cancellation checks.

**Remaining risk:** Small or moderately compressed image files may decode to very large pixel buffers; `Image.FromStream` must decode image data before the resized 600-pixel bitmap is generated. The byte limit alone does not establish a safe peak GDI/managed/private-memory bound on low-RAM Windows PCs. **This review did not reproduce a leak or crash.**

**Recommended verification first:** Exercise extreme pixel dimensions, malformed JPEG/PNG, EXIF-rotated images, transparent PNGs, repeated choose/remove/zoom, slow machines and cancellation. Measure private bytes, working set and GDI handle counts. If needed, add a tested pixel-count / dimension guard or decoder approach before allocation, without distorting ordinary photographs.

### FR-08 — The photo size rule is not enforced by SQL itself

**Evidence:** App-side `StudentPhotoInput.ValidateStoredData` and the historical [photo consolidation analysis](https://github.com/farhan2166-creator/FMSC-Database/blob/e3d2774c29fd44fa089db4df03d7d4f74cf4c1d3/Audit/FMSC_Photo_Storage_2026-10-13.md).

**Boundary:** FMSC's user-interface path restricts new stored photos to 300 KB. The existing `SaveStudentPhoto` procedure was not given a new SQL-side byte-size constraint, and existing historical photos were not rewritten. Direct calls to the SQL procedure outside this application can therefore bypass the app-only size limit.

**Decision / optional fix:** Accept this boundary if the workstation and SQL callers are trusted, or separately authorize a numbered SQL migration that validates new writes at the database layer. Do **not** silently change the schema or convert existing photos. A future SQL change needs review of old data and a disposable-db upgrade test.

### FR-09 — Required deployment acceptance tests remain outstanding

The current master audit documents historical success for portable checks, a cross-build, disposable SQL migration/role tests, and related artifact checks. Those results are valuable, but do not prove the following on the intended user's Windows machine and database:

1. **Admissions:** normal, historical, Quick Registration and pending completion; old-year admission numbers; duplicate submissions; malformed/missing return IDs; failed post-save reload.
2. **Money:** whole-rupee end-to-end setup/billing/collection, two-operator stale fee conflicts, connection loss immediately around payment commit, family receipt consistency and unknown-outcome reconciliation. Never blindly retry a financial write.
3. **Upgrade:** initial MDF/LDF install only once; subsequent **ordered SQL migrations** against the same existing database; version 1→2 refusal/success/idempotence; verified pre-migration backup and recovery. **No DACPAC deployment or replacement MDF/LDF for subsequent updates.**
4. **Restore/recovery:** successful restore, failed restore, incompatible restored DB with safety recovery, failed safety recovery, new reads after cache invalidation, backup integrity and denied disk access.
5. **Windows UI:** run `Tests/ThemeSmokeChecks` and the other documented acceptance suites; inspect 100%, 125%, 150% scaling, 1080×600, 1366×768 and other target laptop dimensions; both themes; unsaved state, focus/tab order, dialogs and printing.
6. **Performance:** profile real school-sized records on representative Windows hardware—opening student profiles, cold lookup loads, pending list filtering, fee collection, report previews/exports and repeated navigation. Synthetic benchmark savings are not guaranteed real-world CPU/RAM savings.

**Status:** Pending verification, not a claim that the application failed these tests.

### Recent improvements confirmed during source review (retain them)

| Area | Source-level improvement observed | Caution |
|---|---|---|
| Quick Registration | `QuickRegistrationWriter` compares fees and record snapshot under serializable transactions, reducing stale-write risks | Keep this protection when adjusting UI refresh/error feedback |
| Money / migration | Version-2 migration preflights fractional historical amounts under transaction and adds eight whole-rupee constraints | Verify on disposable restored copy before real user update |
| Collection | `CollectionDiscountWriter` journals an intent before committing and does not automatically retry uncertain outcomes; `CollectionRecoveryJournal` validates ownership/identity | Test interrupted commit and receipt reconciliation |
| Write navigation | Fee posting and bulk promotion expose `IBusyPage` write state, preventing navigation while active | Do not cancel in-flight financial writes as if they were report reads |
| Restore | `DatabaseBackupManager` refreshes readiness and caches after an attempted restore, including failure paths | Test actual restored/safety-recovered database |
| Dashboard | Independent totals/year/readiness reads, latest-request cancellation, correct sidebar selection and responsive scrolling | Native Windows and SQL latency verification is still outstanding |
| Search/reports | Cached searches, debounce and cancellation/efficient paging improve local loading paths | Measure live SQL + UI paint before projecting speed/RAM gains |
| Photos | Shared resize/orientation processing, pending-photo guards, read cancellation and snapshot-owned enlargement | Verify new save semantics and GDI/memory limits |

### Recommended remediation order

1. **Before release:** FR-01 and FR-02 — correct the distinction between a committed database write, verified response and UI refresh; avoid unconfirmed success messages.
2. **Before release:** FR-03 — protect existing workspace against a destination-activation exception.
3. **Before release:** FR-04 / FR-05 — agree on explicit versus immediate photo replacement and make failed-photo retry consistent with Save and dirty-state behavior.
4. **Next UX pass:** FR-06 — correct date/year dirty-state tracking without false prompts from defaults.
5. **Release testing:** FR-07 / FR-09 — run real Windows/native SQL/performance scenarios and record evidence.
6. **Only if policy requires it:** FR-08 — design an approved additive SQL migration for database-wide photo limits.

**Constraints for any subsequent implementation:** Preserve admission numbering policy, existing money transactions, signed-in permission checks, historical student rules, unsaved-change navigation safeguards, migration ordering, and installed user data. Prefer small isolated C# fixes and targeted tests; do not broaden into a speculative architectural rewrite.

### Verification scope and provenance

- Reviewed current source paths at `main` commit `4d714a9`; the three commits after `ba2a006` consolidated audit documentation and did **not** change C#/SQL files.
- Compared historical `c63bb90` versions for FR-01 and FR-03 and historical separate `StudentPhotoPage` for FR-04. Older flow logic was also inspected for FR-02.
- Findings are **source-based** with status distinctions above. No new tests were executed and no application defects were reproduced on the user's PC.
- The existing [master audit](https://github.com/farhan2166-creator/FMSC-Database/blob/b5cb9342968eebe76f919538ebdbf2dc4662a187/Audit/MASTER_AUDIT_FINDINGS_FIXES_STEPS_DONE_2026-10-13.md) remains unchanged. This addendum does not reopen previously fixed/accepted F- or BL-series issues without new evidence.
- This report is the **only intended repository change** from the follow-up review.

### Follow-up code-fix addenda

### Implementation and verification addendum — 13 October 2026

This section follows the owner's request to implement the approved compact
dashboard and examine this report. The original findings above describe their
pinned baseline; the following table describes the current source review.

| Finding | Current disposition |
|---|---|
| FR-01 | **Source fix implemented:** read all result sets, validate the documented positive ID/official-number response, and reconcile missing/malformed replies or write exceptions by read-only exact-number/identity lookup. Unconfirmed outcomes retain entries/photo, block Save and Save & New, and expose Check Saved Admission. No automatic write retry. Both DACPAC procedures were checked; native/real-SQL failure acceptance remains pending. |
| FR-02 | **Source fix implemented:** typed load outcome, validated complete view before binding, editor rollback on binding/layout failure, and success based on the actual load attempt. Confirmed new/pending saves report a failed refresh separately and require deliberate Reload Registration before further edits, printing or payment. Transaction/snapshot writer is unchanged; native execution remains pending. |
| FR-03 | **Source fix implemented:** restore destination state before retiring the current page; compensate parenting/activation/layout failure by reattaching old controls and restoring page/factory/route/year/subscriptions. A failed destination is disposed; old pages retire only after confirmed activation. The navigation guard is released in `finally`. Native regression fixtures are compiled; Windows execution remains pending. |
| FR-04 | **Owner chose explicit save; source fix implemented:** Choose Photo prepares and previews, Save Photo persists, and Discard Selection restores the saved preview without deleting/replacing stored data. New students retain their preview until admission creates the record. Stored-photo deletion still requires confirmation. |
| FR-05 | **Source fix implemented:** failed saves retain prepared bytes and expose Retry Photo Save. Main Save handles photo-only work without an unrelated details transaction; combined details/photo saves report each outcome independently. Photo persistence keeps student ID/version and immutable-selection ownership guards. |
| FR-06 | **Owner accepted existing behavior:** date/year-only changes on an otherwise blank new registration intentionally allow leaving without a discard prompt. This policy is documented beside C# dirty detection; entered student details/placement and existing pending-record edits retain their warnings. |
| FR-07 | Source byte limit and unrestricted decoded dimensions confirmed. No leak/crash or safe peak memory figure has been measured on Windows. |
| FR-08 | DACPAC `SaveStudentPhoto` validates nonempty bytes but has no application byte cap. The current application cap is **80 KB**, reduced from 300 KB at the owner's request. This remains an application boundary; no SQL or historical-photo conversion was performed. |
| FR-09 | Windows, school-data, migration/restore and deployed-hardware acceptance remain pending. Cross-build/portable results do not close this gate. |

The approved dashboard now has five compact KPI cards, six permission-filtered
quick actions in two rows, and a counted setup-readiness bar that disappears when
ready. The redundant charts and numbered setup-step panel are removed. Active
percentage is retained in the Active Students caption. Existing menu groups,
page routes, academic-year display and themes continue to supply context.

Validation executed for this implementation: the application and native Windows
smoke harness cross-build with zero warnings/errors; **294 portable assertions**
pass (including 29 dashboard count/year/percentage checks). Native fixtures cover
normal/tiny viewport sizing, quick-action order and routing, readiness visibility,
theme rendering, delayed/disposed reads, destination factory/null/state/parenting/
layout failures, and subsequent successful navigation. They are compiled but
unrun on this Linux environment. No school SQL connection was opened, and no
live admission/photo failure scenario was reproduced. SQL procedures, migrations
and DACPAC are unchanged.

#### Registration refresh and explicit photo-save implementation — 13 October 2026

The owner requested remaining points 2 and 3 (FR-02 and FR-04/05) and explanations
of points 1 and 4 (FR-01 and FR-06). FR-01 remains open. After the explanation,
the owner accepted FR-06's existing behavior: date/year-only selections on a
blank new registration should not prevent intentionally leaving. The C# source
now records this policy; the dirty-detection expression remains unchanged.

The subsequent owner request also implements FR-01 and reduces new photo storage
to 80 KB; see the latest implementation section below. The 339-check evidence in
this section describes the earlier registration/photo-save pass.

Quick Registration now returns Loaded/NotFound/Failed/Superseded/Busy from a
specific read attempt. It validates identity, required detail/fee columns,
assignment/payment state, whole-rupee amounts and the saved snapshot before
touching the editor. It prepares derived fees on a copy, suppresses programmatic
placement/family events while binding and restores the old editor on failure.
Both committed-save paths use one read-only completion handler: the ID and
entered committed values survive refresh failure, the old write snapshot is
invalidated, and Save/payment/printing remain blocked until Reload Registration
succeeds. No save transaction is repeated to recover from a read failure.

Photos use an immutable prepared selection with explicit save/retry/discard.
Selecting a file issues no storage write. A failed save retains the same JPEG
bytes/metadata, and duplicate/obsolete saves cannot erase a newer selection.
The standalone action saves only the photo. Main Admission Save separately
handles photo-only versus details-plus-photo work. Stored-photo removal retains
its existing confirmation; discarding a pending preview uses no DeleteStudentPhoto
call. Unsaved selection, permissions, 300 KB limit, proportional preparation,
photo enlargement, and record/version checks remain in effect.

Executed validation: **339 portable assertions pass**, including **45 new
registration-view and photo-save lifecycle checks**; the application and Windows
smoke harness cross-build with zero warnings/errors. Native fixtures add failed
same-ID reopen, malformed fees, midway binding failure, missing record,
new/pending save with failed refresh, stale/disposed callback, preview/discard,
save failure/retry, photo-only main Save and permission checks. These form tests
are compiled but unrun on this Linux host. Real SQL writes, Windows interaction,
and mixed details/photo failure acceptance remain pending. The new pure helpers
were executed with controlled inputs; they do not establish deployment sign-off.

#### Confirmed admission outcome and compact photos — 13 October 2026

The admission response parser now selects the documented admission result among
all returned tables. Exactly one row with a positive int AdmissionId and matching
official number is required; nullable historical registration numbers remain
supported. Missing/malformed replies and write exceptions trigger one read-only
lookup by the original submitted number. The candidate must also match student,
parent, admission date and campus. Failure to establish identity retains the
original request, form entries and prepared photo, blocks both Save paths and
shows Check Saved Admission. That action only rechecks; it never submits another
admission. Leaving/Clear warns that a record might already exist. Confirmed IDs
precede photo persistence and success feedback. Form reload reports a separate
boolean outcome, so an early refusal/missing record is not called refreshed.

The owner changed the stored-photo budget from 300 KB to **80 KB maximum** for
newly selected photos. The full image remains proportional, EXIF-corrected and
uncropped, with no upscaling. Preparation tries JPEG quality 88/82/76/70 at a
600-pixel maximum long edge; if needed, it estimates a smaller proportional
canvas, down to a 320-pixel long edge for large originals, and encodes again from
the original. An impossible fit is refused, not stored over the cap. Bitmaps are
reused between quality attempts and disposed between sizes. Smaller originals
stay smaller; outputs already below 50 KB are not padded. The aim is acceptable
student-portrait quality within the byte budget, not a guaranteed perceptual
quality measure or a 50 KB minimum.

Executed validation: **389 portable assertions pass**, including **50 new
admission-result/reconciliation and compression-policy checks**. They exercise
invalid/empty/wrong/ambiguous results, nested fee tables, historical nullable
registration numbers, lost write replies, missing/unrelated records, read-only
rechecks, the 80 KB boundary, proportional adaptive sizing, quality floor,
bounded attempts and cancellation. The application and Windows smoke harness
cross-build with zero warnings/errors. Native fixtures now enforce the 80 KB cap
for actual encodes, detailed-image dimensions, and unconfirmed-save UI guards.
Native GDI/JPEG output, portrait visual quality and real SQL interrupted-response
acceptance remain **unrun** on this Linux host. Existing stored photos, SQL,
migrations and DACPAC are unchanged. The old 300 KB statements above describe
the original pinned audit or earlier implementation, not the current limit.

---

## D. Independent security/durability/release audit — finding → later authorized fix

**Chronology:** The first section records original IA/DP/RT problem paths; the later owner-authorized sections record their remediation, security policy, SQL tests and release limitations. IA findings were *initially* excluded, then addressed by a separate, explicitly authorized admin redesign.

### 1. Executive assessment and scope

The application has deliberately implemented protections that should be retained: SQL-enforced permissions and whole-rupee constraints, SQL migration preflight/backup discipline, financial payment recovery journaling, guarded active writes on critical money pages, read cancellation/generation checks, and atomic writes for several settings files. This new review **did not establish a newly introduced live financial posting or database-corruption failure**.

A separate class of concerns remains: **the intent expressed by an administrative UI can differ from the permissions actually submitted; multi-call administrative writes do not share one transaction; local report preferences suppress storage errors while UI claims success; report export overwrites are not staged; and an obsolete release verification command references a missing file**.

#### Prioritized register

| ID | Priority | Evidence classification | Area | Problem | Reach / condition |
|---|---|---|---|---|---|
| IA-01 | **P2 High if maintenance UI is used** | Confirmed UI → parameter path | User administration | **Edit Account** unintentionally includes unsaved role checkbox changes in `SaveUser` | `UsersPage` is deliberately absent from ordinary navigation; applies if invoked for maintenance |
| IA-02 | **P2** | Confirmed separate-call transaction boundary; partial outcome requires injected failure | Role administration | Role definition and permission set are saved in **two independent SQL calls** | Controlled/hidden `RolesPage` only |
| IA-03 | **P2/P3** | Confirmed missing shell write contract; adverse navigation outcome needs runtime exercise | Administrative writes | User/role administration pages don't advertise active writes through `IBusyPage` | Matters if maintenance UI is opened and user leaves/closes during a write |
| DP-01 | **P2** | Confirmed failure-response mismatch | Report saved views | `SaveView`/`DeleteView` can **claim success after a failed settings-file write** | Windows access denied, disk full, locked file, etc. |
| DP-02 | **P2/P3** | Confirmed corrupt-file recovery and overwrite path | Report preferences | Malformed settings JSON is treated as empty; next save **overwrites recoverable settings** without quarantining original | File already unreadable/corrupt; later report preference write |
| DP-03 | **P2/P3** | Confirmed non-atomic write strategy; data loss conditional on interrupted overwrite | CSV/PDF exports | A failed replacement export can leave an existing user report truncated/changed | User chooses to overwrite an existing export; write fails |
| DP-04 | **P3** | Confirmed order of validation and allocation | School logo | 2 MB logo size check is applied **after reading the entire file into RAM** | Unexpectedly large selected file |
| RT-01 | **P3 / release-tool defect** | Confirmed repository inventory and script path mismatch | `VerifyBaseline.ps1` | Verification script depends on `Docs/FROZEN_BASELINE.json`, which is absent from the tracked repository | Invoking this optional script from checkout |
| V-01 | **Release verification** | Not a confirmed exploit or functional failure | Supply chain / authorization | Cached tools, Windows UI and real DB tests need assurance beyond code-level review | Documented in `6; **not** asserted as new defects |

**Priority scope:** IA-01 and IA-02 affect a hidden, intentionally restricted administrative module, not a feature presently exposed to normal school operators. The report does **not** imply that a normal user can reach or exploit those pages. DP-01/02/03 concern report/settings files, **not SQL school-data loss**. No P1 finding is claimed.

### 2. Detailed confirmed / conditional findings and proposed fixes

#### IA-01 — Editing an account silently commits pending role changes

**Source evidence:** [Pages/Administration/UsersPage.cs L1414–1433](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/UsersPage.cs#L1414-L1433); [Pages/Administration/UsersPage.cs L1693–1723](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/UsersPage.cs#L1693-L1723); [Pages/Administration/UsersPage.cs L1761–1801](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/UsersPage.cs#L1761-L1801); [Pages/Administration/UsersPage.cs L1873–1955](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/UsersPage.cs#L1873-L1955).

**Problem explained**

1. The Users page retains two distinct role sets: the *saved* roles (`savedRoleNames` / `SavedRoleIds()`) and the checkboxes currently ticked (`SelectedRoleIds()`). It explicitly labels their difference as **unsaved role changes** and offers a separate **Save Roles** action.
2. The **Edit Account** action is enabled for a selected manageable user without checking `HasUnsavedRoleChanges()`. Its dialog saves via `SaveAccountAsync(..., roleIdsOverride: null)`.
3. When `roleIdsOverride` is null, `SaveAccountAsync` reads the **currently checked role boxes**. It then calls `CallSaveUserAsync`, serializing those role IDs into `@RoleIdsJson` for `SaveUser`.
4. Therefore, editing only the username, password or forced-password-change flag may also submit role grants/removals that the operator had **not** committed through **Save Roles**. The separate Save Roles removal-warning workflow can be bypassed unintentionally.

**Cross-checks:** The saved-vs-selected distinction is real (not a cosmetic label); grid-change/navigation/Deselect paths warn about unsaved roles. `RefreshEditAccountEnabled()` does not block account editing while roles differ. The target `SaveUser` call **does** receive the selected role JSON; comments in this method explicitly confirm an empty JSON array removes role memberships. The normal navigation has no `new UsersPage(...)` entry; the file itself says it is for controlled maintenance.

**Impact:** Accidental privilege escalation, unintended role removal, or loss of access during controlled user maintenance, conditional on this page being opened. Actual SQL authorization remains the separate authority.

**Proposed fix:** On **Edit Account**, pass the **saved** role set (`SavedRoleIds()`), not live unsaved checkboxes; leave permission changes exclusively to **Save Roles**. As additional defense, reject the edit with a clear unsaved-role decision if pending roles are present. Keep the established rule that a newly created normal account may have zero roles (do not silently grant a default).

**Required acceptance tests:** Start with saved roles A+B, untick B and tick C, do **not** press Save Roles; edit only username or password. Verify SQL membership remains A+B and the role draft remains visibly unsaved. Confirm Save Roles separately commits B removal/C addition after existing warning. Repeat with zero selected roles, same user, role removal, cancellation, and limited privileges.

#### IA-02 — Role metadata and permissions can be committed separately

**Source evidence:** [Pages/Administration/RolesPage.cs L804–871](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/RolesPage.cs#L804-L871); [Pages/Administration/RolesPage.cs L854–935](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/RolesPage.cs#L854-L935).

**Problem explained**

`SaveRoleFromDialogAsync` awaits `SaveRoleDefinitionAsync` (which calls stored procedure `SaveRole`), **then** separately awaits `ExecuteUserAsync("SaveRolePermissions", ...)`. Each helper opens and executes its own database operation; the C# caller does not share a `SqlTransaction` or invoke a single procedure enclosing both changes.

If `SaveRole` commits and `SaveRolePermissions` then fails (SQL permission error, validation issue, disconnect, etc.), the role name/description or newly created role can persist while the permission update does not. The page catches the exception, reports failure, and reopens the dialog; it does not distinguish a completely rolled-back operation from a *partially committed* role.

**Double-check / bounds:** This conclusion concerns the **absence of a shared transaction across the two C# procedure invocations**. It does not assume anything about the SQL body other than each separate successful call being capable of committing. A failing second call is required for the partial outcome. The operation is on intentionally non-navigable maintenance UI, and creation with an empty permission set is **normally an approved zero-access default**, not itself a bug.

**Proposed fix:** Create an approved transactional SQL procedure that validates role identity and permission membership together, or add a single-connection C# transaction wrapper around both procedure calls and review the procedures' nested-transaction/rollback semantics. Return the authoritative role ID and final permission state; on an ambiguous outcome show “check role before retrying” rather than opening a create dialog that may duplicate the name. Use a numbered, reviewed migration **only** if a new SQL procedure is needed.

**Required acceptance tests:** Make the permission-update step fail after role creation/rename. Verify all-or-nothing final SQL state and role list; retry without duplicates; run concurrent editor and permission-denial scenarios. Do not relax protected Administrator role rules.

#### IA-03 — Administrative async writes are not represented in shell-level busy guards

**Source evidence:** [Pages/Administration/UsersPage.cs L1–8](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/UsersPage.cs#L1-L8); [Pages/Administration/UsersPage.cs L1640–1729](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/UsersPage.cs#L1640-L1729); [Pages/Administration/RolesPage.cs L1–8](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/RolesPage.cs#L1-L8); [Pages/Administration/RolesPage.cs L87–110](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/RolesPage.cs#L87-L110); [Core/V2Page.cs L719–783](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Core/V2Page.cs#L719-L783); [MainDashboardForm.cs L1597–1619](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/MainDashboardForm.cs#L1597-L1619).

**Problem explained**

- `UsersPage` implements `IUnsavedChanges` but **not** `IBusyPage`; `RolesPage` extends `V2Page` without either busy contract.
- Both pages perform asynchronous privileged writes while remaining capable of handling UI actions.
- `AddBusyAction` disables its own button and shows progress through `BusyScope`. Its base implementation expressly does **not** make the shell treat the page as busy. `MainDashboardForm.ConfirmLeaveCurrentPage` blocks active work only for pages implementing `IBusyPage`.
- Therefore, if either maintenance page is made available, a navigation/close gesture is not reliably blocked merely because an administrative write is in flight. A dirty-role prompt is not a substitute: clean forms may still be saving.

**Impact qualification:** Both pages are intentionally not exposed in current regular navigation; no ordinary-operator reachability is established. The potentially adverse outcome (UI disposal while an awaited call resumes, sign-out during write, confused failure reporting) needs a controlled Windows test.

**Proposed fix:** If maintenance UI remains usable, implement `IBusyPage` in both pages and track **all** save paths, including role definition + permission saves and account dialog saves. Capture the acting user and target values before `await`; block navigation and affected controls for the full write outcome. Never cancel a submitted permission write merely to satisfy the UI guard.

**Tests:** Initiate a deliberately delayed privileged save, then attempt workspace navigation, window close and sign-out. Verify the action is refused until the write completes; then test success, SQL refusal and ambiguous completion. Leave the hidden-by-policy menu state unchanged unless separately authorized.

#### DP-01 — Report saved-view actions return success after persistence failure

**Source evidence:** [Core/ReportLayouts.cs L64–78](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Core/ReportLayouts.cs#L64-L78); [Core/ReportLayouts.cs L118–154](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Core/ReportLayouts.cs#L118-L154); [Pages/Reports/Students/StudentReportsPage.cs L1394–1432](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Reports/Students/StudentReportsPage.cs#L1394-L1432).

**Problem explained**

`ReportLayoutStore.Save(...)` catches **all** exceptions from directory creation, serialization and `AtomicTextFile.Write`, logs them, and returns `void`. `SaveView` likewise returns `void`. `DeleteView` returns whether an entry was removed from a temporary in-memory object, not whether persistence succeeded. The Student Reports page unconditionally refreshes its dropdown and announces that the view was “saved” or “deleted” after calling those methods.

When the settings folder is read-only or storage is full, no changed JSON reaches disk but the application still displays a successful save/delete message. The user may lose the chosen report setup on restart.

**Double-check:** Atomic file replacement itself is an improvement; the problem is **error propagation and final operator feedback**, not lack of atomicity in `AtomicTextFile`. The failure is conditional on an actual I/O or serialization error and was not reproduced here.

**Proposed fix:** Make `ReportLayoutStore.Save` return an explicit persisted result or throw a typed storage exception (with existing logging). `SaveView`/`DeleteView` should propagate that result, and their UI handlers should only claim success after verification. Keep the previous on-disk preferences intact on failure, and show the path to retry/fix permissions without showing an imaginary saved view.

**Tests:** Denied `%AppData%` folder, readonly JSON, full drive, invalid directory, successful normal save/delete and reopen. Confirm no success message is displayed when file persistence fails, and previous views survive.

#### DP-02 — Corrupt report-settings JSON is silently replaced on the next write

**Source evidence:** [Core/ReportLayouts.cs L46–79](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Core/ReportLayouts.cs#L46-L79); [Core/ReportLayouts.cs L82–107](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Core/ReportLayouts.cs#L82-L107); [Pages/Reports/Students/StudentReportsPage.cs L1180–1190](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Reports/Students/StudentReportsPage.cs#L1180-L1190).

**Problem explained**

`ReportLayoutStore.Load()` attempts to deserialize the entire user JSON file. If read/deserialization fails, it logs the exception and returns a brand-new empty `ReportUserData`. The next `SetPreference`, `SetColumns` or `SaveView` modifies that blank data and publishes it with `AtomicTextFile.Write` at the **same path**. Even an automatic “last report scope” preference update can therefore replace the pre-existing damaged JSON without preserving it for repair or recovery.

**Impact:** Loss of the prior set of saved report views, layouts and preferences when the original file contains some recoverable content but cannot be deserialized in full. This is a deterministic recovery-policy weakness **given an already corrupt file**, not proof the application's atomic writes normally corrupt preferences.

**Proposed fix:** Quarantine the unreadable file under a timestamped, collision-safe name or retain an untouched copy, and distinguish a fresh missing file from a corrupt one. Inform the operator before reset/replacement or provide a recover/reset path. If recovery is possible, preserve valid saved views from the damaged file without weakening serializer safety.

**Tests:** Truncated JSON, invalid JSON token, unreadable bytes, unexpected schema, external file lock and missing file. After triggering an automatic preference save, verify a damaged original remains recoverable and never disappears without an explicit recovery decision.

#### DP-03 — Replacing an exported report is not failure-atomic

**Source evidence:** [Core/V2Page.cs L1457–1535](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Core/V2Page.cs#L1457-L1535); [Core/ReportExportBuilder.cs L52–72](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Core/ReportExportBuilder.cs#L52-L72); [Core/PdfTableExporter.cs L79–95](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Core/PdfTableExporter.cs#L79-L95).

**Problem explained**

The export dialog allows the user to select an existing PDF/CSV filename with `OverwritePrompt = true`. CSV output is written directly using `File.WriteAllText(finalPath, ...)`, and PDF output is saved directly using `PdfDocument.Save(finalPath)`. Those operations do not first build a complete file at a unique temporary destination and then atomically replace the old file.

If the write fails after it opens/truncates the destination, the previously valid user export can be lost or damaged. The UI checks for a nonempty result file after successful execution, but a **nonempty file alone is not an integrity check** and cannot restore a previous export that was already overwritten.

**Important scope:** This concerns independently exported PDFs/CSVs, **not the application's SQL payment/admission records**. Overwrite was intentionally allowed; the missing protection is preservation of the old file on failure, not the lack of a prompt.

**Proposed fix:** Export to a unique temporary file **in the same destination directory**; validate final output completion (and parse format where practical); flush/close before atomically moving/replacing the destination. On failure delete only the temp file and keep the previous user export intact. Reuse the verified `AtomicTextFile` pattern where appropriate while preserving binary PDF handling.

**Tests:** Existing file, successful overwrite, simulated exception mid-write, access denied, disk exhaustion, temp cleanup, PDF generator exception, CSV failure, and file lock. Hash the old file before an injected failure and prove it is unchanged afterwards.

#### DP-04 — School logo file is fully read before the stated 2 MB limit

**Source evidence:** [Pages/Setup/SchoolProfilePage.cs L230–269](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Setup/SchoolProfilePage.cs#L230-L269).

**Problem explained**

`ChooseLogoAsync` performs `await File.ReadAllBytesAsync(dialog.FileName)` first, **then** checks `bytes.Length > 2_000_000` and refuses the input. A very large selected file is therefore allocated in memory before validation. The JPG/PNG filter is a file-picker convenience, not a reliable byte-size guard; a renamed/large file can still be selected.

**Impact:** Possible responsiveness and memory pressure (especially on low-RAM school computers) despite a UI that promises a 2 MB cap. No out-of-memory exception was reproduced; the incorrect preflight order is directly visible in code.

**Proposed fix:** Inspect `FileInfo.Length` or stream metadata before allocating; reject oversized inputs first, then read through a bounded stream and revalidate the actual bytes to handle file growth/races. Continue validating image decoding and avoid retaining unnecessarily large intermediate buffers. Do not alter existing school-logo data or school-profile save semantics.

**Tests:** Exactly 2,000,000 bytes, one byte larger, 100 MB file, corrupt PNG/JPEG, file growing between size check/read, canceled picker, and GDI/memory measurement.

#### RT-01 — Frozen-baseline verification script references a nonexistent manifest

**Source evidence:** [Installer/VerifyBaseline.ps1 L1–26](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Installer/VerifyBaseline.ps1#L1-L26) and tracked repository inventory at `cf31e953` (no `Docs/` directory or `Docs/FROZEN_BASELINE.json`).

**Problem explained**

`VerifyBaseline.ps1` unconditionally executes `Get-Content -Raw ...\Docs\FROZEN_BASELINE.json`. The current tracked repository has no such file. Running this utility from a clean current checkout therefore cannot finish its intended fingerprint verification and fails while reading the manifest.

**Cross-check and scope:** The current standard installer has a separate migration/release verification path, and this review did **not** find `VerifyBaseline.ps1` invoked by the main installer. It is a **stale optional verification tool**, not proof the standard application installer is broken.

**Proposed fix:** Establish whether the frozen-baseline verifier is intentionally retired. If retired, remove or clearly mark it obsolete in a separately approved code cleanup. If supported, create/approve a current deterministic manifest and update exclusion rules to reflect the actual release layout; add a CI/PowerShell test that runs the tool from a clean checkout. Do not fabricate a manifest or silently treat failures as passing.

**Tests:** Execute verifier in clean Windows checkout; missing manifest fails clearly; valid manifest passes; changed source file fails; added unauthorized file fails; test with expected release assets/paths.

### 3. Confirmed protections and deliberately excluded false positives

This independent review checked relevant safeguards before deciding whether an observation was actionable.

| Topic checked | Evidence found | Disposition |
|---|---|---|
| Privileged admin reachability | Both `UsersPage` and `RolesPage` explicitly state they are not exposed in normal navigation; search found no active `new UsersPage(...)` or `new RolesPage(...)` call sites outside their definitions | **Not** reported as a normal-user privilege escalation |
| SQL authority | User/role saves invoke user-scoped `DatabaseHelper` procedures; SQL authorization remains authoritative | No claim of bypassing stored-procedure permission checks |
| Normal account zero-role policy | `CallSaveUserAsync` documents intentional empty role arrays; new-account flow explicitly provides an empty role list | No demand to assign implicit default privileges |
| Existing report preference atomicity | `AtomicTextFile` uses unique temporary files, flushed data, then `File.Move(... overwrite:true)` | No allegation that its **normal** save truncates previous settings |
| CSV formula safety | `ReportExportBuilder.Cell` calls `CsvSafety.Neutralise` and quotes/escapes cell text | No unverified spreadsheet injection exploit claimed |
| Financial collection | `CollectionDiscountWriter` uses one serializable transaction for charge discounts and collection, plus a durable recovery intent and no automatic retry on unknown commit | No finding alleging partial family collection here |
| Database upgrade discipline | Installer preserves existing MDF/LDF, verifies migration numbering and backup; `Migrations/2.sql` is transactional and enforces whole-rupee constraints | No DACPAC/replacement-MDF update recommendation |
| Pinned prerequisite download | `Installer/core.ps1` pins the downloaded go-sqlcmd archive SHA-256; downloaded checksum is validated | No blanket assertion that downloads lack integrity checks |
| Cached `sqlcmd` executable | A pre-existing `Tools\sqlcmd.exe` or PATH binary is accepted without the download-pin step | **Conditional release-hardening question**; requires trusted packaging and file-permission threat-model review before labeling a vulnerability |
| Prior dashboard/navigation findings | The current HEAD `cf31e953` adds dashboard/navigation fixes after the earlier follow-up audit; those files changed while this audit ran | **Not re-reported as unresolved** based on older revision |

### 4. Recommended remediation sequence

1. **Security maintenance readiness — IA-01:** Isolate **Edit Account** from pending role edits; test exact saved-vs-selected SQL membership. This is the highest-confidence and highest-impact behavior **if** maintenance UI is used.
2. **Administrative transaction integrity — IA-02:** Make role definition plus permission assignment atomic; add fail-between-steps tests and safe re-open/retry messages.
3. **Administrative write lifecycle — IA-03:** Track `IBusyPage` state for every privileged save. Keep these pages hidden from normal menu unless their business policy is explicitly changed.
4. **Save honesty — DP-01:** Propagate settings-file failure to UI, distinguishing attempted save from confirmed persisted view.
5. **Preference recovery — DP-02:** Preserve corrupted JSON before writing defaults; test automatic scope-saving side effects.
6. **Export preservation — DP-03:** Generate CSV/PDF through same-directory temp files and replace only on verified completion.
7. **Input preflight — DP-04:** Enforce logo input size *before* allocation; use bounded reading.
8. **Release tooling — RT-01:** Retire or repair stale frozen-baseline verifier; test from a clean checkout.

None of IA-01, IA-03, DP-01 through DP-04 or RT-01 inherently requires a database migration. IA-02 **may** require an approved stored procedure/migration for all-or-nothing semantics; verify the existing procedure internals before selecting that implementation.

### 5. Acceptance scenarios / negative tests before marking DONE

These tests are **not claimed to have run** in this review:

| Scenario | Setup / injected condition | Expected result |
|---|---|---|
| Unsaved role leak to account edit | Saved roles A/B, checkbox draft A/C, Edit Account username only | DB roles remain A/B; A/C remains unsaved until explicit Save Roles |
| Roles save atomicity | Fail permissions phase after successful role-definition phase | Neither definition nor permissions partially committed; clear reconciliation |
| Admin write navigation | Delayed `SaveUser` / `SaveRolePermissions` and operator closes page | Busy guard blocks page/sign-out/close until outcome established |
| Report view save failure | Access denied to `%AppData%\FMSC_SMS\ReportLayouts` | Explicit failure; no false “View saved” message; prior JSON preserved |
| Damaged settings | Corrupt JSON then run report that persists LastScope | Original JSON is quarantined/preserved, not destroyed |
| Export overwrite failure | Existing valid CSV/PDF; inject failed write | Existing bytes/hash identical after failure; temp removed |
| Oversized school logo | Choose a 100 MB bogus PNG | Rejected without loading whole file into managed buffer |
| Baseline script clean checkout | Run `VerifyBaseline.ps1` on published repository state | Supported path passes with reviewed manifest or script intentionally retired |
| Role/session consistency | SQL revoke during open maintenance session; stale frontend permission display | Backend denies unauthorized writes and app accurately refreshes/communicates rights |

### 6. Release gates and unproven areas

**Runtime/Windows:** The review used the current GitHub C# and PowerShell source; it did not run a Windows smoke harness, exercise a real school database, simulate an actual disk-full filesystem, or inspect SQL stored-procedure bodies within the DACPAC. Existing reported test results must not be repackaged as test execution from this audit.

**Data preservation:** First installation may place the original school MDF/LDF on the user computer. **Future updates must preserve these files and apply ordered SQL migration scripts**; DACPAC is a review/reference artifact only. Rollback/recovery must use approved SQL backup procedures, not replacement files.

**Privilege model:** User/role administration is currently intentionally excluded from normal application navigation. If it is never shipped or invoked, IA-01/02/03 do not constitute a presently reachable end-user defect; they remain maintenance-code correctness risks. Confirm deployment configuration before assigning a production release blocker to them.

**Supply-chain check:** Downloaded go-sqlcmd archives have an embedded pinned hash; a cached or PATH executable is accepted without the same SHA verification. Review installer package custody, admin elevation, OS access controls and signature verification *before* declaring a security weakness. No claim of malware was established.

**Scope limit:** Source inventory was examined across the repository and targeted flows were traced through caller, callee and protective guards. This is a deep **targeted cross-layer audit**, **not** an exhaustive statement of proof over every line of all 379 tracked files. Large SQL procedure bodies, runtime Windows resources, DB-specific multi-user races and real deployment conditions need additional direct testing.

### 7. Final assessment

**Eight reportable source-level findings** were recorded: IA-01/02/03, DP-01/02/03/04, and RT-01. They cover separate failure domains and do not repeat the prior admissions/photo/navigation findings. The strongest issue is an **administrative intent mismatch**: saving account details sends role changes which the UI expressly labels unsaved. This is important when the maintenance page is used, even though the current application intentionally hides it from normal navigation.

The application still has real existing protections and no newly proven P1 school-database corruption was identified in this review. **Nothing is marked fixed or production-verified by producing this report.** Proposed changes should be small, test-backed, and should preserve business logic and the existing database migration/update policy.

**Repository mutation for this task:** This Markdown report only.

### Later implementation and validation

### 8. Owner-authorized implementation — 13 October 2026

The owner requested the five non-Users/Roles fixes, in two passes. The original
review above remains historical evidence for its pinned commit. Current status:

| ID | Current source disposition | Implementation |
|---|---|---|
| DP-04 | Implemented | School logo uses a bounded file reader. Empty/over-2,000,000-byte files are refused before buffer allocation, exact-limit input is accepted, short/growing reads are refused, and the previous preview/data stay intact until decoding succeeds. |
| RT-01 | Retired with explicit refusal | The obsolete freeze verifier has no current callers or maintained manifest. Its retained entry point now fails clearly with supported `VerifySchemaArtifact.ps1` and validation instructions; it never invents a source-freeze pass. Standard publishing continues to use the approved schema verifier. |
| DP-01 | Implemented | Report mutators return persistence success/change/error explicitly. View Save/Delete report success only after publication; failed operations keep the current selections/dropdown. Missing views are reported as already absent. Column-choice persistence failures also explain that the current selection is session-only. |
| DP-02 | Implemented | Report reads may use defaults without changing the source. Mutations refuse unreadable/locked originals. Corrupt JSON/invalid setting shapes require a unique adjacent `.corrupt-<UTC>-<guid>.bak`, containing the exact original bytes and flushed to disk, before replacement. Failed backup prevents replacement; failed publication retains original and any completed backup. Reads/no-op mutations do not repeatedly quarantine files. |
| DP-03 | Implemented | CSV, table PDFs and RDLC-preview PDFs generate a unique temporary file beside the chosen destination, close/flush and validate it, then replace by same-directory rename. Failed generation/validation/replacement preserves the previous export and cleans only the temporary file. PDFs are parsed and must contain pages; CSV uses strict UTF-8, keeps its BOM/escaping/formula safeguards, and streams rows instead of keeping a full second text copy. |
| IA-01/02/03 | Excluded by owner | UsersPage and RolesPage source were not changed or exposed in navigation by this pass. |

**Executed checks:** The portable production-helper suite passes **471
assertions**, including **82 new file-persistence checks**. The new cases cover
logo size boundaries, sparse 100 MB input, growing/truncated/cancelled reads,
actual file-sharing refusal, save/delete write failures, corrupt/null/incomplete
JSON, exact-byte recovery backups, backup and post-backup publication failures,
UTF-8 BOM compatibility, interrupted/empty/invalid exports, refused replacement,
temporary cleanup and actual CSV encoding failure while preserving prior bytes.
The application and native Windows smoke harness cross-build with zero warnings
and errors. PowerShell execution confirms that the retired verifier exits 1 with
its guidance and the supported approved-artifact verifier exits 0.

**Still pending:** Native PDF-generation/parse/file-lock fixtures are compiled,
not executed on this Linux host. Windows report-view dialogs/status, RDLC export,
real access-denied/disk-full behavior and deployed-filesystem acceptance remain
release checks. Injected I/O failures verify controlled recovery paths, not every
storage hardware failure. This pass changes no SQL, migration, DACPAC, school
records, stored images or Users/Roles functionality.

### 9. Completed Users/Roles maintenance flow — 13 October 2026

The owner subsequently authorized a complete, simpler and stronger Users/Roles
editing flow. The exclusion in section 8 applies to that earlier pass only.

| Finding | Current implementation |
|---|---|
| IA-01 | Account details and assigned roles form one explicit inline draft and one **Save User** action. Separate Edit Account/Save Roles dialogs/actions are retired. Password reset is a separate password-only dialog; it is disabled/refused while account/access edits are pending, uses the saved record rather than live checkboxes, and rechecks the exact snapshot before writing. |
| IA-02 | **Save Role** captures name, description and permissions in one request. Existing `SaveRole` and `SaveRolePermissions` execute on one connection inside an enclosing serializable C# transaction. Each nested procedure must preserve the enclosing transaction count; IDs are validated before the outer commit. A failed second step rolls back the definition, permissions, membership side effects and audit entries together. No SQL procedure/schema change or migration is required. |
| IA-03 | Both pages share `IBusyPage`/dirty-state guards across reads, account saves, combined role saves, password hashing/reset, post-save refresh and read-only recovery. Switching records, New, Reload, keyboard Save and affected controls respect those guards. |

**Simplified operator flow:** New User → username/initial password/password-change
requirement → explicit role selection → Save User. Existing User → edit displayed
account/role draft → Save User. Clean saved User → Reset Password, which changes
only credentials and its next-sign-in requirement. New/existing Role → name,
description and filtered task/area permission grid → Save Role. Each editor has a
compact searchable record list, selected-record/state heading, assignment filter
and effective/proposed access summary. Filters use cached DataViews and 150 ms
debounce; hidden checked rows remain selected. Large per-role/per-permission
decorative control trees and redundant dialogs are removed. Fewer source lines
are not claimed as measured deployed performance.

**Additional recovery safeguards:** A coherent catalog/record snapshot is checked
under the write transaction before either procedure is submitted. Competing
edits, catalog/grant meaning changes and changed affected memberships are refused
without overwriting the newer state. A successful write followed by failed UI
refresh retains confirmed identity/values and requires Reload before another
write. Lost commit replies/failed rollback remain unconfirmed and expose Check
Saved Changes; that action only reads and verifies submitted values, including
an opaque credential proof for password changes. It can establish a complete
save, unchanged original/absent new record, or conflicting stored values. It never
automatically repeats a write. Binding failure restores the entire previous
editor, including unsubmitted new-account password inputs. Raw derived hashes
and salts are not added to editor models, logs or audit display; temporary password
buffers are zeroed, plaintext inputs are cleared after confirmed success, and
PBKDF2 remains compatible with sign-in (100,000 SHA-512 iterations, 16-byte salt,
64-byte derived hash) off the UI thread. Cleanup cannot turn a confirmed commit
into an apparent failed save.

**Role-definition scope:** Role names, memberships and grants are database data;
the DACPAC supplies their schema/procedures, not the live school's catalog. No
defined role is merged, renamed, deleted or automatically granted by this
restructure. Permissions are grouped/displayed more clearly, and known viewing
dependencies are explained without silently adding permissions. Future role
consolidation needs the actual catalog, affected memberships, exact permission
sets and business responsibility review before an explicit data migration.
Administrator role/account protection includes both primary-role and membership
paths. Multiple roles, deliberate zero-role/zero-permission access and the legacy
primary-reference rule remain intact. Database permissions are checked on
operations; menus/session permissions refresh on sign-out/sign-in. The existing
hidden maintenance navigation policy is unchanged.

**Executed verification:** **522 portable assertions pass**, including 51 new
production administration model/coordinator checks. **39 native SQL checks pass**
using the production `AdministrationStore` against the approved DACPAC deployed
to a new, isolated SQL Server 2022 Developer container and synthetic
`FMSC_AuditChecks_Admin_20261013` database. They verify user/role round trips,
password compatibility/reset isolation, zero access, both Administrator
protection paths, stale account/role/catalog refusal, captured-actor audit,
read-only write denial, actual trigger-injected second-step rollback (including
audit/membership effects), and competing simultaneous editors. Ordinary login
counters do not invalidate an otherwise unchanged account draft. The application
and Windows smoke harness cross-build with zero warnings/errors; the latter now
uses the production internal UI helpers instead of linked duplicate copies.

**Native UI fixtures remain compiled/unrun:** They cover unified saves, dirty
selection guards, same-record draft retention, password/reset separation,
new-account validation/secret clearing, filtering hidden checks, stale/unconfirmed
recovery, atomic binding rollback including initial passwords, busy/disposed
completion, protected/read-only editors and compact/wide layouts in Professional
Simple and Ocean Blue. Actual Windows painting, DPI/keyboard/modal behavior,
target LocalDB/school-clone acceptance and session refresh remain release checks.
No school database or release MDF/LDF was opened or changed. SQL, migrations,
DACPAC and their approved fingerprints remain unchanged.

### 10. Data-bearing DACPAC review and role-change scope — 13 October 2026

**Reviewed input:** owner's commit `ad60bbb` (`dacpac`), package SHA-256
`4c2eb8cbb76ce892666708b5397c9b4ee1f0fdca529ea4cac72b8cdbca6afabe`.
This artifact actually includes BCP table data. It was published with native DacFx
into a newly created, loopback-only SQL Server 2022 Developer container database
`FMSC_AuditChecks_RoleData_20261013`. No installed school database or detached
release files were opened or updated. Original catalog observations below were
captured before the production-writer tests added or edited fixture records.

#### Actual role definitions and assignments

| Role | Stored permission rows | Assigned accounts | Observed responsibility |
|---|---:|---:|---|
| Administrator | 0 | 1 | Protected; SQL and C# Administrator bypass supplies full access. Zero explicit grants is expected. |
| Accountant | 6 | 1 | Student viewing, fee viewing/collection, broad reports, fee reports and annual fee statements. No `Fees.Adjust` grant. |
| Students | 0 | 1 | Empty role; contributes no permissions. Its intended business purpose is unknown. |
| Front Desk | 6 | 0 | Admissions, student viewing/administration, fee viewing/collection and broad reports. |

There are **57 permission definitions, 12 role-permission mappings, three role
memberships and one account**. The same account has Administrator, Accountant
and Students memberships. Accountant/Students therefore add no effective access
to this account beyond Administrator. This snapshot cannot demonstrate separate
staff operation; fresh role-only probe accounts were used in the disposable copy.

Accountant grants: `Students.View`, `Fees.View`, `Fees.Collect`, `Reports.View`,
`Fees.Reports.View`, `Fees.Statement.View`.
Front Desk grants: `Students.View`, `Fees.View`, `Students.Administer`,
`Fees.Collect`, `Reports.View`, `Students.Admit`.

#### Confirmed behavior and potential inconsistencies

- `Students.Administer` is broad maintenance access, including saved student
  editing/photos, re-admission and multiple status/lifecycle/legacy operations.
  Front Desk's stored description mentions only admissions and fee collection;
  the grant is broader than that description. Whether this is intended is a
  business decision. Removing it immediately would also remove existing useful
  detail/photo operations. The current source references this code in ten C#
  files and at least 24 procedure authorization checks.
- `Reports.View` opens student and finance categories in `ReportsHomePage` and
  guards their SQL reports. Accountant and Front Desk can therefore see broad
  reports; `Fees.Reports.View` does not isolate the report hub to finance. It
  currently also controls billing-status checks.
- Granular catalog codes such as `Students.Edit`, `Students.Photo.Manage` and
  `Students.ReAdmit` have no corresponding action gates or procedure permission
  checks in the reviewed source. Checking them alone does not enable those
  workflows. They must not be presented as functional substitutes for
  `Students.Administer` before C# and SQL are aligned.
- `System.Users`/`System.Roles` have legacy names that say "Manage", while the
  completed editors use them as read prerequisites and `.Manage` codes for
  writes. No staff role in this snapshot grants either management path.
- `System.Roles.Deactivate` exists in the catalog, but the schema has no role
  active flag and the application has no matching workflow. It cannot currently
  implement role deactivation. This is dormant metadata, not a finished feature.

These observations do not authorize automatic deletion, renaming, redistribution
or expansion of existing grants. The owner requested the scope before choosing
whether to redefine/reorganize roles. Existing definitions, memberships,
Administrator protection, zero-access behavior and hidden maintenance navigation
remain unchanged.

#### Scope before choosing a role redesign

| Possible change | Scope | Required implementation |
|---|---|---|
| Use three operator roles: Administrator, Accountant, Front Desk; retire Students if it has no intended purpose | Small, data only | Inventory all assigned accounts and legacy `Users.RoleId` references; review responsibility matrix; transactionally remove/reassign only approved memberships and role. No permissions need merging. |
| Improve descriptions and read/manage labels | Small | Update selected catalog labels/descriptions after agreeing their meaning; preserve permission codes and IDs. |
| Limit Front Desk to admission, ordinary detail/photo edits and collection | Medium, C# and SQL | Define allowed actions first; replace broad maintenance guards selectively with functional granular permissions in both layers; migrate reviewed grants; verify allowed operations and denied lifecycle/correction actions. |
| Limit Accountant/Front Desk reports by responsibility | Medium, report navigation and SQL | Choose report categories; align hub visibility, report loading, exports and SQL checks; migrate reviewed grants and test both allowed and refused paths. |
| Remove/merge dormant permission definitions | Broader cleanup | Map all UI/SQL/legacy consumers and assignments first; preserve code/ID compatibility where needed. An absent C# literal alone does not prove a permission is safe to delete. |

Recommended next decision: agree an action matrix for the three operator roles,
then implement only the required guard/grant changes. Role count alone does not
resolve the broad-versus-granular permission mismatch. Any chosen production SQL
changes will be numbered scripts in **Migrations/**. **No SQL migration is
required by this artifact review or the already completed administrative flow.**

#### Verification and reviewed artifact metadata

- Native DacFx successfully restored schema and embedded data. SQL counts and
  all twelve grants matched independently decoded role-catalog BCP records.
- Administrator returned all 57 effective permissions. All eight whole-rupee
  constraints were enabled and trusted; database version rows include version 2.
- **39 production administration SQL checks passed again** on this imported
  disposable copy: combined saves, credentials, protection, stale/catalog guards,
  concurrent edits, second-step rollback/audit rollback and read-only recovery.
  They modified only disposable fixtures; they are not real staff GUI acceptance.
- **12 additional SQL boundary assertions passed** using separately assigned
  Accountant, Students and Front Desk probe accounts. Effective grants matched
  exactly; none could manage users/roles; Accountant was denied fee adjustment;
  Front Desk passed broad student-administration and report checks. Probe account
  writes were rolled back.
- All **273 SQL module fingerprints remain identical**. Model differences are
  SQL Server's `FLOOR` keyword casing, serialization/order of those constraints,
  and identity last-value annotations. Full model comparison after these reviewed
  normalizations found no other changes. Runtime whole-rupee policy is intact.
- Updated approved package/model hashes and startup-contract package provenance
  after that review. Offline policy validation accepts casing of the `FLOOR`
  keyword only; it still rejects different columns, operators or extra SQL.
  Full package/model/module fingerprint checks remain strict.
- **12 artifact regression tests and 522 portable assertions pass.** Native
  Windows GUI/DPI and release acceptance remain pending.

The disposable SQL container and private test credentials were removed after
verification. No role definitions, production procedures, schema versions or
DACPAC bytes were changed by this review.

### 11. Owner-approved two-role policy and consistent permissions — 13 October 2026

The owner subsequently approved keeping **Administrator and Accountant only**, with
Administrator always holding full authority, and explicitly chose to preserve
Accountant's current access. This supersedes section 10's pending role decision.

**Implemented SQL update: `Migrations/3.sql`.** The script requires version 2,
uses one transaction for data, seven procedure definitions, audit and version 3,
and makes repeat execution at version 3 a no-op. Existing Accountant grants and
IDs remain intact. In the supplied snapshot these are exactly `Students.View`,
`Fees.View`, `Fees.Collect`, `Reports.View`, `Fees.Reports.View` and
`Fees.Statement.View`; student and finance reports remain available. The migration
removes every other role and its assignments. It preserves all user accounts,
credentials and password requirements. Accounts with only removed roles receive
no automatic replacement grant: their legacy primary reference becomes
Accountant while an empty membership set continues to mean zero access.

Administrator primary-role or membership accounts are normalized to Administrator
with only that membership. No extra role or permission grant is needed for full
authority. Role saves no longer automatically assign Accountant to Administrator,
and first-run setup now assigns Administrator only.

**Permission consistency:** a shared embedded `Core/PermissionCatalog.json`
contains **28 implemented permissions**. Migration 3 carries the same catalog and
removes 29 dormant/superseded options from the former 57 definitions. No permission
IDs are renumbered and no grant is remapped to a broader capability. Names and
descriptions distinguish viewing from management and make the broad scope of
`Reports.View` and `Students.Administer` explicit. `Fees.Reports.View` accurately
names billing-status checks; annual fee receipts retain `Fees.Statement.View`.

The updated procedures retain their existing parameter signatures/defaults:

| Procedure | Change |
|---|---|
| RequireAnyPermission | Same unconditional Administrator primary/membership bypass as RequirePermission. |
| SaveRole | Edit the existing Accountant only; no new roles/renaming; remove automatic assignment to Administrator. |
| SaveRolePermissions | Require management access, not System.Roles read access; validate the complete JSON array and positive/known supported IDs; require management viewing prerequisites. |
| SaveUser | Protect Administrator membership as well as primary role; validate whole role arrays and the supported two-role selections. |
| BootstrapFirstAdministrator | Assign only Administrator while retaining protected setup locking and credential checks. |
| AddStudentSibling | Require Students.Administer, matching the C# add/remove family controls and DeleteStudentSibling; retire Students.Siblings. |
| GetPermissions | Read via System.Roles/System.Administration, matching the maintained catalog flow; retire System.Permissions. |

C# and installer require **database version 3**. The role editor hides New Role,
keeps role names read-only and rejects direct new/rename calls. It edits only
Accountant's description and checked permissions; Administrator remains full and
protected. Its account display shows actual role membership rather than falsely
checking Accountant to represent full authority. User assignment offers
Accountant or no access for normal accounts.
C# refuses incomplete administrative management/view combinations before saving,
and SQL enforces the same rule. Catalog loading refuses unmigrated/unsupported
role or permission definitions. Existing dirty/stale/busy guards, combined saves,
password-only reset and read-only outcome recovery remain. Hidden maintenance
navigation remains the established policy.

**Verification:** 530 portable assertions (59 administrative), 67 production
administration SQL checks, 15 migration durability assertions, four cross-layer
permission-catalog checks and twelve artifact regression tests pass. The SQL
fixture was restored from the owner's version-2 DACPAC in a disposable loopback
SQL Server 2022 container. The migration tests inject a final version-write failure
and compare role/permission/account/audit state and procedure definitions: all
roll back. They verify older-version refusal, exact Accountant grant preservation,
legacy references, empty access, Administrator membership normalization, unchanged
school record counts and no-op repeat execution. The writer tests cover malformed
payloads, direct SQL read/manage boundaries, full Administrator authority,
concurrent/stale edits, combined-save rollback and exact Accountant capabilities.
Three first-run checks also pass on a separate, empty native schema-only copy:
Administrator-only assignment, full primary/membership authority and refusal of
repeat setup. `Tests/AdministrationSqlChecks/FirstRunChecks.sql` documents that fixture.
The application and Windows smoke harness compile with zero warnings/errors;
interactive Windows/DPI tests remain unrun on this Linux executor.

**Deployment handoff:** apply migration 3 with the established recovery backup and
all application sessions closed, then provide a newly extracted version-3 DACPAC
for review. The supplied version-2 DACPAC and its approved fingerprints were left
unchanged; the artifact verifier passing for it does not certify version-3 release
packaging. Updated MDF/LDF preparation remains the owner's existing release step.
No installed school database was changed. The completed flow is committed source
plus a tested migration, not a claim that the owner's database is already upgraded.

### 12. Owner-authorized dashboard Account navigation — 13 October 2026

The owner explicitly requested Users/Roles access in the dashboard Account menu.
This supersedes the hidden-navigation policy recorded in earlier sections.

The Account dropdown now groups **Users** and **Roles & Permissions** after the
personal username/password commands. Visibility uses the same `AccessRules` read
prerequisites as the pages. Administrator's full authority makes both entries
available; Accountant's preserved six grants expose neither. Separately granted
read access can open a read-only page, while writes retain the existing management
checks in both C# and SQL.

Opening an administrative entry is deferred until the dropdown click completes,
then rechecks the original signed-in user and current read permissions. Navigation
uses the normal dashboard route, breadcrumb/title and dirty/busy/recovery guards.
Role names remain fixed, Administrator remains protected/full, and permission
editing remains the existing combined Roles & Permissions page. No additional
SQL change, migration, DACPAC or data update is required by this navigation change.

The application and Windows smoke harness compile with zero warnings/errors;
the existing 530 portable assertions pass. Interactive Windows menu execution
remains part of native acceptance, not a claim made by the Linux build checks.

### 13. Colored lightweight themes and rendering reliability — 13 October 2026

The owner requested lightweight versions of the existing selectable color themes,
stronger colors and improvements to the shared light-rendering system. The
notification-system proposal remains on hold; no communication feature is added.

**Available appearances:** the four original themes retain their standard
rendering. Account → Change - Appearance/Theme presents each color family as an
explicit **Standard / Lightweight pair**: Ocean Blue, Emerald Green, Professional
Orange and Royal Violet. Professional Simple remains an additional lightweight
choice. The owner's clarification is implemented in menu labels/grouping; no
standard theme was removed or converted to lightweight. All five
share the established flat/no-shadow/immediate-feedback path; forms and report
flows are not duplicated. The existing enum IDs 0–7 and saved names keep their
meaning; four new IDs 8–11 are appended. Retired Rose/Teal/Amber preferences keep
their established mappings. Unknown preferences safely fall back to Ocean Blue.
The selectable collection is read-only and theme preference writes retain the
existing atomic file replacement and logged fallback behavior.

**Stronger presentation:** each lightweight family has a distinct saturated
accent, darker hover, more visible selection/header tints and clearer input
boundaries. Professional Simple's blue is refreshed too. Shared success/warning
colors and muted guidance use readable contrast. Menus no longer use pale text
against their white surfaces. Selected student rows use a contrast-selected
foreground; modern and legacy solid actions share actual sRGB luminance-based
text selection, including hover/press fills. Lightweight hover uses its darker
semantic role color. Standard theme accent palettes remain intact.

`AppTheme` remains the public source of colors/rendering behavior;
`AppTheme.Appearance.cs` holds its portable palette/policy implementation.
Palette lookup uses fixed switches/cached palette values rather than a theme-list
search during painting. Fonts, spacing, widths, heights, data, control order,
action meaning and keyboard navigation are retained. The 55 portable theme
assertions verify saved identities/fallbacks, all five distinct palettes, at
least 4.5:1 primary/hover/text contrast and 3:1 input-boundary contrast on white,
plus readable solid-action foregrounds and independent refresh delivery.

**Resource/lifecycle improvements:** loading indicators stay static in lightweight
mode without allocating animation timers. Standard-mode loading timers are lazy,
released while hidden, on handle destruction and on disposal; Windows motion/
remote-session preferences are respected. Loading labels reuse the shared font
cache and refresh their colors. Status-attention flashing is limited to one
session per label, replaces prior sessions, cancels on theme changes and releases
its timer/subscriptions on disposal. Lightweight warnings retain their text and
tooltip with no flash timer. Popup/calendar shadows are disabled in lightweight
mode. Existing flat rendering avoids rounded clipping/path allocation and button
shadows; hover-transition timers are released on entry to any lightweight option.

**Safer switching:** recursive theme requests during refresh are ignored. Failure
to capture the old control bindings logs and aborts before changing the theme.
A failed theme subscriber is logged without blocking later subscribers; individual
control/form refresh failures during a theme change are contained so the remaining
UI can refresh. Forms/children are snapshotted during theme-change iteration.
The regular idle initialization does not add a snapshot allocation on every idle
callback. Account-menu theme changes are deferred until its click completes,
then recheck the session before the sidebar rebuild retires its owner button.
These safeguards contain refresh failures; they do not claim every third-party
control can always redraw successfully or that deployed CPU/RAM was measured.

**Verification:** **585 portable assertions** pass, including 55 new theme checks.
The application and expanded Windows smoke harness build with **zero warnings
and errors**. The harness covers forward/reverse switching of all nine choices,
saved selection, draft/validation/focus/geometry preservation, clipping regions,
flat paint paths, loading/attention lifecycle, contrast, failed listener/control
refresh and recursive callbacks. Compact/wide Users/Roles layouts include all
appearances. Those native GUI tests are **compiled but unrun** on Linux; Windows
100/125/150% DPI, physical color/readability and low-end resource measurements
remain acceptance work. The sixteen artifact/catalog tests still pass.

No SQL procedures, migrations, permissions, database version or DACPAC were changed
by this work. Database version 3 and its earlier migration handoff remain as
previously documented.


### 14. Quick Registration parent links, first-time siblings and safe entry flow — implemented 13 October 2026

**Owner requests:** expose linking an existing parent before and after quick
registration, allow unlinking, make single-child bills clear, and strengthen the
entry flow. First-time siblings must be handled before confirmed admission.

- **Link Existing Parent** and **Unlink Parent** are explicit page actions, using
  the existing `Students.Administer` permission in C# and SQL. The original card
  accessory was difficult to discover and disabled after saving. Both actions now
  remain available for saved Pending/Active students when the form is clean.
  The same maintenance guard covers editing saved Pending records and adding
  another child into a family; admission/view-only access can create a standalone
  registration without being offered unusable family/edit actions.
- Parent lookup uses the existing shared picker. Verified `GetStudents` with
  `IncludeFormerStudents=1` includes pending children under QR references; the
  picker retains the Pending status and excludes the currently selected student.
  There is no separate Parents table in this DACPAC: linking chooses another
  child of the same parent and updates existing FamilyMembers relationships.
- First-time siblings: save child one; use **Add Another Child** to retain that
  pending child's identity as family anchor and carry parent/contact details;
  save child two. The existing AdmitStudent/AddStudentSibling transaction creates
  the family when neither child had one. Admission confirmation is not required.
- Saved students can link, unlink and relink from the same page. A saved link
  confirmation shows both students/parent names; unlink confirmation explains
  exactly what remains. New-draft unlink retains entered details and makes the
  parent field editable without a database write. Saved unlink removes only the
  current membership, preserving other children and all student/financial data.
  Parent/contact fields remain per-student; saved linking does not silently change them.
- The production writer performs saved family mutations in a serializable
  transaction around existing procedures. Its token includes the current family's
  memberships as well as student/enrollment/fee assignments. Stale student or
  sibling changes reject the mutation; different established families cannot be
  merged by the existing SQL safeguard. Family ID/count are loaded within the
  same transaction and validated before applying a view.
- Dirty edits must be saved before billing/payment/family mutations. Starting
  **New Family** or **Add Another Child** now confirms before discarding a draft.
  Both wait during in-flight work; old load generations are invalidated before
  clearing. Bill preparation holds the page busy so its student/family context
  cannot change during the read. Saved screens offer **Reload Registration**;
  failed/uncertain family writes and failed post-commit refresh require recovery
  before continuing. Initial/save status messages and action tooltips explain
  next steps and pending-sibling handling.
- The neutral **Print Bill** action remains correct for one or several children.
  One child's outstanding charges print a **Student Bill** headed with that child;
  multiple child references print a **Family Bill**, retaining totals and separate
  sections even when children share a name. Billing scope still includes existing
  family members with outstanding charges.

**Verification:** **602 portable assertions** pass, including eight executed
student/family report-data checks. The expanded production QR
SQL harness passes **34 checks** against a disposable loopback SQL Server 2022
publication of the supplied data-bearing DACPAC with migration 3 applied. Checks
include first-time Pending search/creation, later link/unlink/relink, actual
CompleteQuickAdmission followed by family edits, stale/member-change/cross-family
refusal, permission enforcement, exact preservation of student/enrollment/fee/
charge/payment rows and preservation of remaining family members. The app and
Windows smoke harness compile with zero warnings/errors. New GUI scenarios are compiled but unrun on Linux; supported DPI,
compact layouts, printed output and real keyboard/dialog behavior remain Windows
acceptance work. The disposable container and temporary credentials are removed.

No new procedure/schema migration, permission definition, version or DACPAC change
is required. Existing migration 3 and the earlier owner DACPAC handoff remain.


### 15. Consistent fee-grid captions — implemented 13 October 2026

The owner requested consistent terminology wherever fee grids are displayed.
`UiText.Fees` now owns Fee Type, Amount (Rs.), Discount (Rs.), Net Amount (Rs.)
and Frequency captions, plus contextual current/new and posted/collected/
uncollected amounts. Net Amount means the fee after discount; Due, Pay Now and
Net Paid retain their separate financial meaning.

Shared captions are applied across admission/quick registration, return and
readmission/transfer fee previews, student assignments/future/pending fees,
fee-type/class-rate setup and copy/bulk/apply previews, fee collection, posting/
reversal, student statements, payment details and unposted-fee reports. Fee-history
mixed headings now say Fee Type / Action. Bound fee columns use the same captions
through the shared grid presenter, including columns already humanized during
binding. Quick Registration's net-amount column has room for its longer caption.

Only presentation changes: data names, column order, monetary calculations,
permissions, schema, migrations and DACPAC are retained. Application and Windows
smoke-harness builds pass with zero warnings/errors; all 602 existing portable
checks pass. No new label-only test suite or SQL migration is added. Windows
visual/DPI verification of the longer captions remains acceptance work.


### 16. Quick Registration interrupted-save recovery — implemented 13 October 2026

**Confirmed source gap:** the new-registration Save catch restored a usable new
form after any write exception, including a lost outer-transaction commit reply.
The earlier successful-save refresh guard only ran once an ID had been returned;
retrying an uncertain new save could submit the child again.

**Implemented:** an exact request GUID, actor, database-scope digest and payload
hash accompany each new save. `Migrations/4.sql` adds the small
QuickRegistrationRequests table with primary/unique keys and student/user foreign
keys. The existing admission procedure and the request marker commit together in
the C# outer transaction. Procedure signatures, fees and admission rules are
unchanged. A repeated identical request returns its original identity, and an
actor/payload mismatch is rejected. A malformed response cannot commit.

The commit coordinator distinguishes confirmed success, confirmed rejection and
unconfirmed commit/rollback. It never automatically retries a write. Recovery
uses an indexed lookup by the exact request, after acquiring a shared request
lock; an unfinished writer or timeout cannot become a false "not saved" result.
The writer takes the same lock exclusively. Recovery requires student viewing;
checking another operator's request also requires Administrator authority.
Migration 4 also creates a persistent database recovery GUID. Startup validates
its type/nonempty value; scope survives aliases/restarts and rejects another
database identity even under the same connection name. Migration rollback undoes
this identity, and an idempotent rerun preserves it.

A small exclusive local intent is flushed before submission and survives page
closure or process restart. The intent contains a short student/parent/date
summary and hashes, not credentials, full input, connection strings or photos.
The form blocks further save/new-family/sibling/parent/payment/bill actions until
resolution. It automatically checks on reopening and exposes **Check Save
Outcome** for failed checks. Confirmed saves are loaded before clearing recovery;
a failed refresh or journal-clear failure retains recovery. Current input stays
on-screen after rejected saves. Following a restart and confirmed absence, staff
may need to re-enter the draft. Corrupt journals are retained for review.

Application and installer now require **schema version 4**. Migration 4 must
follow migration 3. Runtime checks merge an additive migrated-schema definition
with the reviewed artifact inventory. The owner supplied an updated version-3
DACPAC at `51abb13` while this change was in progress; its review is recorded below. Native checks prove version 4 with missing request
uniqueness is refused. Request metadata adds small fixed-size SQL rows and a tiny
local file, with no timers/polling; low-end RAM/CPU measurements remain acceptance
work.

**Verification:** **636 portable checks** pass, including 34 new exact-result,
commit/rollback and durable-journal checks. **51 production SQL checks** pass on
a disposable SQL Server 2022 publication of the latest version-3 DACPAC plus
migration 4. These exercise an actual server commit followed by an injected lost reply;
exact recovery; sequential/concurrent duplicate-request refusal; payload/scope
and other-operator denial; Administrator recovery; student/marker/fee/enrollment/
charge/family/audit rollback together; deliberate retry after confirmed rollback;
recovery while a SQL request lock is held; typed discounts and startup rejection
of a missing recovery uniqueness safeguard.

A separate version-3 DACPAC copy passes **9 migration checks**: older-version
refusal, injected final-version failure, atomic DDL/version rollback, successful
version 4, two trusted foreign keys/two uniqueness safeguards, idempotence, exact
preservation of school/accounts data and final application structural validation.
The **16 artifact/catalog tests** pass. App, smoke and historical administration
harnesses build cleanly. New native GUI recovery guards are compiled but unrun on
Linux; actual Windows disconnection, file permissions, keyboard/DPI and restart
acceptance remain required. Task-created SQL containers and credentials are
removed after verification; no live school MDF/LDF is modified.


#### Latest owner DACPAC review incorporated during this change

GitHub commit `51abb13` replaced the DACPAC. Native DacFx publication verifies
its data includes version 3, exactly Administrator/Accountant, 28 definitions
matching PermissionCatalog, Accountant's six preserved grants and normalized
Administrator membership. All 273 module identities are retained. Exactly seven
module fingerprints changed: AddStudentSibling, BootstrapFirstAdministrator,
GetPermissions, RequireAnyPermission, SaveRolePermissions, SaveRole and SaveUser.
Each new body exactly matches the corresponding approved migration-3 script;
parameter/table/index/constraint inventory remains compatible. The only other
model-element differences are LastValue identity annotations in AuditLogs,
DatabaseBackups and DatabaseVersion. Package ZIP/model integrity and contract
checks pass. Approved provenance/fingerprints and the base contract package hash
are synchronized after this review, not silently accepted before inspection.

- Package SHA-256: `0afe32d41a914b348b67d248b31ed50e86b21bbfcde17dd374d14df9e2ee8267`
- Model SHA-256: `bf0ee32940ec650bce0e7cde001bc2d6fb8022a84c8b2aecf484e255774e2888`

The 51 recovery/QR SQL checks and nine migration checks are rerun on fresh copies
of this latest artifact. The new request table is absent in the supplied snapshot;
apply migration 4 before using the updated application. Earlier source reports
refer to the older version-2 snapshot as historical evidence.

---

## E. Functional workflow audit (FQA) — original scenario paired with final fix

Each item retains its original operator reproduction path and evidence, followed immediately by Codex's verified-source disposition, test claims and acceptance limits. This avoids needing separate "problem" and "fix progress" files.

### FQA-01 — Review / Confirm modal has no dirty/busy closing guard [P2]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Students/Maintenance/StudentDataReviewPage.cs` L63–72](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentDataReviewPage.cs#L63-L72), [`Pages/Students/Admissions/StudentAdmissionPage.cs` L300–304](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Admissions/StudentAdmissionPage.cs#L300-L304), compare protected [`Pages/Reports/Students/StudentReportsPage.cs` L259–287](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Reports/Students/StudentReportsPage.cs#L259-L287) and [`Pages/Students/Profile/StudentProfilePage.cs` L294–323](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Profile/StudentProfilePage.cs#L294-L323).

**Exact behavior:** `AdmissionReviewListPage.ReviewRegistrationAsync` creates a modal `ApplicationWindowForm` and hosts `new StudentAdmissionPage(...registrationReview:true)`. It subscribes to `RegistrationFinished`, but has **no `FormClosing` handler** to inspect the admission editor's `IUnsavedChanges` and `IBusyPage`. Comparable modal editors in the project already implement such a guard.

**Reproduce:** Open Pending Registrations → Review / Confirm; change an admission detail, click X or Escape. The shell never sees the nested editor because this is its own modal. **Expected:** discard confirmation for edits and absolute block for submitted writes. **Fix:** reuse a common modal leave-guard helper that checks child contracts. Coordinate with **FQA-15** so `IsBusy` reflects the actual confirmation operation. Test X, Escape, Cancel Registration, failed save, and active confirm.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** Added ModalPageCloseGuard using existing IBusyPage and
IUnsavedChanges contracts. The registration review owner attaches this guard:
submitted operations block every close reason, edited drafts require discard,
and an explicitly completed/cancelled registration closes without a false draft
prompt. FQA-15's shared-operation state is included through the hosted editor.

**Verification:** App and Windows smoke project build without warnings/errors.
Added modal event fixtures for clean close, Stay/discard, active user/app close,
completion while still busy and confirmed completion. Native modal interaction
and SQL-delayed close acceptance remain outstanding on Windows. No SQL changes.

---

### FQA-02 — Correction/refund amount and reason persist across different payments [P2/P3]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs` L289–349](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs#L289-L349), [`Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs` L350–451](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs#L350-L451).

**Exact behavior:** Payment `SelectionChanged` resolves the new `selectedPaymentId` and `selectedPaymentAmount`, then `UpdateActionMode` rebuilds operations. For **Correction** and **Refund**, `actionAmount` is reset to `"0"` **only when blank**. A previously entered value can remain from another payment/student; `reason` and transaction date are also retained. The confirmation **does correctly name payment, student, date, amount and reason**, which limits but does not eliminate the UX risk.

**Reproduce:** Choose payment A → type Rs. 500 / a reason → select payment B (with same adjustment operation). Amount and reason can still describe A while B is targeted. **Expected:** clear or explicitly preserve/revalidate draft by payment identity. **Fix:** reset per-payment inputs when identity changes, but avoid wiping values while editing the same payment; add A→B→A and different-student fixtures. Keep the existing final confirmation and server-side eligibility checks.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** Adjustment amount, reason and transaction date are now
owned by the (student, payment) identity. Changing identity clears those fields;
reselecting the same identity retains its in-progress values. UpdateActionMode
still derives locked full-reversal amounts and the existing final confirmation
and SQL eligibility checks remain in place.

**Verification:** App and Windows smoke project build without warnings/errors.
Added actual payment-grid fixtures for same-payment preservation, A→B→A resets,
date reset and a changed student context. No financial write is submitted in
these fixtures; native GUI execution remains required. No SQL changes.

---

### FQA-03 — Student Profile shortcuts accept selection ID 0 [P3, UX decision]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Students/Profile/StudentProfilePage.cs` L216–279](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Profile/StudentProfilePage.cs#L216-L279), [`Pages/Students/Profile/StudentProfilePage.cs` L281–321](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Profile/StudentProfilePage.cs#L281-L321).

Student-specific `JumpButton` click handlers pass `student.SelectedId` without requiring a valid selection; most buttons begin enabled. On a blank profile, they can open unrelated blank/default pages rather than warn and direct the operator to select a student. **Qualification:** “Open Admissions” might intentionally double as opening a blank new-admission form. Do **not** disable it automatically without deciding that product policy. Restrict only buttons with a genuinely required existing student, or clearly label generic navigation.

**Test:** No student; selected A; clear typed lookup; selected B; permission-limited role. Decide which shortcuts are generic versus record-bound.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**UX policy resolved and implemented.** Open Admissions remains a general entry
point for new admissions and the selected record. Photo, family, record-check and
annual-receipt shortcuts require a committed profile student: unavailable until
selection, reset when cleared, and guarded again in the click handler with inline
selection guidance. Existing target factories and role/SQL gates are unchanged.

**Verification:** App and Windows smoke project build without warnings/errors.
Added blank-profile refusal, selected-student navigation and cleared-identity
fixtures without opening destination forms or querying SQL. Native keyboard and
permission acceptance remains required. No SQL changes.

---

### FQA-04 — Reverse a Posting is not a shell-busy page [P2]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Finance/Billing/FeePostingReversalPage.cs` L1–52](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Billing/FeePostingReversalPage.cs#L1-L52), [`Pages/Finance/Billing/FeePostingReversalPage.cs` L285–363](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Billing/FeePostingReversalPage.cs#L285-L363), [`Pages/Finance/Billing/FeePostingReversalPage.cs` L363–429](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Billing/FeePostingReversalPage.cs#L363-L429), compare [`Pages/Finance/Billing/FeePostingPage.cs` L5–14](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Billing/FeePostingPage.cs#L5-L14).

**Exact behavior:** `ReverseAsync` submits `ReverseMonthlyFees`. The page inherits `V2Page` but does **not** implement `IBusyPage` or maintain a reversal transaction flag. The clicked reverse button is guarded against a repeated click, but the sidebar, preview action, filters and page lifetime are not managed as an active write.

**Reproduce:** Preview a posted month, enter a reason, confirm, delay the SQL response, then switch page or close. **Expected:** prevent leaving until summary and remaining-posting refresh are resolved, and prevent starting a competing preview/second reverse. **Fix:** separate `reversing` from ordinary read/preview loading; implement `IBusyPage`, hold scope-changing controls, preserve existing snapshot/filter-signature and “collected charges cannot reverse here” protections. On an unknown SQL outcome, insist on fresh reconciliation rather than blind retry.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** Reversal now runs through the established shared
RunBusyAsync gate while the whole page is held. IBusyPage remains true through
validation, confirmation, the SQL call and the post-reversal month/impact refresh.
Conflicting preview/filter/selection actions cannot run during the operation.
Collected-charge exclusions, scope validation and server result counts remain.

**Verification:** App and Windows smoke project build without warnings/errors.
Added actual-page shared-operation fixtures for busy visibility, conflicting
control lock and settled restoration. Native delayed SQL/navigation acceptance
remains outstanding. No SQL migration is needed.

---

### FQA-05 — Late Payment Details can open for a previous selection [P3, conditional]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs` L489–529](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs#L489-L529).

`ViewPaymentDetailsAsync` captures payment/student identifiers and awaits an SQL detail query, then shows a modal dialog if the page is **not disposed**. It does not re-check the current selected student/payment. The grid and student finder remain interactive during this read.

**Inject delay:** Select A, click View Details, select B while query awaits. A's details may appear over B. **Fix:** check captured IDs and a selection-generation token before rendering, or deliberately lock the relevant selection while the read is active. No database mutation is implicated.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Conditional source race verified and fixed.** Detail reads now use LatestReadScope
and compare captured student/payment/actor before showing data. Payment identity
changes, history reload and disposal invalidate those requests; A→B→A cannot
revive A's earlier response. A superseded error cannot overwrite the new context.

**Verification:** App and Windows smoke project build without warnings/errors.
Added delayed read fixtures for A→B→A rejection, valid current preview and disposal
using injected reads/preview callbacks. Existing portable latest-read tests passed
with FQA-16. Native Windows execution remains required. No SQL changes.

---

### FQA-06 — Financial adjustment with lost commit acknowledgment [P2 verification, **not confirmed defect**]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs` L350–488](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs#L350-L488); compare existing [collection recovery implementation](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Collection/CollectionRecoveryJournal.cs).

`ExecuteAsync` submits `ProcessFeeTransaction` and displays generic `Fail(ex)` if the call throws. A lost connection after SQL commit can make the outcome ambiguous; this C# flow has no durable request/outcome reconciliation like direct collections or Quick Registration. **Do not assert that the SQL procedure allows duplicate refunds** without inspecting and executing it.

**Required test:** Against a **disposable SQL copy**, inject a failure after server commit but before the client sees the reply; inspect both payment history and any SQL idempotency/reversal controls. Test same command retry, stale eligibility, receipt relation and restart. If unknown commits are possible, add an idempotent request identity and/or durable outcome lookup; display “outcome unknown, check transactions before repeating.” No automatic retry.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** Reproduced two partial refunds using identical ordinary
procedure parameters after discarding the first committed reply. Each adjustment
now persists a small, flushed local request identity before SQL, commits the
existing operation and its exact result marker together, and blocks further
adjustments while its outcome is uncertain. Check Save Outcome performs a locked,
read-only lookup; unresolved or damaged recovery is retained. Confirmed recovery
clears the old payment selection, shows the authoritative receipt and refreshes
history; confirmed absence requires deliberate reselection, with no automatic
write retry. Cross-database, changed-payload and unauthorized recovery are refused.

**SQL:** `Migrations/6.sql` atomically upgrades version 5 to 6 with request/result
foreign keys and JSON/type safeguards. Business procedures, role definitions and
existing school records are unchanged. App and installer require version 6;
startup verifies the additive schema. The shared protocol also supports bank
creation, whose page integration is the separate FQA-25 change.

**Verification:** 14 portable recovery/journal checks; 21 native SQL recovery
checks covering lost committed replies for Refund/Correction/Reversal, same-request
and concurrent replay, marker-failure rollback, locked unresolved requests, scope,
payload and permission rejection; four migration rollback/rerun/preservation
checks and startup validation; all 72 existing quick-registration SQL checks also
pass on schema 6. App and Windows smoke project build with zero warnings/errors. SQL ran only on a disposable loopback clone of the
reviewed v3 DACPAC plus migrations; the relevant procedure bodies in the uploaded
v4 DACPAC were compared and are identical. Windows page recovery fixtures compile;
actual Windows interaction and real transport loss remain acceptance work.
The native harness reports NU1900 because NuGet vulnerability metadata is currently
unreachable; this did not prevent compilation or SQL checks. The uploaded DACPAC's
full new baseline approval remains a separate review, not silently replaced here.

---

### FQA-07 — Daily Collection Closing can be left mid-write by accepting “discard” [P2]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Finance/Collection/DailyCollectionClosingPage.cs` L7–22](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Collection/DailyCollectionClosingPage.cs#L7-L22), [`Pages/Finance/Collection/DailyCollectionClosingPage.cs` L151–190](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Collection/DailyCollectionClosingPage.cs#L151-L190), [`MainDashboardForm.cs` L1637–1658](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/MainDashboardForm.cs#L1637-L1658).

**Exact behavior:** `DailyCollectionClosingPage` implements `IUnsavedChanges`, **not `IBusyPage`**, and returns `HasUnsavedChanges => dirty || busy`. While `SaveDailyCollectionClosing` or `ApproveDailyCollectionClosing` is awaited, `busy=true`. Dashboard's leave guard checks `IBusyPage` first, then lets the operator **confirm discarding** via `IUnsavedChanges`. The displayed text says “Wait …”, but clicking the affirmative discard/leave option can still allow the page to be retired.

**Reproduce:** Start Submit Closing or Approve on a test day with delayed DB response; click another sidebar module or close the app; accept the discard confirmation. The pending server call and local post-save reload outlive the workspace. **Expected:** unconditional no-leave during a submitted closing write.

**Fix:** Implement `IBusyPage` with `IsBusy => busy` and an explicit blocking message, while keeping `IUnsavedChanges` exclusively for **unsubmitted** values/notes. Protect Submit/Approve through complete outcome reconciliation, then allow leaving. Test SQL refusal, server commit with delayed reply, navigation, app close, and sign-out. Do not treat a submitted close as cancellable.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** DailyCollectionClosingPage now implements IBusyPage
using its existing operation flag, including submit, approval and their reloads.
IUnsavedChanges tracks only actual unsubmitted edits. Dashboard leave/close now
blocks the operation before it can offer a discard decision; draft-only notes
and amounts retain their normal discard prompt.

**Verification:** App and Windows smoke project build without warnings/errors.
Added actual-page close-contract fixtures for busy clean/dirty state, zero discard
prompts during the operation and normal settled draft behavior. Native delayed-
SQL, sign-out and navigation acceptance remains required. No SQL changes.

---

### FQA-08 — Family maintenance's asynchronous writes are not shell-protected [P2/P3]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Students/Profile/StudentFamilyPage.cs` L1–63](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Profile/StudentFamilyPage.cs#L1-L63), [`Pages/Students/Profile/StudentFamilyPage.cs` L176–199](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Profile/StudentFamilyPage.cs#L176-L199), [`Pages/Students/Profile/StudentFamilyPage.cs` L341–394](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Profile/StudentFamilyPage.cs#L341-L394).

**Exact behavior:** `StudentFamilyPage` is a plain `V2Page` with local `familyActionBusy`. It disables its own Add/Remove controls and picker using `BeginBusy`, but does **not** implement `IBusyPage`. `RemoveSelectedMemberAsync` directly awaits `Task.Run(DatabaseHelper.Execute("DeleteStudentSibling",...))`; after a successful add, the page may also await a full student/family reload. Shell navigation remains possible during these awaits.

**Reproduce:** Remove a family member with delayed server response, then navigate away/exit. **Expected:** operation completion and clear refresh/unknown-outcome feedback before the page is disposed. **Fix:** expose `familyActionBusy` via `IBusyPage`, while retaining the existing per-button/selection guards and SQL permission checks. Note that the **Add Member** flow uses a modal sibling-picker; its modal phase is already owner-blocking, so the direct Remove path is the clearest regression fixture.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** StudentFamilyPage now implements IBusyPage through its
existing RunFamilyActionAsync flag. Both add/remove actions and their family
reload remain protected by shell/modal leave guards. The existing action,
selection and Students.Administer/SQL guards are preserved.

**Verification:** App and Windows smoke project build without warnings/errors.
Added pending family-action, overlap refusal and full-completion fixtures using
delayed delegates, with lookup initialization disabled only in the test seam.
Native delayed SQL and shell leave acceptance remains required. No SQL changes.

---

### FQA-09 — Duplicate-pair Confirm/Dismiss actions can overlap [P2]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Students/Maintenance/DuplicateStudentAuditPage.cs` L10–33](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/DuplicateStudentAuditPage.cs#L10-L33), [`Pages/Students/Maintenance/DuplicateStudentAuditPage.cs` L112–146](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/DuplicateStudentAuditPage.cs#L112-L146), [`Pages/Students/Maintenance/DuplicateStudentAuditPage.cs` L269–332](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/DuplicateStudentAuditPage.cs#L269-L332), [`Core/V2Page.cs` L718–769](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Core/V2Page.cs#L718-L769).

**Exact behavior:** The page exposes two independent `AddBusyAction` buttons, **Confirm Duplicate** and **Dismiss**. The helper disables **only the clicked button**. The other remains available, and `Grid.SelectionChanged` calls `UpdateResolveButtons`, which may re-enable **both** without consulting a shared write-in-progress flag. `ResolveSelectedAsync` processes multiple `ReviewId` values in an `await` loop with no global operation gate.

**Reproduce:** Select one or several candidate pairs, click Confirm, approve the prompt, then click Dismiss (or change row selection) while a delayed server response holds the first batch open. **Expected:** once a batch starts, both decisions, Scan, and selection-sensitive commands remain unavailable until reconciliation. **Risk:** competing status writes, confusing last-writer outcome, overlapping reloads and inaccurate final messages; stored-procedure restrictions may reject one write, but this client conflict should not be permitted.

**Fix:** Track `resolving` (and scanning if needed); implement `IBusyPage` for writes; disable both decision buttons and the candidate grid throughout the batch; return if another decision is running; re-derive action states after reload. Preserve partial-failure-per-row accounting. Add an injected delay test with rapid Confirm→Dismiss and selection changes.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** Scan and resolution batches now share RunBusyAsync and
hold all conflicting page controls. Selection events cannot re-enable either
decision during a batch. Captured ReviewIds, individual SQL calls, partial-result
accounting and permission checks are retained; no batch auto-retry is added.

**Verification:** App and Windows smoke project build without warnings/errors.
Added selected-pair tests for both decision buttons remaining disabled during
selection changes, both handler paths refusing overlap, scan refusal and settled
restoration. Native SQL-delayed multi-row acceptance remains required. The host
leave-contract gap is tracked separately under FQA-10. No SQL changes.

---

### FQA-10 — Duplicate scan and multi-row resolution can outlive the page [P2/P3]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Students/Maintenance/DuplicateStudentAuditPage.cs` L1–41](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/DuplicateStudentAuditPage.cs#L1-L41), [`Pages/Students/Maintenance/DuplicateStudentAuditPage.cs` L242–332](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/DuplicateStudentAuditPage.cs#L242-L332), [`Pages/Students/Maintenance/StudentDataReviewPage.cs` L1–26](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentDataReviewPage.cs#L1-L26).

**Exact behavior:** `ScanAsync` invokes `ScanForDuplicateStudents`, which can insert new review candidates. `ResolveSelectedAsync` issues separate asynchronous `ResolveDuplicateStudentReview` calls for all selected rows. Neither child page nor its parent communicates an active-write `IBusyPage` contract to the dashboard. Clicking out of the workspace can leave a partial batch with the visible summary undisplayed.

**Fix / test:** Reuse the same batch-busy state from FQA-09 and delegate it through the Student Data Review host. Test two/ten selected pairs with SQL delay, one rejected row, navigation attempt, sign-out, and close. Preserve partial success details and reloaded Open candidates; **do not** automatically replay an entire batch.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** DuplicateStudentAuditPage advertises its existing shared
operation gate through IBusyPage. StudentDataReviewPage forwards active state
and its message from every loaded tab and refuses tab changes while any child
is busy. The dashboard therefore blocks departure through full scan/resolution
and refresh, including a cached child that is not currently selected.

**Verification:** App and Windows smoke project build without warnings/errors.
Added real-host fixtures using inert cached children for hidden-child visibility,
tab refusal and settled navigation; extended the actual duplicate-page fixture
with its busy contract. Native delayed SQL/navigation execution remains required.
No SQL changes.

---

### FQA-11 — Student Record Checks correction writes lack a composite busy contract [P2]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Students/Maintenance/StudentDataReviewPage.cs` L1–26](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentDataReviewPage.cs#L1-L26), [`Pages/Students/Maintenance/StudentRecordChecksPage.cs` L1–9](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentRecordChecksPage.cs#L1-L9), [`Pages/Students/Maintenance/StudentRecordChecksPage.cs` L130–146](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentRecordChecksPage.cs#L130-L146), [`Pages/Students/Maintenance/StudentRecordChecksPage.cs` L1283–1363](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentRecordChecksPage.cs#L1283-L1363), [`Pages/Students/Maintenance/StudentRecordChecksPage.cs` L1364–1434](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentRecordChecksPage.cs#L1364-L1434).

**Exact behavior:** `StudentRecordChecksPage` performs `Save Status`, `Save Placement`, `Create Missing Record` and date repairs; some operations await preflight reads and later invoke corrective SQL. The child is a plain `V2Page`. Its parent `StudentDataReviewPage` is a plain `UserControl`; neither forwards a write-busy contract to the dashboard. Per-button `AddBusyAction` is **not** a shared transaction guard and does not prevent switching tabs/closing the whole workspace.

**Reproduce:** In Record Issues / History, begin a correction with delayed SQL preflight/commit; change to Possible Duplicates, navigate elsewhere, or close the app. **Fix:** guard correction lifecycle within the child, expose a parent `IBusyPage` that delegates to all relevant cached tabs (not just selected tab), and disable conflicting actions until full outcome established. Review the synchronous `DatabaseHelper.Query` calls separately; they can freeze the UI and are **not** evidence that async navigation is safe. Test stale A→B student/placement changes and delayed writes.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** Status save, placement save, missing-record creation and
date review now enter one shared correction gate and hold the whole page through
preflight, SQL and post-save reads. Beginning a correction invalidates older mode
reads. IBusyPage forwards this state through the FQA-10 review host, preventing
conflicting corrections and workspace departure. Existing correction rules and
permission checks remain; synchronous SQL performance is a separate concern.

**Verification:** App and Windows smoke project build without warnings/errors.
Added delayed actual-page correction tests for composite-host busy visibility,
whole-page control hold, overlapping correction refusal and settled restoration.
Native SQL and GUI execution remains required. No SQL changes.

---

### FQA-12 — Annual receipt printing can use a superseded student/year [P3, conditional]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Reports/Finance/AnnualFeeReceiptsPage.cs` L136–226](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Reports/Finance/AnnualFeeReceiptsPage.cs#L136-L226).

`ViewLedgerAsync` captures student/year, then awaits annual-receipts data, school identity, many receipt-line queries, and current balances before opening the preview. The student and year selectors remain available; there is no latest-selection check before `RdlcReportPreview.Show`. A late preview is a correctly queried **old** student/year, but can surprise the clerk who has already selected a new record. There is no proof the receipt contents themselves are mismatched.

**Test:** Start A/year-1 preview with a delay, switch to B/year-2. **Fix:** freeze the selection for the duration, or supersede/cancel stale reads before preview. Maintain the captured IDs when generating all data.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Conditional source race verified and fixed.** Annual receipt preparation now
uses the shared operation gate, holds the page's student/year controls and
advertises IBusyPage through the full preview lifecycle. After receipt, identity,
receipt-line and balance awaits it checks captured student/year and disposal
before continuing or opening a preview. Receipt contents still use captured IDs.

**Verification:** App and Windows smoke project build without warnings/errors.
Added actual-page preparation fixtures for held student/year controls, owner busy
visibility and settled restoration. Native delayed SQL/RDLC preview acceptance
remains required. No SQL changes.

---

### FQA-13 — Payment adjustment may commit, then its failed refresh hides success [P2]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs` L231–286](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs#L231-L286), [`Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs` L448–486](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs#L448-L486).

**Exact behavior:** `ExecuteAsync` awaits `ProcessFeeTransaction` and constructs a success message from the returned data. It then clears editor state and does `await LoadPaymentsAsync(completionMessage, admissionId)`. `LoadPaymentsAsync` **catches its own database errors**, calls `Fail(ex)`, and returns without surfacing a failed reload result. The already built “corrected/refunded/reversed” success message is shown **only** after successful reload.

**Reproduce:** Allow adjustment submission to succeed, then fail the next payment-history SQL read. **Observed-from-code outcome:** ordinary load failure feedback without a distinct “the adjustment succeeded but the list could not refresh” state.

**Fix:** Return a typed load outcome or explicit `bool` from history refresh. Once server acknowledgment is obtained, display “Adjustment recorded; payment list could not refresh” on read failure and disable subsequent adjustment until explicit reload. Do not automatically resubmit. Acceptance: committed result + subsequent failed read; failed write; stale student switching; and manual reconciliation after restart.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** The payment-history loader now distinguishes a normal
read failure from a read after a confirmed adjustment response. The latter keeps
the exact completion/receipt message, explains that the adjustment was recorded
and only history failed to refresh, and directs reload instead of repeating the
financial command. Existing selection clearing and SQL behavior are retained.

**Verification:** Traced the confirmed-write → completion message → load failure
branch and current-student/load-version guards; app and Windows smoke project
build without warnings/errors. Native successful-write/failed-read injection is
still required. This does not resolve FQA-06's separate lost-write-response risk.
No SQL changes.

---

### FQA-14 — Unsaved Student Record Checks correction drafts are invisible to dashboard leave guard [P2/P3]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Students/Maintenance/StudentDataReviewPage.cs` L1–26](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentDataReviewPage.cs#L1-L26), [`Pages/Students/Maintenance/StudentRecordChecksPage.cs` L1–50](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentRecordChecksPage.cs#L1-L50), [`Pages/Students/Maintenance/StudentRecordChecksPage.cs` L1195–1221](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentRecordChecksPage.cs#L1195-L1221), [`Pages/Students/Maintenance/StudentRecordChecksPage.cs` L1283–1318](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentRecordChecksPage.cs#L1283-L1318).

**Exact behavior:** The correction console contains mutable status, withdrawal/placement/date fields and reason text, but it does **not** implement `IUnsavedChanges`, and its parent dashboard workspace does not either. The operator can prepare a correction and leave the entire workspace without the general unsaved draft prompt.

**Reproduce:** Select a student, change status/withdrawal date or placement reason, do not save, then navigate from the Student Data Review workspace. **Expected:** a normal Save/Discard/Stay decision. **Fix:** record a clean baseline after each successful load; track correction-specific dirty fields, including create-missing state; forward aggregate `IUnsavedChanges` from the parent across instantiated tabs. Do not show false prompts for programmatic binding or an untouched form. Separate FQA-11's **active write** guard from this **unsaved draft** guard.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** StudentRecordChecksPage implements IUnsavedChanges using
separate loaded snapshots for status and placement/date/reason fields, including
missing-record intent. Programmatic successful loads establish clean baselines;
default fields without a selected student are not drafts. User student changes
and explicit Clear check discard before replacing the draft. StudentDataReviewPage
aggregates dirty state from all cached tabs for shell navigation/close.

**Verification:** App and Windows smoke project build without warnings/errors.
Added loaded-clean, changed reason, billing-date, missing-record and hidden-tab
aggregation fixtures. Native GUI/discard-dialog acceptance remains required.
No SQL changes.

---

### FQA-15 — Admission's shared busy operation is invisible to the shell [P2]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Students/Admissions/StudentAdmissionPage.cs` L228–236](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Admissions/StudentAdmissionPage.cs#L228-L236), [`Pages/Students/Admissions/StudentAdmissionPage.cs` L3749–3788](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Admissions/StudentAdmissionPage.cs#L3749-L3788), [`Pages/Students/Admissions/StudentAdmissionPage.cs` L906–921](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Admissions/StudentAdmissionPage.cs#L906-L921), [`Core/V2Page.cs` L786–804](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Core/V2Page.cs#L786-L804), [`MainDashboardForm.cs` L1637–1655](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/MainDashboardForm.cs#L1637-L1655).

**Exact code behavior:** `StudentAdmissionPage.IsBusy` returns only `saveBusy || photoBusy`. However, the page performs **Confirm Admission** (`CompleteQuickAdmission`), **Confirm Placement**, **Verify Legacy**, and report preparation inside `V2Page.RunBusyAsync`. That helper tracks `_busyOperationRunning` privately; it does not set either admission-specific flag and is not included in `IsBusy`. `RunBusyAsync` blocks a second call to itself, **not navigation/window closure**.

**Reproduce:** On normal Admission, initiate Confirm Placement or legacy verification with intentionally delayed SQL; attempt sidebar navigation. On Review / Confirm, submit Confirm Admission and attempt dialog X/Escape while the SQL response is pending. **Expected:** both leave attempts refused until the exact result is known. **Current source contract:** busy state is not advertised to host. No claim that server write is duplicated; the risk is losing the operator result / UI continuation.

**Proposed fix:** Expose a protected read-only shared `RunBusyAsync` operation state from `V2Page` (or fold these calls into the existing page-level activity gate) and include it in `StudentAdmissionPage.IsBusy` and its busy message. Keep `saveBusy` / `photoBusy` behavior intact. Add tests for all three write operations **and both print commands** while in flight; verify no premature close or navigation. **Do not fix only FQA-01**, because a new dialog close guard querying old `IsBusy` would still miss this state.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** V2Page now exposes its existing RunBusyAsync state and
operation message to derived pages. StudentAdmissionPage includes that state in
IBusyPage along with its existing admission-save and photo flags. Placement/
admission confirmation, legacy verification and both print preparations now
remain visible to shell/modal guards until their shared operation settles.
The shared double-submission gate and exception propagation are retained.

**Verification:** App and Windows smoke project build without warnings/errors.
Added pending-operation tests for the five operation messages, duplicate refusal,
success and server refusal releasing the guard. Tests use delayed delegates,
not SQL writes; native Windows execution remains required. No SQL changes.

---

### FQA-16 — Additional Fee Posting accepts an obsolete student-list response [P2, confirmed source gap]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Finance/Billing/AdditionalFeePostingPage.cs` lines 19–25](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Billing/AdditionalFeePostingPage.cs#L19-L25), [`Pages/Finance/Billing/AdditionalFeePostingPage.cs` lines 70–85](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Billing/AdditionalFeePostingPage.cs#L70-L85), [`Pages/Finance/Billing/AdditionalFeePostingPage.cs` lines 103–142](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Billing/AdditionalFeePostingPage.cs#L103-L142), [`Pages/Finance/Billing/AdditionalFeePostingPage.cs` lines 146–181](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Billing/AdditionalFeePostingPage.cs#L146-L181), [`Core/V2Page.cs` lines 718–769](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Core/V2Page.cs#L718-L769).

**Operator sequence:** Choose Campus A, Class X and a year → click **Load Students** → while SQL is delayed, change Campus to B → the filter-change event clears the grid/selection → the earlier A query resolves and unconditionally assigns `candidates` and `students.DataSource`. The page now shows students from **A** while Campus reads **B**. Tick or **Select All**, supply a fee/amount/reason, and click **Post Selected**.

**Cross-checks before confirming:** `ClearCandidates()` clears the current dataset and selection but does **not** invalidate a pending load generation. `LoadAsync` captures the lookup IDs as query arguments before `await`, but after the await applies the response without comparing them to the live lookups. There is no version/generation check or scope check in `PostAsync`; it sends the selected enrollment IDs in a table-valued parameter. `AddBusyAction("Load Students")` disables its clicked button, **not** the scope controls. The final confirmation displays fee, count, date, amount and total, **but neither the campus nor the specific student names**. This is a proven **client-side stale-context acceptance path**, not proof of incorrect SQL charges; `PostAdditionalFee` server rules must also be tested.

**Expected:** A response for Campus A must never bind under Campus B. Post must not accept student selections from an obsolete scope.

**Recommended fix (C#):**
1. Capture a full candidate-query signature (year, campus, class, section) plus incrementing load generation **before** the await. Invalidate generation whenever any of those filters changes or the page is disposed; apply only a still-current result.
2. Either hold scope filters for the entire load or allow changes and discard stale responses, with an intelligible “Filters changed; load again” status.
3. Record the exact loaded scope alongside `candidates`; recheck it in `PostAsync` immediately before confirmation/submission, and check all selected enrollment IDs belong to the currently displayed candidate set.
4. Make the final posting confirmation name the chosen scope and ideally a short summary of student identifiers with a way to inspect the full list. Keep the existing request-ID/idempotency and `IBusyPage` protections.

**Windows/SQL acceptance:** Slow A query → change to B; clear class; reload B; cancel load; double-click Load; select all/untick; verify UI filters, displayed rows, selected IDs, confirmation and SQL TVP match the same scope. SQL assertions should use an isolated test database. **Never test posting to real students.**

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** A delayed Campus A response could bind after the operator
selected Campus B. Candidate loads now use LatestReadScope and a full year/campus/
class/section signature. Filter changes invalidate pending results and selections;
disposal rejects late replies. Posting checks the loaded signature and membership
of every selected enrollment, snapshots those IDs, and names the scope and a
student summary in its confirmation. Post/Select All require a current loaded list.
Existing SQL permissions, request identity and transaction behavior are retained.

**Verification:** Windows application and smoke project compile without warnings
or errors; added delayed A→B, current-result selection, scope clearing and disposed-
response GUI fixtures. Existing portable latest-read/cancellation regressions run
in PerformanceChecks. The Windows GUI fixtures require native Windows execution.
No SQL migration is needed.

---

### FQA-17 — Fee Rate Matrix campus switch can silently lose unsaved edits [P2, confirmed source gap]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Finance/Configuration/FeeRateMatrixForm.cs` lines 169–197](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Configuration/FeeRateMatrixForm.cs#L169-L197), [`Pages/Finance/Configuration/FeeRateMatrixForm.cs` lines 220–246](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Configuration/FeeRateMatrixForm.cs#L220-L246), [`Pages/Finance/Configuration/FeeRateMatrixForm.cs` lines 426–439](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Configuration/FeeRateMatrixForm.cs#L426-L439), [`Pages/Finance/Configuration/FeeRateMatrixForm.cs` lines 639–662](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Configuration/FeeRateMatrixForm.cs#L639-L662).

**Operator sequence:** Select a campus → edit a class/fee cell, without **Save Grid** → change Campus → observe the editor reset/rebuilt from another campus. There is no confirm/discard/stay decision attached to the **Campus.SelectedIndexChanged** event.

**Cross-checks:** This form already has `HasUnsavedChanges => IsDirty()` and a `FormClosing` discard prompt; its `OnConfigurationsChanged` handler expressly refuses to reload while dirty. However, the Campus handler immediately calls `LoadGridAsync`. That method **first clears the grid and sets `loadedSnapshot = ""` and clears stored cell values**, then issues SQL queries. It does not check `IsDirty` or ask to discard before wiping the draft. Even a failed new-campus query can leave the original edits unavailable.

**Expected:** Switching campus with edited fee values requires a Save / Discard / Stay decision, not silent loss. The old campus should remain selected if the user chooses Stay.

**Proposed fix:** Reuse `IsDirty` / the existing modal confirmation concept for an *internal campus-selection change*, preserving prior ID and avoiding selection-change recursion during rollback. Optionally allow an explicit Save Grid followed by campus switch only once the save is confirmed; don't auto-submit financial edits. Do not disturb the clean-campus fast path and existing grid-version latest-load checks.

**Tests:** Edit amount/frequency/remarks → switch campus → Stay, Discard and Save decisions; cancel picker; empty campus; failed destination load; rapid campus A→B→A; exact row focus and fee values. Include embedded-tab mode and standalone dialog.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** Internal campus changes now finish validation of the
edited cell and check the actual grid snapshot. Changed grids require an explicit
discard; declining restores the previous campus without loading or touching the
edited cells. Clean switches remain immediate. Programmatic changes during a
submitted save also restore the original campus. This decision does not reuse
or consume the independent form-close discard confirmation.

**Verification:** App and Windows smoke project build without warnings/errors.
Added fixtures for Stay retaining campus/amount, deliberate discard, clean switch
and a campus change during save. Native GUI execution remains outstanding.
No SQL migration is needed.

---

### FQA-18 — Embedded Fee Rate Matrix does not advertise Save Grid as page-busy [P2, confirmed source gap]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Finance/Configuration/FeeRateMatrixForm.cs` lines 1–9](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Configuration/FeeRateMatrixForm.cs#L1-L9), [`Pages/Finance/Configuration/FeeRateMatrixForm.cs` lines 187–232](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Configuration/FeeRateMatrixForm.cs#L187-L232), [`Pages/Finance/Configuration/FeeRateMatrixForm.cs` lines 150–172](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Configuration/FeeRateMatrixForm.cs#L150-L172), [`Pages/Finance/Configuration/FeeConfigurationWorkspace.cs` lines 1–14](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Configuration/FeeConfigurationWorkspace.cs#L1-L14), [`WorkspacePage.cs`](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Core/WorkspacePage.cs), [`MainDashboardForm.cs` lines 1637–1655](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/MainDashboardForm.cs#L1637-L1655).

**Operator sequence:** Open **Fee Configuration → Fee Rate Matrix** (embedded as a tab) → edit one or more fee cells → click **Save Grid** → while SQL write is delayed, switch to **Fee Types** / **Copy Fees**, or navigate elsewhere/exit. The form holds `savingGrid` and disables its own campus selector/grid, but does **not** implement `IBusyPage`. Its existing `FormClosing` protection applies to the independent Form close event and **does not supply an `IBusyPage` interface to the host when embedded**.

**Cross-checks:** The parent `WorkspacePage` **already** implements `IBusyPage` and `IUnsavedChanges`, scans *all loaded tabs*, and cancels tab switching on a busy child. Thus the parent infrastructure is **not** missing. The exact gap is the embedded `FeeRateMatrixForm`, which only implements `IUnsavedChanges`: while saving, the parent cannot detect that write via `BusyChild`. A dirty-change confirmation is not a substitute for an unconditional write block.

**Fix:** Implement `IBusyPage` on `FeeRateMatrixForm` mapping `IsBusy => savingGrid` and a descriptive `BusyMessage`; preserve the existing unsaved/dirty contract. Ensure it covers the entire async save **including success/reload**, and the parent tab+dashboard automatically sees it. Test modal X/Escape, embedded tab switch, sidebar, sign-out and closing while the SQL server holds the save. Keep read-only tabs navigable when no write is running.

**Important distinction:** FQA-17 concerns a **draft before save** and an internal campus switch. FQA-18 concerns a **submitted database write in flight** and leaving an embedded tab/workspace. They require different responses.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** FeeRateMatrixForm's existing savingGrid state now
implements IBusyPage for the entire save and post-save reload. WorkspacePage
already scans all loaded children and blocks switching tabs when one is busy;
the dashboard can now see this save through that established contract.

**Verification:** App and Windows smoke project build without warnings/errors.
Added an embedded-host fixture covering active-save tab refusal, navigation after
completion and a busy child in a non-selected tab. Native Windows execution of
these GUI fixtures remains required. No SQL or permission changes.

---

### FQA-19 — Failed Student Account Statement regeneration leaves an old Export enabled [P2, confirmed source gap]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Reports/Finance/StudentAccountStatementPage.cs` lines 665–691](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Reports/Finance/StudentAccountStatementPage.cs#L665-L691), [`Pages/Reports/Finance/StudentAccountStatementPage.cs` lines 1230–1315](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Reports/Finance/StudentAccountStatementPage.cs#L1230-L1315), [`Pages/Reports/Finance/StudentAccountStatementPage.cs` lines 169–194](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Reports/Finance/StudentAccountStatementPage.cs#L169-L194).

**Operator sequence:** Select one or more students/dates → Generate Statement successfully → some new payment or charge is recorded elsewhere → return to the **same filters and selection** → click **Generate Statement** again → inject a SQL failure while fetching new data. The page catches the error through `Fail(ex)`. **But** it does not clear or mark the previous bound statement as stale and does not clear `_appliedFilterSignature` or disable `exportButton` at the start/failure of regeneration.

**Cross-checks:** `OnFiltersChanged` correctly disables Export when the *controls change*. That does **not** protect this scenario because the controls have not changed: the old signature still matches. `LoadStatement()` builds its new table off-screen; when a query fails before `Bind(combined)`, the old grid remains visible. Therefore Export may still operate on the old, potentially outdated report, even though the latest generate attempt failed. This is not proof the old amounts were wrong *when originally generated*.

**Expected:** If a fresh Generate Statement attempt fails, the UI must not silently leave the old result exportable as a currently refreshed statement.

**Fix:** On a new Generate attempt, mark the existing result **not current** before database calls and disable Export. On failure, either clear it or retain it explicitly labeled **previous snapshot / not refreshed** with Export disabled until a successful regeneration. Distinguish “latest query failed” from “no data” in status. Restore `_appliedFilterSignature` and actions only after all selected students' results, totals and grid binding succeed. Preserve user selection and date inputs after a failure.

**Tests:** Generate A → insert payment in disposable SQL → fail subsequent Generate A → Export disabled and stale warning; recover and Generate → new totals/export match; change dates mid-flow; fail on second of several student queries; zero transaction rows; missing opening-balance row.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** Starting Generate Statement now clears the applied
signature and disables Export before validation or SQL. Only complete successful
binding/totals restore export eligibility. A failure explicitly labels the visible
snapshot as not refreshed. The previous table can remain for reference, with
student selection and dates retained for another generation attempt.

**Verification:** App and Windows smoke project build without warnings/errors.
Added an actual-page rejected-generation fixture asserting old export/signature
invalidation and previous-table retention without reaching SQL. Native SQL failure
and GUI status acceptance remains required. No SQL changes.

---

### FQA-20 — Statement generation serially blocks the WinForms UI during SQL reads [P3, performance-verification item]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [`Pages/Reports/Finance/StudentAccountStatementPage.cs` lines 176–190](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Reports/Finance/StudentAccountStatementPage.cs#L176-L190), [`Pages/Reports/Finance/StudentAccountStatementPage.cs` lines 1227–1312](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Reports/Finance/StudentAccountStatementPage.cs#L1227-L1312).

**Source design:** The **Generate Statement** button synchronously invokes `LoadStatement()` on the UI thread. Inside it, `foreach (int admissionId in admissionIds)` repeatedly calls synchronous `LoadStatementData` → `DatabaseHelper.QueryUserSet("GetFeeStatementTransactions")` before building the report. With a multi-student/family selection this is **one blocking SQL round trip per selected student** and the window cannot process normal paint/cancel/input events during those waits. This is a source-confirmed synchronous execution path, **not** a measured hang/crash on the user's PC.

**Impact to verify:** A single student on local SQL may feel normal; 50–500 selected students on a slow Windows computer or delayed SQL instance could make the application appear frozen. A failed query later in the loop compounds FQA-19's feedback problem.

**Proposed approach:** Benchmark first (1, 10, 50, 200 students; warm/cold DB). Move the existing verified result assembly off the UI thread with a cancellable/latest-load coordinator or, preferably, a bulk SQL retrieval interface if it can preserve financial semantics and balances. Capture immutable selected IDs/dates on the UI thread; never read WinForms controls inside background tasks. Bind all results atomically and preserve opening-balance verification and report sums. Do not schedule simultaneous unbounded queries or assume parallel SQL reads improve performance. Report a loading state and safe Cancel/Leave behavior while reads run, distinct from `IBusyPage` financial writes.

**Acceptance:** Window remains responsive at 125–150% DPI under injected SQL delay; only the latest requested result binds, totals match the synchronous version exactly for all sample students, error and no-data states are clear, Export disabled while result is stale, and no further SQL activity continues after page disposal/cancellation where supported.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Synchronous source design confirmed and improved.** Statement generation now
awaits each captured student's read and assembles/sorts tables on a worker. Reads
remain sequential, avoiding a burst of SQL connections and simultaneous tables.
Selectors are held by the existing busy gate; filter changes/disposal invalidate
results, and only a complete current batch can bind and enable Export. Names are
captured from visible rows or resolved asynchronously. Existing transaction
filtering, distinct charge totals, payments and opening balance are retained.

**Verification:** 7 portable batch tests pass for immediate caller return, ordered
assembly, cancellation, failed-read stopping and one-at-a-time reads. Synthetic
profile: 32 delayed 8-ms queries finished in 266 ms with 62 caller heartbeats and
maximum 1 active read. This excludes real SQL and WinForms and is not a deployment
speed/RAM claim. Existing performance suites also pass (20,028 + 2,040 + 10,108).
App and Windows smoke project build without warnings/errors; added delayed
multi-student assembly/identical-total/export fixtures. Real SQL latency, Windows
responsiveness and resource measurements remain outstanding. No SQL changes.

---

### FQA-21 — Certificate editors do not protect unsaved new-certificate drafts [P2/P3, confirmed source guard gap]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [Pages/Students/Certificates/CharacterCertificatePage.cs L1-103](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Certificates/CharacterCertificatePage.cs#L1-L103), [Pages/Students/Certificates/CharacterCertificatePage.cs L173-205](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Certificates/CharacterCertificatePage.cs#L173-L205), [Pages/Students/Certificates/CharacterCertificatePage.cs L1070-1094](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Certificates/CharacterCertificatePage.cs#L1070-L1094), [Pages/Students/Certificates/LeaveCertificatePage.cs L1-100](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Certificates/LeaveCertificatePage.cs#L1-L100), [Pages/Students/Certificates/LeaveCertificatePage.cs L1294-1345](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Certificates/LeaveCertificatePage.cs#L1294-L1345), [Pages/Students/Certificates/CollegeLeaveCertificatePage.cs L1-103](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Certificates/CollegeLeaveCertificatePage.cs#L1-L103). **Working comparison:** [Pages/Students/Certificates/RegularStudentCertificatePage.cs L1-18](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L1-L18).

**Operator reproduction:** In Character Certificate, select a student, enter BISE roll number/remarks/date without clicking Create, and either select another student, use Next Student, or navigate to another dashboard page. Similar sequences exist for Leave and College Leave with date, promotion/academic, attendance, and remarks. The handlers clear or replace the inputs without a shared `IUnsavedChanges` contract, and the pages inherit plain `V2Page`. In Character, `StudentChanged` clears certificate fields immediately. This is loss of **unsaved user input**, not loss of a committed certificate.

**Caveat:** These forms sometimes intentionally reset fields on a new student. The missing behavior is a decision prompt *when the user has actually edited a draft*. A clean form must still switch directly.

**Recommended fix:** Capture a baseline after loading/clearing a student, compare the true editable fields, implement `IUnsavedChanges`, and attach `StudentPicker.SelectionChangeGuard` plus explicit Next Student / refresh confirmation. Take care to distinguish fields deliberately not persisted (character-certificate exam-type print wording) and existing saved issuance from a new unsaved draft. Add change A→B→cancel, typed-search clear, Next Student, X/navigation, and after-success baseline reset tests.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** Character, School Leave and College Leave editors now
expose loaded-baseline dirty tracking for new drafts. User student changes,
Next Student, Refresh and switching to an issuance check discard first; declining
an issuance switch clears its misleading highlight without changing the draft.
The certificate hub's modal route attaches the common page-close guard. Existing
read-only issuances/reprint wording are not treated as new persisted drafts;
exam-type storage and optional leaving-clearance business rules are unchanged.

**Verification:** App and Windows smoke project build without warnings/errors.
Added all-three-editor fixtures for clean loaded defaults, edited wording, modal
Stay, existing-issuance exclusion and unbound defaults. Native student-picker,
refresh and real create/print acceptance remains required. No SQL changes.

---

### FQA-22 — Bank Account Opening Certificate create does not block page departure [P2, confirmed source gap]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L1-13](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L1-L13), [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L68-112](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L68-L112), [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L444-517](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L444-L517).

**Operator reproduction:** Start Create Certificate with a delayed `CreateBankAccountOpeningCertificate` database operation, then switch to another page or trigger New / Clear / Refresh while Create awaits. The clicked Create button and the Staff picker/Grid are disabled temporarily, but the page implements only `IUnsavedChanges`, **not `IBusyPage`; other buttons are not comprehensively locked**. The dashboard's whole-page busy guard cannot see this submitted write, and asynchronous continuation may run after the page is disposed.

**Recommended fix:** Track a page-level `creating` flag; implement `IBusyPage` with clear busy text; guard all context-dependent actions and leave gestures, and preserve the existing staff/grid button restoration. Capture submitted IDs and values before await; don't treat a submitted write as cancellable. Regression: delayed Create → sidebar, sign-out, window X, New and Refresh; verify exactly one SQL submission and no continuation on a disposed control.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** Create now uses the shared RunBusyAsync gate and holds
the whole page, so New/Clear/Refresh, editor fields, staff and history cannot
conflict with the submitted create. IBusyPage advertises the complete operation
through SQL and history reload; the existing staff/row restoration is preserved.

**Verification:** App and Windows smoke project build without warnings/errors.
Added an actual-page delayed operation fixture for owner busy visibility,
conflicting control hold and settled restoration. Native delayed Create,
close/sign-out and return-ID acceptance remains required. No SQL changes.

---

### FQA-23 — Bank certificate save feedback may conceal failed history reload [P2/P3, confirmed conditional feedback gap]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L257-308](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L257-L308), [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L444-530](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L444-L530).

**Sequence:** The Create stored procedure returns successfully. The handler sets `successMessage` and then calls `LoadCertificates(staffId)`. That loader catches its own SQL exceptions, clears its grid and calls `Fail(ex)` without returning a load-success value. The enclosing Create method still runs its `finally` and displays `Info(successMessage)`. The operator sees a success message over a failed/unrefreshed certificate list, with no explicit distinction between **committed write** and **failed refresh**.

**Recommended fix:** Return an explicit loaded/failed result from `LoadCertificates`; show “Certificate created, but history could not be refreshed; reload before acting further.” For the UI, don't infer an ID or issue number from empty history and don't retry Create just because its *read* failed. Acceptance: successful Create + failed Get Records, successful Create + empty history, failed Create, delayed staff switch.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** The bank history loader now returns an explicit result.
Create/Update preserve confirmed save feedback separately from history reads;
a failed or empty post-save history produces a qualified warning directing reload
and no repeat save. Only verified nonempty history is used for saved-row selection.
Post-save read errors are logged without a second generic failure popup masking
the confirmed outcome. Ordinary manual-read failures retain their error handling.

**Verification:** Traced both successful-write/failed-read branches and empty
history handling; app and Windows smoke project build without warnings/errors.
Native injected SQL history failure remains required. FQA-25's lost Create reply
is a separate verification item. No SQL changes.

---

### FQA-24 — Confirm Return Admission opening-fee edits have no modal discard protection [P2/P3, confirmed source guard gap]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [Pages/Students/Enrollment/ConfirmReturnAdmissionPage.cs L1-64](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Enrollment/ConfirmReturnAdmissionPage.cs#L1-L64), [Pages/Students/Enrollment/ConfirmReturnAdmissionPage.cs L70-115](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Enrollment/ConfirmReturnAdmissionPage.cs#L70-L115), [Pages/Students/Enrollment/ConfirmReturnAdmissionPage.cs L188-214](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Enrollment/ConfirmReturnAdmissionPage.cs#L188-L214), [Pages/Students/Enrollment/ConfirmReturnAdmissionDialog.cs L1-26](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Enrollment/ConfirmReturnAdmissionDialog.cs#L1-L26).

**Operator reproduction:** Open the current compact **Confirm Return** dialog for a promoted student, change the editable Amount or Discount Amount cells, then press Cancel/Escape or X. The modal `FormClosing` only refuses to close while `workflow.IsConfirmingReturn`. There is **no unsaved fee-draft guard** or confirmation. The fee adjustments are local until Confirm, so this does not roll back a database write, but an operator can lose substantial input without warning.

**Cross-check:** The compact dialog **disables its student picker**, so a claimed switch-student failure inside this dialog is **not** a currently reachable finding. Its active SQL confirmation is already blocked on close. This finding is only about the editable *pre-submission draft*.

**Recommended fix:** Snapshot the original fee choices and return date/transport state after load, detect changed values, and prompt on Cancel/X only when dirty. Clear the baseline following successful Confirm. Test edited amount, edited discount, transport/date changes, clean Cancel, and active Confirm (must remain unconditionally blocked).

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** Return admission tracks a clean loaded snapshot of fee
amounts/discounts, return date and transport, including an active uncommitted cell.
The modal uses ModalPageCloseGuard: clean Cancel/X remains prompt-free, edits
require discard, and confirmation remains an unconditional busy close block.
A confirmed successful return clears draft intent before its completion close.

**Verification:** App and Windows smoke project build without warnings/errors.
Added actual-page snapshots for loaded-clean, amount, discount, transport/date,
active confirmation and committed draft clearing. Common modal decision fixtures
cover Stay/discard and busy closure. Native Windows acceptance remains required.
No SQL changes.

---

### FQA-25 — Lost Create acknowledgment for bank certificates requires SQL reconciliation testing [P2 verification; not a proved duplicate bug]

**Original operator scenario and source evidence (historical baseline `3a3fbd6`):**

**Evidence:** [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L68-105](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L68-L105), [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L444-518](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L444-L518).

The Create path submits one asynchronous `CreateBankAccountOpeningCertificate` call, and on any exception shows a general error. There is no durable client-generated request identity or unknown-outcome resolution on this path. **If** a SQL commit succeeds but its response is lost, the operator may press Create again. A source comment suggests this procedure has no duplicate guard, but **this review did not independently inspect its stored-procedure body or test its DB constraints**, so a duplicate-certificate conclusion remains **unconfirmed**.

**Verification:** In a disposable SQL Server, deliberately drop the reply after commit, inspect created certificate IDs, retry, and inspect uniqueness constraints. If the server is not idempotent, design a request-token contract with safe outcome lookup. Do not insert a new SQL migration without explicitly reviewing existing schema and upgrade policy. On unknown outcomes, first prompt to check existing certificates, rather than blind retry.

**Fix disposition and reported validation (commits through `6e4fca3`):**

**Confirmed and fixed.** Disposable SQL reproduced two certificates after repeating
identical Create parameters with the first committed reply discarded. The bank
page now uses migration 6's exact request protocol: durable local intent before
SQL, atomic business row/result marker, retained unknown outcome and an explicit
read-only Check Save Outcome. Create/Update and context-changing actions are
blocked while unresolved, including direct handler calls. Leaving explains that
recovery is retained. Corrupt/foreign recovery is preserved rather than discarded.

Normal save and positive recovery select the returned certificate ID, never the
newest row guessed from history. Confirmed absence clears the old draft and requires
a deliberate new create; no automatic write retry. A committed result remains
qualified success when history cannot be verified. Optional income and existing
certificate permissions are preserved. Recovery control sits beside history
Refresh; the existing four editor action buttons remain together.

**Verification:** 12 native SQL checks passed for actual commit/lost acknowledgement,
exact ID reconciliation, same-request and concurrent replay, changed payload,
marker-failure atomic rollback, optional income and permission refusal. All writes
used the disposable clone described under FQA-06, never school files. Application
and Windows smoke project compile with zero warnings/errors. Actual-page pending
recovery and exact-ID selection fixtures were added, but require native Windows
execution. The native harness retains the NU1900 metadata warning described above.
No additional migration beyond `Migrations/6.sql` is required.

---

## F. Still-open deployment tests and decisions

| Track | Remaining acceptance (do not mark fixed without evidence) |
|---|---|
| **Active RFA** | Implement/test [RFA-01, RFA-02, RFA-03 and verification RVA-01/02](FMSC_POST_FIX_INDEPENDENT_REGRESSION_AUDIT_2026-10-14.md) separately |
| **Windows UI** | Execute WinForms GUI fixtures; real button/tab/close/keyboard/print behavior at 100%, 125%, 150% DPI and laptop-scale resolutions |
| **Financial recoveries** | Test true post-commit reply-loss / network interruption and local recovery journaling on a disposable clone; verify exact-result search, authorization and other-operator behavior |
| **Extra-fee posting replay** | Reconcile same request vs changed fee/selection after lost response; don't infer SQL idempotency from C# `requestId` alone |
| **Database install and restore** | Validate current approved schema/DACPAC, migrations through version 6, rerun/rollback, structural startup gate and existing records on restored representative data |
| **Reports / files** | RDLC/A4/thermal printing, corrupted report settings recovery, CSV/PDF atomic overwrite and denied/locked destination on actual Windows filesystem |
| **Deferred or accepted topics** | BL-03 local SQL identity trust boundary; FR-07 memory measurement; FR-08 DB-photo cap policy; F-07 live SQL semantics, F-08 size/performance; original owner policies intact |

**Documentation-only consolidation.** No further source/migration/test modifications are part of this action. Historic complete originals remain permanently available at the commit-pinned URLs above.
