
# FMSC_SMS — Independent Post-Fix Functional Regression Audit

**Review:** 14 October 2026  
**Repository:** `farhan2166-creator/FMSC-Database`  
**Pinned application baseline:** `6e4fca3650a429e9b6cc213cbfa0cd09383756a6` (26 commits after the functional audit report update)  
**Purpose:** Independently evaluate whether the 25 functional-audit fixes exist, whether their shared safeguards apply to real navigation paths, and whether new gaps remain when a school operator performs actions in unexpected order.  
**Method:** Read-only GitHub C#/SQL/test-source inspection, cross-page event tracing and comparison with existing protections. **No actual WinForms session or fresh SQL execution was performed by this reviewer.**  
**Disposition:** The 25 numbered FQA items have corresponding fix commits and source implementations; release-level acceptance **remains open**. This report identifies **three additional source-level issues** (RFA-01 to RFA-03), plus deployment and SQL fault-injection work that must not be conflated with confirmed defects.  
**Repo mutations:** This report only. No application, migration, executable, or test project edits.

Related documents: [consolidated historical audits](FMSC_CONSOLIDATED_HISTORICAL_AUDITS_2026-10-14.md).

**Implementation follow-up:** [RFA fixes and acceptance evidence](FMSC_POST_FIX_IMPLEMENTATION_PROGRESS_2026-10-14.md); [uploaded schema-6 verification](FMSC_DACPAC_SCHEMA6_VERIFICATION_2026-10-14.md). The findings below retain their original pinned-baseline evidence.

## 1. Executive conclusion

The previous implementation pass is materially complete at source level. The checked fixes include transaction-aware mutation recovery, stale candidate rejection for Additional Fee Posting, workspace busy contracts for fee-matrix saves and student maintenance, modal close guards, certificate drafts, and nonblocking statement regeneration. **Do not re-open the original 25 findings merely because this report finds other issues.**

However, shared contracts only protect routes that actually consult them. **Quick Registration's internal tabs bypass the busy contract**, and **two certificate workflows still bypass or under-scope their unsaved-edit checks**. The new issues concern editing/navigation; no confirmed new financial duplication, database corruption, or privilege escalation was established by this review.

| ID | Priority | Status | Operator workflow | Source-level failure |
|---|---|---|---|---|
| **RFA-01** | **P2** | Confirmed missing UI guard; runtime outcome conditional | Quick Registration → Pending Registrations | Internal tab selection is allowed while the registration form is saving/loading or its outcome is unresolved |
| **RFA-02** | **P3** | Confirmed missing draft tracking | Regular Student Certificate → new issue | A changed date on a new certificate is not recognized as an unsaved draft |
| **RFA-03** | **P3** | Confirmed bypass of existing confirmation | Regular Student Certificate / Bank Account Opening Certificate → New | New clears edited existing certificate fields without checking the known dirty state |
| **RVA-01** | **Verification** | No defect claimed | Additional Fee Posting under lost SQL response | Request-id reuse and changed-payload retry should be demonstrated safe against actual stored-procedure behavior |
| **RVA-02** | **Deployment acceptance** | No defect claimed | Existing school DB → schema 6 | Version-5/6 upgrade, restore and rollback must be tested on a representative disposable data copy |

## 2. New confirmed source findings

### RFA-01 — Quick Registration's tabs do not honor the form busy contract [P2]

**Evidence:** [Pages/Students/Admissions/QuickRegistrationPage.cs L5–32](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Students/Admissions/QuickRegistrationPage.cs#L5-L32); [Pages/Students/Admissions/QuickRegistrationPage.cs L57–67](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Students/Admissions/QuickRegistrationPage.cs#L57-L67); [Pages/Students/Admissions/QuickRegistrationPage.cs L388–417](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Students/Admissions/QuickRegistrationPage.cs#L388-L417); [Pages/Students/Maintenance/StudentDataReviewPage.cs L45–54](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Students/Maintenance/StudentDataReviewPage.cs#L45-L54); compare [Pages/Students/Maintenance/StudentDataReviewPage.cs L5–35](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Students/Maintenance/StudentDataReviewPage.cs#L5-L35) and [WorkspacePage](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Core/WorkspacePage.cs).

**Operator sequence:**
1. Open **Quick Registration → Registration Form** and submit **Save Registration** or **Save Changes** with a deliberately delayed SQL response.
2. While the form's `busy` flag is true, click **Pending Registrations**.
3. The outer `QuickRegistrationPage` does publish `form.IsBusy` to the **dashboard**, but the internal `TabControl` has **no `Selecting` event that cancels the tab change**.
4. The pending tab automatically invokes `RefreshRecordsAsync`; when it finishes, its own **Open Registration** and **Review / Confirm** actions can become available without checking the sibling form's write state.

**Expected:** The operator cannot start another registration-context workflow until the in-flight form write and its reconciliation/refresh have settled. The UI should explain why the switch is temporarily blocked. A draft-only tab change can follow a separate explicit UX policy, because a hidden untouched draft is still retained.

**Risk boundary:** This is a **confirmed missing tab-level guard**, *not* proof that the SQL server permits duplicate or cross-student writes. One particular concern is that the pending list can be used while its sibling form is still recording an overlapping registration state. Reproduce with a delayed test gateway before claiming a real duplicate or stale-row effect.

**Suggested fix:** Attach `tabs.Selecting` and refuse change while `form.IsBusy`, just as `StudentDataReviewPage` and `WorkspacePage` already do. If the pending child gains its own write operations, aggregate that child's `IBusyPage` state too. Avoid blanket disabling the whole module or discarding the draft; preserve status and active tab, and do not launch an auto-refresh when selection was refused.

**Regression tests:** Start delayed new save → try tab switch; delayed saved-registration update → try tab switch; release response and repeat; verify no pending-grid review opens mid-write, returned registration ID/snapshot and family details stay intact; verify ordinary tab switching when clean.

### RFA-02 — Regular Student Certificate's *new* draft is excluded from unsaved checks [P3]

**Evidence:** [Pages/Students/Certificates/RegularStudentCertificatePage.cs L1–14](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L1-L14), [Pages/Students/Certificates/RegularStudentCertificatePage.cs L102–127](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L102-L127), [Pages/Students/Certificates/RegularStudentCertificatePage.cs L191–230](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L191-L230), [Pages/Students/Certificates/RegularStudentCertificatePage.cs L391–424](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L391-L424), [Pages/Students/Certificates/RegularStudentCertificatePage.cs L691–745](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L691-L745).

**Operator sequence:**
1. Open **Regular Student Certificate** and choose a student who does not yet have a certificate for the current class, leaving `selectedRegularStudentCertificateId == 0`.
2. Change the editable `certificateDate` to another valid date but **do not click Create**.
3. Select another student, click **Next Student**, refresh, or leave the page.
4. `HasUnsavedChanges` simply delegates to `HasCertificateEdits`, and that property requires `selectedRegularStudentCertificateId > 0`. The new unsaved date is therefore not protected; `StudentChanged` / `ClearCertificateFields` reset it.

**Expected:** The newly entered date is considered a draft, with Stay preserving its value. An untouched default date should not trigger a false prompt.

**Suggested fix:** Add a **new-certificate baseline** after a student's default certificate fields load. Include current editable new-issue fields in `HasUnsavedChanges` when no saved certificate is selected. Reuse this same check for `StudentPicker.SelectionChangeGuard`, Next Student, Refresh, and dashboard leave. Preserve the existing distinction: edits to a saved certificate must continue using the old saved-record snapshot.

**Tests:** New A/date-changed → B (Stay and Discard); Next Student and Refresh; date reverted to baseline; Create success → no stale draft prompt; saved-record edit remains protected.

### RFA-03 — Both certificate editors' New commands bypass existing dirty checks [P3]

**Regular Student Certificate evidence:** [Pages/Students/Certificates/RegularStudentCertificatePage.cs L1–14](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L1-L14), [Pages/Students/Certificates/RegularStudentCertificatePage.cs L92–112](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L92-L112), [Pages/Students/Certificates/RegularStudentCertificatePage.cs L618–636](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L618-L636), [Pages/Students/Certificates/RegularStudentCertificatePage.cs L725–748](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L725-L748).

**Bank Account Opening Certificate evidence:** [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L1–17](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L1-L17), [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L95–106](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L95-L106), [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L399–420](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L399-L420), [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L663–685](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L663-L685), [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L780–810](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L780-L810); see programmatic-picker semantics [Core/StudentPicker.cs L257–287](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Core/StudentPicker.cs#L257-L287) and [Core/StudentPicker.cs L327–352](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Core/StudentPicker.cs#L327-L352).

**Operator sequence A — Regular:** Select an existing saved certificate; edit its date so `HasCertificateEdits` is true; click **New**. `StartNewCertificate` calls `ClearForm(confirmDiscard: false)` **explicitly bypassing** the dirty prompt. `ClearForm` performs a programmatic `student.Clear()`; the picker does not run its user-change guard for that programmatic setter.

**Operator sequence B — Bank:** Select a saved bank certificate; modify bank/branch/income to activate **Update Certificate**; click **New**. `StartNewCertificate` checks busy/recovery only, then clears the selected certificate and its fields without `ConfirmDiscard()`. The New button is available whenever a certificate is selected, even when edits are dirty. By contrast, **Next Staff Member** and **Refresh** already consult `ConfirmDiscard()`.

**Expected:** New is an intentional way to begin a new record, but if existing unsaved edits would be lost, the user must explicitly approve discarding them; choosing Stay must retain the old selected row and editor.

**Suggested fix:** Make both New handlers check the exact existing dirty predicate and reuse the existing discard dialog. In Regular Student Certificate, do not pass `confirmDiscard:false` unless the call follows an explicitly accepted discard or a successful write. In Bank Certificate, call `ConfirmDiscard()` before clearing. Keep recovery-blocked and active-write paths as unconditional refusals.

**Tests:** Existing saved certificate → edit → New → Stay; edit → New → Discard; clean → New without a prompt; repeated New; update success followed by New; pending bank recovery must still prevent New; verify no SQL save occurs merely from New.

## 3. Verification-only scenarios not called bugs

### RVA-01 — Additional fee uncertain outcome and changed-payload retry

**Evidence:** [Pages/Finance/Billing/AdditionalFeePostingPage.cs L13–25](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Finance/Billing/AdditionalFeePostingPage.cs#L13-L25), [Pages/Finance/Billing/AdditionalFeePostingPage.cs L177–224](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Pages/Finance/Billing/AdditionalFeePostingPage.cs#L177-L224).

The page retains a `requestId` after a failed/unknown `PostAdditionalFee` reply and allocates a new one only after the call succeeds. That is potentially a useful idempotency mechanism. But a staff member may change fee, amount, date or selected students after an error and attempt another submission with the same request ID. **Whether the server returns a safe mismatch refusal or silently treats it as the earlier request depends on the stored procedure's exact replay contract, which was not executed in this review.**

**Required fixture:** On **disposable SQL**, commit a post and suppress its response, then retry (a) the same request/payload, (b) changed fee amount, (c) changed enrollment selection, and (d) after restart. Inspect the `ManualFeePostingRequests` marker and `BillingCharges` deltas. Document the exact error/confirm behavior. Do **not** infer duplicate charges from the C# alone.

### RVA-02 — Upgrade and recovery acceptance of schema 6

**Evidence:** [Required version](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/DatabaseHelper.cs#L1-L12), [Migration 5](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Migrations/5.sql), [Migration 6](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Migrations/6.sql), [SQL recovery checks](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Tests/MutationSqlChecks/Program.cs), [Migration 5 checks](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Tests/QuickRegistrationSqlChecks/Migration5Checks.sql), [Migration 6 checks](https://github.com/farhan2166-creator/FMSC-Database/blob/6e4fca3650a429e9b6cc213cbfa0cd09383756a6/Tests/MutationSqlChecks/Migration6Checks.sql).

The application requires schema **6**. New tables and exact-request markers are checked by startup compatibility. Codex's fix-progress document records successful **disposable** SQL tests, rollback/rerun tests, and native WinForms build results. This reviewer **inspected** representative transaction and test implementations; no tests were rerun here.

Before deploying to a school PC, restore a verified copy of real-like version-4 data and apply **5 → 6 in order**, with app sessions closed. Verify no changes to original student/payment/role records, repeat migrations as no-ops, verify startup compatibility, back up/restore again, and test post-commit lost-response recovery. **Do not replace MDF/LDF with a DACPAC**. Neither this audit nor the migration tests modified a live school database.

## 4. Regression coverage versus prior 25 findings

| Existing fix area | Independent source inspection | What remains unproven here |
|---|---|---|
| Financial adjustment and bank certificate exact mutation requests | Inspected journal, request hashing, DB scope, permission checks, transaction+marker atomicity, outcome lookup and associated tests | Real Windows workflow, physical network interruption, different Windows users, school-like DB performance |
| Additional Fee Posting stale candidates (FQA-16) | Scope signature / request version and membership checks present | Actual delayed SQL UI run; RVA-01 failure/retry |
| Fee matrix draft/busy protections (FQA-17/18) | Internal campus-change guard, `IBusyPage`, shared host aggregation present | Real WinForms campus picker, SQL error and closing tests |
| Admission/modal, Daily Closing, review workspace, duplicate review (FQA-01/04/07/09/10/11/14/15) | Busy/dirty contracts present in representative pages and modal guard helper | Native Windows navigation, double-click and long-running SQL |
| Statements and receipt previews (FQA-12/19/20) | Stale export signature invalidation and sequential nonblocking read worker present | Real RDLC printing, UI responsiveness, DPI and multi-student SQL |
| Certificate and return-admission drafts (FQA-21/22/23/24) | Certificate new-draft tracking added on Character/Leave/College Leave, bank creation guard and return modal dirty guard present | The **additional Regular and New-button gaps** RFA-02/03; actual Windows interaction |
| Test coverage | Examined regression fixtures for quick registration, certificates, busy guards and mutation SQL | Compiled WinForms fixtures have not been independently run by this reviewer; Codex's reported SQL passes are not an independent test run |

The original 25 FQA items remain **source-addressed**. This report **does not reopen them**, and it does not certify all 25 as Windows-pass. The three newly identified RFA gaps are separate incremental fixes.

## 5. Recommended work for Codex

1. **First: RFA-01.** Reuse the existing tab `Selecting` busy guard; cover both new registration save and saved pending registration update with delayed UI fixtures. Do not change SQL business logic.
2. **Then: RFA-02/03.** Centralize certificate draft / discard checks so changing student, New, Refresh, and page leave share the same definition of dirty. Keep intentional clean New actions one click.
3. **Verification: RVA-01.** Fault-inject `PostAdditionalFee` on disposable SQL before deciding if idempotency or UI recovery needs adjustment. No claim of defect without server evidence.
4. **Release gate: RVA-02.** Run schema-6 migrations and recovery tests on a restored disposable version-4-like database; run the WinForms GUI fixtures on Windows at the school PC's intended DPI/resolution.

**No confirmed new issue here requires an immediate SQL migration.** Make small C# changes with matching UI tests, and recheck against any commits newer than `6e4fca3` before implementation.

## 6. Provenance

- **Verified repository baseline:** `6e4fca3650a429e9b6cc213cbfa0cd09383756a6` (newer Codex fixes may alter this after publication).
- **GitHub files read:** C# handlers for Quick Registration and pending review, StudentPicker, Regular Student Certificate, Bank Account Opening Certificate, financial adjustment, Additional Fee Posting, Fee Rate Matrix, Student Record Checks, Daily Collection Closing, Student Account Statement, shared modal/busy helpers, migration requirements, migration SQL, and associated tests/fix progress.
- **Classification:** Three source-confirmed missing checks; two follow-up verification cases. Severity describes potential user impact, not a demonstrated production incident.
- **Excluded assertions:** no actual Windows click tests performed; no proof of duplicate extra-fee charges; no claim that Codex's SQL tests were independently re-executed; no modification of school data.
