# FMSC: comprehensive independent billing and system audit

Read this file as your task instructions. Audit the current repository and produce an evidence-based report. Act as a senior SQL Server database architect, financial systems engineer, C# WinForms reviewer and school-software designer.

## Objective and authority

We have introduced automatic billing into an established school management system. Determine whether the actual implementation is financially correct, safe, consistent with the approved rules, and practical for school staff. Find defects and omissions, including interactions with older code. Do not assume the implementation is correct because its tests pass.

This is an audit, not implementation approval. Do not modify application code, production SQL, migrations, configuration, schemas, DACPACs or compatibility manifests. Do not deploy, push, merge, activate students, post school fees, remove procedures or modify a school database. You may run safe tests and create temporary synthetic reproduction scripts in a disposable workspace. Your only intended repository change is the audit report at `Audit/FMSC_BILLING_SYSTEM_AUDIT.md`.

Do not send real email/SMS, contact live payment providers or enable outbound automations during testing. Use the existing fake/offline providers and synthetic fixtures.

Do not rebuild or redesign the system. Recommend the smallest practical corrections using existing tables, procedures, transactions, permissions and UI. Distinguish a demonstrable defect from an optional improvement, an unresolved policy question and an environment limitation.

## Establish the actual current state first

1. Read applicable `AGENTS.md` instructions, `Docs/BillingImplementation.md`, current C#/SQL, deployment helpers, tests and relevant workflows. Documentation records intent and previous results; implementation and reproducible evidence determine your conclusions.
2. Record the audited commit. Compare the billing implementation against its earlier foundation when useful: original commit `9ef057c312d0c85cfac379969eb037a8d973b0f7`; implementation commit `7e9731e383de5f6059cbac91d8d9b46ef71b0b29`. Audit current HEAD, including changes after these commits.
3. C# currently requires database schema **19**. Migrations **7–13** were earlier pending changes; **14–19** introduce this billing design. Review all in order, including dependencies and superseded procedure definitions. The final effective definition matters, not an earlier definition with the same name.
4. The developer applied migrations 7–19 to the local restored school database after a verified recovery backup. Application installation verification and CHECKDB passed; that is previous local evidence, not cloud verification. Do not attempt to access that database.
5. The root `fmsc.dacpac` is a fresh **schema-only version 19** database snapshot. Its package metadata may retain the extraction tool's default `1.0.0.0`; assess actual schema objects rather than treating that label as the application's schema version. `Tests/LifecycleSqlChecks/Baseline/schema.dacpac` is a separate older, data-free regression fixture intentionally used to test upgrading through the numbered migrations. Do not replace the test fixture or confuse the two packages. A schema-only extraction has no `DatabaseVersion` rows, school configuration or student data; package metadata alone does not seed runtime version state.
6. Release packaging uses a user-supplied detached MDF/LDF pair beside the project. Their absence in cloud development is not a release blocker. Assess fresh installation, upgrades, backup/restore and recovery separately; do not invent a requirement to publish live school data or database backups.

## Approved business rules: use these as the acceptance criteria

### Account model and automatic billing

- Student identity persists across enrolments. Obligations and their settlement use `StudentFeeAssignments`, `BillingCharges`, `FeePayments` and `FeePaymentAllocations`.
- Outstanding liability is net recorded charges minus net payments applied. Distinguish cash received, money applied, unused credit, refunds and reversals. They are not interchangeable totals.
- Generate eligible monthly obligations when an authenticated user with the appropriate existing collection authority opens the app; check again after reconnection and month change. No always-running service or independent scheduler is required.
- Payment, including a zero-value payment, is not required to establish a bill. Fully free monthly assignments should not manufacture zero-value charges or payments.
- Require explicit approved `BillingStartDate`, billing readiness, completed legacy activation, confirmed placement, active student/enrolment, valid academic year and effective assignment. Do not infer an old commencement date from historical enrolment or admission.
- Preserve legacy reference students, even if labelled Active. Missing commencement/readiness means exclusion, not automatic activation. Show useful exceptions without treating every retained legacy record as an operator failure.
- Activation of automation establishes a recovery boundary; it must not create years of legacy arrears. Current-month billing is automatic. An administrator can review missed months and choose to bill them too or bill this month only. Show exact months, student/charge counts and amounts. Deferred gaps remain available for deliberate recovery without repeatedly prompting on every opening.
- Automatically cancelled/reversed fees must not reappear. Explicit authorized manual recovery can charge them again with a reason. Duplicate monthly obligations remain prohibited.
- Accrual ends with the academic year, even if someone remained Active because staff lost track. Promotion is explicitly selected and return confirmation still controls eligibility.

### Collection, discounts and rate changes

- Suggest the **oldest outstanding tuition first**, then other fees from oldest to newest. This is tuition-first FIFO, not unrestricted global FIFO or fee-display-order priority. The accountant retains deliberate manual allocation control.
- Support full/partial payment, selected months/fees, multiple months, family collection and charges from previous enrolments/academic years. Collection must preserve each charge's true enrolment identity and produce accurate single/group receipts.
- Fee Matrix is the class structure. Assignments are the individual student's effective fee structure. Saving the matrix alone changes no existing assignment. Explicit reviewed application can update matching eligible assignments.
- Admission and subsequent monthly assignment discounts begin next month and continue through academic-year end. Promotion uses the latest class matrix and resets the previous year's discounts/concessions. Zero/blank means no discount.
- A rate change preserves the existing fixed discount unless explicitly changed. If the discount exceeds the new fee, cap it automatically to that fee; payable must not become negative. A labelled full concession remains full after a rate increase. A partial scholarship remains a fixed discount, capped when necessary.
- Existing issued bills require a separate explicit correction choice. Distinguish future assignment changes from corrections of selected issued bills. Preserve the original financial history and correct the difference.
- In Fee Collection, a concession can settle an unpaid bill or only the remaining unpaid part of a partial bill. It must not quietly refund or reverse already collected money.
- A paid rate reduction may return the released paid difference as student credit or cash refund, according to the authorized explicit choice. Use unpaid liability first; never create credit and refund the same money or leave correction-child charges owing incorrectly.
- Admission/fee review offers fee-specific `Free`, `Scholarship` and `Waived` labels. An optional assigned transport fee remains chargeable. Show free children on selected family bills/receipts and concession reports without fictional money or charges. Respect effective dates and year boundaries; do not relabel old fees retroactively.

### Lifecycle and credit

- Admission before the School Profile `FeeProrationCutoffDay` starts current-month monthly fees; on/after the cutoff starts next month. Example with cutoff 16: day 15 current month, day 16 next month. Admission/nonmonthly fees remain immediately chargeable as applicable. Apply the shared rule consistently to direct admission, Quick Registration, readmission and promoted return.
- If leaving is officially recorded before cutoff, that month's monthly fees are not due; on/after cutoff they remain due. Stop later months, preserve earlier legitimate arrears and nonmonthly charges. Until official leaving is recorded, Active billing continues within the year.
- Midyear transfer uses the existing transfer workflow: before cutoff, destination monthly rates apply this month; on/after cutoff, next month. Preserve discounts and correct already-issued bills through explicit differences. Do not introduce daily trigger-based proration or silently recalculate paid history. Existing future schedules require deliberate review.
- Promotion preserves the same student, historical enrolment and original old charges. Do not copy existing arrears into duplicate opening balances. A new enrolment uses current matrix rates and awaits the established return confirmation.
- Readmission/return uses existing mechanisms, reviewed rates and explicit commencement. It must not reactivate historical gaps or lose credit and arrears.
- Excess money is future student credit until exhausted. Credit follows the student across academic years, leaving and readmission. An administrator or accountant with the existing authorized collection access may refund unused credit or transfer it to a sibling. A transfer itself moves credit; it must not unexpectedly spend it immediately.
- Applying credit settles real charges through existing allocations and does not count as new cash received. Opening checks must also handle credit against an already-issued bill whose charge date has now arrived, even when no new charge is generated.
- Existing collection corrections/refunds/reversals through `ProcessFeeTransaction` remain authoritative. Dishonoured payments retain history and restore the correct balances. Do not add a parallel reversal engine or spend time proposing new cheque restrictions; cash is the normal collection flow.

### Permissions and gradual retirement

- Preserve financial authorization, whole-rupee validation, transaction protections, original receipts/payments/allocations and lifecycle history. SQL must enforce permissions and ownership; a hidden UI button is not sufficient.
- Prefer existing family collection and student assignment discounts. Do not propose an automatic sibling-discount engine without a demonstrated need; any future family policy must be deterministic and preserve the designated beneficiary, effective dates and historical rates.
- The user explicitly wants **gradual retirement of generic auditing**. Changed procedures should not add `AuditOperations`/`AuditLogs` writes or unused audit JSON preparation. Unchanged auditing may remain. Do not report the intentional removal of generic audit writes as a defect or recommend restoring audit everywhere. Financial correction/credit history, reasons, permissions and durable uncertain-outcome recovery still matter.
- Manual monthly posting becomes exceptional recovery, additional-fee posting and correction administration. Do not remove an old procedure merely because billing is now automatic. Check C#, SQL, report, test, manifest and dynamic callers, then identify genuinely unused/redundant candidates. Do not drop anything during this audit.

## Code and dependency review

Follow real call paths across these components and their dependencies; this list is a starting point, not a boundary:

- `Core/AutomaticMonthlyBilling.cs`, `MainDashboardForm.cs`, connection/reconnection and authentication/logout lifecycle.
- `fn_MonthlyBillingCandidates`, `RunAutomaticMonthlyBilling`, `PostSelectedMonthlyFees`, `PreviewMonthlyFees`, `GetPostableMonths`, `GetBillingCycleStatus`, `GetMissingChargeCandidates`, `fn_ExpectedRecurringChargeMonths`, opening/additional posting and cancellation/reversal guards.
- `FeeCollectionPage`, `CollectionDiscountWriter`, receipt/group-print paths, recovery journals, `ProcessFeeTransaction`, `ProcessFamilyFeeTransaction`, `GetOutstandingCharges`, `GetFeeChargeBalance`.
- Credit helpers/UI, `StudentCreditEntries`, `FeePayments.IsCreditApplication`, original-payment links and receipts; cash dashboards, daily closing and collection reports.
- Assignment/discount/configuration workspaces, `SaveStudentFeeDiscount`, amount-change helpers, matrix application, `AdjustBillingChargeRate`, batch previews/corrections and `FeeBillCorrections`.
- Direct admission, Quick Registration and saved-pending edits, legacy setup/activation, billing-start edits, leaving/certificates, readmission, promotion/return and class/campus transfers.
- Family membership/selection, `Core/Reports/Fees/FeeBillPreview.cs`, `Core/Reports/Fees/FeeReceiptQueries.cs`, zero-payable concession snapshots, assignment/billing/discount/outstanding/aging/account reports and printed/exported results.
- `DatabaseHelper.RequiredDatabaseSchemaVersion`, both compatibility manifests, migration transaction/backup wrappers in `Installer/core.ps1`, DACPAC content and reference SQL synchronization.

Trace adjacent pre-existing callers where the new behavior could break them: saved student details invoking discount procedures, pending status versus Active restrictions, future-effective assignments, correction/reversal screens, certificates/clearance and report SQL. A correct new procedure can still break an old caller's parameter/result contract.

Review related system protections as well: role-to-permission mapping, direct SQL write paths, parameter/TVP validation, cross-student/family ownership, login/logout actor changes during asynchronous operations, SQL injection, sensitive-data/log handling, durable save-outcome recovery, settings/database connection identity, backup/restore and report/export behavior. Verify navigation and permissions still expose the necessary correction/recovery actions. Prioritize demonstrable regressions and financial risk; keep unrelated cosmetic observations separate.

## Adversarial checks and financial invariants

Prioritize concrete failures that can lose, duplicate or misattribute money, create unintended debt, change historical records, bypass permissions or mislead the operator.

1. Repeated opening, two users starting together, month rollover, application closed on the 1st, reconnect, clock/year boundaries and administrator postponing recovery. Ensure failed/cancelled checks do not mark success or suppress needed retries.
2. A changed rate, assignment, bill, allocation, membership, placement or actor between preview and save. Inspect snapshot coverage, transaction isolation, consistent lock ordering, deadlocks, nested transaction ownership and `INSERT ... EXEC` error paths. Inject failure after earlier work and prove the whole required financial operation rolls back.
3. Missing/null/invalid commencement; legacy Active records; ended years; future assignments; multiple effective versions; overlapping schedules; cancelled charges and idempotent retry after a connection drops following commit. Never rely only on a version row, a happy-path test or a UI check.
4. Tuition 10,000 in October plus 10,000 in November, received 12,000: suggest October 10,000 and November 2,000. Add older transport and prove tuition priority. Manually select November instead and confirm October stays unpaid.
5. A 10,000 bill with 4,000 paid: a 6,000 remaining-balance waiver closes it without refunding the 4,000 or recording a fake payment. Reject an excessive concession or apply only the authorized remaining amount.
6. Preserve a 2,000 fixed discount when a fee rises 10,000 to 12,000; cap an 8,000 discount when the fee falls to 5,000; preserve fully Free/Waived and distinguish partial Scholarship. Check both future assignments and explicit issued-bill corrections, including repeated up/down corrections with child differences.
7. For a fully paid 10,000 bill reduced to 8,000: exactly 2,000 becomes credit OR cash refund. For partial payment, reduce unpaid portions before releasing money. Reconcile every correction, outstanding balance, cash report and original receipt.
8. Collect more than allocations, then apply/refund/transfer that credit; retry the same request and a mismatched reused request ID. Check simultaneous credit spending/refunding/transferring, sibling ownership, positive/whole amounts and no negative available credit. Reconcile source and destination without creating cash.
9. Family collection with paid, partial and free children, one student across two enrolments, discount-only groups, overpayment and an included free child. Verify one appropriate receipt, unique student counts, totals and names/placement, and no orphan partial family commit. Reprints must not relabel historical concessions from today's assignment.
10. Admission and leaving on both sides of cutoff, late-year admission, pending admission, promotion, return and readmission. Test calendar-independent cases rather than relying on today's day being before/after cutoff.
11. Promotion with previous-year arrears: collect the old charge after return without copying it or changing its historical class amount. Preserve unused credit and selected promotion behavior.
12. Transfer before/on cutoff with an unpaid, partial and paid bill, a scheduled discount/rate, optional transport and destination fees added/removed. Check the chosen credit/refund outcome and billed-period protections.
13. Refund/reversal after full/partial collection, after credit application and after an issued-bill correction. Confirm that restrictions are understandable and there remains a valid authorized correction route; do not accept an unexplained permanent financial dead end.
14. Reports/receipts/exports: net liabilities, net allocations, actual cash received/refunded, unused credit, current fees, prior arrears and free assignments must reconcile. Check different views and groupings, filters, original receipt reprints and historical enrolment attribution. Do not maintain parallel totals or mix accounting revenue-recognition policy into operational billing.
15. Query/load behavior on thousands of students and years of data: startup locking, month enumeration, repeated balance-function calls, unbounded recovery work, unnecessary round trips/memory and UI responsiveness. Report measurable or well-supported problems, not speculative rewrites.

## Testing strategy and environment honesty

First inventory available runtimes and read each harness's ownership/cleanup guards. Run existing relevant tests before inventing new infrastructure.

- `Tools/TestAutomaticBilling.ps1` contains the focused SQL regressions and creates an owned, uniquely named Windows LocalDB fixture. Previous local evidence was **95 assertions**, including concurrent posting, injected rollback, production family-bill SQL and compatibility validation. Review the assertions themselves for blind spots. Never retarget this script to a school database.
- `Tests/LifecycleSqlChecks/run.py` and the SQL CI workflow provide the existing disposable SQL Server/Docker route when available. Inspect current migration application, provider versions and seed state. Use synthetic data only.
- Review and run applicable `Tests/AuditFixChecks`, `Tests/QuickRegistrationSqlChecks`, lifecycle/mutation/administration/communication checks, installer inert checks and test-harness safety checks. Inspect regressions in existing test expectations after schema 19.
- C# uses Windows WinForms. A Linux cross-build does not prove native behavior; offscreen dialog construction does not prove collection workflow usability. `Tests/ThemeSmokeChecks --billing-design` previously rendered four dialogs without SQL. Windows UI/DPI/printing and actual workflow interaction may require a Windows runner.
- Root DACPAC validation, the older upgrade fixture and supplied release MDF/LDF are different verification questions. Do not rewrite fixtures or manifests to make a real incompatibility disappear. Existing test-only DacFx SQL-variant/extended-property handling must not be silently promoted to a production data repair.
- Do not fake GitHub environment variables, disable ownership guards, use arbitrary existing SQL instances or access production secrets to overcome a blocked test. If Docker, SQL Server, Windows or dependencies are unavailable, record the exact limitation and continue meaningful static review and portable tests.
- You may build minimal synthetic reproductions in temporary ignored locations. Explain what they prove, cleanup owned resources and include the reproduction in the report. Do not claim unexecuted tests passed or mistake an intentionally rejected invalid request for a product defect.

## Required deliverable

Write `Audit/FMSC_BILLING_SYSTEM_AUDIT.md` with these sections:

1. **Overall assessment:** ready for controlled operator testing, or blocked by named defects; separate this from production-readiness claims.
2. **Confirmed findings, highest severity first:** P0/P1/P2 as warranted, with exact file paths and current line numbers, affected SQL/C# callers, realistic trigger, expected versus actual financial effect, reproduction/evidence, and the smallest suggested fix. Explain who/what is affected. Consolidate duplicate symptoms of one root cause. Do not invent findings to fill severity categories.
3. **Business-rule coverage matrix:** each rule group above marked Confirmed / Defect / Not verified / Policy ambiguity, linked to code and tests. Explain genuine ambiguities without reopening settled rules.
4. **Financial reconciliation:** evidence for partial/full/manual allocations, family receipts, unused credit, corrections, refunds/reversals and cash versus noncash reporting. Include at least one small numeric worked example for each confirmed money defect.
5. **Concurrency, migrations and compatibility:** tested paths, rollback/retry results, schema/DACPAC agreement, fresh install versus existing upgrades, and realistic deployment/restore limitations.
6. **Complexity and retirement:** what correctly reuses the foundation; duplicated eligibility/calculation paths; a caller-backed list of retirement candidates and procedures that must remain. Do not remove anything.
7. **Verification record and gaps:** commands, outcomes/assertion counts, environment and exact untested cases; distinguish previous local results from your own execution. List required Windows/operator checks clearly.
8. **Minimal next actions:** prioritized fixes and targeted regression cases. Put optional improvements separately. Finish with a concise school-operator test sequence using synthetic students/families, including legacy exclusion, first billing, partial payment, manual override, free child, credit, promotion arrears, transfer and reversal.

Keep the executive summary concise, but make the findings detailed enough that another engineer can reproduce them. If no significant defect is found, say so with the actual scope and limitations. Do not certify financial safety from static review alone.
