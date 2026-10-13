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

**Twenty-five findings or verification items** across Admission Review, Student Profile, Financial Adjustments, Posting Reversal, Daily Collection Closing, Family maintenance, Duplicate review, Record Checks, Annual Receipts, Additional Fee Posting, Fee Configuration and Student Account Statements. **Fifteen** identify confirmed **source-level control-flow, state, or guard gaps**; **two** are conditional asynchronous UI issues requiring delayed-response fixtures; **two** are verification risks (one SQL uncertain-outcome question and one synchronous performance issue); **one** is a navigation/UX policy decision. The five additional items (FQA-16 through FQA-20) were discovered in the second-pass review below. None is claimed to have been reproduced on the user's live Windows installation.

**Most important pattern:** Disabling an individual `AddBusyAction` button is **not the same** as blocking page departure or other conflicting actions while a database write is running. `MainDashboardForm` checks the **top-level active page** for `IBusyPage` and then `IUnsavedChanges`. Several nested pages and dialogs fail to expose the true operation/draft state. Adding `IBusyPage` to a nested control **alone** is insufficient unless its host forwards that contract.

### Triage register (risk order, not effort order)

| ID | Severity | Classification | Workflow | Concise problem |
|---|---|---|---|---|
| **FQA-21** | **P2/P3** | Confirmed missing dirty guard | Student certificate editors | Editable Character/Leave/College Leave drafts are not protected when switching students or leaving |
| **FQA-22** | **P2** | Confirmed missing busy guard | Staff bank certificate | Creating a certificate does not block shell navigation or all conflicting controls |
| **FQA-23** | **P2/P3** | Confirmed post-save feedback gap | Staff bank certificate | A confirmed Create followed by failed history reload can still show unqualified success |
| **FQA-24** | **P2/P3** | Confirmed modal dirty guard gap | Confirm Return Admission | Edited opening fees can be abandoned via Cancel/X without discard confirmation |
| **FQA-25** | **P2 verification** | Unknown commit outcome, SQL behavior untested | Staff bank certificate | Lost Create response requires safe duplicate/reconciliation testing |
| **FQA-16** | **P2** | Confirmed stale-result acceptance | Additional Fee Posting | Filter A can finish loading after operator changes to filter B, exposing an outdated student set for charging |
| **FQA-17** | **P2** | Confirmed draft-discard gap | Fee Rate Matrix | Switching Campus clears unsaved fee-cell edits without asking |
| **FQA-18** | **P2** | Confirmed missing busy contract | Embedded Fee Rate Matrix | Workspace navigation/tab changes are not blocked while Save Grid is executing |
| **FQA-19** | **P2** | Confirmed failure-state gap | Student Account Statement | Failed regeneration preserves old statement plus enabled Export for unchanged filters |
| **FQA-20** | **P3 verification** | Synchronous source design; impact unmeasured | Student Account Statement | Generating a statement serially queries each student on the UI thread, risking long unresponsive periods |
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
- **Not** all buttons or forms were executed. User's reported live defects are evidence from the operator, but are explicitly **excluded** from the 20-item count.
- The report records **confirmed source control-flow gaps** separately from **conditional timing** and **database verification** questions. Risks involving server commit/retry are **not asserted as reproduced financial loss**.
- Repository may receive additional Codex commits. Rebase findings against the **new HEAD** before modifications. Fixes should be small, independently testable, and should preserve transaction semantics, role permissions, opening-fee policy, sibling links, admission numbering and SQL migration ordering.
- **Only intended repository mutation:** create or update **this** functional-audit Markdown file. No C#/PowerShell/SQL/test files or other audit documents should be changed by this review.


## H. Second-pass deepening — Additional Fee Posting, Fee Configuration, Statements

**Audit addendum:** 13 October 2026. **Code baseline:** `3a3fbd63590cc7dedb3f17f008e84f7be67a9132` (unchanged from the original report); **preceding report commit:** `43a10b3fa829efa3727f3844e2ad17d871d0015d`. Five newly numbered items, FQA-16 through FQA-20. Old FQA-01 through FQA-15 are **retained**, not automatically marked fixed. The reported Quick Registration live-testing observations remain excluded from the new-finding count.

### FQA-16 — Additional Fee Posting accepts an obsolete student-list response [P2, confirmed source gap]

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

### FQA-17 — Fee Rate Matrix campus switch can silently lose unsaved edits [P2, confirmed source gap]

**Evidence:** [`Pages/Finance/Configuration/FeeRateMatrixForm.cs` lines 169–197](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Configuration/FeeRateMatrixForm.cs#L169-L197), [`Pages/Finance/Configuration/FeeRateMatrixForm.cs` lines 220–246](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Configuration/FeeRateMatrixForm.cs#L220-L246), [`Pages/Finance/Configuration/FeeRateMatrixForm.cs` lines 426–439](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Configuration/FeeRateMatrixForm.cs#L426-L439), [`Pages/Finance/Configuration/FeeRateMatrixForm.cs` lines 639–662](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Configuration/FeeRateMatrixForm.cs#L639-L662).

**Operator sequence:** Select a campus → edit a class/fee cell, without **Save Grid** → change Campus → observe the editor reset/rebuilt from another campus. There is no confirm/discard/stay decision attached to the **Campus.SelectedIndexChanged** event.

**Cross-checks:** This form already has `HasUnsavedChanges => IsDirty()` and a `FormClosing` discard prompt; its `OnConfigurationsChanged` handler expressly refuses to reload while dirty. However, the Campus handler immediately calls `LoadGridAsync`. That method **first clears the grid and sets `loadedSnapshot = ""` and clears stored cell values**, then issues SQL queries. It does not check `IsDirty` or ask to discard before wiping the draft. Even a failed new-campus query can leave the original edits unavailable.

**Expected:** Switching campus with edited fee values requires a Save / Discard / Stay decision, not silent loss. The old campus should remain selected if the user chooses Stay.

**Proposed fix:** Reuse `IsDirty` / the existing modal confirmation concept for an *internal campus-selection change*, preserving prior ID and avoiding selection-change recursion during rollback. Optionally allow an explicit Save Grid followed by campus switch only once the save is confirmed; don't auto-submit financial edits. Do not disturb the clean-campus fast path and existing grid-version latest-load checks.

**Tests:** Edit amount/frequency/remarks → switch campus → Stay, Discard and Save decisions; cancel picker; empty campus; failed destination load; rapid campus A→B→A; exact row focus and fee values. Include embedded-tab mode and standalone dialog.

### FQA-18 — Embedded Fee Rate Matrix does not advertise Save Grid as page-busy [P2, confirmed source gap]

**Evidence:** [`Pages/Finance/Configuration/FeeRateMatrixForm.cs` lines 1–9](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Configuration/FeeRateMatrixForm.cs#L1-L9), [`Pages/Finance/Configuration/FeeRateMatrixForm.cs` lines 187–232](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Configuration/FeeRateMatrixForm.cs#L187-L232), [`Pages/Finance/Configuration/FeeRateMatrixForm.cs` lines 150–172](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Configuration/FeeRateMatrixForm.cs#L150-L172), [`Pages/Finance/Configuration/FeeConfigurationWorkspace.cs` lines 1–14](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Finance/Configuration/FeeConfigurationWorkspace.cs#L1-L14), [`WorkspacePage.cs`](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Core/WorkspacePage.cs), [`MainDashboardForm.cs` lines 1637–1655](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/MainDashboardForm.cs#L1637-L1655).

**Operator sequence:** Open **Fee Configuration → Fee Rate Matrix** (embedded as a tab) → edit one or more fee cells → click **Save Grid** → while SQL write is delayed, switch to **Fee Types** / **Copy Fees**, or navigate elsewhere/exit. The form holds `savingGrid` and disables its own campus selector/grid, but does **not** implement `IBusyPage`. Its existing `FormClosing` protection applies to the independent Form close event and **does not supply an `IBusyPage` interface to the host when embedded**.

**Cross-checks:** The parent `WorkspacePage` **already** implements `IBusyPage` and `IUnsavedChanges`, scans *all loaded tabs*, and cancels tab switching on a busy child. Thus the parent infrastructure is **not** missing. The exact gap is the embedded `FeeRateMatrixForm`, which only implements `IUnsavedChanges`: while saving, the parent cannot detect that write via `BusyChild`. A dirty-change confirmation is not a substitute for an unconditional write block.

**Fix:** Implement `IBusyPage` on `FeeRateMatrixForm` mapping `IsBusy => savingGrid` and a descriptive `BusyMessage`; preserve the existing unsaved/dirty contract. Ensure it covers the entire async save **including success/reload**, and the parent tab+dashboard automatically sees it. Test modal X/Escape, embedded tab switch, sidebar, sign-out and closing while the SQL server holds the save. Keep read-only tabs navigable when no write is running.

**Important distinction:** FQA-17 concerns a **draft before save** and an internal campus switch. FQA-18 concerns a **submitted database write in flight** and leaving an embedded tab/workspace. They require different responses.

### FQA-19 — Failed Student Account Statement regeneration leaves an old Export enabled [P2, confirmed source gap]

**Evidence:** [`Pages/Reports/Finance/StudentAccountStatementPage.cs` lines 665–691](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Reports/Finance/StudentAccountStatementPage.cs#L665-L691), [`Pages/Reports/Finance/StudentAccountStatementPage.cs` lines 1230–1315](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Reports/Finance/StudentAccountStatementPage.cs#L1230-L1315), [`Pages/Reports/Finance/StudentAccountStatementPage.cs` lines 169–194](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Reports/Finance/StudentAccountStatementPage.cs#L169-L194).

**Operator sequence:** Select one or more students/dates → Generate Statement successfully → some new payment or charge is recorded elsewhere → return to the **same filters and selection** → click **Generate Statement** again → inject a SQL failure while fetching new data. The page catches the error through `Fail(ex)`. **But** it does not clear or mark the previous bound statement as stale and does not clear `_appliedFilterSignature` or disable `exportButton` at the start/failure of regeneration.

**Cross-checks:** `OnFiltersChanged` correctly disables Export when the *controls change*. That does **not** protect this scenario because the controls have not changed: the old signature still matches. `LoadStatement()` builds its new table off-screen; when a query fails before `Bind(combined)`, the old grid remains visible. Therefore Export may still operate on the old, potentially outdated report, even though the latest generate attempt failed. This is not proof the old amounts were wrong *when originally generated*.

**Expected:** If a fresh Generate Statement attempt fails, the UI must not silently leave the old result exportable as a currently refreshed statement.

**Fix:** On a new Generate attempt, mark the existing result **not current** before database calls and disable Export. On failure, either clear it or retain it explicitly labeled **previous snapshot / not refreshed** with Export disabled until a successful regeneration. Distinguish “latest query failed” from “no data” in status. Restore `_appliedFilterSignature` and actions only after all selected students' results, totals and grid binding succeed. Preserve user selection and date inputs after a failure.

**Tests:** Generate A → insert payment in disposable SQL → fail subsequent Generate A → Export disabled and stale warning; recover and Generate → new totals/export match; change dates mid-flow; fail on second of several student queries; zero transaction rows; missing opening-balance row.

### FQA-20 — Statement generation serially blocks the WinForms UI during SQL reads [P3, performance-verification item]

**Evidence:** [`Pages/Reports/Finance/StudentAccountStatementPage.cs` lines 176–190](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Reports/Finance/StudentAccountStatementPage.cs#L176-L190), [`Pages/Reports/Finance/StudentAccountStatementPage.cs` lines 1227–1312](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Reports/Finance/StudentAccountStatementPage.cs#L1227-L1312).

**Source design:** The **Generate Statement** button synchronously invokes `LoadStatement()` on the UI thread. Inside it, `foreach (int admissionId in admissionIds)` repeatedly calls synchronous `LoadStatementData` → `DatabaseHelper.QueryUserSet("GetFeeStatementTransactions")` before building the report. With a multi-student/family selection this is **one blocking SQL round trip per selected student** and the window cannot process normal paint/cancel/input events during those waits. This is a source-confirmed synchronous execution path, **not** a measured hang/crash on the user's PC.

**Impact to verify:** A single student on local SQL may feel normal; 50–500 selected students on a slow Windows computer or delayed SQL instance could make the application appear frozen. A failed query later in the loop compounds FQA-19's feedback problem.

**Proposed approach:** Benchmark first (1, 10, 50, 200 students; warm/cold DB). Move the existing verified result assembly off the UI thread with a cancellable/latest-load coordinator or, preferably, a bulk SQL retrieval interface if it can preserve financial semantics and balances. Capture immutable selected IDs/dates on the UI thread; never read WinForms controls inside background tasks. Bind all results atomically and preserve opening-balance verification and report sums. Do not schedule simultaneous unbounded queries or assume parallel SQL reads improve performance. Report a loading state and safe Cancel/Leave behavior while reads run, distinct from `IBusyPage` financial writes.

**Acceptance:** Window remains responsive at 125–150% DPI under injected SQL delay; only the latest requested result binds, totals match the synchronous version exactly for all sample students, error and no-data states are clear, Export disabled while result is stale, and no further SQL activity continues after page disposal/cancellation where supported.

## I. Second-pass regression matrix, ruled-out candidates, and new priorities

| Test case (isolated Windows / disposable SQL) | Findings | Required outcome |
|---|---|---|
| Additional Fee Load Campus A delayed → switch to B → click Select All → Post | 16 | No stale A list binds under B; no A enrollment IDs posted under B |
| Edit Fee Rate Matrix cell → campus change → Stay/Discard | 17 | Draft retained on Stay, discarded only after explicit decision |
| Save Grid delayed → switch tabs/sidebar/exit | 18 | Parent host blocks all departures during active save and shows committed/unknown outcome |
| Generate statement successfully → fail regeneration on same filters → Export | 19 | Existing grid marked stale or cleared; Export disabled until successful refresh |
| Generate statement for many students with SQL delay | 20 | UI paints/responds, controls held appropriately; values and balances unchanged |
| Test previous 15 FQA findings again after shared busy guards are changed | 01–15 | No regressions to protected dialogs, preview filters, payment identity or financial journaling |

**Cross-checks deliberately excluded from defects:**

- `WorkspacePage` already forwards `IUnsavedChanges` and `IBusyPage` from **all loaded child tabs**, and blocks switching tabs on a busy child. FQA-18 must be fixed **at the fee-matrix child**, not by duplicating host logic.
- `StudentPurchasesPage` uses `StudentPicker.SelectionChangeGuard`, and `StudentPicker.OnTextEdited` invokes the guard before clearing a committed selection. The tempting claim that simply typing a different student's name silently discards a purchase draft was **rejected**.
- `ApplyFeeConfigurationPage` only has a known production caller from `FeeRateMatrixForm` passing `matrixCampusId` and scopes; that route **disables its campus selector**. A generic “change campus mid-apply” bug is therefore **not claimed reachable** in the currently used dialog route.
- In `ConfirmReturnAdmissionDialog` the currently used `ConfirmReturnAdmissionPage(compactDialog:true)` disables/hides the student picker. The suggestion that the clerk can switch students *inside this modal* and lose edits was **rejected as a current-route defect**. A future standalone page route should still protect edited opening-fee drafts.
- `BulkPromotionPage` already disables its selection/scope controls for candidate loads and promotions, revalidates source IDs, and reports server counts. Do not report a generic filter-race bug there simply because the other fee-posting page has one.

**Updated implementation order (risk-first):** First FQA-16 (wrong student scope can reach financial charge request); FQA-18 and FQA-17 (keep Fee Rate Matrix operations/drafts safe); FQA-19 (financial report freshness); then the original FQA-15/07/04/01/09/11 and remaining original findings; finally benchmark FQA-20 and improve only where justified. Coordinate shared busy/dirty-state changes with the existing `WorkspacePage` aggregator instead of introducing parallel mechanisms.

**Execution/provenance:** These are additional static source findings at unchanged code HEAD `3a3fbd6`. Recheck against Codex's current pushed commit before fixing. No native UI, financial write, SQL fault injection, deployment, or performance benchmark was executed by this second pass. Updating this audit is documentation only.


## J. Third-pass expansion — certificates, staff, setup, security, and cross-module coverage

**Pass date:** 14 October 2026 (Pakistan local date). **Application source baseline:** `3a3fbd63590cc7dedb3f17f008e84f7be67a9132`, still unchanged when reviewed. **Review scope:** screen-by-screen source inventory plus targeted handler/guard verification; no live Windows UI or production database access. Five additions numbered FQA-21 to FQA-25. Previous FQA-01–20 retained.

### FQA-21 — Certificate editors do not protect unsaved new-certificate drafts [P2/P3, confirmed source guard gap]

**Evidence:** [Pages/Students/Certificates/CharacterCertificatePage.cs L1-103](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Certificates/CharacterCertificatePage.cs#L1-L103), [Pages/Students/Certificates/CharacterCertificatePage.cs L173-205](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Certificates/CharacterCertificatePage.cs#L173-L205), [Pages/Students/Certificates/CharacterCertificatePage.cs L1070-1094](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Certificates/CharacterCertificatePage.cs#L1070-L1094), [Pages/Students/Certificates/LeaveCertificatePage.cs L1-100](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Certificates/LeaveCertificatePage.cs#L1-L100), [Pages/Students/Certificates/LeaveCertificatePage.cs L1294-1345](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Certificates/LeaveCertificatePage.cs#L1294-L1345), [Pages/Students/Certificates/CollegeLeaveCertificatePage.cs L1-103](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Certificates/CollegeLeaveCertificatePage.cs#L1-L103). **Working comparison:** [Pages/Students/Certificates/RegularStudentCertificatePage.cs L1-18](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Certificates/RegularStudentCertificatePage.cs#L1-L18).

**Operator reproduction:** In Character Certificate, select a student, enter BISE roll number/remarks/date without clicking Create, and either select another student, use Next Student, or navigate to another dashboard page. Similar sequences exist for Leave and College Leave with date, promotion/academic, attendance, and remarks. The handlers clear or replace the inputs without a shared `IUnsavedChanges` contract, and the pages inherit plain `V2Page`. In Character, `StudentChanged` clears certificate fields immediately. This is loss of **unsaved user input**, not loss of a committed certificate.

**Caveat:** These forms sometimes intentionally reset fields on a new student. The missing behavior is a decision prompt *when the user has actually edited a draft*. A clean form must still switch directly.

**Recommended fix:** Capture a baseline after loading/clearing a student, compare the true editable fields, implement `IUnsavedChanges`, and attach `StudentPicker.SelectionChangeGuard` plus explicit Next Student / refresh confirmation. Take care to distinguish fields deliberately not persisted (character-certificate exam-type print wording) and existing saved issuance from a new unsaved draft. Add change A→B→cancel, typed-search clear, Next Student, X/navigation, and after-success baseline reset tests.

### FQA-22 — Bank Account Opening Certificate create does not block page departure [P2, confirmed source gap]

**Evidence:** [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L1-13](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L1-L13), [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L68-112](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L68-L112), [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L444-517](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L444-L517).

**Operator reproduction:** Start Create Certificate with a delayed `CreateBankAccountOpeningCertificate` database operation, then switch to another page or trigger New / Clear / Refresh while Create awaits. The clicked Create button and the Staff picker/Grid are disabled temporarily, but the page implements only `IUnsavedChanges`, **not `IBusyPage`; other buttons are not comprehensively locked**. The dashboard's whole-page busy guard cannot see this submitted write, and asynchronous continuation may run after the page is disposed.

**Recommended fix:** Track a page-level `creating` flag; implement `IBusyPage` with clear busy text; guard all context-dependent actions and leave gestures, and preserve the existing staff/grid button restoration. Capture submitted IDs and values before await; don't treat a submitted write as cancellable. Regression: delayed Create → sidebar, sign-out, window X, New and Refresh; verify exactly one SQL submission and no continuation on a disposed control.

### FQA-23 — Bank certificate save feedback may conceal failed history reload [P2/P3, confirmed conditional feedback gap]

**Evidence:** [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L257-308](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L257-L308), [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L444-530](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L444-L530).

**Sequence:** The Create stored procedure returns successfully. The handler sets `successMessage` and then calls `LoadCertificates(staffId)`. That loader catches its own SQL exceptions, clears its grid and calls `Fail(ex)` without returning a load-success value. The enclosing Create method still runs its `finally` and displays `Info(successMessage)`. The operator sees a success message over a failed/unrefreshed certificate list, with no explicit distinction between **committed write** and **failed refresh**.

**Recommended fix:** Return an explicit loaded/failed result from `LoadCertificates`; show “Certificate created, but history could not be refreshed; reload before acting further.” For the UI, don't infer an ID or issue number from empty history and don't retry Create just because its *read* failed. Acceptance: successful Create + failed Get Records, successful Create + empty history, failed Create, delayed staff switch.

### FQA-24 — Confirm Return Admission opening-fee edits have no modal discard protection [P2/P3, confirmed source guard gap]

**Evidence:** [Pages/Students/Enrollment/ConfirmReturnAdmissionPage.cs L1-64](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Enrollment/ConfirmReturnAdmissionPage.cs#L1-L64), [Pages/Students/Enrollment/ConfirmReturnAdmissionPage.cs L70-115](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Enrollment/ConfirmReturnAdmissionPage.cs#L70-L115), [Pages/Students/Enrollment/ConfirmReturnAdmissionPage.cs L188-214](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Enrollment/ConfirmReturnAdmissionPage.cs#L188-L214), [Pages/Students/Enrollment/ConfirmReturnAdmissionDialog.cs L1-26](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Students/Enrollment/ConfirmReturnAdmissionDialog.cs#L1-L26).

**Operator reproduction:** Open the current compact **Confirm Return** dialog for a promoted student, change the editable Amount or Discount Amount cells, then press Cancel/Escape or X. The modal `FormClosing` only refuses to close while `workflow.IsConfirmingReturn`. There is **no unsaved fee-draft guard** or confirmation. The fee adjustments are local until Confirm, so this does not roll back a database write, but an operator can lose substantial input without warning.

**Cross-check:** The compact dialog **disables its student picker**, so a claimed switch-student failure inside this dialog is **not** a currently reachable finding. Its active SQL confirmation is already blocked on close. This finding is only about the editable *pre-submission draft*.

**Recommended fix:** Snapshot the original fee choices and return date/transport state after load, detect changed values, and prompt on Cancel/X only when dirty. Clear the baseline following successful Confirm. Test edited amount, edited discount, transport/date changes, clean Cancel, and active Confirm (must remain unconditionally blocked).

### FQA-25 — Lost Create acknowledgment for bank certificates requires SQL reconciliation testing [P2 verification; not a proved duplicate bug]

**Evidence:** [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L68-105](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L68-L105), [Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs L444-518](https://github.com/farhan2166-creator/FMSC-Database/blob/3a3fbd63590cc7dedb3f17f008e84f7be67a9132/Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L444-L518).

The Create path submits one asynchronous `CreateBankAccountOpeningCertificate` call, and on any exception shows a general error. There is no durable client-generated request identity or unknown-outcome resolution on this path. **If** a SQL commit succeeds but its response is lost, the operator may press Create again. A source comment suggests this procedure has no duplicate guard, but **this review did not independently inspect its stored-procedure body or test its DB constraints**, so a duplicate-certificate conclusion remains **unconfirmed**.

**Verification:** In a disposable SQL Server, deliberately drop the reply after commit, inspect created certificate IDs, retry, and inspect uniqueness constraints. If the server is not idempotent, design a request-token contract with safe outcome lookup. Do not insert a new SQL migration without explicitly reviewing existing schema and upgrade policy. On unknown outcomes, first prompt to check existing certificates, rather than blind retry.

## K. Screen-by-screen coverage ledger: source trace vs native UI test

This ledger measures *review scope*, not proof of correct functionality. `T` = targeted handlers and safeguards traced; `I` = file/workflow inventoried, representative patterns inspected; `P` = priority for a future deep scenario pass; `W` = native Windows UI behavior still untested. No group below has been certified as exhaustively exercised.

| Module and actual operator workflow | Source-review depth | Existing/new findings or disposition | Native test status |
|---|---|---|---|
| Quick Registration, pending list, parent links, review/confirm, admission | T | Operator-reported issues excluded from finding count; FQA-01, FQA-15; previous follow-up audit | W |
| Student profiles, sibling/family linking | T | FQA-03 (policy), FQA-08 | W |
| Record issues, historical correction, duplicate scan and batch review | T | FQA-09/10/11/14 | W |
| Transfers, readmission, promotion, return admissions | T | Bulk Promotion verified existing busy/loading protection; FQA-24; SQL life-cycle acceptance P | W |
| Standard and additional fee posting, reversals, student purchases | T | FQA-04/16; Student Purchases change guard verified | W |
| Collection, daily closing, corrections/refunds/reversals | T | FQA-02/05/06/07/13; collection journal exists | W |
| Fee configuration matrix, rate copy/bulk, applying saved changes | T | FQA-17/18; shared WorkspacePage busy aggregation verified | W |
| Student statements, annual receipts, class sheets, canonical finance reports | T | FQA-12/19/20; Class Sheets guards roster revision and selected scope | W |
| Student Character, Leave, College Leave, Regular and other certificates | T / I | FQA-21; Regular Student page exposes dirty/busy state; further issue/collect/print scenarios P | W |
| Staff profile, employment, employment dialogs | T | `MasterPage` busy/dirty guards, staff context guard, and employment dialog close checks verified | W |
| Staff bank certificate create/edit/reprint | T | FQA-22/23/25 | W |
| Campus/Class/Section assignment, years, campuses, subjects, school profile | T / I | `MasterPage` guards standard CRUD; campus assignment retains matrix draft on picker changes; special setup multi-edit tests P | W |
| Users/Roles administration | I / T | Redesigned `AccessEditorPage` has dirty, busy, generation, and uncertain-save contracts; permission/version acceptance still P | W |
| Audit Logs, backup/restore, migration cleanup | I / T | Backup/Restore exposes `IBusyPage`; actual restore/relink and audit pruning still P | W |
| Login, password/username change, permissions | I | Authentication paths inventoried; session switching, role revocation, mid-operation SQL faults still P | W |
| Print/export/RDLC and theme/scale/narrow screens | I | Export durability earlier fixed in code; physical printing and DPI/keyboard test P | W |

**Do not interpret “T” as an assertion that every button and every combination was tested.** In a repository with many WinForms pages, complete interaction proof requires running the assembled program under the intended Windows scaling, SQL Server version, permissions, and workflows.

## L. Additional failure-injection and usability scenarios

1. **Certificate draft:** edit Character / Leave / College Leave fields, switch selected student or click Next Student; verify Stay preserves the exact editable values and Save/Discard intent is explicit.
2. **Bank create:** create certificate, induce SQL reply loss and separate history-reload failure; verify the status never conflates an unknown Create with a confirmed Create + failed refresh. Reopen staff history before retrying.
3. **Return admission:** edit Amount/Discount/date/transport, press X / Cancel; verify dirty confirmation; test committing concurrently with modal close (already busy-blocked).
4. **Setup:** edit Campus Class/Section assignments, change campus before Apply; confirm existing guard preserves drafts. Test clearing 100 cells, permission denied, and clicking Apply twice.
5. **Staff:** edit staff details, select another staff member via quick picker, then open Employment; verify combined dirty-state and permission checks. Existing staff employment modal already blocks active writes.
6. **Reports:** modify a student's fees between Generate and regeneration; test FQA-19, and print under changed scope after slow load; profile first-use SQL latency.
7. **Security/admin:** role change while page open; concurrent administrator attempts; permission denial after submit; uncertain outcome state and forced password reset. Validate against current schema and accounts only in disposable DB.
8. **Backup/restore:** use fake history paths, denied backup directory, interrupted restore and post-restore compatibility failure; retain safety backup and do not experiment on real school data.

## M. Priorities and correction strategy after third pass

**High-value sequence:** FQA-16 (stale financial target set), FQA-18/17 (fee matrix write/draft), FQA-07/04/15 (write-busy gaps), FQA-22 (bank certificate in-flight write), FQA-19/23/13 (save versus refresh messages), FQA-21/24 (draft-loss), then other previously documented issues. FQA-25 and FQA-06 require disposable-SQL reconciliation before specifying schema changes. FQA-20 requires measured performance data before a refactor.

**Generalized fixes:** Reuse the existing `IBusyPage` and `IUnsavedChanges` contracts and modal closing helper; don't invent new global state where a child host already delegates correctly. Make asynchronous read responses generation- and identity-scoped. Separate confirmed committed writes from refresh failures. Verify both positive and negative paths and record native-Windows test results explicitly.

**Original owner reports:** Quick Registration screenshot issues stay distinct and are not counted as assistant discoveries; when Codex pushes changes, re-check those live-reported scenarios and this report against the new application commit.

**Documentation-only mutation:** This addendum updates the existing functional audit, not the application. No SQL migrations, C# source files, or tests were changed for this pass.
