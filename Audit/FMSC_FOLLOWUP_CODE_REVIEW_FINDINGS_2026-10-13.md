# FMSC_SMS — Follow-up Code Review: Problems, Impact, and Proposed Fixes

**Date:** 13 October 2026  
**Repository:** farhan2166-creator/FMSC-Database  
**Branch inspected:** `main`, HEAD `4d714a9a8956312678a2a389413225570355e971`  
**Code baseline:** `ba2a00632ae387f412d384eb407ea3488455ccaf` (the three subsequent commits only consolidated Audit documents)  
**Historical comparison:** `c63bb90b8b5df83f3a13ed33ae2e26348535ef18` and intervening changes where relevant  
**Scope:** Targeted read-only examination of recent C# changes and business workflows; comparison against older implementations. No source, database, migration, or test modifications were made to produce this report.  
**Related report:** [Master audit](MASTER_AUDIT_FINDINGS_FIXES_STEPS_DONE_2026-10-13.md) consolidates earlier findings and fixes; this file adds the follow-up review of remaining failure paths.  
**Original review status:** Findings and recommendations only — **NOT IMPLEMENTED** at the inspected baseline. The implementation addendum below records subsequent work; deployment/runtime sign-off remains **PENDING**.

> This is a targeted source review, not a claim that all project files were inspected end-to-end. Where a defect requires a rare exception, changed SQL response, or particular operator action, its *conditional nature* is stated. No real school database was accessed, and no native Windows UI test was executed during this review.

## Executive summary

The recent work materially improves Quick Registration concurrency, financial constraints, collection recovery, active-write navigation guards, cancellation of stale reads, database compatibility, and dashboard usability. The follow-up review did **not** demonstrate a new P1 data-corruption regression. It did identify several code-level error/feedback gaps and a changed photo-management user experience worth addressing before end-user release.

### Prioritized register

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

## FR-01 — Admission success needs an authoritative returned record ID

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

## FR-02 — Quick Registration save/refresh status can hide a failed reload

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

## FR-03 — Recover the previous workspace if destination initialization fails

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

## FR-04 — Photo management now has different commit semantics from the retired Photo page

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

## FR-05 — Main Admission Save cannot retry a failed pending photo write

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

## FR-06 — Fresh Quick Registration dirty tracking misses year/date-only edits

**Evidence:** [`QuickRegistrationPage.cs`, `HasUnsavedChanges`](https://github.com/farhan2166-creator/FMSC-Database/blob/4d714a9a8956312678a2a389413225570355e971/Pages/Students/Admissions/QuickRegistrationPage.cs#L49-L56).

**Problem:** The new-record dirty check accounts for name, parent, phone, address, or placement, but not a manually changed joining date or academic year when these other inputs remain empty.

**Example:** Start a blank Quick Registration form, change only Joining Date or Academic Year, then navigate away. The form can report clean. Whether a date-only change *ought* to prompt is a product/UX policy decision, but the dirty tracking is inconsistent with other editable inputs.

**Proposed fix (C#):** Capture a clean baseline *after* asynchronous default-year/date initialization. Compare all user-editable fields (including year/date) against that baseline. Distinguish user input from programmatic population to avoid false unsaved prompts. Preserve any explicitly intended New/Clear behavior.

**Tests:** Blank→change year; blank→change date; leave default values untouched; async default loading; reset to original values; New/Clear actions; Navigate/Sign Out.

## FR-07 — Test decoded-photo memory, not only compressed file sizes

**Evidence:** [`StudentPhotoInput.cs`](https://github.com/farhan2166-creator/FMSC-Database/blob/4d714a9a8956312678a2a389413225570355e971/Core/StudentPhotoInput.cs) and [`StudentPhotoProcessor.cs`](https://github.com/farhan2166-creator/FMSC-Database/blob/4d714a9a8956312678a2a389413225570355e971/Core/StudentPhotoProcessor.cs).

**Current improvement:** Source file limit is 25,000,000 bytes; output is a proportional, <=600-pixel-long-side JPEG of <=300×1024 bytes; decoding/compression is off the UI thread, with cancellation checks.

**Remaining risk:** Small or moderately compressed image files may decode to very large pixel buffers; `Image.FromStream` must decode image data before the resized 600-pixel bitmap is generated. The byte limit alone does not establish a safe peak GDI/managed/private-memory bound on low-RAM Windows PCs. **This review did not reproduce a leak or crash.**

**Recommended verification first:** Exercise extreme pixel dimensions, malformed JPEG/PNG, EXIF-rotated images, transparent PNGs, repeated choose/remove/zoom, slow machines and cancellation. Measure private bytes, working set and GDI handle counts. If needed, add a tested pixel-count / dimension guard or decoder approach before allocation, without distorting ordinary photographs.

## FR-08 — The photo size rule is not enforced by SQL itself

**Evidence:** App-side `StudentPhotoInput.ValidateStoredData` and the historical [photo consolidation analysis](https://github.com/farhan2166-creator/FMSC-Database/blob/e3d2774c29fd44fa089db4df03d7d4f74cf4c1d3/Audit/FMSC_Photo_Storage_2026-10-13.md).

**Boundary:** FMSC's user-interface path restricts new stored photos to 300 KB. The existing `SaveStudentPhoto` procedure was not given a new SQL-side byte-size constraint, and existing historical photos were not rewritten. Direct calls to the SQL procedure outside this application can therefore bypass the app-only size limit.

**Decision / optional fix:** Accept this boundary if the workstation and SQL callers are trusted, or separately authorize a numbered SQL migration that validates new writes at the database layer. Do **not** silently change the schema or convert existing photos. A future SQL change needs review of old data and a disposable-db upgrade test.

## FR-09 — Required deployment acceptance tests remain outstanding

The current master audit documents historical success for portable checks, a cross-build, disposable SQL migration/role tests, and related artifact checks. Those results are valuable, but do not prove the following on the intended user's Windows machine and database:

1. **Admissions:** normal, historical, Quick Registration and pending completion; old-year admission numbers; duplicate submissions; malformed/missing return IDs; failed post-save reload.
2. **Money:** whole-rupee end-to-end setup/billing/collection, two-operator stale fee conflicts, connection loss immediately around payment commit, family receipt consistency and unknown-outcome reconciliation. Never blindly retry a financial write.
3. **Upgrade:** initial MDF/LDF install only once; subsequent **ordered SQL migrations** against the same existing database; version 1→2 refusal/success/idempotence; verified pre-migration backup and recovery. **No DACPAC deployment or replacement MDF/LDF for subsequent updates.**
4. **Restore/recovery:** successful restore, failed restore, incompatible restored DB with safety recovery, failed safety recovery, new reads after cache invalidation, backup integrity and denied disk access.
5. **Windows UI:** run `Tests/ThemeSmokeChecks` and the other documented acceptance suites; inspect 100%, 125%, 150% scaling, 1080×600, 1366×768 and other target laptop dimensions; both themes; unsaved state, focus/tab order, dialogs and printing.
6. **Performance:** profile real school-sized records on representative Windows hardware—opening student profiles, cold lookup loads, pending list filtering, fee collection, report previews/exports and repeated navigation. Synthetic benchmark savings are not guaranteed real-world CPU/RAM savings.

**Status:** Pending verification, not a claim that the application failed these tests.

## Recent improvements confirmed during source review (retain them)

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

## Recommended remediation order

1. **Before release:** FR-01 and FR-02 — correct the distinction between a committed database write, verified response and UI refresh; avoid unconfirmed success messages.
2. **Before release:** FR-03 — protect existing workspace against a destination-activation exception.
3. **Before release:** FR-04 / FR-05 — agree on explicit versus immediate photo replacement and make failed-photo retry consistent with Save and dirty-state behavior.
4. **Next UX pass:** FR-06 — correct date/year dirty-state tracking without false prompts from defaults.
5. **Release testing:** FR-07 / FR-09 — run real Windows/native SQL/performance scenarios and record evidence.
6. **Only if policy requires it:** FR-08 — design an approved additive SQL migration for database-wide photo limits.

**Constraints for any subsequent implementation:** Preserve admission numbering policy, existing money transactions, signed-in permission checks, historical student rules, unsaved-change navigation safeguards, migration ordering, and installed user data. Prefer small isolated C# fixes and targeted tests; do not broaden into a speculative architectural rewrite.

## Verification scope and provenance

- Reviewed current source paths at `main` commit `4d714a9`; the three commits after `ba2a006` consolidated audit documentation and did **not** change C#/SQL files.
- Compared historical `c63bb90` versions for FR-01 and FR-03 and historical separate `StudentPhotoPage` for FR-04. Older flow logic was also inspected for FR-02.
- Findings are **source-based** with status distinctions above. No new tests were executed and no application defects were reproduced on the user's PC.
- The existing [master audit](MASTER_AUDIT_FINDINGS_FIXES_STEPS_DONE_2026-10-13.md) remains unchanged. This addendum does not reopen previously fixed/accepted F- or BL-series issues without new evidence.
- This report is the **only intended repository change** from the follow-up review.

## Implementation and verification addendum — 13 October 2026

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

### Registration refresh and explicit photo-save implementation — 13 October 2026

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

### Confirmed admission outcome and compact photos — 13 October 2026

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
