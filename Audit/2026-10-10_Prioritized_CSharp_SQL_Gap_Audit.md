# FMSC_SMS — Prioritized C# / SQL Gap Audit

**Prepared:** 10 October 2026  
**Repository:** farhan2166-creator/FMSC-Database — main  
**Nature:** Read-only source audit findings and remediation planning  
**State:** OPEN / NOT FIXED / NOT REPRODUCED ON LIVE DATABASE

## Purpose and interpretation

The owner has live-tested most normal flows successfully. This report focuses only on **nine identified gaps and edge cases** that can escape happy-path tests; it does not imply that the application as a whole is defective. The evidence consists of the current C# source, the SQL reference definitions, and existing test documentation. The SQL definitions are a **reference snapshot**, not change scripts. Future SQL changes, if approved, should be delivered through the project's established migration/versioning process.

**Do not change C#, SQL, data, migrations or user workflows on the strength of this report alone.** First confirm each intended business rule and reproduce applicable scenarios using a disposable database copy. No live runtime, full Windows build, multi-user stress test, or all-file certification was performed during the underlying review.

## Priority summary

| ID | Priority | Finding | Complete C#-only fix? | Business risk / decision |
| --- | --- | --- | --- | --- |
| **H-01** | **High** | Bulk posting reversal includes Annual/Manual charges, not only Monthly | **No** if monthly-only is required | Unintended reversal of receivables |
| **H-02** | **High** | Selected academic year does not restrict the fee-posting enrollment year | **No** if strict year isolation is required | Cross-year billing / report disagreement |
| **H-03** | **High** | Add Missing Record passes plain-text reason to JSON-only audit procedure | **No, not safely** | Documented failure of legacy enrollment creation |
| **H-04** | **High** | Campus/class/section mapping can be deactivated while referenced by active students | **No** for enforcement; C# can mitigate | Hidden/unavailable placements |
| **M-01** | **Medium** | Billing Status calls a student billed after any one monthly charge | **Partly** | Partially billed students counted as fully billed |
| **M-02** | **Medium** | Reversible month list and actual reversal use different enrollment-status eligibility | **Partly** | Month offered but no reversible charges |
| **M-03** | **Medium** | Multi-scope Apply Fee Rates can commit some scopes before an exceptional error | **Yes** for reporting/recovery | Operator cannot tell exactly what was applied |
| **L-01** | **Low** | Recurring-charge projection can exceed SQL recursive CTE limit | **No** for unrestricted correctness | Very old enrollments may fail read-only reports |
| **L-02** | **Low** | Bulk Promotion shows the same result before and after list refresh | **Yes** | Duplicate success/warning dialog |

**How to read "C# only":**
- **Yes:** Correctly solvable by modifying only the C# client while retaining all existing database contracts.
- **Partly:** UI checks, warnings, filtering or label corrections can reduce confusion; they **cannot enforce a database-level invariant** for every caller.
- **No:** A definitive business-rule fix needs an SQL contract or query change, with coordinated C# changes if necessary.

---

# HIGH PRIORITY

## H-01 — Posting reversal can cancel Annual and Manual charge kinds

**Classification:** Code-confirmed SQL scope; business-policy-dependent defect.  
**Key question:** Is the “Reverse a Posting” screen intended to reverse **only recurring monthly postings**, or **every kind of uncollected charge** within the selected billing month?

### Evidence and affected files
- [Pages/Finance/Billing/FeePostingReversalPage.cs](../Pages/Finance/Billing/FeePostingReversalPage.cs): describes reversal of a monthly posting and an uncollected charge preview.
- [dbo.GetReversibleMonths.sql](../Database/Reference/Definitions/dbo.GetReversibleMonths.sql): groups all non-cancelled billing charges by BillingMonth; no ChargeKind filter.
- [dbo.PreviewPostingReversal.sql](../Database/Reference/Definitions/dbo.PreviewPostingReversal.sql): previews all non-cancelled charges for month/scope with an Active enrollment; no ChargeKind filter.
- [dbo.ReverseMonthlyFees.sql](../Database/Reference/Definitions/dbo.ReverseMonthlyFees.sql): cancels charges by billing month, scope, enrollment state, and net allocated payments; no ChargeKind filter.
- [dbo.PostEnrollmentOpeningCharges.sql](../Database/Reference/Definitions/dbo.PostEnrollmentOpeningCharges.sql) and [dbo.PostAdditionalFee.sql](../Database/Reference/Definitions/dbo.PostAdditionalFee.sql): provide Annual and Manual billing-charge creation paths.

### Example
On 1 April, an Active student has an **Rs. 8,000 Annual admission fee** and **Rs. 2,000 Monthly tuition**, neither paid. An operator opens “Reverse a Posting” intending to undo a wrong April monthly fee posting. The SQL predicates admit both charge kinds. If both match the campus/class/section scope, both can be cancelled, even though the annual fee was not part of monthly posting.

### Impact and implications
- The annual/manual receivable may disappear from outstanding balances and become a cancelled charge in history.
- Reposting ordinary monthly fees may **not** recreate Annual/Manual charges; restoring those requires the correct underlying workflow.
- Payment-protection trigger logic and Active-enrollment conditions still restrict the operation; this finding does **not** claim collected or closed-enrollment charges are always cancellable.
- The preview currently does not clearly distinguish Monthly/Annual/Manual to the operator.

### Remediation options
1. **Recommended if monthly-only is intended:** Add ChargeKind = Monthly consistently to the month list, impact preview, final reversal SQL, and subsequent still-posted result. Show ChargeKind in the UI if useful. Maintain all existing paid/closed protections, audit logging and concurrency behavior. Make the SQL change through a reviewed migration.
2. **If every uncollected charge kind is intentionally in scope:** Keep the SQL rules but make the C# page name and confirmation explicitly say all charge kinds, show kind plus counts and monetary amounts for each kind. This is a **C#-only clarity fix**, but it changes the user's understanding, not the underlying scope.
3. If a single exceptional Annual/Manual reversal is needed, use/review an existing **single-charge** correction/reversal flow rather than broadening the bulk action.

**Can this be fixed solely in C#?** **No** for a Monthly-only guarantee. Excluding other kinds from C# previews does not restrict what ReverseMonthlyFees may update after the call.

### Safe verification and acceptance
On a disposable copy, make three unpaid charges in the same billing month with Monthly, Annual and Manual kinds. Run discovery, preview, final reversal, and read resulting statuses. Repeat with partially paid charges, closed enrollments, and a previously cancelled monthly charge. Success: only the **approved kinds** are included; amount/count previews and audit trail agree; monthly repost works without altering unrelated charges.

**Change scope:** 3+ SQL procedures, one C# page, representative regression fixtures, report/ledger cross-check. **Regression sensitivity:** High (money, historical charges).

---

## H-02 — Academic year chosen in fee posting does not bound enrollment year

**Classification:** Code-confirmed discrepancy; policy must be decided first.  
**Key question:** Does the Academic Year selector mean **the enrollment belongs to that year**, or **which months the operator wishes to bill**?

### Evidence and affected files
- [Pages/Finance/Billing/FeePostingPage.cs](../Pages/Finance/Billing/FeePostingPage.cs): Academic Year and Billing Month selectors with explicit selected-row posting.
- [dbo.GetPostableMonths.sql](../Database/Reference/Definitions/dbo.GetPostableMonths.sql): uses AcademicYearId to derive start/end months, but does not require Enrollment.AcademicYearId = the selected year.
- [dbo.PreviewMonthlyFees.sql](../Database/Reference/Definitions/dbo.PreviewMonthlyFees.sql): no AcademicYearId parameter.
- [dbo.PostSelectedMonthlyFees.sql](../Database/Reference/Definitions/dbo.PostSelectedMonthlyFees.sql): no AcademicYearId parameter; revalidates Active/BillingReady/fee-effective-date rules.
- [dbo.GetBillingCycleStatus.sql](../Database/Reference/Definitions/dbo.GetBillingCycleStatus.sql): **does** restrict enrollments by AcademicYearId, producing a possible difference between posting and status report scopes.

### Example
Student A remains Active in a **2025–26 enrollment** because promotion/readmission has not yet been completed. Staff choose **2026–27** as Academic Year and **May 2026** to post fees. The old Active enrollment remains potentially eligible, provided BillingStartDate and fee periods permit it. The 2026–27 Billing Status report may omit that enrollment because it belongs to 2025–26.

### Impact and implications
- A payment obligation can be attached to an older-year enrollment while the UI suggests a newer billing year.
- Posting vs Billing Status can disagree, confusing reconciliation.
- Blocking this may also stop a currently intentional carry-over workflow for unpromoted students. This is a **business decision**, not an automatic recommendation to enforce new-year enrollment.
- **Global campus/class fee rates must remain independent of academic years.** This finding concerns enrollment/billing eligibility only, **not** academic-year-specific rates.

### Remediation options
1. **If enrollment-year matching is required:** Make month discovery, preview, and posting all validate the same AcademicYearId, including the **final SQL write**, and pass that reviewed parameter from C#. Keep idempotency, fee-effective periods, and return/promotion controls intact.
2. **If Academic Year is intentionally only a billing-month calendar:** Preserve SQL and change selector/help/confirmation wording so that this distinction is explicit. Consider a C# warning for preview rows whose enrollment year differs.
3. Regardless of chosen policy, align Billing Status scope and naming to avoid comparing unlike populations.

**Can this be fixed solely in C#?** **No** for strict year isolation. UI filtering and TVP selection can reduce accidental posting but cannot enforce the rule for other SQL callers or prevent server-side population drift.

### Safe verification and acceptance
Create test students with Active/BillingReady enrollments in adjacent academic years; use a billing month within the selected year, compare month list, detail preview, final posting result and Billing Status. Include pending promotion/readmission, historical billing and partially posted fees. Success: all four views implement the **same agreed scope**.

**Change scope:** Coordinated 3 SQL posting modules, C# selector/parameters and reporting checks. **Regression sensitivity:** High (billing and promotion).

---

## H-03 — Add Missing Record violates audit logging's JSON contract

**Classification:** **Documented known failure**, confirmed by SQL implementation and repository test notes.

### Evidence and affected files
- [dbo.CorrectEnrollmentStatus.sql](../Database/Reference/Definitions/dbo.CorrectEnrollmentStatus.sql): in the **create missing enrollment** branch, passes @Reason (ordinary text) as @NewValues to dbo.WriteAuditLog.
- [dbo.WriteAuditLog.sql](../Database/Reference/Definitions/dbo.WriteAuditLog.sql): requires @NewValues to be JSON; throws SQL **59941** if not.
- [Tests/StudentCorrections/README.md](../Tests/StudentCorrections/README.md): explicitly identifies this blocker; states successful missing-record creation and returned ID are unverified pending repair.
- [Pages/Students/Maintenance/StudentRecordChecksPage.cs](../Pages/Students/Maintenance/StudentRecordChecksPage.cs): offers the Add Missing Record maintenance flow.

### Example
An administrator creates a missing historical enrollment and writes the reason “Historical record omitted during data entry.” The procedure temporarily inserts the enrollment, then calls the JSON-only audit writer with that plain text. The audit writer throws 59941; the enclosing transaction rolls back, so **no record is created**.

### Impact and implications
- Blocks exceptional correction of legitimate old records, even while routine admissions work.
- Expected rollback is safer than half-saving, but the workflow is unusable for ordinary plain-text reasons.
- The bug is **specific to the insert/create branch**, not a general failure of all student corrections.

### Remediation options
1. **Recommended SQL fix:** Build a structured JSON @NewValues payload using FOR JSON PATH, WITHOUT_ARRAY_WRAPPER from confirmed new enrollment fields, including enrollment ID, admission ID, year, class section, dates, status and human-readable reason. Pass that JSON to WriteAuditLog, leaving the true reason unchanged in other business fields.
2. Reuse an existing verified JSON serializer inside the SQL procedure if it already matches the audit schema.
3. **Do not** relax JSON validation globally in WriteAuditLog; other financial/security audit consumers depend on structured records.

**Can this be fixed solely in C#?** **No, not safely.** Sending a JSON-shaped reason from C# would contaminate remarks/history and fail for other callers. A server-side contract correction is required.

### Safe verification and acceptance
Run the existing [LeavingDateChecks.sql](../Tests/StudentCorrections/LeavingDateChecks.sql) on a disposable database and add a positive Add Missing Record test: choose a historical student, use a plain-text reason, assert exactly one enrollment and matching audit event are committed; verify duplicates and invalid dates still fail atomically; confirm result ID matches the new enrollment. Recheck existing corrections and history.

**Change scope:** One targeted SQL procedure plus one regression test; likely **no C# change** needed. **Regression sensitivity:** Medium, but high operational priority due to confirmed blocker.

---

## H-04 — Deactivating campus/class/section availability while occupied

**Classification:** Code-confirmed permissive behavior; whether allowed is a business-rule question.

### Evidence and affected files
- [dbo.SaveCampusClassSections.sql](../Database/Reference/Definitions/dbo.SaveCampusClassSections.sql): when assignment IsSelected = 0, it updates existing CampusClassSections.IsActive to 0; there is no check for Active enrollments referencing the ClassSectionId.
- [dbo.GetClassSections.sql](../Database/Reference/Definitions/dbo.GetClassSections.sql): offers only CCS.IsActive = 1 in class/section selections.
- [Pages/Setup/Academics/CampusClassSectionAssignmentsPage.cs](../Pages/Setup/Academics/CampusClassSectionAssignmentsPage.cs): grid-based assignment changes are committed via SaveCampusClassSections.

### Example
Campus A / Class 8 / Section B currently contains **34 Active students**. An administrator unticks that combination in setup and saves it. The SQL preserves the students' ClassSectionId references but makes that mapping inactive. Later admission/transfer screens filtering available destinations no longer list Section B, even though those students still occupy it.

### Impact and implications
- Existing enrollments remain in the database; this is **not** data deletion.
- A setup operator can inadvertently create an “occupied but unavailable” placement; future transfers, admissions and support troubleshooting become confusing.
- Some schools may intentionally retire a placement while preserving current/historical students. Blocking all deactivations would be too strict without agreement.

### Remediation options
1. **If occupied mappings must stay selectable/active:** SQL should atomically refuse deactivation when current Active enrollment references the mapping and return a helpful count/message. C# should display the reasons and ideally preview the affected count before save.
2. **If closing to new admissions while retaining existing students is intentional:** Keep IsActive semantics but ensure occupied mappings remain visibly identifiable in history/current-student screens; make setup confirmation describe “no longer available for new placements” without implying existing students moved.
3. For mass unassign, show how many saved assignments will be deactivated; maintain unsaved-change protections already present.

**Can this be fixed solely in C#?** **Partly.** A C# lookup before saving can warn or block routine use, but cannot guarantee the invariant across other callers/concurrent operations; server-side validation is needed for enforcement.

### Safe verification and acceptance
In a disposable copy, assign one Active student and one historical Left student to two different mappings. Attempt deactivation. Check configured policy: occupied mapping either blocked or clearly treated as retired while retaining history; unused mapping deactivates; record identity/enrollments are unchanged; destination pickers and reports remain consistent. Test two administrators editing concurrently.

**Change scope:** SQL setup procedure if enforcing; C# affected-count/confirmation only if useful; lifecycle and lookup regression tests. **Regression sensitivity:** Moderate because retirement may be legitimate.

---

# MEDIUM PRIORITY

## M-01 — Billing Status uses “any charge” rather than “all eligible charges”

**Classification:** Code-confirmed report semantics; may be misleading alongside selective posting.

### Evidence and affected files
- [dbo.GetBillingCycleStatus.sql](../Database/Reference/Definitions/dbo.GetBillingCycleStatus.sql): IsBilled = 1 when **EXISTS any** non-cancelled Monthly charge for the enrollment in the selected month.
- [Pages/Reports/Finance/BillingStatusReportPage.cs](../Pages/Reports/Finance/BillingStatusReportPage.cs): displays “Billed This Month” and “Remaining To Bill”.
- [Pages/Finance/Billing/FeePostingPage.cs](../Pages/Finance/Billing/FeePostingPage.cs): supports posting selected fee rows rather than requiring all rows.

### Example
A student has Tuition Rs. 2,000 and Transport Rs. 600. Tuition is posted, Transport is still eligible/unposted. Billing Status counts the student among “Billed This Month” and **not** “Remaining To Bill”. A busy operator might reasonably interpret this as fully billed.

### Impact and implications
- Understates the number of **partially billed** students, not necessarily the amount of posted tuition.
- Can falsely suggest end-of-month posting work is complete.
- No underlying bill is altered by the report.

### Remediation options
1. **Preferred for accurate completion status:** Change the SQL read-only report to compare count of eligible fee assignments vs count of matching active Monthly billing charges for the month, using the same eligibility rules as PostableMonths/PreviewMonthlyFees. Return fully billed, partially billed and unbilled counts; map these in C#.
2. **Lightweight C#-only clarity:** Rename the current “Billed This Month” to **“At Least One Monthly Fee Posted”** and the other count to **“No Monthly Fee Posted”**. This **fixes misleading wording**, but does not provide fully billed counts.
3. Avoid computing a new authoritative total by postprocessing an incomplete result; use a SQL contract for exact counts.

**Can this be fixed solely in C#?** **Yes for accurate labels; no for a trustworthy “fully billed” calculation from the present response.**

### Safe verification and acceptance
Test a student with two eligible fees: none posted, one posted, both posted, one reversed. Compare Billing Status vs the detailed Post Monthly Fees preview; no double counting and clear labels. Verify zero-fee students excluded per existing intent.

**Change scope:** One SQL report procedure if adding real statuses, one C# report page and tests; no mutation to financial data.

---

## M-02 — Reversible month discovery disagrees with reversal's Active requirement

**Classification:** Code-confirmed inconsistent query filters.

### Evidence and affected files
- [dbo.GetReversibleMonths.sql](../Database/Reference/Definitions/dbo.GetReversibleMonths.sql): joins enrollments but **does not** restrict enrollment Status = Active.
- [dbo.PreviewPostingReversal.sql](../Database/Reference/Definitions/dbo.PreviewPostingReversal.sql): restricts E.Status = Active.
- [dbo.ReverseMonthlyFees.sql](../Database/Reference/Definitions/dbo.ReverseMonthlyFees.sql): restricts E.Status = Active.
- [Pages/Finance/Billing/FeePostingReversalPage.cs](../Pages/Finance/Billing/FeePostingReversalPage.cs): displays count of posted/reversible charges for each month before preview.

### Example
In August, only a former student's charge remains unpaid and un-cancelled under a **Left** enrollment. The month discovery says “August: 1 posted, 1 reversible.” The preview returns nothing, because the charge's enrollment is not Active.

### Impact and implications
- Users chase false-positive months and distrust the preview.
- Charge history is **not** necessarily wrong and should remain protected.
- Fix is independent from the Annual/Manual scope decision in H-01, but ideally both queries are aligned in the same carefully reviewed change.

### Remediation options
1. **Recommended:** Apply matching eligibility rules to GetReversibleMonths: Active enrollment and, if H-01 is approved, matching ChargeKind. Make sure payment-allocation and cancellation handling are identical.
2. **C# workaround:** After month discovery, query/derive eligible detail for each candidate or hide zero-result months; this increases calls and still cannot correct the SQL API's inconsistent contract. Avoid unless SQL cannot be changed.
3. C# text can call discovery totals “live charges” rather than “reversible” until the contract is aligned.

**Can this be fixed solely in C#?** **Partly**, but a small **SQL read-only query fix** is safer and more efficient.

### Safe verification and acceptance
Create uncollected charges for Active and Left enrollments in the same month; verify month list counts equal corresponding preview counts and actual eligible reversal counts, while leaving closed enrollment charges untouched. Check policy for previously paid, refunded and cancelled rows.

**Change scope:** GetReversibleMonths SQL plus tests; optionally minor C# wording. Low-to-medium implementation risk; finance workflow needs regression testing.

---

## M-03 — Unexpected failure after earlier fee-apply scopes were committed

**Classification:** Code-confirmed C# orchestration boundary; not proven common.

### Evidence and affected files
- [Pages/Finance/Configuration/ApplyFeeConfigurationPage.cs](../Pages/Finance/Configuration/ApplyFeeConfigurationPage.cs): loops through previewScopes, awaiting ApplyFeeConfigurationToExistingAssignments once per scope; aggregates counts and displays a full summary **after the loop**; outer catch calls Fail(ex).
- [dbo.ApplyFeeConfigurationToExistingAssignments.sql](../Database/Reference/Definitions/dbo.ApplyFeeConfigurationToExistingAssignments.sql): handles per-assignment outcomes, counts successful/failed updates, logs each invocation; uses per-assignment error capture with XACT_ABORT OFF.

### Example
The operator applies saved rates to 3 class/fee-type scopes. Scope A commits 40 updates; scope B commits 30. Scope C fails before its normal result can be read (timeout/connection loss). The C# catch shows an error but does **not** describe the 70 earlier confirmed updates. A user may retry the entire batch without knowing the state.

### Impact and implications
- Uncertainty, especially after network failures or application interruptions.
- No evidence that earlier successful changes were rolled back; separate stored-procedure calls are **not** one transaction.
- The failing scope itself has an **unknown** outcome if execution reached the server but the result was lost.

### Remediation options
1. **Recommended C# only:** Track each scope's confirmed results and preserve a durable in-session progress summary. On failure, state: completed scopes, confirmed successes/failures, the scope at interruption, and **unknown until re-checked** status of that last scope. Re-run **read-only preview** before retrying.
2. Optionally add a safe scope-specific reconciliation view using the existing preview query, without new persistence or SQL.
3. **Do not** claim that the entire batch was rolled back or automatically retry the entire operation. Don't add distributed transactions or new bulk procedures without explicit need.

**Can this be fixed solely in C#?** **Yes**, for truthful outcome feedback and retry guidance; the design intentionally commits separate scopes.

### Safe verification and acceptance
Inject a controlled exception on scope 2 in a local test environment; verify the UI records the confirmed result from scope 1, never claims the unknown second scope succeeded/failed, and refreshes the read-only preview before allowing retry. Verify ordinary all-success and per-student partial failures still display correctly.

**Change scope:** One C# page, small helper/result model only if already available; no SQL change necessary. Low risk when kept to messaging/recovery.

---

# LOW PRIORITY

## L-01 — Long historical enrollment projections can hit recursion limit

**Classification:** Code-confirmed default SQL CTE depth risk; exceptionally old data needed.

### Evidence and affected files
- [dbo.fn_ExpectedRecurringChargeMonths.sql](../Database/Reference/Definitions/dbo.fn_ExpectedRecurringChargeMonths.sql): recursively constructs one row per month from BillingStartDate to the requested AsOfDate.
- [dbo.GetStudentFeeProjection.sql](../Database/Reference/Definitions/dbo.GetStudentFeeProjection.sql): calls the function for a future horizon; caps MonthsAhead at 24, but this does **not** cap the time since original enrollment.
- [dbo.GetMissingChargeCandidates.sql](../Database/Reference/Definitions/dbo.GetMissingChargeCandidates.sql): calls the same function across eligible histories.

### Example
A legacy enrollment has billing start in 2015. An operator asks for projections in 2026. The recursive CTE may enumerate substantially more than 100 months before reaching the projection horizon and SQL Server's standard recursion limit can fail the query.

### Impact and implications
- Read-only projection or missing-charge reports may error for a few exceptional historical records.
- No indication from this alone that live charges or account balances are corrupted.
- Normal one-year enrollments seldom trigger it.

### Remediation options
1. **Preferred SQL:** Replace recursion with a bounded, set-based month generator/calendar/tally, or apply an appropriate query-level recursion strategy to the consuming statement; verify SQL Server rules for inline TVFs and the outer OPTION (MAXRECURSION...) hint.
2. If the business domain has a legitimate maximum enrollment duration, enforce a clear supported horizon and label any truncated report as incomplete; never silently omit past months.
3. Avoid misleading C# catch/retry; show a clear error and retain inputs.

**Can this be fixed solely in C#?** **No** for correct full results: C# cannot bypass a failing SQL function for arbitrary callers. A UI-limited query could avoid some cases but would change the intended reporting range.

### Safe verification and acceptance
On a disposable copy, include enrollments with spans of 12, 99, 101, and 130 months. Confirm projections return exactly the expected months and fee amounts without double billing; avoid timeouts/large unbounded results.

**Change scope:** One shared SQL function or its consumers, tests. Low urgency; test historical-data compatibility.

---

## L-02 — Bulk Promotion result is shown twice

**Classification:** Code-confirmed UI duplication; comment suggests intentional behavior.

### Evidence and affected files
- [Pages/Students/Enrollment/BulkPromotionPage.cs](../Pages/Students/Enrollment/BulkPromotionPage.cs): constructs reportOutcome(), calls it **before** await LoadCandidates(), then calls it again **after** refresh if the page is not disposed.
- The code comment says the second outcome is shown so that it remains the last thing the operator sees.

### Example
Promoting 12 students succeeds. The operator closes a success dialog, waits for candidate reload, then receives the same success dialog again. The same applies to warning results.

### Impact and implications
- Duplicate interruption and confusion (“Did it run twice?”), although the underlying promotion action is only invoked once in this sequence.
- Does **not** establish duplicate SQL promotion or double charges.
- More useful to preserve the actual confirmed server counts than to preserve both modal dialogs.

### Remediation options
1. **Recommended C# only if users dislike the behavior:** Show the full result **once** and preserve the result text in a status area after reloading. Preserve the current post-promotion next-step offer.
2. Alternatively, reload first, then show the modal once **after** successful rebind; ensure a reload error does not conceal the already-confirmed promotion result.
3. Keep existing validation that checks ProcessedCount, PromotedCount, HeldBackCount and unknown server result.

**Can this be fixed solely in C#?** **Yes.** No SQL change necessary.

### Safe verification and acceptance
Test all promoted, held back, partial server counts, unreadable server result, failed refresh and page disposal. Each operation must report **one accurate result**, preserve promoted student's next-step navigation and never suggest retry where outcome is unknown.

**Change scope:** One C# page, small message sequencing change. Very low business risk, moderate UX benefit.

---

# Cross-cutting remediation rules

1. **Preserve the proven happy paths.** Do not refactor enrollment, billing, collection or reports wholesale.
2. **Agree on policies before edits**, especially H-01, H-02 and H-04. These are not safe to “fix” blindly.
3. For **financial/data integrity**, validate at the database transaction boundary, not only via disabled buttons, C# preview filters or confirmation dialogs.
4. For **UI/reporting gaps**, favor C# changes where a complete truthful correction is possible (M-03, L-02; wording aspect of M-01).
5. Apply SQL fixes through the project's existing **Migrations** workflow. Do not treat Database/Reference definitions as writable deployment scripts.
6. Use a **disposable restored database copy**. Preserve original student/payment data; add fixtures under rollback transactions where feasible.
7. Compare before/after **totals, statuses, enrollment IDs, payment allocations, audit entries** and UI descriptions. Include multiple operators and stale previews where relevant.
8. Stop on an ambiguous business rule rather than changing behavior. Capture the decision in the associated future issue or test.

## Recommended verification order

1. **H-03 — Add Missing Record audit failure** (known isolated blocker; simplest scope).
2. **H-01 — Reversal ChargeKind policy** (greatest direct accounting risk if monthly-only intended).
3. **H-02 — Cross-year posting policy** (coordinate year, enrollment and reports).
4. **H-04 — Occupied mapping retirement** (clarify allowed behavior before SQL guard).
5. **M-02 — Month list/preview agreement**, coordinated with H-01.
6. **M-01 — Partial billing status meaning**.
7. **M-03 — Interrupted multi-scope fee rate apply feedback**.
8. **L-01 — Long-history projection test**.
9. **L-02 — Duplicate modal message polish**.

## Out-of-scope and provenance

This folder records source findings only. It does not represent the results of a live SQL execution, a complete codebase test, or a statement that all SQL modules and schema constraints were exhaustively analyzed. Findings involving policy or concurrency remain hypotheses until reproduced. User-provided live test results are not overridden by hypothetical examples.

**Audit artifact only:** creating this document does not modify any C# file, database definition, migration, installer, application behavior or live data.
