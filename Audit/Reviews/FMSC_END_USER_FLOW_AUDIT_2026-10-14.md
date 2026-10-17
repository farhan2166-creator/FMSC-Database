# FMSC_SMS end-user flow audit — 14 October 2026

Reviewed source: [`14aba80a8751322e04aa028f960e0a68efaf08f2`](https://github.com/farhan2166-creator/FMSC-Database/tree/14aba80a8751322e04aa028f960e0a68efaf08f2), the latest `main` checked both at the start and end of source review. Audit branch: `audit/end-user-flow`.

This is a **source-backed operator-flow review**, covering startup, the dashboard, each form/page/dialog, report workflows, retained compatibility screens, shared controls and all 20 RDLC templates. It is not an interactive Windows acceptance test. No application, installer, SQL, permission or financial behavior was changed.

## Result

**Nine confirmed source-level findings: two High, six Medium, one Low.** The most urgent are the certificate creation re-entry path and dialogs that can close during a database write. Another concrete reporting defect prevents daily closing summaries from printing when the day includes refunds or corrections.

The core workflows already have useful protections: explicit student/placement context, optional phone entry, pending registrations and family links, whole-rupee payment validation, transaction outcome recovery, prepared-photo retry, latest-read guards, report filter signatures, bounded logo/photo input, atomic export replacement and restore safety backups. Those should be preserved when fixing the gaps below.

“No additional finding” in the coverage tables means no further source-level flow gap was established in that review. It does not mean the screen passed live database, printer, DPI or accessibility acceptance.

## Confirmed findings

### UFA-01 — Certificate field changes can re-enable Create during an outstanding write

**High · Sports, Provisional and No Objection certificates.**

Operator sequence: select a student, complete a valid certificate, click Create, then change a still-enabled field before the database reply. For Sports, edit performance; for Provisional, edit a valid board/result field; for NOC, change the certificate date. Create can become enabled again and accept another click.

The click handlers disable only the student picker, records grid and Create button. Field-change handlers call `ApplyWorkflowState()`, which sets the picker and Create enabled without consulting `creatingCertificate`. The click handler checks the button's enabled state but does not refuse when `creatingCertificate` is already true. A second call also shares the same Boolean; completion of one call can release the shell busy guard while another call remains pending.

Evidence: [Sports handlers/state](../../Pages/Students/Certificates/SportsCertificatePage.cs#L56), [Sports field events](../../Pages/Students/Certificates/SportsCertificatePage.cs#L183), [Sports state](../../Pages/Students/Certificates/SportsCertificatePage.cs#L342); [Provisional handlers](../../Pages/Students/Certificates/ProvisionalCertificatePage.cs#L116), [events](../../Pages/Students/Certificates/ProvisionalCertificatePage.cs#L432), [state](../../Pages/Students/Certificates/ProvisionalCertificatePage.cs#L895); [NOC handlers](../../Pages/Students/Certificates/NoObjectionCertificatePage.cs#L49), [events](../../Pages/Students/Certificates/NoObjectionCertificatePage.cs#L171), [state](../../Pages/Students/Certificates/NoObjectionCertificatePage.cs#L322).

The reviewed DACPAC definitions of `CreateSportsCertificate`, `CreateProvisionalCertificate` and `CreateNoObjectionCertificate` each insert a new certificate per successful invocation; they do not deduplicate the submitted request. The procedure transaction protects each individual write, but does not prevent this second submission. No duplicate was created in a real database during this audit.

Fix scope: use one operation guard at the submission boundary; make all enabled-state calculations respect it; hold inputs, record changes, Refresh and Next Student for the full operation; capture the submitted values once. Test with a deliberately delayed, injected write and field-change/second-click attempts. Do not change certificate business rules merely to mask the UI issue.

### UFA-02 — Admission closure and legacy go-live dialogs can close while SQL is running

**High · Cancel Registration / Mark as Left and Finish Legacy Setup / Go Live.**

Operator sequence: click Apply or Confirm & Go Live, then use the title-bar close or Alt+F4 before the reply. The content is disabled, but there is no `FormClosing` busy guard. The custom window caption is outside that disabled content. The modal caller can resume with `Changed == false`, skip its refresh and dispose the dialog while the operation is still outstanding. SQL may complete even though the operator appears to have cancelled the window.

Evidence: [CloseAdmissionDialog](../../Pages/Students/Admissions/AdmissionLifecycleDialogs.cs#L4), [LegacyGoLiveDialog](../../Pages/Students/Admissions/AdmissionLifecycleDialogs.cs#L40), [admission caller](../../Pages/Students/Admissions/StudentAdmissionPage.cs#L3789), [legacy caller](../../Pages/Students/Maintenance/StudentDataReviewPage.cs#L65), [caption creation](../../Core/ApplicationWindowForm.cs#L49).

Closure Review also leaves its date/reason/reversal inputs editable during the preview read, allowing an obsolete response to become the displayed plan. The reviewed `CloseStudentAdmission` SQL snapshot includes the date, reason and reversal choice, so Apply rejects a mismatched plan; **this is not evidence that SQL applies an unreviewed financial plan**. `FinishLegacyAdmissionSetup` likewise enforces its reviewed snapshot.

Fix scope: an explicit busy flag and close guard for both preview/write lifetimes; invalidate obsolete preview replies; keep the result attached to the submitted operation and distinguish a confirmed write from a failed refresh. Test title-bar close, Escape and Alt+F4 with delayed replies and failures. Preserve the SQL snapshot and transaction checks.

### UFA-03 — Some embedded editors do not forward their leave protection

**Medium · Classes & Sections; the Bulk Fee Rate Changes tab.**

In Classes & Sections, type a class/section draft or start Save, then navigate Home, refresh the page or sign out. `ClassesAndSectionsWorkspacePage` implements neither `IUnsavedChanges` nor `IBusyPage`, despite its inner `MasterPage` editors implementing both. The shell examines only its current outer control.

For Bulk Fee Rate Changes, change the adjustment value in its embedded tab, then leave the fee-configuration workspace. Its `IsDirty()`/`FormClosing` guard protects the standalone modal form, but it does not implement `IUnsavedChanges`. Disposing an embedded form during navigation does not invoke its modal close guard. Merely switching a tab retains the editor and is not the loss being reported.

Evidence: [Classes & Sections host](../../Pages/Setup/Academics/ClassesAndSectionsWorkspacePage.cs#L3), [Master contracts](../../Core/MasterPage.cs#L6), [shell leave check](../../MainDashboardForm.cs#L1637), [fee workspace](../../Pages/Finance/Configuration/FeeConfigurationWorkspace.cs#L8), [Bulk close guard](../../Pages/Finance/Configuration/BulkFeeRateChangesForm.cs#L161), [Bulk dirty state](../../Pages/Finance/Configuration/BulkFeeRateChangesForm.cs#L590), [workspace aggregation](../../Core/WorkspacePage.cs).

Fix scope: forward the child contracts in the composite workspace; expose the existing Bulk dirty state through the same contract. Preserve the owner's explicit Clear/New behavior. Test actual shell leave, refresh and sign-out against both an entered draft and a delayed class/section save.

### UFA-04 — New Sports and Provisional certificate drafts are not tracked

**Medium · Entered certificate content can disappear without a leave warning.**

Select a student and enter a sport/performance, or a board result with roll number, marks and subjects, without saving it. Navigate away or select another student. Both pages report dirty state only when a **saved certificate ID is greater than zero**. Their new business-content draft is therefore invisible to shell leave and student-change protection.

Evidence: [Sports dirty state](../../Pages/Students/Certificates/SportsCertificatePage.cs#L6), [Sports selection guard](../../Pages/Students/Certificates/SportsCertificatePage.cs#L100), [Provisional dirty state](../../Pages/Students/Certificates/ProvisionalCertificatePage.cs#L6), [Provisional selection guard](../../Pages/Students/Certificates/ProvisionalCertificatePage.cs#L160). Compare the established [Regular Student draft handling](../../Pages/Students/Certificates/RegularStudentCertificatePage.cs#L10) and [Bank certificate baseline](../../Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs#L16).

Fix scope: track substantive new content as well as saved-record edits, using a baseline after the selected student's defaults finish loading. Leave date-only untouched dialogs consistent with the owner's accepted policy; do not introduce a prompt simply because someone opened a certificate page.

### UFA-05 — Admission and collection fields remain editable during submission

**Medium · Edits made while waiting can be silently overwritten.**

In Admission, click Save and type into a still-enabled name/contact/address/detail field while the SQL response is delayed. Save holds find/new/quick-selection and, for a new admission, numbering inputs; it does not hold the complete editor. The confirmed save subsequently reloads the student, replacing values typed after submission.

In Fee Collection, Process freezes student, enrollment and siblings, but leaves amount/discount/reference/remarks and allocation controls available. A change made during the outstanding collection is not part of its already-captured parameters; post-commit refresh/reset clears those entries. This can look as though the later entry was accepted when it was never submitted.

Evidence: [Admission Save lifetime](../../Pages/Students/Admissions/StudentAdmissionPage.cs#L4230), [new numbering hold](../../Pages/Students/Admissions/StudentAdmissionPage.cs#L4272), [existing save/reload](../../Pages/Students/Admissions/StudentAdmissionPage.cs#L4588), [collection hold](../../Pages/Finance/Collection/FeeCollectionPage.cs#L2611), [captured submission](../../Pages/Finance/Collection/FeeCollectionPage.cs#L3011), [post-commit reset](../../Pages/Finance/Collection/FeeCollectionPage.cs#L3190).

Fix scope: hold all inputs defining the submitted draft for the entire submission and refresh; preserve entered values on validation/failure; keep photo-only save/retry and confirmed receipt handling intact. This finding does **not** establish a wrong-student payment, partial family commit or database corruption.

### UFA-06 — Quick Navigation hides destinations in collapsed sidebar groups

**Medium · A permitted feature can appear to be missing.**

Collapse Students, or use the compact sidebar, then search for Quick Registration using Ctrl+K / Search features. Navigation passes the actual child buttons to the command palette, but the palette filters out `!button.Visible`. A child of a hidden/collapsed group reports `Visible == false`, although that page remains permitted. Selection also requires visibility and uses `PerformClick()` on the hidden sidebar control.

Evidence: [palette entry](../../MainDashboardForm.cs#L347), [collapsed child container](../../MainDashboardForm.cs#L900), [registered child destinations](../../MainDashboardForm.cs#L944), [palette filtering and selection](../../Core/CommandPaletteDialog.cs#L98).

Fix scope: build the palette from permitted navigation destinations independently of expansion state, and navigate through the destination callback. Do not expose forbidden destinations. Test all groups collapsed and compact sidebar, search by page name and keyboard selection.

### UFA-07 — Daily closing cannot print when adjustment rows have blank actual amounts

**Medium · A correctly submitted refund/correction day can fail to produce its summary.**

Operator sequence: close a day with Cash plus a Refund or Correction, enter actual amounts only for ordinary methods as instructed, submit it, then Print Summary. The print path verifies that **non-adjustment** actual amounts are present, but calculates the total over **all rows** with `Convert.ToDecimal(r["Actual Amount"])`. An adjustment's legitimate `DBNull` produces `InvalidCastException` before preview opens.

Evidence: [instruction and adjustment input rules](../../Pages/Finance/Collection/DailyCollectionClosingPage.cs#L43), [adjustment classification](../../Pages/Finance/Collection/DailyCollectionClosingPage.cs#L87), [submission excludes adjustment actuals](../../Pages/Finance/Collection/DailyCollectionClosingPage.cs#L152), [print total](../../Pages/Finance/Collection/DailyCollectionClosingPage.cs#L207).

Verified against the reviewed DACPAC: `GetDailyCollectionClosing` outer-applies actual amounts by method, leaving missing entries null; `SaveDailyCollectionClosing` treats adjustment methods separately. This is a valid null, not an incomplete ordinary-method entry.

Fix scope: reuse the established closing reconciliation semantics for adjustment rows and total calculation. Do not invent an adjustment actual amount or change payment accounting. Test cash-only, cash/refund, cash/correction, adjustment-only and missing ordinary actuals.

### UFA-08 — Daily closing accepts fractions but prints rounded whole amounts

**Medium · Screen, saved closing and printed summary can disagree.**

An ordinary actual amount such as Rs. 1.50 is permitted by the closing editor and `SubmitAsync()` (up to two decimal places). The reviewed SQL accepts `decimal(18,2)` inside its JSON actual amounts. The screen formats totals with `N2`; Print Summary passes `N0` to `GridPrintJob`, so that amount is shown rounded on the printed document.

Evidence: [editor amount guidance](../../Pages/Finance/Collection/DailyCollectionClosingPage.cs#L59), [screen totals](../../Pages/Finance/Collection/DailyCollectionClosingPage.cs#L120), [fraction acceptance](../../Pages/Finance/Collection/DailyCollectionClosingPage.cs#L161), [printed format](../../Pages/Finance/Collection/DailyCollectionClosingPage.cs#L220).

Fix scope: align closing inputs, stored invariants and print formatting with the owner's **whole-rupees-throughout** decision. If a database constraint is needed, first review existing synthetic/disposable closing data and put the reviewed SQL in a new migration. Do not silently round existing records or alter payment rules. No schema change was made during this audit.

### UFA-09 — Some shortcut availability differs from the destination's permissions

**Low · Unavailable actions remain inviting or lead to a permission failure.**

The dashboard's Student Reports quick action checks `Students.View`, while the reviewed `GetStudentRegister` requires `Reports.View` as well (and `Students.View` for detailed register modes). Editing Accountant permissions to retain student viewing but remove reporting leaves that quick action visible. Student Profile also creates its Manage Photo / family / maintenance shortcuts without destination permission gates; availability is based principally on selecting a student.

Evidence: [dashboard quick actions](../../Pages/Dashboard/WelcomePage.cs#L310), [profile shortcuts](../../Pages/Students/Profile/StudentProfilePage.cs#L245), [generic shortcut availability](../../Pages/Students/Profile/StudentProfilePage.cs#L286), [sidebar permission filtering](../../MainDashboardForm.cs#L863), [permission definitions](../../Core/PermissionCatalog.json).

The default Accountant currently has the intended viewing/report access. SQL still enforces write/report permissions; **this finding is not an authorization bypass**. It matters when the existing editable permission set changes, or when a shortcut promises a maintenance action to a viewing-only account.

Fix scope: derive shortcut visibility/enabled state from the same destination requirements used elsewhere, with an understandable read-only explanation where appropriate. Keep Administrator's full authority and Accountant's current approved grants.

## Additional improvements and manual acceptance

These are separate from the nine confirmed defects:

- **Responsiveness:** synchronous database paths remain in Possible Sibling Search, Promotion History, several certificate read/update/print paths, Copy/Bulk fee-rate tools and backup-file metadata reading. A slow engine or large result can block the UI thread. Convert measured bottlenecks to cancellable reads/guarded writes in small patches; do not assume splitting files or deleting themes improves speed. The synthetic timings below are not real-school SQL latency measurements.
- **Draft consistency in small dialogs:** Add Charge exposes `entry.HasDraft`, but its modal close guard currently checks only posting. Placement/date correction dialogs generally guard writes but vary in whether an entered reason is warned about on close. Decide meaningful typed-content leave behavior separately from date-only context and explicit Clear/New.
- **Receipt follow-through:** preserve the existing deliberate Print Receipt / Next Student flow, avoiding automatic print popups after payment. Confirm successful payment plus failed print remains clearly “recorded; reprint”, not “pay again”.
- **DPI, keyboard and low-end PC checks:** exercise 1366×768 at 100%, 125% and 150%; all standard/lightweight variants; long student/parent names; Tab/Enter/Escape/Ctrl shortcuts; blank/failed lookups; zero, one and many records. Source layout calculations alone cannot establish readability or acceptable RAM/CPU.
- **Printing:** render every template with synthetic data, especially same-name class sheets, multi-child/multi-placement receipts, long certificate wording and multipage financial reports. Check A4 portrait/landscape, thermal width, page selection, cancellation, unavailable printers and PDF overwrite denial.
- **Recovery:** on a disposable compatible database, test delayed replies, connection loss before/after commit, registration/payment/adjustment outcome checks, missing recovery-file access, backup disk-full and failed restore recovery. Test Database Connection Help / engine restart while a page is busy without touching real school data.
- **Separate existing database evidence:** the previously proposed migration-retirement review remains gated on database identity/type and independent supplied-database evidence. This UI audit's provenance check does not resolve that issue or approve installation against an unverified database pair. See the separate [verification PR #6](https://github.com/farhan2166-creator/FMSC-Database/pull/6).

## Scope and accepted behavior

The owner-approved immediate **Clear/New** discard is explicitly documented in `MasterPage`; it is not reported as a defect. Academic Program's explicit New reset is likewise distinguished from unguarded navigation loss. Switching a workspace tab normally keeps its loaded editor; it is not equivalent to discarding a page. Date-only context on an otherwise untouched dialog may be intentionally abandoned. Photos retain their compact 80 KB maximum and aspect-preserving preparation; no separate photo-management page is proposed. Original themes and their lightweight variants are preserved. No notification system, new product feature or role redesign is proposed.

The static inventory found 124 directly recognizable UI inheritance declarations across 115 C# files. That number includes framework classes and auxiliary controls; **it is not a claim of 124 user screens or 124 executed tests**. Transfer Section/Class/Campus and Readmission are additional named modal variants in the same action-dialog source. The coverage catalogue below lists source units individually, including retained/unrouted code, with inline generic modal prompts and preview windows recorded separately.

## Screen and shared-flow coverage

| Source unit / classes | Flow review and outcome |
|---|---|
| [AboutForm](../../Core/AboutForm.cs) | Read-only application/schema/runtime identity and clipboard feedback. |
| [ApplicationWindowForm](../../Core/ApplicationWindowForm.cs) | Shared modal caption, close/keyboard behavior and work-area fit; caption is outside page content (UFA-02). |
| [CommandPaletteDialog](../../Core/CommandPaletteDialog.cs) | Permission-filtered source list, text search and keyboard selection; expansion visibility gap UFA-06. |
| [ContextPage](../../Core/ContextPage.cs) | Campus/year/class/section lookup context and dependent selection flow; reviewed with admission callers. |
| [DatabaseConnectionForm](../../Core/DatabaseConnectionForm.cs) | Startup/help connection attempts, incompatible-schema guidance, details, retry/engine repair and close-busy guard; live engine acceptance pending. |
| [DateSelectionPanel](../../Core/DateSelectionPanel.cs) | Year/month/day selection, optional clear, min/max bounds and explicit use-date flow. |
| [HotKeysPage](../../Core/HotKeysPage.cs) | Searchable read-only shortcut catalogue; no data writes. |
| [KeyBadge](../../Core/KeyboardShortcutHints.cs) | Nonactivating shortcut hints, target visibility and badge disposal; keyboard/DPI acceptance pending. |
| [MasterPage](../../Core/MasterPage.cs) | Shared entry/records/filter/reorder/delete/save behavior; dirty and saving contracts. Explicit Clear/New discard is owner-approved. |
| [ReportColumnDialog](../../Core/ReportColumnDialog.cs) | Tick/reorder within categories, defaults, at-least-one-column validation; applies only on OK. |
| [ReportNameDialog](../../Core/ReportNameDialog.cs) | Saved-view name prompt, trim/blank validation and cancel; persistence occurs in caller. |
| [StudentPhotoViewForm](../../Core/StudentPhotoViewForm.cs) | Read-only zoom snapshot, aspect fit and independent image disposal; no photo/database write. |
| [StudentPickerPopupForm](../../Core/StudentPickerPopupForm.cs) | Nonactivating picker popup; selection/clearing/guard behavior reviewed through StudentPicker callers. |
| [V2Page](../../Core/V2Page.cs) | Shared actions, busy scope, status/validation, report loading/staleness/pager, column settings, preview and durable export. |
| [WorkspacePage](../../Core/WorkspacePage.cs) | Lazy child tabs, retained editor state and child dirty/busy aggregation; embedded Bulk editor gap UFA-03. |
| [MainDashboardForm](../../MainDashboardForm.cs) | Menu permissions, location/breadcrumb, page state/disposal, home/refresh/account/sign-out/close; UFA-03 and UFA-06. |
| [AccessEditorPage](../../Pages/Administration/AccessEditorPage.cs) | User/role drafts, exact selected identity, stale access token, read-only protected Administrator, save/outcome-check/reload feedback. |
| [AuditLogsPage](../../Pages/Administration/AuditLogsPage.cs) | User/table/date/text filters, newest run/filter checks, export and documented retention/pruning; no new finding established. |
| [BackupRestorePage](../../Pages/Administration/BackupRestorePage.cs) | Folder persistence/relink, backup history/file selection, destructive restore review, busy guard and restart; safety-copy/compatibility/rollback service traced. |
| [MigrationBillingCleanupPage](../../Pages/Administration/MigrationBillingCleanupPage.cs) | Retained historical billing-start page; no live construction/navigation caller found. Async result/disposal and one-time scope reviewed; not a fresh reachable-screen blocker. |
| [PasswordResetDialog](../../Pages/Administration/PasswordResetDialog.cs) | Password-only validation and write, input hold, close/draft guards and uncertain-save handoff to Users editor. |
| [RolesPage](../../Pages/Administration/RolesPage.cs) | Only Accountant description/grants editable; protected full Administrator; inherited draft/save/outcome flow. |
| [UsersPage](../../Pages/Administration/UsersPage.cs) | Existing/new user mode, password confirmation, assigned Accountant access, reset of clean saved users; inherited recovery and leave guards. |
| [WelcomePage](../../Pages/Dashboard/WelcomePage.cs) | Async/cancellable counts/year/readiness, location and permission-based quick actions; Student Reports shortcut mismatch UFA-09. |
| [AddAdditionalFeeDialog / AdditionalFeeEntry](../../Pages/Finance/Billing/AddAdditionalFeeDialog.cs) | Current placement charge editor, request identity and posting close guard; typed-draft close consistency is an improvement item.<br>Same shared charge editor: fee/amount/discount/reason/date validation, held inputs, request ID, confirmed posted event. |
| [AdditionalFeePostingPage](../../Pages/Finance/Billing/AdditionalFeePostingPage.cs) | Scope/read generation, explicit checked students, amount/reason review, captured request ID and posting hold. |
| [FeePostingPage](../../Pages/Finance/Billing/FeePostingPage.cs) | Bounded billing months, eligible-row keys, explicit ticked selection, preview freshness, posting hold and server counts/partial-result feedback. |
| [FeePostingReversalPage](../../Pages/Finance/Billing/FeePostingReversalPage.cs) | Scope/month preview, collected-money exclusions, reason/confirmation, held inputs and server reversal/blocked outcomes. |
| [ReverseChargeDialog](../../Pages/Finance/Billing/ReverseChargeDialog.cs) | Single charge/reason confirmation, permission-backed write, save/close/draft guard and reversed result. |
| [StudentPurchasesPage](../../Pages/Finance/Billing/StudentPurchasesPage.cs) | Student/placement context, one-off charge draft, selection discard, held posting, refreshed recent-charge list. |
| [DailyCollectionClosingPage](../../Pages/Finance/Collection/DailyCollectionClosingPage.cs) | Load/submit/revision/approve/history/print, captured payment snapshot, entered actuals and draft/busy protection; UFA-07 and UFA-08. |
| [FeeCollectionPage](../../Pages/Finance/Collection/FeeCollectionPage.cs) | Student/enrollment/family, allocation/discounts, confirmation, atomic family collection, durable uncertain outcome, result/receipt/reprint and next-student flow; UFA-05. |
| [ApplyFeeConfigurationPage](../../Pages/Finance/Configuration/ApplyFeeConfigurationPage.cs) | Captured preview scope and month, conflicts, reviewed apply, per-group partial/interrupted outcome; live path is guarded matrix modal. |
| [BulkFeeRateChangesForm](../../Pages/Finance/Configuration/BulkFeeRateChangesForm.cs) | Scope/value/frequency/rounding preview, checked plan, atomic apply and stale preview rejection; embedded leave gap UFA-03. |
| [CopyFeeRatesForm](../../Pages/Finance/Configuration/CopyFeeRatesForm.cs) | Source/destination scope, skip/overwrite choices, explicit reviewed plan and atomic copy; successful embedded refresh. |
| [FeeRateDetailsDialog](../../Pages/Finance/Configuration/FeeRateDetailsDialog.cs) | One matrix-cell amount/frequency/remark edit; no SQL until the matrix saves. |
| [FeeRateMatrixForm](../../Pages/Finance/Configuration/FeeRateMatrixForm.cs) | Cell validation, own dirty/saving contract, campus-change guard, full-plan save, details/copy/bulk/apply tools; host contracts present. |
| [FeeTypesPage](../../Pages/Finance/Configuration/FeeTypesPage.cs) | Shared Master editor with fee ordering, default frequency, dependency-safe delete and changed notifications. |
| [FeeAssignmentsDialog / AddFeeAssignmentForm / DiscontinueFeeAssignmentForm / ChangeFeeAssignmentAmountForm / ChangeFeeDiscountForm](../../Pages/Finance/StudentAccounts/FeeAssignmentsDialog.cs) | Placement-scoped recurring fees/discount/transport panel; holds close during pending posting/review.<br>Available recurring fee or transport restart selection; configured options, explicit add and permission-backed save.<br>Transport-stop month and consequence, reason, saved outcome and close/draft guard.<br>Whole-rupee future rate, carried discount floor, effective-month consequence, reason and close/draft guard.<br>Whole-rupee discount or full period waiver, monthly boundaries, confirmed write and close/draft guard. |
| [FeeHistoryDialog](../../Pages/Finance/StudentAccounts/FeeHistoryDialog.cs) | Read-only modal wrapper for student statement; cancellation/disposed-read behavior inherited from page. |
| [FutureFeesDialog](../../Pages/Finance/StudentAccounts/FutureFeesDialog.cs) | Read-only projection horizon and refresh; failed read clears the stale list. |
| [PendingStudentFeesDialog](../../Pages/Finance/StudentAccounts/PendingStudentFeesDialog.cs) | Current/historical month eligibility, individually checked fees, repost warning, reason, guarded posting and refreshed result. |
| [StudentFeeAdjustmentsPage](../../Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs) | Exact selected payment eligibility, refund/correction/reversal semantics, reason/default-No confirmation, held inputs and durable Check Save Outcome. |
| [StudentFeeAssignmentsPanel](../../Pages/Finance/StudentAccounts/StudentFeeAssignmentsPanel.cs) | Exact recurring row identity, add/start/stop transport, amount/discount periods, pending-posting review and changed notifications. |
| [AnnualFeeReceiptsPage](../../Pages/Reports/Finance/AnnualFeeReceiptsPage.cs) | Student/year readiness, held preparation, oldest-first receipts, actual student/placement grouping and current balance enrichment. |
| [BillingDiscountsReportPage](../../Pages/Reports/Finance/BillingDiscountsReportPage.cs) | Canonical billing summary/detail/discount views, date/location grouping and captured totals/print/export. |
| [BillingStatusReportPage](../../Pages/Reports/Finance/BillingStatusReportPage.cs) | Year/campus/as-of snapshot, cancellation/stale invalidation and explicit at-least-one-fee-posted wording. |
| [CanonicalFinanceReportPage](../../Pages/Reports/Finance/CanonicalFinanceReportPage.cs) | Async captured report dataset/totals, optional dates, view/group/payment filters, saved scope and stale print/export protection. |
| [CollectionsReceiptsReportPage](../../Pages/Reports/Finance/CollectionsReceiptsReportPage.cs) | Canonical receipts/transactions/summary, method/date/location and server reconciled totals. |
| [FeeReminderSelectionDialog](../../Pages/Reports/Finance/FeeReminderSelectionDialog.cs) | Checked students versus shown filter, paper choice, optional pay-by/message and clear selection; balances refreshed in caller. |
| [OutstandingAgingReportPage](../../Pages/Reports/Finance/OutstandingAgingReportPage.cs) | Student/charge/aging/family snapshots; separately captured class A4 and reminder scope, refreshed selected balances. |
| [StudentAccountStatementPage](../../Pages/Reports/Finance/StudentAccountStatementPage.cs) | Individual/family selection, dates, opening balance, sequential reads, generation/freshness guard, print/CSV/PDF and failed-refresh export protection. |
| [UnpostedFeesReportPage](../../Pages/Reports/Finance/UnpostedFeesReportPage.cs) | Year/campus/as-of missing-charge query, captured signature, cancellation, stale pager/export invalidation. |
| [ReportsHomePage](../../Pages/Reports/ReportsHomePage.cs) | Searchable student/finance report destinations and read-access availability; native card fit acceptance pending. |
| [AdmissionsReportPage](../../Pages/Reports/Students/AdmissionsReportPage.cs) | Retained preset wrapper around unified Student Reports; no separate active constructor caller found. |
| [ClassSheetsPage](../../Pages/Reports/Students/ClassSheetsPage.cs) | Campus/year/class/section, checked roster and hidden checked students, captured exam/month metadata, scoped multi-class preview. |
| [CurrentStudentsReportPage](../../Pages/Reports/Students/CurrentStudentsReportPage.cs) | Retained Student Reports list preset wrapper; no separate active constructor caller found. |
| [StudentMovementReportPage](../../Pages/Reports/Students/StudentMovementReportPage.cs) | Retained Student Reports movement preset wrapper; no separate active constructor caller found. |
| [StudentReportsPage](../../Pages/Reports/Students/StudentReportsPage.cs) | List/admission/strength/movement, independent admission-number lookup, grouping/sorting/comparison, columns and durable saved views/exports. |
| [StudentStrengthReportPage](../../Pages/Reports/Students/StudentStrengthReportPage.cs) | Retained Student Reports strength preset wrapper; no separate active constructor caller found. |
| [ChangePasswordForm](../../Pages/Security/ChangePasswordForm.cs) | Password policy/confirmation, own-password write and forced-change flow, saved outcome and busy/draft close guards. |
| [ChangePasswordHostPage](../../Pages/Security/ChangePasswordHostPage.cs) | Dashboard account entry opens the established guarded password modal. |
| [ChangeUsernameForm](../../Pages/Security/ChangeUsernameForm.cs) | Trim/required/unchanged validation, captured own username, rename feedback and busy/draft close guards. |
| [FirstRunSetupForm](../../Pages/Security/FirstRunSetupForm.cs) | No-admin startup branch, required username/password confirmation, create response and explicit first-run discard guidance. |
| [LoginForm](../../Pages/Security/LoginForm.cs) | Async captured sign-in, input/close hold, validation/connection-help retry and session/forced-password continuation. |
| [AcademicProgramManagerForm](../../Pages/Setup/Academics/AcademicProgramManagerForm.cs) | Program type/code/name, selected-record/close discard guards, save and selected result; explicit New reset distinguished from leave loss. |
| [AcademicYearsPage](../../Pages/Setup/Academics/AcademicYearsPage.cs) | Shared Master date range/current-year handling, meaningful date choices and server-backed save. |
| [CampusClassSectionAssignmentsPage](../../Pages/Setup/Academics/CampusClassSectionAssignmentsPage.cs) | Campus scope, classes/sections matrix, dirty campus change, synchronous apply plan, result and baseline; no async save-lifetime gap claimed. |
| [ClassesAndSectionsWorkspacePage](../../Pages/Setup/Academics/ClassesAndSectionsWorkspacePage.cs) | Combined editor host and class/section child context; missing shell contracts UFA-03. |
| [ClassesPage](../../Pages/Setup/Academics/ClassesPage.cs) | Master class type/program/ordering fields, guarded record changes and dependency-aware delete; composite host affected by UFA-03. |
| [SectionsPage](../../Pages/Setup/Academics/SectionsPage.cs) | Master section definition/ordering and dependency-aware delete; composite host affected by UFA-03. |
| [SubjectsPage](../../Pages/Setup/Academics/SubjectsPage.cs) | Master subject name/code/order flow and saved baseline. |
| [CampusesPage](../../Pages/Setup/CampusesPage.cs) | Master campus definition, records/filter and saved baseline. |
| [SchoolOfficialsPage](../../Pages/Setup/SchoolOfficialsPage.cs) | Master school official fields, validation/save and saved baseline. |
| [SchoolProfilePage](../../Pages/Setup/SchoolProfilePage.cs) | Loaded-profile precondition, fields/email, bounded logo selection/removal, save/reload and dirty baseline. |
| [SetupReadinessPage](../../Pages/Setup/SetupReadinessPage.cs) | Async readiness checks and permission-aware route guidance to missing setup. |
| [BankAccountOpeningCertificatePage](../../Pages/Staff/Certificates/BankAccountOpeningCertificatePage.cs) | Staff/bank/income/purpose, meaningful draft, full-operation hold, durable creation outcome, saved-ID print and new/next guards. |
| [RegularLecturerCertificatePage](../../Pages/Staff/Certificates/RegularLecturerCertificatePage.cs) | Staff/role/subject, loaded/new baseline, selected certificate update/print and guarded reset/student change. |
| [StaffEmploymentDialog](../../Pages/Staff/StaffEmploymentDialog.cs) | Captured staff identity, selected employment, dirty/busy close forwarding and saved event. |
| [StaffEmploymentOverview](../../Pages/Staff/StaffEmploymentOverview.cs) | Generation-safe read-only active/history display and permission-gated add/edit modal. |
| [StaffEmploymentPage](../../Pages/Staff/StaffEmploymentPage.cs) | Master job dates/type/status, parent staff context, selected exact saved job and chronology validation. |
| [StaffManagementPage](../../Pages/Staff/StaffManagementPage.cs) | Staff editor and overview composition; forwards staff dirty/busy state before employment/navigation. |
| [StaffProfilePage](../../Pages/Staff/StaffPage.cs) | Name/DOB/optional identity/contact fields, automatic staff number, save/view permissions and selected/saved events. |
| [CloseAdmissionDialog / CompleteAdmissionDialog / LegacyGoLiveDialog](../../Pages/Students/Admissions/AdmissionLifecycleDialogs.cs) | Preview/apply closure or registration cancellation, record/fee impact; close lifetime/obsolete preview gap UFA-02.<br>Official number review/normalization, retained bill/payment/family guidance; confirmation write occurs in Admission caller.<br>Explicit dates/optional replacement, blocker preview, one-time activation and SQL snapshot; modal write-close gap UFA-02. |
| [QuickRegistrationPage / QuickRegistrationFormPage](../../Pages/Students/Admissions/QuickRegistrationPage.cs) | Registration/pending tabs, unresolved-save restriction, form/child busy aggregation and form draft forwarding.<br>Optional phone, basic/placement/opening bill, add/remove fee, parent link/unlink, new siblings, saved-record refresh and durable outcome check. |
| [StudentAdmissionPage](../../Pages/Students/Admissions/StudentAdmissionPage.cs) | Find/new/edit/pending confirmation, placement actions, fees/siblings, compact photo retry, admission confirmation and prints; UFA-05. |
| [StudentPlacementCorrectionDialog](../../Pages/Students/Admissions/StudentPlacementCorrectionDialog.cs) | Selected placement versus new missing record, review snapshot, preserved finances and guarded write; typed-reason close consistency remains an improvement item. |
| [CharacterCertificatePage](../../Pages/Students/Certificates/CharacterCertificatePage.cs) | Student/wording/draft, issuance/collection, latest selected issue and SSC/HSSC print wording. |
| [CollegeLeaveCertificatePage](../../Pages/Students/Certificates/CollegeLeaveCertificatePage.cs) | Placement-specific leave, chronology, fee impact review, original/duplicate issuance, collection and print. |
| [DateOfBirthCertificatePage](../../Pages/Students/Certificates/DateOfBirthCertificatePage.cs) | DOB eligibility, date-only context, original/duplicate issue, exact collection selection and print permission. |
| [LeaveCertificatePage](../../Pages/Students/Certificates/LeaveCertificatePage.cs) | Current/retroactive placement leave, meaningful draft, fee review, duplicate limits, collection and print selection. |
| [NoObjectionCertificatePage](../../Pages/Students/Certificates/NoObjectionCertificatePage.cs) | Create/update/select/print and date-only draft policy; outstanding-create re-entry gap UFA-01. |
| [ProvisionalCertificatePage](../../Pages/Students/Certificates/ProvisionalCertificatePage.cs) | Board/exam/roll/marks/grade/subjects validation and selected print; UFA-01 and UFA-04. |
| [RegularStudentCertificatePage](../../Pages/Students/Certificates/RegularStudentCertificatePage.cs) | Class-specific create/update/print, existing certificate selection, meaningful baseline, new/reset guards and status warning. |
| [SportsCertificatePage](../../Pages/Students/Certificates/SportsCertificatePage.cs) | Sport/performance suggestions, create/update/saved selection/print; UFA-01 and UFA-04. |
| [StudentCertificatesPage](../../Pages/Students/Certificates/StudentCertificatesPage.cs) | Permission-based certificate destinations; shell/modal creation and modal leave forwarding. |
| [PossibleSiblingSearchForm](../../Pages/Students/Dialogs/PossibleSiblingSearchForm.cs) | Current/candidate family identities, search/exclusion, explicit same-parent review, existing-family warning, link and refreshed feedback. |
| [StudentSearchDialog](../../Pages/Students/Dialogs/StudentSearchDialog.cs) | Admission-number versus location/name/status filters, captured signature/latest result, clear selection and choose/create result. |
| [BulkPromotionPage](../../Pages/Students/Enrollment/BulkPromotionPage.cs) | Source/destination/year/date, explicit promote/hold selection, section mapping, fee prerequisites, full-operation hold and server count/partial-outcome guidance. |
| [ConfirmReturnAdmissionDialog](../../Pages/Students/Enrollment/ConfirmReturnAdmissionDialog.cs) | Promoted student modal wrapper, substantive draft and confirmation busy forwarding. |
| [ConfirmReturnAdmissionPage](../../Pages/Students/Enrollment/ConfirmReturnAdmissionPage.cs) | Selected returning placement, opening fee choices/transport/date, guarded confirmation, refreshed pending list and bill/payment next steps. |
| [PromotionHistoryPage](../../Pages/Students/Enrollment/PromotionHistoryPage.cs) | Read-only completed batches, filters applied to grid/export, stale export gating and clear. |
| [PromotionSectionMappingDialog](../../Pages/Students/Enrollment/PromotionSectionMappingDialog.cs) | Explicit source-section destinations, matching suggestions, all-row validation and reviewed mapping summary. |
| [ReadmissionFeeDecisionDialog](../../Pages/Students/Enrollment/ReadmissionFeeDecisionDialog.cs) | Old debt/month charges versus reviewed new rates, explicit monthly start/yearly decision and admission-number policy; caller owns write. |
| [StudentEnrollmentActionDialog](../../Pages/Students/Enrollment/StudentEnrollmentActionDialog.cs) | Shared exact-student modal for Transfer Section, Transfer Class, Transfer Campus and Re-Admit; refuses close while execution pending. |
| [StudentTransferReadmissionPage](../../Pages/Students/Enrollment/StudentTransferReadmissionPage.cs) | Captured source/destination, permitted operations, no-op/chronology restrictions, fee decisions, returned outcome and refreshed placement; live caller is action modal. |
| [TransferFeeSyncDialog](../../Pages/Students/Enrollment/TransferFeeSyncDialog.cs) | Keep/next/current/custom-month fee choice, billed-period and schedule/waiver conflict warnings; explicit replacement choice. |
| [DuplicateStudentAuditPage](../../Pages/Students/Maintenance/DuplicateStudentAuditPage.cs) | Scan, explicit candidate-pair identity and confirm/dismiss batch, serialized hold, per-row failure summary; no merge/delete of school records. |
| [EnrollmentDateCorrectionDialog](../../Pages/Students/Maintenance/EnrollmentDateCorrectionDialog.cs) | Captured date/reason/options preview, held fields, exact snapshot apply and close-busy protection. |
| [LeavingDateCorrectionDialog](../../Pages/Students/Maintenance/LeavingDateCorrectionDialog.cs) | Related leaving/withdrawal/certificate date review, preserved finances, acknowledged history and guarded apply. |
| [LifecycleDateRepairDialog](../../Pages/Students/Maintenance/LifecycleDateRepairDialog.cs) | Selected placement/date/certificate changes, meaningful review/reason/finance acknowledgement, exact snapshot and held write. |
| [StudentDataReviewPage / AdmissionReviewListPage](../../Pages/Students/Maintenance/StudentDataReviewPage.cs) | Lazy review tabs, child draft/busy forwarding and completed legacy-tab state.<br>Pending/legacy lists, literal filters, visible family/temp reference/combined placement, deliberate selection, review modal and go-live caller. |
| [StudentRecordChecksPage](../../Pages/Students/Maintenance/StudentRecordChecksPage.cs) | Findings/placement/timeline modes, current draft/busy contract, reason/impact confirmation, missing-record checks and snapshot-aware correction routes. |
| [StudentFamilyPage](../../Pages/Students/Profile/StudentFamilyPage.cs) | Exact selected family/member cards, add/remove review, held family changes and refreshed membership; empty-family route guidance. |
| [StudentProfilePage](../../Pages/Students/Profile/StudentProfilePage.cs) | Generation/cancellation-safe details/photo/family/timeline/balance, read-only identity, contextual shortcuts/history; permission UX UFA-09. |
| [CurrentEmploymentPanel](../../Pages/Staff/CurrentEmploymentPanel.cs) | Retained employment editor: own draft/write/read generations and held save; current live Staff Management uses the read-only overview plus modal editor. No active construction caller found. |
| [LeaveFeeReviewBar / ClearanceUi](../../Pages/Students/Certificates/LeaveFeeReviewBar.cs) | Leave/certificate fee-impact preview, relevant unpaid/paid history and explicit reviewed choices; reviewed with both leave pages. |
| [TransferSectionDialog / TransferClassDialog / TransferCampusDialog / StudentReadmissionDialog](../../Pages/Students/Enrollment/StudentEnrollmentActionDialog.cs) | Four concrete modal variants use the shared exact-student workflow and execution close guard described above. |
| [Collector prompts](../../Pages/Students/Certificates/CharacterCertificatePage.cs) | Character, DOB, school-leave and college-leave pages build generic modal collector-name prompts; captured issue, validation/cancel and collection result traced in each caller. |
| [Payment history / detail / fee-change-history modals](../../Pages/Finance/Collection/FeeCollectionPage.cs) | Read-only table modals, selected-payment/receipt identity and reprint; fee-change history uses captured student and permissions. |
| [Daily closing history modal](../../Pages/Finance/Collection/DailyCollectionClosingPage.cs) | Captured day, read-only revision history and busy lifetime. |
| [Registration review modal](../../Pages/Students/Maintenance/StudentDataReviewPage.cs) | Generic host forwards Admission busy/draft contracts via ModalPageCloseGuard and refreshes pending rows after confirmation/cancellation. |
| [Apply-to-students modal](../../Pages/Finance/Configuration/FeeRateMatrixForm.cs) | Matrix captures saved fee scopes; host checks IsApplying before closing and reports per-group outcomes. |
| [RDLC preview / print](../../Core/RdlcReportPreview.cs) | Shared modal readiness/failure, keyboard page/zoom, all/current-page print, setup and atomic PDF export; printer/render acceptance pending. |
| [Grid/PDF/CSV report helpers](../../Core/ReportExportBuilder.cs) | Shared visible-column order/captions, all captured report rows, CSV formula safety, atomic file replacement and failed-persistence feedback. |

## RDLC template coverage

All 20 XML definitions parsed successfully. Static `Fields!…Value` references resolve to fields declared in their datasets. This check does not validate live procedure result columns, dynamic layout modifications, VB rendering, page fit or actual printer output.

| Template | Dataset | Reviewed caller / flow |
|---|---|---|
| AdmissionBill | FeeReceiptData | Admission bill and Quick Registration family bill; individual/family rows, actual/nominal presentation. |
| AdmissionForm | AdmissionFormData | Admission print; captured student details and school identity. |
| BankAccountOpeningCertificate | BankAccountOpeningCertificateData | Staff bank certificate; saved ID, recovery and dirty/print restrictions. |
| CharacterCertificate | CharacterCertificateData | Create → issue → collect → print; explicit SSC/HSSC wording. |
| ClassAttendanceSheet | ClassSheetData | Selected roster; exam metadata; grouped class/section pages. |
| ClassMarksSheet | ClassSheetData | Selected roster; same-name/father display and handwritten marks. |
| ClassMonthlyAttendanceSheet | ClassSheetData | Selected month and roster; blank attendance register, landscape flow. |
| ClassOutstandingCharges | ClassOutstandingData | Outstanding report's independent scope capture and A4 preparation. |
| ClassSectionSummary | ClassSummaryData | Student report's class/section summary with captured filters. |
| CollegeLeaveCertificate | LeaveCertificateData | Placement-scoped leave, fee review, duplicate issue and collection. |
| DateOfBirthCertificate | DateOfBirthCertificateData | Stored DOB eligibility, issue/collection selection and print gate. |
| FeeAgingReport | FeeAgingData | Retained static template; current canonical aging grid uses shared generated report preview. No active direct caller found. |
| FeePaymentReceipt | FeeReceiptData | Individual/family/reprinted/annual receipts; grouping by student and placement. |
| FeeReminder | FeeReceiptData | Checked-student selection, refreshed balances, optional message/date, thermal or A4. |
| LeaveCertificate | LeaveCertificateData | School leave and retroactive leave; placement scope and issuance history. |
| NoObjectionCertificate | NoObjectionCertificateData | Saved certificate print; creation re-entry gap UFA-01. |
| ProvisionalCertificate | ProvisionalCertificateData | Saved board result/subjects; UFA-01 and UFA-04. |
| RegularLecturerCertificate | RegularLecturerCertificateData | Staff wording, dirty baseline and saved-ID print. |
| RegularStudentCertificate | RegularStudentCertificateData | Class-scoped saved certificates, deliberate new draft and status warning. |
| SportsCertificate | SportsCertificateData | Saved sport/performance and saved-ID print; UFA-01 and UFA-04. |

Runtime RDLC definitions generated by `GridReportPreview`, certificate letterhead, receipt layouts and annual receipt layouts were also traced through shared preview/export/print helpers. Native rendering remains a manual acceptance item.

## Checks actually performed

On Linux, against the reviewed source:

| Check | Actual result | Boundary |
|---|---|---|
| `dotnet build FMSC_SMS.sln -c Release --no-restore -p:EnableWindowsTargeting=true` | Passed, 0 warnings / 0 errors. | Compiles Windows application; does not run WinForms. |
| Build `Tests/ThemeSmokeChecks` Release with Windows targeting | Passed, 0 warnings / 0 errors. | Compilation only locally. |
| `dotnet run --project Tests/AuditFixChecks/AuditFixChecks.csproj -c Release` | Passed: 650 checks across 14 reported groups. | Existing portable money, permissions, guidance, settings/export durability, drafts and recovery helpers. Does not reproduce the new UI findings. |
| `dotnet run --project Tests/PerformanceChecks/PerformanceChecks.csproj -c Release` | Passed: 32,183 synthetic checks across four groups. | Cache/loading/pagination/latest-read and statement assembly, not production SQL or PC RAM profiling. Restore emitted NU1900 because the local NuGet vulnerability feed was unreachable; compilation and execution passed. |
| `python Tools/verify_schema_artifact.py --check` | Passed: reviewed package/model checksum, inventory, whole-rupee checks, 273 SQL module fingerprints. | No DACPAC replacement or approval-baseline update. |
| `python Tools/test_audit_schema.py` | Passed: 12 tests. | Offline schema utility checks. |
| `python Tools/test_permission_policy.py` | Passed: 4 tests. | Existing permission policy checks. |
| XML parse and static declared-field reference review | Passed: all 20 RDLC files, no unknown static field references. | Not a render/print test. |
| Read-only DACPAC module inspection | Confirmed certificate insert behavior, closure/go-live snapshot protection, closing null/fraction semantics and register permissions. | Definitions only; no school rows printed or database mutations performed. |

Existing Windows CI is intentionally limited to the focused `--post-fix` control contracts. It does not run every form or prove that the newly identified sequences pass. The full interactive GUI/DPI/printer suite and disposable SQL fault scenarios remain pending. A passing build or the counts above must not be described as a bug-free application.

GitHub CI execution and its actual status are recorded in the audit pull request. Read the linked run's conclusion; this report does not assume a future run will pass.

## Suggested fix order

1. UFA-01: certificate submission guard and enabled-state consistency.
2. UFA-02: admission closure/go-live modal lifetime and result reporting.
3. UFA-07: daily closing summaries with refund/correction rows.
4. UFA-03 and UFA-04: host forwarding and substantive certificate draft tracking.
5. UFA-05 and UFA-06: submission input locking and permission-aware quick navigation.
6. UFA-08 and UFA-09: closing monetary consistency and shortcut permission consistency.

Keep each fix small and separately reviewable, with focused injected-delay or synthetic fixtures and actual Windows execution where required. The audit records findings; it does not implement or merge these changes.
