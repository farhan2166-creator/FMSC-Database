# FMSC_SMS — Deep Functional, UI-State & End-to-End Workflow Audit

**Review date:** 13 October 2026  
**Repository:** `farhan2166-creator/FMSC-Database`  
**Pinned source baseline:** `main` at `3a3fbd63590cc7dedb3f17f008e84f7be67a9132`  
**Requested emphasis:** Real school-operator journeys: buttons and fields, changing selections, switching tabs, confirm/cancel, refreshing, unsaved drafts, delayed database responses, post-commit failures, and unexpected action order.  
**Audit method:** Read-only GitHub source tracing across callers, controls, shared infrastructure, related modal wrappers, SQL invocation boundaries and existing safeguards. **No live Windows UI or school database test was run.**  
**Implementation status:** FINDINGS ONLY — **NOT FIXED**, **NOT RUNTIME REPRODUCED** by this report.  
**Related:** [Master audit](MASTER_AUDIT_FINDINGS_FIXES_STEPS_DONE_2026-10-13.md), [follow-up audit](FMSC_FOLLOWUP_CODE_REVIEW_FINDINGS_2026-10-13.md), [independent security/durability audit](FMSC_INDEPENDENT_SECURITY_DURABILITY_RELEASE_AUDIT_2026-10-13.md).

> The operator separately reported Quick Registration fee choice/removal, optional phone, disappearing buttons, refresh/reopen friction, family links in grid, temporary reference numbering, combined placement, and stale grid-selection actions. Those are **not counted again** as discoveries here. Codex may be actively fixing them outside the current committed baseline. This report examines additional scenarios and shared failure patterns.

## A. Executive summary

**Fifteen findings or verification items** across Admission Review, Student Profile, Financial Adjustments, Posting Reversal, Daily Collection Closing, Family maintenance, Duplicate review, Record Checks and Annual Receipts. **Eleven** are confirmed **source-level control-flow/guard gaps**; **two** are conditional asynchronous UI behavior that needs a delayed-response fixture; **one** is a database uncertain-outcome *verification risk*; **one** is an intentional-UX decision to confirm. None should be called a proven production failure without an appropriate Windows/SQL reproduction.

**Most important pattern:** Disabling an individual `AddBusyAction` button is **not the same** as blocking page departure or other conflicting actions while a database write is running. `MainDashboardForm` checks the **top-level active page** for `IBusyPage` and then `IUnsavedChanges`. Several nested pages and dialogs fail to expose the true operation/draft state. Adding `IBusyPage` to a nested control **alone** is insufficient unless its host forwards that contract.

### Triage register (risk order, not effort order)

| ID | Severity | Classification | Workflow | Concise problem |
|---|---|---|---|---|
| **FQA-15** | **P2** | Confirmed source gap | Admission / confirmation | `RunBusyAsync` writes are not reflected by `StudentAdmissionPage.IsBusy` |
| **FQA-07** | **P2** | Confirmed source gap | Daily Collection Closing | Active financial write is handled as a confirmable discard, not an unconditional leave block |
| **FQA-09** | **P2** | Confirmed source gap | Duplicate review | Confirm and Dismiss can operate concurrently on the same selected pairs |
| **FQA-04** | **P2** | Confirmed source gap | Posting Reversal | Whole-page navigation is not blocked during `ReverseMonthlyFees` |
| **FQA-06** | **P2 verify** | Unconfirmed SQL outcome risk | Refund / correction / reversal | Lost reply after `ProcessFeeTransaction` may be shown as generic failure; SQL duplicate prevention needs test |
| **FQA-13** | **P2** | Confirmed error-feedback gap | Payment adjustments | Successful adjustment followed by failed history refresh loses success acknowledgment |
| **FQA-01** | **P2** | Confirmed source gap | Pending registration Review / Confirm | Modal close ignores dirty form and busy operation |
| **FQA-11** | **P2** | Confirmed composite guard gap | Student Record Checks | Admin corrections aren't protected by workspace-level write-in-progress contract |
| **FQA-14** | **P2/P3** | Confirmed composite dirty-state gap | Student Record Checks | Edited correction fields have no workspace-level discard prompt |
| **FQA-08** | **P2/P3** | Confirmed source gap | Family / Siblings | Direct remove/link maintenance write lacks dashboard busy contract |
| **FQA-10** | **P2/P3** | Confirmed source gap | Duplicate scan / batch review | Page departure remains allowed during long-running write/batch |
| **FQA-02** | **P2/P3** | Confirmed state carryover | Payment adjustments | Amount/reason from payment A can follow selection to payment B |
| **FQA-05** | **P3** | Conditional UI timing | Payment Details | Delayed details dialog may open for previous payment selection |
| **FQA-12** | **P3** | Conditional UI timing | Annual Fee Receipts | Delayed report can open for previously selected student/year |
| **FQA-03** | **P3 / decision** | UX contract question | Student Profile shortcuts | Student-specific actions accept no selected student; may open blank/default destinations |

**P1:** None claimed. **No direct evidence** of unintended SQL database edits, double refunds or data corruption from this read-only review.

## B. Confirmed functional findings — reproduction and fixes

### FQA-15 — Admission's shared busy operation is invisible to the shell [P2]

**Evidence:** [`Pages/Students/Admissions/StudentAdmissionPage.cs` L228–236](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Admissions/StudentAdmissionPage.cs#L228-L236), [`Pages/Students/Admissions/StudentAdmissionPage.cs` L3749–3788](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Admissions/StudentAdmissionPage.cs#L3749-L3788), [`Pages/Students/Admissions/StudentAdmissionPage.cs` L906–921](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Admissions/StudentAdmissionPage.cs#L906-L921), [`Core/V2Page.cs` L786–804](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Core/V2Page.cs#L786-L804), [`MainDashboardForm.cs` L1637–1655](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/MainDashboardForm.cs#L1637-L1655).

**Exact code behavior:** `StudentAdmissionPage.IsBusy` returns only `saveBusy || photoBusy`. However, the page performs **Confirm Admission** (`CompleteQuickAdmission`), **Confirm Placement**, **Verify Legacy**, and report preparation inside `V2Page.RunBusyAsync`. That helper tracks `_busyOperationRunning` privately; it does not set either admission-specific flag and is not included in `IsBusy`. `RunBusyAsync` blocks a second call to itself, **not navigation/window closure**.

**Reproduce:** On normal Admission, initiate Confirm Placement or legacy verification with intentionally delayed SQL; attempt sidebar navigation. On Review / Confirm, submit Confirm Admission and attempt dialog X/Escape while the SQL response is pending. **Expected:** both leave attempts refused until the exact result is known. **Current source contract:** busy state is not advertised to host. No claim that server write is duplicated; the risk is losing the operator result / UI continuation.

**Proposed fix:** Expose a protected read-only shared `RunBusyAsync` operation state from `V2Page` (or fold these calls into the existing page-level activity gate) and include it in `StudentAdmissionPage.IsBusy` and its busy message. Keep `saveBusy` / `photoBusy` behavior intact. Add tests for all three write operations **and both print commands** while in flight; verify no premature close or navigation. **Do not fix only FQA-01**, because a new dialog close guard querying old `IsBusy` would still miss this state.

### FQA-07 — Daily Collection Closing can be left mid-write by accepting “discard” [P2]

**Evidence:** [`Pages/Finance/Collection/DailyCollectionClosingPage.cs` L7–22](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Collection/DailyCollectionClosingPage.cs#L7-L22), [`Pages/Finance/Collection/DailyCollectionClosingPage.cs` L151–190](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Collection/DailyCollectionClosingPage.cs#L151-L190), [`MainDashboardForm.cs` L1637–1658](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/MainDashboardForm.cs#L1637-L1658).

**Exact behavior:** `DailyCollectionClosingPage` implements `IUnsavedChanges`, **not `IBusyPage`**, and returns `HasUnsavedChanges => dirty || busy`. While `SaveDailyCollectionClosing` or `ApproveDailyCollectionClosing` is awaited, `busy=true`. Dashboard's leave guard checks `IBusyPage` first, then lets the operator **confirm discarding** via `IUnsavedChanges`. The displayed text says “Wait …”, but clicking the affirmative discard/leave option can still allow the page to be retired.

**Reproduce:** Start Submit Closing or Approve on a test day with delayed DB response; click another sidebar module or close the app; accept the discard confirmation. The pending server call and local post-save reload outlive the workspace. **Expected:** unconditional no-leave during a submitted closing write.

**Fix:** Implement `IBusyPage` with `IsBusy => busy` and an explicit blocking message, while keeping `IUnsavedChanges` exclusively for **unsubmitted** values/notes. Protect Submit/Approve through complete outcome reconciliation, then allow leaving. Test SQL refusal, server commit with delayed reply, navigation, app close, and sign-out. Do not treat a submitted close as cancellable.

### FQA-09 — Duplicate-pair Confirm/Dismiss actions can overlap [P2]

**Evidence:** [`Pages/Students/Maintenance/DuplicateStudentAuditPage.cs` L10–33](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/DuplicateStudentAuditPage.cs#L10-L33), [`Pages/Students/Maintenance/DuplicateStudentAuditPage.cs` L112–146](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/DuplicateStudentAuditPage.cs#L112-L146), [`Pages/Students/Maintenance/DuplicateStudentAuditPage.cs` L269–332](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/DuplicateStudentAuditPage.cs#L269-L332), [`Core/V2Page.cs` L718–769](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Core/V2Page.cs#L718-L769).

**Exact behavior:** The page exposes two independent `AddBusyAction` buttons, **Confirm Duplicate** and **Dismiss**. The helper disables **only the clicked button**. The other remains available, and `Grid.SelectionChanged` calls `UpdateResolveButtons`, which may re-enable **both** without consulting a shared write-in-progress flag. `ResolveSelectedAsync` processes multiple `ReviewId` values in an `await` loop with no global operation gate.

**Reproduce:** Select one or several candidate pairs, click Confirm, approve the prompt, then click Dismiss (or change row selection) while a delayed server response holds the first batch open. **Expected:** once a batch starts, both decisions, Scan, and selection-sensitive commands remain unavailable until reconciliation. **Risk:** competing status writes, confusing last-writer outcome, overlapping reloads and inaccurate final messages; stored-procedure restrictions may reject one write, but this client conflict should not be permitted.

**Fix:** Track `resolving` (and scanning if needed); implement `IBusyPage` for writes; disable both decision buttons and the candidate grid throughout the batch; return if another decision is running; re-derive action states after reload. Preserve partial-failure-per-row accounting. Add an injected delay test with rapid Confirm→Dismiss and selection changes.

### FQA-04 — Reverse a Posting is not a shell-busy page [P2]

**Evidence:** [`Pages/Finance/Billing/FeePostingReversalPage.cs` L1–52](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Billing/FeePostingReversalPage.cs#L1-L52), [`Pages/Finance/Billing/FeePostingReversalPage.cs` L285–363](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Billing/FeePostingReversalPage.cs#L285-L363), [`Pages/Finance/Billing/FeePostingReversalPage.cs` L363–429](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Billing/FeePostingReversalPage.cs#L363-L429), compare [`Pages/Finance/Billing/FeePostingPage.cs` L5–14](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Billing/FeePostingPage.cs#L5-L14).

**Exact behavior:** `ReverseAsync` submits `ReverseMonthlyFees`. The page inherits `V2Page` but does **not** implement `IBusyPage` or maintain a reversal transaction flag. The clicked reverse button is guarded against a repeated click, but the sidebar, preview action, filters and page lifetime are not managed as an active write.

**Reproduce:** Preview a posted month, enter a reason, confirm, delay the SQL response, then switch page or close. **Expected:** prevent leaving until summary and remaining-posting refresh are resolved, and prevent starting a competing preview/second reverse. **Fix:** separate `reversing` from ordinary read/preview loading; implement `IBusyPage`, hold scope-changing controls, preserve existing snapshot/filter-signature and “collected charges cannot reverse here” protections. On an unknown SQL outcome, insist on fresh reconciliation rather than blind retry.

### FQA-13 — Payment adjustment may commit, then its failed refresh hides success [P2]

**Evidence:** [`Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs` L231–286](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs#L231-L286), [`Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs` L448–486](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs#L448-L486).

**Exact behavior:** `ExecuteAsync` awaits `ProcessFeeTransaction` and constructs a success message from the returned data. It then clears editor state and does `await LoadPaymentsAsync(completionMessage, admissionId)`. `LoadPaymentsAsync` **catches its own database errors**, calls `Fail(ex)`, and returns without surfacing a failed reload result. The already built “corrected/refunded/reversed” success message is shown **only** after successful reload.

**Reproduce:** Allow adjustment submission to succeed, then fail the next payment-history SQL read. **Observed-from-code outcome:** ordinary load failure feedback without a distinct “the adjustment succeeded but the list could not refresh” state.

**Fix:** Return a typed load outcome or explicit `bool` from history refresh. Once server acknowledgment is obtained, display “Adjustment recorded; payment list could not refresh” on read failure and disable subsequent adjustment until explicit reload. Do not automatically resubmit. Acceptance: committed result + subsequent failed read; failed write; stale student switching; and manual reconciliation after restart.

### FQA-01 — Review / Confirm modal has no dirty/busy closing guard [P2]

**Evidence:** [`Pages/Students/Maintenance/StudentDataReviewPage.cs` L63–72](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentDataReviewPage.cs#L63-L72), [`Pages/Students/Admissions/StudentAdmissionPage.cs` L300–304](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Admissions/StudentAdmissionPage.cs#L300-L304), compare protected [`Pages/Reports/Students/StudentReportsPage.cs` L259–287](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Reports/Students/StudentReportsPage.cs#L259-L287) and [`Pages/Students/Profile/StudentProfilePage.cs` L294–323](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Profile/StudentProfilePage.cs#L294-L323).

**Exact behavior:** `AdmissionReviewListPage.ReviewRegistrationAsync` creates a modal `ApplicationWindowForm` and hosts `new StudentAdmissionPage(...registrationReview:true)`. It subscribes to `RegistrationFinished`, but has **no `FormClosing` handler** to inspect the admission editor's `IUnsavedChanges` and `IBusyPage`. Comparable modal editors in the project already implement such a guard.

**Reproduce:** Open Pending Registrations → Review / Confirm; change an admission detail, click X or Escape. The shell never sees the nested editor because this is its own modal. **Expected:** discard confirmation for edits and absolute block for submitted writes. **Fix:** reuse a common modal leave-guard helper that checks child contracts. Coordinate with **FQA-15** so `IsBusy` reflects the actual confirmation operation. Test X, Escape, Cancel Registration, failed save, and active confirm.

### FQA-11 — Student Record Checks correction writes lack a composite busy contract [P2]

**Evidence:** [`Pages/Students/Maintenance/StudentDataReviewPage.cs` L1–26](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentDataReviewPage.cs#L1-L26), [`Pages/Students/Maintenance/StudentRecordChecksPage.cs` L1–9](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentRecordChecksPage.cs#L1-L9), [`Pages/Students/Maintenance/StudentRecordChecksPage.cs` L130–146](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentRecordChecksPage.cs#L130-L146), [`Pages/Students/Maintenance/StudentRecordChecksPage.cs` L1283–1363](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentRecordChecksPage.cs#L1283-L1363), [`Pages/Students/Maintenance/StudentRecordChecksPage.cs` L1364–1434](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentRecordChecksPage.cs#L1364-L1434).

**Exact behavior:** `StudentRecordChecksPage` performs `Save Status`, `Save Placement`, `Create Missing Record` and date repairs; some operations await preflight reads and later invoke corrective SQL. The child is a plain `V2Page`. Its parent `StudentDataReviewPage` is a plain `UserControl`; neither forwards a write-busy contract to the dashboard. Per-button `AddBusyAction` is **not** a shared transaction guard and does not prevent switching tabs/closing the whole workspace.

**Reproduce:** In Record Issues / History, begin a correction with delayed SQL preflight/commit; change to Possible Duplicates, navigate elsewhere, or close the app. **Fix:** guard correction lifecycle within the child, expose a parent `IBusyPage` that delegates to all relevant cached tabs (not just selected tab), and disable conflicting actions until full outcome established. Review the synchronous `DatabaseHelper.Query` calls separately; they can freeze the UI and are **not** evidence that async navigation is safe. Test stale A→B student/placement changes and delayed writes.

### FQA-14 — Unsaved Student Record Checks correction drafts are invisible to dashboard leave guard [P2/P3]

**Evidence:** [`Pages/Students/Maintenance/StudentDataReviewPage.cs` L1–26](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentDataReviewPage.cs#L1-L26), [`Pages/Students/Maintenance/StudentRecordChecksPage.cs` L1–50](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentRecordChecksPage.cs#L1-L50), [`Pages/Students/Maintenance/StudentRecordChecksPage.cs` L1195–1221](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentRecordChecksPage.cs#L1195-L1221), [`Pages/Students/Maintenance/StudentRecordChecksPage.cs` L1283–1318](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentRecordChecksPage.cs#L1283-L1318).

**Exact behavior:** The correction console contains mutable status, withdrawal/placement/date fields and reason text, but it does **not** implement `IUnsavedChanges`, and its parent dashboard workspace does not either. The operator can prepare a correction and leave the entire workspace without the general unsaved draft prompt.

**Reproduce:** Select a student, change status/withdrawal date or placement reason, do not save, then navigate from the Student Data Review workspace. **Expected:** a normal Save/Discard/Stay decision. **Fix:** record a clean baseline after each successful load; track correction-specific dirty fields, including create-missing state; forward aggregate `IUnsavedChanges` from the parent across instantiated tabs. Do not show false prompts for programmatic binding or an untouched form. Separate FQA-11's **active write** guard from this **unsaved draft** guard.

### FQA-08 — Family maintenance's asynchronous writes are not shell-protected [P2/P3]

**Evidence:** [`Pages/Students/Profile/StudentFamilyPage.cs` L1–63](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Profile/StudentFamilyPage.cs#L1-L63), [`Pages/Students/Profile/StudentFamilyPage.cs` L176–199](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Profile/StudentFamilyPage.cs#L176-L199), [`Pages/Students/Profile/StudentFamilyPage.cs` L341–394](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Profile/StudentFamilyPage.cs#L341-L394).

**Exact behavior:** `StudentFamilyPage` is a plain `V2Page` with local `familyActionBusy`. It disables its own Add/Remove controls and picker using `BeginBusy`, but does **not** implement `IBusyPage`. `RemoveSelectedMemberAsync` directly awaits `Task.Run(DatabaseHelper.Execute("DeleteStudentSibling",...))`; after a successful add, the page may also await a full student/family reload. Shell navigation remains possible during these awaits.

**Reproduce:** Remove a family member with delayed server response, then navigate away/exit. **Expected:** operation completion and clear refresh/unknown-outcome feedback before the page is disposed. **Fix:** expose `familyActionBusy` via `IBusyPage`, while retaining the existing per-button/selection guards and SQL permission checks. Note that the **Add Member** flow uses a modal sibling-picker; its modal phase is already owner-blocking, so the direct Remove path is the clearest regression fixture.

### FQA-10 — Duplicate scan and multi-row resolution can outlive the page [P2/P3]

**Evidence:** [`Pages/Students/Maintenance/DuplicateStudentAuditPage.cs` L1–41](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/DuplicateStudentAuditPage.cs#L1-L41), [`Pages/Students/Maintenance/DuplicateStudentAuditPage.cs` L242–332](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/DuplicateStudentAuditPage.cs#L242-L332), [`Pages/Students/Maintenance/StudentDataReviewPage.cs` L1–26](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Maintenance/StudentDataReviewPage.cs#L1-L26).

**Exact behavior:** `ScanAsync` invokes `ScanForDuplicateStudents`, which can insert new review candidates. `ResolveSelectedAsync` issues separate asynchronous `ResolveDuplicateStudentReview` calls for all selected rows. Neither child page nor its parent communicates an active-write `IBusyPage` contract to the dashboard. Clicking out of the workspace can leave a partial batch with the visible summary undisplayed.

**Fix / test:** Reuse the same batch-busy state from FQA-09 and delegate it through the Student Data Review host. Test two/ten selected pairs with SQL delay, one rejected row, navigation attempt, sign-out, and close. Preserve partial success details and reloaded Open candidates; **do not** automatically replay an entire batch.

### FQA-02 — Correction/refund amount and reason persist across different payments [P2/P3]

**Evidence:** [`Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs` L289–349](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs#L289-L349), [`Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs` L350–451](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs#L350-L451).

**Exact behavior:** Payment `SelectionChanged` resolves the new `selectedPaymentId` and `selectedPaymentAmount`, then `UpdateActionMode` rebuilds operations. For **Correction** and **Refund**, `actionAmount` is reset to `"0"` **only when blank**. A previously entered value can remain from another payment/student; `reason` and transaction date are also retained. The confirmation **does correctly name payment, student, date, amount and reason**, which limits but does not eliminate the UX risk.

**Reproduce:** Choose payment A → type Rs. 500 / a reason → select payment B (with same adjustment operation). Amount and reason can still describe A while B is targeted. **Expected:** clear or explicitly preserve/revalidate draft by payment identity. **Fix:** reset per-payment inputs when identity changes, but avoid wiping values while editing the same payment; add A→B→A and different-student fixtures. Keep the existing final confirmation and server-side eligibility checks.

## C. Conditional UI races and release-verification items

### FQA-05 — Late Payment Details can open for a previous selection [P3, conditional]

**Evidence:** [`Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs` L489–529](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs#L489-L529).

`ViewPaymentDetailsAsync` captures payment/student identifiers and awaits an SQL detail query, then shows a modal dialog if the page is **not disposed**. It does not re-check the current selected student/payment. The grid and student finder remain interactive during this read.

**Inject delay:** Select A, click View Details, select B while query awaits. A's details may appear over B. **Fix:** check captured IDs and a selection-generation token before rendering, or deliberately lock the relevant selection while the read is active. No database mutation is implicated.

### FQA-12 — Annual receipt printing can use a superseded student/year [P3, conditional]

**Evidence:** [`Pages/Reports/Finance/AnnualFeeReceiptsPage.cs` L136–226](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Reports/Finance/AnnualFeeReceiptsPage.cs#L136-L226).

`ViewLedgerAsync` captures student/year, then awaits annual-receipts data, school identity, many receipt-line queries, and current balances before opening the preview. The student and year selectors remain available; there is no latest-selection check before `RdlcReportPreview.Show`. A late preview is a correctly queried **old** student/year, but can surprise the clerk who has already selected a new record. There is no proof the receipt contents themselves are mismatched.

**Test:** Start A/year-1 preview with a delay, switch to B/year-2. **Fix:** freeze the selection for the duration, or supersede/cancel stale reads before preview. Maintain the captured IDs when generating all data.

### FQA-06 — Financial adjustment with lost commit acknowledgment [P2 verification, **not confirmed defect**]

**Evidence:** [`Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs` L350–488](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs#L350-L488); compare existing [collection recovery implementation](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Collection/CollectionRecoveryJournal.cs).

`ExecuteAsync` submits `ProcessFeeTransaction` and displays generic `Fail(ex)` if the call throws. A lost connection after SQL commit can make the outcome ambiguous; this C# flow has no durable request/outcome reconciliation like direct collections or Quick Registration. **Do not assert that the SQL procedure allows duplicate refunds** without inspecting and executing it.

**Required test:** Against a **disposable SQL copy**, inject a failure after server commit but before the client sees the reply; inspect both payment history and any SQL idempotency/reversal controls. Test same command retry, stale eligibility, receipt relation and restart. If unknown commits are possible, add an idempotent request identity and/or durable outcome lookup; display “outcome unknown, check transactions before repeating.” No automatic retry.

### FQA-03 — Student Profile shortcuts accept selection ID 0 [P3, UX decision]

**Evidence:** [`Pages/Students/Profile/StudentProfilePage.cs` L216–279](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Profile/StudentProfilePage.cs#L216-L279), [`Pages/Students/Profile/StudentProfilePage.cs` L281–321](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Profile/StudentProfilePage.cs#L281-L321).

Student-specific `JumpButton` click handlers pass `student.SelectedId` without requiring a valid selection; most buttons begin enabled. On a blank profile, they can open unrelated blank/default pages rather than warn and direct the operator to select a student. **Qualification:** “Open Admissions” might intentionally double as opening a blank new-admission form. Do **not** disable it automatically without deciding that product policy. Restrict only buttons with a genuinely required existing student, or clearly label generic navigation.

**Test:** No student; selected A; clear typed lookup; selected B; permission-limited role. Decide which shortcuts are generic versus record-bound.

## D. Protections verified — do not report them as absent

| Area reviewed | Existing safeguard | Why it matters |
|---|---|---|
| Standard monthly fee posting | `FeePostingPage : V2Page, IBusyPage` exposes posting state | Do not replace safe existing write guards |
| Additional fee posting | `AdditionalFeePostingPage : V2Page, IBusyPage` tracks posting | Existing SQL idempotency / client requests still need their own tests |
| Fee Collection | `FeeCollectionPage` exposes `transactionBusy` and uses a durable financial recovery journal | Do not extend FQA-06 into an unsupported claim about normal fee collections |
| Readmission/transfer modal | `StudentEnrollmentActionDialog.FormClosing` blocks closing when `workflow.IsExecuting` | Do not incorrectly report this modal as unprotected; separately verify any future direct page route |
| Confirm Return modal | `ConfirmReturnAdmissionDialog.FormClosing` checks `workflow.IsConfirmingReturn` | Already protected for its known modal caller |
| Staff employment modal | `StaffEmploymentDialog.FormClosing` checks `jobs.IsBusy` and its dirty state | Established pattern can be reused |
| Student Profile modal jumps | Their `FormClosing` checks child `IBusyPage` and `IUnsavedChanges` | Contrast proving FQA-01's missing guard |
| Student profile reads | `LatestReadScope` cancels obsolete A→B reads and checks identity before rendering | Don't generalize FQA-05 to already-guarded profile results |
| Duplicate candidate resolution | Snapshots `ReviewId` values before the batch; reports partial row failures | FQA-09 is about **concurrent commands and lifecycle**, not accidental mutation of the wrong selected pair within one call |
| Payment correction confirmation | Explicit student, payment, amount, reason and date, plus server-side eligibility | FQA-02 is a pre-confirmation UX hazard, not proof of unnoticed wrong-payment posting |
| Posting reversal preview | Uses loaded month, filter signatures, blocked collected charges, result counts | Keep all these safeguards when adding page-wide busy state |
| Quick Registration new saves | Durable exact-request journal and server marker, schema version 4 | Not included as a new duplicate-save defect without contrary evidence |

## E. Regression suite Codex should add

**Run against isolated Windows/SQL fixtures only. Never run write/failure-injection tests on the school's live database.** Build an automation harness around real WinForms windows where practical, then complement with deterministic injectable SQL delays/failures:

| Scenario | Coverage IDs | Pass condition |
|---|---|---|
| Dirty admission review → X/Escape | 01, 15 | Confirm discard; Stay keeps all unsaved fields |
| Confirm Admission / Placement / Verify Legacy → close or navigate mid-await | 01, 15 | No leave while server outcome unresolved |
| Posting Reversal → confirm → switch page, change scope, second preview | 04 | One exact write and accurate refreshed result |
| Daily Closing → submit/approve → discard prompt/navigation/exit | 07 | No confirmable leave during write; draft-only changes still prompt normally |
| Duplicate batch → Confirm then Dismiss/scan/change selection | 09, 10 | Single serialized batch, stable selected IDs, no conflicting status writes |
| Record Checks → edit only → navigate / switch tabs / sign out | 11, 14 | Unsaved draft protected; closing an active correction refused |
| Family member remove → navigate before reply | 08 | No lost feedback or stranded page while SQL save in flight |
| Payment A (amount/reason filled) → B → A | 02 | Different payment never silently inherits previous command details |
| Payment A detail-read delayed → B selected | 05 | Stale detail result ignored or original selection explicitly locked |
| Adjustment succeeds but reload fails | 13 | Clearly states committed operation; does not invite immediate retry |
| Financial adjustment commit → response lost → restart | 06 | Reconcile exact SQL state; safe retry policy demonstrated by DB test |
| Annual receipt A/year X → B/year Y before response | 12 | No old-context preview masquerading as current selection |
| Profile shortcut with no student | 03 | Intentional generic navigation or unambiguous “select student” explanation |
| Denied SQL permission; cancellation; page disposed | All relevant | No unintended role bypass; no stale UI continuation or unhandled async exceptions |

For each case, assert **visible button enabled/disabled/hidden**, **actual row/student identity**, **form values**, **DB write count/outcome**, **status text**, and **reopening behavior**. A unit assertion that a handler was called is not a substitute for the Windows interaction.

## F. Recommended implementation order

1. **Create one common, trustworthy write-busy/dirty-state contract:** fix `RunBusyAsync` visibility (FQA-15), modal leave guard (FQA-01), and parent-container delegation (FQA-11/14). Ensure changing tabs **does not hide an active child write**.
2. **Protect financial write lifetimes:** FQA-07, FQA-04, FQA-13; verify FQA-06 on real disposable SQL and implement outcome recovery only if needed.
3. **Serialize student maintenance batches:** FQA-09/10 and family changes FQA-08; retain row-wise result accounting and permission checks.
4. **Repair selection-context UX:** FQA-02, FQA-05, FQA-12; make profile shortcuts FQA-03 follow an explicit navigation policy.
5. **Run real Windows interaction and target database acceptance:** scaling, keyboard close/Escape, disabled controls, error banners, row sort/filter, interrupted read/write, restart and session switching.

No DACPAC or database replacement should be used for an update. If a SQL change is truly required after testing FQA-06, deliver a separately approved **numbered SQL migration** applied to the user's existing MDF/LDF-backed database, with a verified backup and ordered upgrade checks.

## G. Review provenance and limitations

- **Read-only code review** of the source linked above at **`3a3fbd6`**; compared several guarded working equivalents before creating findings.
- **Not** all buttons or forms were executed. User's reported live defects are evidence from the operator, but are explicitly **excluded** from the new 15-item count.
- The report records **confirmed source control-flow gaps** separately from **conditional timing** and **database verification** questions. Risks involving server commit/retry are **not asserted as reproduced financial loss**.
- Repository may receive additional Codex commits. Rebase findings against the **new HEAD** before modifications. Fixes should be small, independently testable, and should preserve transaction semantics, role permissions, opening-fee policy, sibling links, admission numbering and SQL migration ordering.
- **Only intended repository mutation:** create this new audit Markdown file. No existing C#/PowerShell/SQL/test/audit files should be changed to publish the report.
