# FMSC_SMS scenario testing findings

Audit date: 11 October 2026 (Asia/Karachi). Status: original database evidence retained; user-approved C# fixes and decisions recorded below.

## Current disposition — user decisions and C# changes

This section supersedes the original proposed fixes and release recommendation below. The original reproduction evidence remains useful; it does not describe the current C# implementation.

- **SC-01: C# recovery protection implemented.** A flushed local intent is created before financial writes, held exclusively during the transaction, and cleared only after confirmed commit/rollback. Unconfirmed commit outcomes retain the intent across page closure/app restart and require Payment History/receipt review before another collection. The operator must acknowledge that review; clearing the notice clears the current student/allocations and requires fresh balances. Single and family collection use the same writer. A screen/receipt refresh failure after confirmed commit reports recorded payment and drops stale allocation rows. Recovery details include student identities, amount, date and method. This is manual reconciliation, not automatic SQL receipt replay or request deduplication. Direct SQL submissions can still represent two separate identical legitimate payments. The journal is local to the Windows user; it does not coordinate separate computers/users.
- **SC-02: accepted as handled in C#, per user instruction.** Both monthly-posting entry points obtain choices from `dbo.GetPostableMonths`. The main picker cannot accept free text, invalidates previews on scope changes, and checks the captured preview against current scope before submitting. Student-account pending posting uses typed choices from the same bounded list. Decision comments are in `FeePostingPage.cs` and `FeeAssignmentPosting.cs`. Read-only execution on the audit copy returned five eligible choices from November 2026 through March 2027, all within academic year 28. No SQL validation change was made for a direct-call bypass.
- **SC-03: accepted policy; no additional-charge detail audit required.** The user explicitly declined extra audit logging. No change was made.
- **Q-01: verified historical register numbers allowed.** The user approved accepting verified physical-register numbers despite legacy admission-date conflicts. Historical admission mode bypasses chronological number bounds while preserving normalization, global uniqueness, future-date rejection and selected academic-year date bounds. Ordinary admission keeps chronological checks and current next-number behavior. The historical suggestion is advisory and the screen asks the operator to verify it against the register. This change does not correct existing legacy dates or renumber existing students.

**Verification of this implementation:** `dotnet build --no-restore -v minimal` passed with zero warnings/errors. `Tests/ScenarioRecovery.Tests.ps1` passed 19 checks against the compiled C#: active/concurrent intent exclusion, interruption persistence, retry blocking, identity-checked review, confirmed-outcome cleanup, corrupted-file rejection, and numbering policy using all 6,592 saved student records. Historical `1030b` is accepted while duplicate `1030A`, malformed numbers, out-of-year dates and future dates are rejected; current ordinary suggestion remains `6591`.

**Verification limits:** The 19 checks exercise the journal and numbering policy, not a real network failure during SQL commit or the WinForms recovery dialog. The writer's commit/rollback and post-commit refresh branches were reviewed in code; end-to-end fault injection remains unverified. The existing 41 database scenarios are the earlier reproduction run, not newly rerun claims. No production business data, SQL procedure definitions, commits, pushes or PRs were changed by this fix pass.

## Original database assessment (before the C# fixes)

This continuation inspected the actual database, restored a verified disposable copy, and executed data-changing scenarios on that copy. It supersedes the earlier limited static assessment. Three defects/gaps were reproduced: repeated partial collection is accepted twice, monthly posting accepts a month beyond the selected academic year, and additional-charge posting omits the detailed audit log. One historical-numbering limitation needs a business-rule decision rather than an automatic code change.

**Original recommendation before fixes: unrestricted financial use required SC-01 recovery protection.** A supervised pilot with receipt/payment-history reconciliation is possible, but this report is not certification of every module. SC-02 is a direct SQL validation gap with normal UI exposure not demonstrated. SC-03 concerns traceability rather than an incorrect charge amount. These distinctions must be retained when resolving findings.

Do not classify absent release MDF/LDF files or an empty migration folder as blockers. The established adjacent Database snapshot preparation remains the user's release step. Fresh installation, existing-database upgrades and full application recovery were not validated here.

## Safety and database identity

- Source: `FMSC_DB`, SQL Server LocalDB 15.0.4382.1, attached to `C:\ProgramData\FMSC_Database\FMSC_DB.mdf` and its corresponding log.
- The existing LocalDB instance was started; it was not restarted, recreated or force-stopped. The normal discovery failure was bypassed using its actual named-pipe endpoint.
- Source baseline: 6,592 students, 730 enrollments, 28 academic years (1999–2000 through 2026–2027), seven billing charges and one completed payment. Enrollment statuses: two Active, 727 Left, one Promoted.
- A SQL `COPY_ONLY` backup with `CHECKSUM` was created. `RESTORE VERIFYONLY ... WITH CHECKSUM` confirmed the backup valid.
- Disposable database: **`FMSC_AUDIT_20261011`**. Both restored files are under `bin\ScenarioAudit_20261011`, separately from the source files. Restore did not use `WITH REPLACE`.
- Restored copy passed `DBCC CHECKDB WITH NO_INFOMSGS` without reported errors and initially contained all 6,592 students.
- Every scenario script checks the audit database name and its physical data-file path before writing. The tested business procedures use the selected database's `dbo` objects. A hard-coded live-database backup procedure exists in the exported definitions but was not invoked.
- Thirty-four transaction-contained scenarios roll back their fixtures. Four additional scenarios use separate sessions/commits on the copy to test concurrency and repeated committed submissions. The disposable database therefore intentionally retains a small amount of test state.
- Read-only source verification after those commits still showed 6,592 students, 730 enrollments, seven charges, one payment, counter 6,590, charge 1 balance Rs. 4,000, charge 7 balance Rs. 1,000, and no `AUDIT` students or payment references.
- During the original database reproduction run, no production business-record writes, application/SQL implementation changes, commits, pushes or PRs were performed. SQL backup history and engine startup are operational effects of creating the copy, not school-record changes.

Private reproduction assets, procedure exports, backup and output files are retained at:

`C:\Users\pc vision\source\FMSC_SMS_GitHub\bin\ScenarioAudit_20261011`

These assets are in the existing ignored build-output area. This report contains no student names, contact details, passwords or password hashes.

## SC-01 — Identical partial collection can create a second committed payment

**Severity:** High. **Confidence:** reproduced through actual SQL procedures, including two independently committed calls. Client recovery path confirmed by static analysis and the earlier compiled timeout-message check. Actual packet loss/WinForms retry interaction was not injected.

**Locations:** `dbo.ProcessFeeTransaction` (export `ProcessFeeTransaction.txt`, lines 27–106 and 285–291); `Pages/Finance/Collection/CollectionDiscountWriter.cs`, lines 13–58; `Pages/Finance/Collection/FeeCollectionPage.cs`, lines 2509–2543, 2982–3028 and 3185–3202; `Core/DbMessage.cs` timeout handling; `Core/V2Page.cs`, lines 1635 onward.

**Preconditions and reproduction:** On the restored copy, charge 1 belongs to admission 6591/enrollment 728 and has Rs. 4,000 outstanding. Submit Collection Rs. 100 allocated to charge 1, Cash, date 2026-10-11, with reference `AUDIT-LOST-RESPONSE`. Commit the first call and deliberately do not consume its receipt in the test driver. Open a separate connection and submit exactly the same parameters again. This models an operator without confirmation; both SQL connections actually succeed, so it is not a network-fault reproduction.

**Expected versus actual:** Recovery from an uncertain first submission must establish whether it committed before making a second financial write. Actual: two completed payment records totaling Rs. 200; charge balance Rs. 3,800. A separate rollback-contained repetition produced the same double deduction (F01). New receipt numbers make these look like two distinct legitimate collections.

**Root cause/invariant:** Collection has no durable caller request identity or duplicate-submission reconciliation. `ReferenceNumber` is not a deduplication token. Existing balance validation prevents overpayment but permits a repeated partial amount while enough balance remains. The client catches a write error with generic `Fail(ex)`, then re-enables collection using its existing grid. Timeout guidance says to try again without first establishing the previous outcome. The violated invariant is preservation of one intended collection through uncertain-result recovery. This is not a claim that all identical legitimate payments must be forbidden.

**Impact:** Duplicate collection, extra receipts, incorrect cash closing/payment totals relative to cash received, and subsequent refund/correction work. Existing SQL atomicity does not solve ambiguity after commit.

**Fix scope/minimum-risk proposal:** C# can explicitly distinguish confirmed success, definite rejection and uncertain outcome, retain the submitted details, require reconciliation, and suppress blind retry guidance. Durable request identity with SQL-enforced matching/replay would require coordinated changes. Inspect related family-collection behavior before choosing that implementation. Never deduplicate merely on student/date/amount/reference: separate legitimate payments may match those fields.

**Regression risks:** Blocking valid later payments; duplicate request keys reused for changed allocations; losing a confirmed receipt after a refresh exception; treating a definite validation rejection as an uncertain commit.

**Required regression:** Failure before commit leaves no payment; lost commit acknowledgment recovers exactly the original receipt; failure after confirmed commit during refresh does not invite a second payment. Changed payload under a reused request identity is rejected if durable request identity is implemented. Cover single and family collections.

**Deployment blocker:** Yes for unrestricted collection workflows until recovery is safe. Evidence: F01 outputs; C04 first/second response files; `concurrency-results.txt`; source call sites. C04 is independently committed SQL replay, not proof of the complete UI fault sequence.

## SC-02 — Monthly posting accepts a month after the academic year ends

**Severity:** Medium for the demonstrated direct-SQL entry point; potentially High if an application path can supply that month. **Confidence:** reproduced.

**Locations:** `dbo.PostSelectedMonthlyFees` (`PostSelectedMonthlyFees.txt`, lines 3–16, 28–75 and 111 onward); `dbo.GuardClosedEnrollmentCharges`; contrast `dbo.GetPostableMonths`, which limits its month list to the selected year's bounds. C# posting call: `Pages/Finance/Billing/FeePostingPage.cs`.

**Preconditions/reproduction:** Active billing-ready enrollment 730 belongs to academic year 28, ending 2027-03-31, and has an open monthly fee assignment. Pass a PostingSelectionType row `(730, 2, 2027-04-01)` and execute `PostSelectedMonthlyFees` with academic year 28 and billing month 2027-04-01.

**Expected versus actual:** Reject an out-of-year month or create zero charges with an explicit rejection outcome. Actual: a monthly charge for April 2027 is inserted against the 2026–2027 enrollment. B04's invariant fails with test error 59992. The test transaction rolls it back.

**Root cause/invariant:** SQL verifies enrollment-year identity and the billing-start lower bound, but omits the academic-year end bound. The trigger protects closed enrollment history and paid charges, not year boundaries. Posting dates must remain within the enrollment's academic year.

**Impact:** Out-of-year receivables and misleading historical-year report totals if this entry point is called with an invalid month. No normal UI route to supplying April 2027 for this year was demonstrated: the usual month list is bounded. Do not present this as a reproduced ordinary UI failure.

**Fix scope/minimum-risk proposal:** SQL validation of the normalized month against both selected-year month bounds before insertion. Keep the existing cancellation/reposting and historical posting policy. No broad schema rewrite is proposed.

**Regression risks:** Academic years starting/ending midmonth, deliberate historical posting, changing year bounds after existing charges, and valid final-month billing.

**Required regression:** First and last academic-year months succeed when otherwise eligible; immediately preceding/following months reject without changes or audit residue; wrong-year selections still reject. Test every monthly-posting entry point for consistent bounds.

**Deployment blocker:** Not independently established for the normal UI. Resolve before relying on SQL as a complete business-rule boundary. Evidence: B04 script/output and actual definitions.

## SC-03 — Additional-charge posting omits the detailed financial audit event

**Severity:** Medium. **Confidence:** reproduced through the actual procedure.

**Locations:** `dbo.PostAdditionalFee` (`PostAdditionalFee.txt`, lines 120–148); `dbo.WriteAuditLog`; `Pages/Administration/AuditLogsPage.cs` uses `GetAuditLogs`.

**Preconditions/reproduction:** Select active enrollment 728 with ManualChargeSelectionType. Call PostAdditionalFee with a new request GUID, fee type 2, date 2026-10-11, amount Rs. 500, discount zero and a reason. Compare AuditLogs before/after and inspect the operation associated with that request.

**Expected versus actual:** A financial charge should have an audit detail identifying the affected charge and relevant financial values. Actual: charge created and AuditOperations header inserted, but zero new AuditLogs rows and no BillingCharges detail linked to the operation. A01 fails with test error 59993. The charge/request records still exist within the test transaction; this is not missing financial data.

**Root cause/invariant:** The procedure inserts its operation header and commits without invoking WriteAuditLog. No installed charge-auditing trigger supplies the missing detail. WriteAuditLog explicitly preserves financial auditing while filtering routine housekeeping.

**Impact:** Additional charges do not appear through the same detailed audit trail used for other financial operations. Their request/charge records provide some traceability, but the audit UI cannot show the missing detailed event. No incorrect charge amount was demonstrated.

**Fix scope/minimum-risk proposal:** SQL-only: write financial detail for every newly created charge inside the existing transaction using its operation ID. Include affected records, amounts, discounts, date, reason and request correlation; consider newly created assignment details. An idempotent replay must not emit another creation event.

**Regression risks:** Extra events on replay, oversized bulk audit payloads, an audit failure causing a legitimate write to roll back, and inconsistency with audit filtering.

**Required regression:** New posting has linked charge audit detail; identical request replay produces neither a new charge nor a duplicate creation event; changed details under that request still reject; a failed bulk posting leaves no partial charges or audit details.

**Deployment blocker:** No by itself; meaningful financial traceability gap. Evidence: A01, B07/B08 outputs and the actual procedure definition.

## Q-01 — Existing register dates conflict with the global chronological-number rule

**Classification:** Business-rule/data question, not a proven implementation defect. **Confidence:** compiled numbering calculation executed against all 6,592 actual admission-number/date rows from the copy.

`AdmissionNumberPolicy.Calculate`, `Core/AdmissionNumberPolicy.cs` lines 75–78, computes global bounds from all earlier/later admission dates. For date 2005-04-14 in year 2005–2006, it produces lower bound 1027 and upper bound 32 and deliberately refuses the plan. Number 1027 is dated 2005-04-12; number 32 is dated 2014-03-17. `StudentAdmissionPage.cs` lines 4229–4237 validates that plan before invoking the admission procedure.

Consequently, the direct SQL historical-admission regression N03 passed with explicit number 1030b, but that result alone does not establish the corresponding normal UI save works against this snapshot. The compiled pre-save policy rejects the historical plan. No interactive UI save was performed.

For 2025-10-11, the same full-data calculation suggested 6589b, bounded by 6589 and 6590. For 2026-10-11 it suggested 6591. These outcomes distinguish the fixed old-year/latest-number case from the legacy date-order conflict.

Before proposing a fix, determine whether number 32's date is an error to correct, a legitimate historical exception, or evidence that numbering has a narrower scope than the global rule. Preserve the deliberate guard until that policy is resolved. Do not automatically rewrite admission dates/numbers or loosen all chronology checks.

Evidence: `numbering-policy-results.json`, `numbering-fixture.json`, anonymized database date/number checks, and the compiled source method. The JSON export had SQLCMD chunk line breaks removed before parsing, and the fixture row count was asserted to equal 6,592. Earlier malformed-export calculations were discarded.

## Coverage and execution outcomes

**41 final database scenarios/checks: 37 passed, four failed assertions.** The four failures represent three distinct findings: F01 and C04 both support SC-01; B04 supports SC-02; A01 supports SC-03. Counts exclude repeated development runs and harness corrections. Initial incorrect expected error numbers, SQLCMD session SET options, result-reader dependencies and an out-of-order fixture outside its billing eligibility were corrected rather than reported as application bugs.

| Area | Executed evidence | Remaining limits |
|---|---|---|
| Admissions | Historical save with explicit suffix; no historical fees; unchanged counter; duplicate rejection; high manual counter update; concurrent reservation; full-snapshot C# plans | Concurrent full admission forms; date/year edits during asynchronous save; physical register policy |
| Lifecycle | Transfer chronology and paid-finance preservation; readmission identity/waiver; repeated readmission, prior leaving-date and stale-rate rejection | Full promotion, leaving/certificate clearance, maintenance correction sequences |
| Billing | Overlap trigger; repeated opening post; newer/older months; wrong-year selection; cancellation/reposting; year-end bypass; concurrent posting | Transport stop/restart; interrupted multi-scope configuration; exhaustive legacy effective periods |
| Payments | Repeated partial collection; overpayment; pre-enrollment payment; cancelled charge; correction/refund in both orders; excessive refund; repeated reversal; partially paid charge reversal rejection; competing concurrent collections | Actual lost network acknowledgment; family collection atomicity and retry; multi-charge reversal concurrency |
| Fee rates | Future rate version preserves paid bill; billed-month rate change rejected | All amount/frequency/discount/configuration combinations |
| Certificates | Regular-student certificate number issued; name snapshot preserved after student edit; issued number immutable | Other certificate types, printing, leaving-date repairs and reissuance policy |
| Security | Unknown user rejected; revoked Fees.Collect permission rejects direct ProcessFeeTransaction call for synthetic restricted user | Windows/SQL caller-to-app-user identity threat model; cached UI access; remaining internal procedures |
| Reports | Charges, Students and Aging views: detail and summary each equal authoritative current positive noncancelled balances, Rs. 11,900 on mutated copy | Historical/as-of semantics, every filter/transfer combination, other reports and exports |
| Recovery | Copy-only backup verification; separate-file restore; restored DBCC integrity check | Application restore UI, failed restore recovery, installation and upgrades |

Additional static evidence: shared serializable transactions protect collection discount changes and reviewed fee posting; required uniqueness definitions cover admission numbers, one Active enrollment per student and active monthly/annual charges. These protections were inspected in the actual restored database rather than inferred solely from the compatibility manifest.

Baseline consistency queries found zero duplicate Active-enrollment groups, orphan payment allocations, negative noncancelled charge balances or completed-payment/allocation-total mismatches. There were 5,863 students without enrollments; this is recorded as legacy coverage context, not automatically classified as a defect. The data has deep student history but sparse financial history, so financial scenarios were deliberately constructed on the copy.

The previous application build succeeded with zero warnings/errors. The Tests directory was empty at initial inspection. The earlier ten isolated compiled-method checks are separate from the 41 database checks above; they are not ten additional database scenarios.

## Invariants and observed enforcement

| Invariant | Observed enforcement/outcome |
|---|---|
| Duplicate admission number rejected | SQL allocator and unique index; rejection executed |
| Historical explicit number remains unchanged | Actual historical admission executed; passed |
| One Active enrollment per student | Filtered uniqueness; repeated readmission rejected |
| Payment cannot exceed remaining balance | SQL locking/recheck; overpayment and concurrent competition rejected |
| Retry cannot repeat one intended collection | Not established; repeated partial submissions accepted (SC-01) |
| Corrections/refunds reconcile allocations | Both operation orders and excessive refund executed; passed |
| Active monthly charge unique per enrollment/type/month | SQL uniqueness/posting checks; repeat/concurrency passed |
| Monthly billing inside academic year | UI month generation bounded; SQL write gap reproduced (SC-02) |
| Paid financial history survives future rate/keep-fees transfer | Executed, passed |
| Noncancelled fee periods do not overlap | SQL trigger; rejection executed |
| Issued document identity/history preserved | Regular-student snapshot and immutable number tests passed |
| Financial write has detailed audit | Admission checked; additional-charge detail missing (SC-03) |
| Revoked permission rejected at procedure boundary | Synthetic restricted-user collection rejected |

## Cross-module risks and manual work still required

Prioritize actual lost collection acknowledgment, family collections, promotion followed by prior-year correction, transfer followed by historical reports, leaving-date correction after certificate issuance, and transport restart with mixed paid/cancelled/scheduled periods. Execute whole sequences and compare final charges, allocations, statuses, counters and audit details.

Manual Windows checks remain for two WinForms sessions competing with stale previews, changing selections during awaits, receipt recovery, navigation/refresh after a successful commit, actual RDLC/PDF output and physical printers. SQL concurrency tests do not substitute for concurrent UI tests.

No actual network failure, app termination during commit, physical printing, all-module exhaustive test or full failure-recovery drill was performed. Do not describe those as passed.

## Fix order and reusable regression protection

1. Resolve SC-01's uncertain collection outcome handling; then prove single/family receipt recovery. C# containment is possible; durable deduplication would be coordinated C#/SQL work.
2. Resolve SC-02's SQL academic-year bounds with first/last-month regression checks.
3. Resolve SC-03's detailed audit creation without breaking request replay.
4. Decide Q-01 against physical register/business rules before changing numbering or legacy data.

Retain a small release regression suite around historical admission, competing identifier allocation, stale readmission rates, rate changes after paid billing, out-of-order posting, cancellation/reposting, uncertain payment recovery, refund/correction order, detail-to-total report reconciliation, certificate snapshots and revoked permissions. Execute database fixtures only on a separately identified restored copy with database/file guards.

Scripts and evidence: `run-scenarios.ps1` / `scenario-results.json` (28 rollback-contained cases), `run-more.ps1` / `more-results.json` (six rollback-contained cases), `concurrency.ps1` / session outputs and `concurrency-results.txt` (four scenarios), `reconcile.ps1` / `report-results.json` (three checks). Scripts use this audit's fixed endpoint, IDs and evolving fixture state; they are reproduction artifacts, not a general-purpose production test runner. Reproduce from the retained verified baseline in a fresh disposable database and adapt the guards before rerunning. Do not point them at FMSC_DB.

## Detailed scenario ledger

The final outcomes follow below. Failed invariants are audit evidence, not production test failures; every failure was confined to the disposable copy.

| Scenario | Outcome |
|---|---|
| F01_repeated_partial_collection | FAIL |
| F02_overpayment_rejected | PASS |
| F03_payment_before_enrollment | PASS |
| F04_cancelled_charge_rejected | PASS |
| F05_collection_correction_refund | PASS |
| F06_collection_refund_correction | PASS |
| F07_excess_refund_rejected | PASS |
| F08_repeat_reversal_rejected | PASS |
| F09_reverse_partially_paid_charge | PASS |
| N01_duplicate_number_rejected | PASS |
| N02_manual_number_advances_counter | PASS |
| B01_overlapping_period_rejected | PASS |
| L01_transfer_before_enrollment | PASS |
| L02_transfer_preserves_paid_finance | PASS |
| N03_actual_historical_admission | PASS |
| N04_duplicate_historical_admission | PASS |
| B02_repeat_opening_post | PASS |
| B03_post_newer_then_older_month | PASS |
| B04_month_outside_academic_year | FAIL |
| B05_wrong_year_selection_rejected | PASS |
| B06_cancel_and_repost_monthly | PASS |
| L03_readmission_waiver_retains_identity | PASS |
| L04_repeat_readmission_rejected | PASS |
| L05_readmission_on_prior_leaving_date | PASS |
| L06_stale_readmission_rates_rejected | PASS |
| B07_additional_fee_retry_idempotent | PASS |
| B08_additional_fee_changed_retry_rejected | PASS |
| A01_additional_fee_has_detail_audit | FAIL |
| R01_rate_change_after_paid_month | PASS |
| R02_rate_change_in_billed_month_rejected | PASS |
| D01_certificate_preserves_name_snapshot | PASS |
| D02_certificate_number_immutable | PASS |
| S01_nonexistent_user_rejected | PASS |
| S02_revoked_permission_blocks_procedure | PASS |
| C01 concurrent number reservation: 6591 and 6592 | PASS |
| C02 concurrent monthly posting: one active charge | PASS |
| C03 competing Rs. 800 collections: one payment, Rs. 200 remaining | PASS |
| C04 identical independently committed partial collections | FAIL — SC-01 |
| Report Charges detail/summary reconciliation | PASS |
| Report Students detail/summary reconciliation | PASS |
| Report Aging detail/summary reconciliation | PASS |

## Action-button availability pass — 11 October 2026

User requested that actions requiring data be disabled before their prerequisites exist, while retaining click-time validation.

- Admissions: Collect Payment requires a saved student, navigation support and Fees.Collect permission. Print and enrollment Actions require a saved student; enrollment Actions also require the applicable permission. Add/find sibling requires a saved student and Students.Administer permission. These states are recalculated on initialization, load, save and clear/new, and disabled during saves. Existing click-time and individual menu-item guards remain.
- Family/Siblings: Remove Selected Member requires a selected member card, rather than merely a loaded family. Add/remove are held during a family write and their availability is recalculated after it finishes, including after the selected card is cleared by reloading.
- Student Account Statement: Generate Statement and Print Amounts Due require at least one included student, including students retained from earlier searches and family selections. Clearing selections or starting over disables both. Amounts Due stays disabled during its print preparation.
- Reviewed certificate pages, duplicate-student resolution, campus/class/section assignment and fee collection already contain prerequisite-driven guards. This pass is not a claim that every action in every page has been manually exercised.

Validation: application build passed with zero warnings/errors; git diff whitespace check passed. State transitions and permission guards were reviewed in source. No interactive WinForms checks or production database writes were performed for this pass.

## Explicit Clear / New actions — 12 October 2026

User decision: clicking Clear / New already authorizes discarding the current editor, so these explicit actions should not ask again. Removed reset confirmations from the shared MasterPage Clear / New toolbar (covering record-editor pages), Admissions Clear / New, Current Employment Clear, Academic Program New, and New Certificate on NOC, Provisional, Sports, Regular Student, Regular Lecturer and Bank Account Opening certificate pages.

Busy/save guards and baseline recapture remain. Regular Student New Certificate bypasses the separate Next Student confirmation explicitly. Next Student/Next Staff Member, record selection, refresh/reload, page navigation and modal close confirmations retain their existing behavior. Reset actions clear the editor; they do not delete saved records or financial entries. Updated the admission button accessibility description to reflect immediate discard.

Verification: reviewed the changed handlers and their callers, including Regular Student New's indirect ClearForm path; application build passed with zero warnings/errors and git diff whitespace check passed. No production database changes. Interactive WinForms verification was not performed.

## Admission finder width consistency — 12 October 2026

The admission toolbar search field was recalculated by FitQuickFindWidth during every Actions.Layout and after Confirm Return availability changed. Its width depended on current toolbar/status allocation and visible controls, making it change between 150 and 320 scaled pixels after actions. Removed this page-specific fitting logic and fixed the field's width/minimum/maximum width at 320 scaled pixels. The shared toolbar and status placement remain unchanged. Shortened admission-number guidance in its current status location.

Verification: no remaining dynamic quickStudent width assignments or FitQuickFindWidth call sites; application build passed with zero warnings/errors; whitespace check passed. Narrow windows retain the shared toolbar's existing wrap behavior. No database changes. Interactive WinForms action/resize checks were not performed.

## Cross-page field-width review — 12 October 2026

Searched page/shared-control layout handlers and field Width/Size assignments for the admission pattern: resizing an input according to currently visible actions/status text. No additional instance of that rule was found. Reviewed certificate fields, shared master-record editor, report filters, Student Account Statement, fee tools and campus/class/section assignments.

Certificate and master-editor column reflow depends on available container width, rather than save/clear status text. Provisional Certificate has explicit viewport-width breakpoints; report filter arrangement likewise depends on viewport width and a fixed field count. Student Account Statement avoids repeated reflow when its viewport size has not changed. Picker and combo item text measurement changes popup/dropdown width, not the input field width. Other reviewed layout handlers size cards, grids, labels or button rows.

No further input-width code change was justified from this source review. The shared toolbar remains unchanged per the user's instruction. This does not certify runtime action stability on every page: scrollbar/container changes can still alter responsive widths, and interactive checks at fixed window size remain unperformed.

## Concise guidance review — 12 October 2026

Shortened shared fee/certificate/report guidance and page-specific hints across certificates, student reports, billing reports, daily closing, promotion, transfers, family records, duplicate review and student maintenance. Guidance stays in its existing location; shared layout controls and field sizing were not changed in this pass. Retained collection-before-printing rules, current-year certificate scope, saved certificate data provenance, fee effective-month restrictions, closing revisions and blank-versus-zero reconciliation guidance. Confirmation dialogs and payment recovery warnings were not changed.

Verification: application build passed with zero warnings/errors; git diff whitespace check passed. Changes are wording only, with no database writes or business-logic changes. Interactive visual verification across the running pages was not performed.

## Consistent tab appearance — 12 October 2026

Replaced shared native tab headers with equal-width ModernButton strips matching Admissions: filled active tabs, neutral inactive tabs, rounded buttons and shared font/height metrics. This applies to Fee Posting, the other WorkspacePage groups and MasterPage Entry/Records tabs. Admissions, Student Record Checks and transfer/readmission selectors now use the same shared active/inactive styling. Larger groups wrap according to viewport width and caption size. Existing admission icons remain.

The original TabControl still owns pages and selection, preserving workspace lazy loading, state restoration, selection vetoes and tab shortcuts. Its native header is clipped inside a content viewport. Added a dedicated TabActive role so captions such as Reverse a Posting receive navigation styling and do not intercept Post/Print action shortcuts. Shared toolbars and guidance placement were not changed.

Verification: application build passed with zero warnings/errors; whitespace check passed. An isolated real WinForms harness passed 27 checks covering initial selection, click/keyboard switching, disabled tabs, selection cancellation, content alignment, narrow wrapping, caption and page changes, shortcut exclusion, workspace busy guards and lazy loading. Inspected rendered wide/narrow strips. Harness and renders remain under the Git-ignored Tests folder. No database writes; the authenticated application's business pages were not exercised end to end.

## Test cleanup — 12 October 2026

At the user's request, removed the six attached audit/spelling/name test databases after verifying every database file was inside its designated repository test directory. Immediately after removal, MSSQLLocalDB contained only FMSC_DB among user databases; its student count remained 6,590. The live MDF/LDF in ProgramData were preserved.

Removed Tests, bin/ScenarioAudit_20261011, bin/SpellingAudit_20261011 and bin/SuggestionRegressionBaseline, including temporary copies, test backups, SQL scripts, private CSV extracts, regression harnesses and rendered test images. Also deleted the stopped FMSC_ReleaseFix_20261003 and FMSC_ReleaseSmoke_20261003 instances and their verified historical ReleaseTesting directory. Cleared orphan logs for those instances, AUDIT14_LOCALDB_TEST and FMSC_Corrective_20261003. MSSQLLocalDB and ProjectModels registrations remain.

Audit reports and historical FreezeRecovery backups were retained. Automatic approval review rejected broad deletion of bin/Validation and bin/ReleaseValidation because they may contain release artifacts; both build folders were preserved. Historical references to temporary test evidence in this report no longer resolve after the requested cleanup. A later connection check found that the live instance had been shut down; the server log records a shutdown requested by the user's login. It was not restarted for cleanup.