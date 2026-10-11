# FMSC_SMS — PRE-RELEASE DEEP BUSINESS LOGIC & EDGE-CASE AUDIT

## ROLE

Act as a senior C#/.NET developer, SQL Server database architect, financial systems auditor, QA automation engineer, and release-readiness specialist.

You are auditing **FMSC_SMS**, an established C# WinForms school management system using SQL Server LocalDB and stored procedures.

The application has already undergone considerable development, refactoring, manual testing, and real-data workflow validation.

**This is not an ordinary code review, a refactoring pass, or a request to improve the UI.**

Your mission is to uncover **hidden business logic defects that can survive normal testing and only emerge after real users have created a complicated history of records.**

Assume the obvious happy paths generally work. Challenge the overlooked assumptions.

## THE PROBLEM THAT MOTIVATES THIS AUDIT

A previously encountered issue involved admitting a student into an older academic year and the handling of the last allocated admission number.

Normal admission testing had not exposed the issue.

This illustrates the class of defect I want investigated:

* A form works under normal conditions.
* Existing records create an unusual database state.
* The user performs an otherwise legitimate action.
* The application calculates, selects, validates, saves, or reports something incorrectly.
* The mistake may not produce an exception.
* The database might accept it, making the issue difficult to notice until later.

**Use the historical admission-number case as a model for discovering similar overlooked scenarios in every module. Do not limit this audit to admission numbers.**

## NON-NEGOTIABLE SAFETY INSTRUCTIONS

1. **Do not modify production application code, SQL definitions, migrations, schema, triggers, database files, or application behavior.**
2. **Do not make commits, push changes, or create pull requests.**
3. This is an audit and scenario-testing task. First establish evidence and document findings.
4. Do not run destructive tests against real school records or the live database.
5. Execute data-changing tests only against a clearly identified disposable database copy, with backups and isolation verified first.
6. If no safe database copy is available, perform static analysis and prepare executable tests without running them against production.
7. Respect established, intentional business rules. Do not classify a deliberate policy as a defect simply because an alternative design seems preferable.
8. Do not rewrite functional code, introduce new architecture, add unnecessary helpers, or suggest broad refactoring.
9. Do not claim a scenario was tested if you merely reviewed its code.
10. Do not recreate intentionally removed folders, reference SQL snapshots, or historical migrations. Inspect the actual current environment and available database objects.
11. Report findings and proposed minimal fixes, but await my approval before making application or database changes.

The priority is **correctness and preservation of existing business behavior**, not cosmetic code quality.

---

# PHASE 1 — UNDERSTAND THE COMPLETE BUSINESS SYSTEM

Before testing, map the application at a business-rule level.

Examine:

* C# WinForms pages, dialogs, event handlers and input validation.
* DatabaseHelper and supporting services.
* Stored procedures, SQL functions, triggers, constraints and indexes available in the actual database.
* Database transaction and locking patterns.
* Application-to-SQL parameter contracts.
* Reports and their filtering/calculation rules.
* Permission and user-management workflows.
* Existing automated tests and their blind spots.

For every important operation, identify:

**User action → C# validation → SQL call → database changes → triggers → resulting state → subsequent dependent operations.**

Do not assess a procedure in isolation if its results affect other features.

For instance, admitting a student affects numbering, enrollment, fee assignment, billing, student search, reporting, certificates, promotion, and historical records.

Find the dependencies before judging correctness.

---

# PHASE 2 — IDENTIFY BUSINESS INVARIANTS

Determine which conditions must always remain true, regardless of the order of operations.

Examples include:

### Student and enrollment integrity

* A student must never end up with two conflicting Active enrollments.
* Student status and enrollment status must remain consistent under established rules.
* Admission, enrollment, promotion, leaving and readmission dates must respect valid chronology.
* Historical records should remain historically correct after later changes.
* Transfers must not silently change unrelated historical financial obligations.
* Corrections must preserve required history and audit information.

### Identifier and numbering integrity

* Admission and registration numbers must be unique within their defined scope.
* Automatically generated numbers must respect the actual numbering policy, including historical years.
* Counters must remain consistent with existing records.
* Manually entered numbers must not produce future automatic conflicts.
* Simultaneous number requests must not produce duplicates.
* Receipt and certificate numbers must retain their established uniqueness and ordering requirements.

### Financial integrity

* No fee is posted twice unless the established cancellation/reposting rules explicitly permit it.
* A payment must not exceed the valid collectible balance.
* Refunds, corrections, reversals and payment allocations must remain mathematically consistent.
* Paid financial history must not be silently rewritten by unrelated corrections.
* Financial reports must agree with the authoritative financial records.
* Confirmed transactions must not silently disappear after refresh, navigation or partial failure.
* Unexpected interruptions must not create a completed operation with missing corresponding records.

### Security and audit integrity

* Restricted actions must be rejected by the database even if C# validation is bypassed.
* Permission changes must not unexpectedly grant or retain unauthorized access.
* Important financial and lifecycle changes must have valid audit records.
* Invalid inputs must not partially commit related changes.

Identify further invariants from the real application. Do not assume this list is complete.

For each invariant, identify where it is enforced and whether the protection is adequate under concurrency and historical-data conditions.

---

# PHASE 3 — GENERATE SCENARIOS THAT HUMANS OFTEN OVERLOOK

Create a systematic scenario inventory for every major module.

Do not simply list typical operations. Generate tests across these dimensions:

1. **Time:** past, present, future, year boundaries, month boundaries, first/last dates, backdating.
2. **Historical state:** no records, one record, many records, closed records, corrected records, legacy records.
3. **Operation sequence:** valid actions performed in different orders.
4. **Repeated execution:** same action twice, double-click, retry after timeout, reopening and resubmitting.
5. **Data boundaries:** zero, null, empty, maximum, minimum, duplicate, just below/above limits.
6. **Related records:** payments exist, certificates exist, fees posted, student transferred, status changed.
7. **Concurrency:** two windows or users working on the same records.
8. **Interrupted execution:** network failure, timeout, process termination, lost response after commit.
9. **Stale state:** records changed between loading, previewing, confirming and saving.
10. **Permissions:** administrator, restricted user, newly changed role, revoked permission.
11. **Cross-module interaction:** changing one record after another module has already used it.
12. **Database consistency:** orphan references, conflicting statuses, mismatched counters, missing configuration, legacy exceptions.

Use risk-based combinations, pairwise testing, boundary-value testing, state-transition testing, and targeted multi-step workflows.

Avoid an enormous Cartesian product of redundant scenarios. Concentrate on combinations where business rules interact.

The key question for every operation is:

**What unusual but valid database state could cause this operation to behave incorrectly?**

---

# PHASE 4 — TARGETED MODULE INVESTIGATION

## A. Student admission and numbering

Investigate:

* Current-year admission.
* Backdated admission.
* Admission into an older academic year.
* New admission while a future academic year exists.
* Last allocated admission number within each relevant scope.
* Gaps in number sequences.
* Manually overridden admission numbers.
* Number counter behind or ahead of actual records.
* Highest existing number differing from the last allocated counter.
* Numbers created out of chronological order.
* Existing historical students with unusual numbering.
* Duplicate previewed numbers before save.
* Simultaneous admissions from two application windows.
* Changing academic year or admission date after generating a proposed number.
* Saving after another user consumes the proposed number.
* Cancelling, correcting, or retrying number allocation.

Trace the complete numbering calculation, database lookup, counter update, uniqueness enforcement and C# save operation.

Pay special attention to a number that looks correct in the UI but becomes incorrect when saved.

## B. Enrollment, status, promotion and readmission

Investigate combinations involving:

* Active, Left, Promoted and historical enrollment states.
* Multiple academic years.
* Missing historical enrollments.
* Backdated lifecycle operations.
* Already promoted students.
* Students with unpaid balances.
* Students with previously issued certificates.
* Same-year readmission.
* Readmission after several years.
* Promotion followed by correction.
* Correction followed by promotion.
* Leaving followed by readmission.
* Transfer followed by leaving or promotion.
* Student status inconsistent with enrollment history.
* Historical enrollment dates overlapping later records.

Check that operations are independently valid **and remain valid when combined in different sequences**.

## C. Fees, billing and configuration

Investigate:

* Fee assignments created before and after admission.
* Monthly and annual fee interactions.
* Fee effective-period boundaries.
* Partially posted monthly fees.
* Posting older months after newer months.
* Academic-year changes.
* Backdated student enrollment.
* Promotion with existing bills.
* Transfers between fee configurations.
* Changes to fee rates after charges already exist.
* Transport starting and stopping midyear.
* Cancellation and reposting.
* Multiple overlapping fee assignment periods.
* Applying configuration changes to students with different billing histories.
* Partial success across multiple scope updates.
* SQL or connection failure after one scope has already committed.

Test whether previewed amounts, committed amounts, balances, reports and audit entries agree.

## D. Fee collections, receipts and corrections

Investigate:

* Zero or exact-balance payments.
* Partial collections.
* Repeated payments.
* Payments across multiple outstanding charges.
* Family collections.
* Already partially paid charges.
* Refund after correction.
* Correction after refund.
* Reversal after partial payment.
* Repeated reversal or retry.
* Receipt-number generation under concurrency.
* Historical payment dates.
* Cancellation after printing.
* Connection loss after payment commits but before C# receives confirmation.
* Reopening payment screens and selecting another student.

Distinguish between **an operation that was never committed** and **an operation that committed but whose result was lost**.

Never propose blind automatic retries for financial operations.

## E. Transfers, certificates and record maintenance

Test changes made after related documents or financial records exist.

Examples:

* A certificate was issued, then the student's name, date or enrollment status changed.
* A student was transferred after billing.
* A student left after fees were posted for a later month.
* A historical leaving date is corrected after certificate issuance.
* An administrator manually repairs a missing record.
* A setup class/section is deactivated while students still occupy it.

Identify whether each operation should synchronize, preserve, invalidate, or refuse to change dependent records.

## F. Reports and reconciliation

Look for reports that appear plausible but contain incorrect totals.

Check:

* Partial versus full billing.
* Cancelled charges included/excluded incorrectly.
* Historical academic-year filters.
* Campus and class transfers.
* Double counting across enrollments.
* Outstanding balances after refunds/corrections.
* Reports based on current status rather than historical status.
* Date-range boundaries.
* Totals not matching underlying detail rows.
* Stale reports being printed or exported.
* Different reports using inconsistent interpretations of the same field.

For financial reports, reconcile detail-level records to displayed totals.

## G. Security, persistence and failure recovery

Verify that:

* A C# restriction is not the only protection for a sensitive SQL write.
* A user cannot perform an unauthorized operation by invoking a stored procedure directly.
* Double submission cannot bypass transaction safeguards.
* Errors do not leave contradictory records.
* Failed multi-step operations report which changes definitely committed and which are uncertain.
* SQL compatibility checks match the deployed application requirements.
* Database restore preserves required data and logical consistency.

---

# PHASE 5 — AUTOMATE SCENARIO TESTING WHERE SAFE

Do not rely on manual inspection alone.

Where practical:

1. Build isolated synthetic database fixtures.
2. Reproduce historical and inconsistent states that normal UI testing may never create.
3. Execute stored procedures against a disposable copy.
4. Capture before/after database state.
5. Assert business invariants.
6. Exercise different valid operation sequences.
7. Run repeated-operation and concurrency tests.
8. Compare financial totals and identifiers.
9. Verify rollback, audit logging and idempotency.
10. Run existing C# tests and compile the current application without modifying source.

Develop tests that demonstrate failures **with evidence**, not assumptions.

For example:

* Admit student → generate number → change academic year → save.
* Admit student → consume generated number in second session → save first session.
* Post fees → transfer student → correct enrollment → produce outstanding report.
* Collect payment → lose client response → attempt same collection again.
* Promote student → correct prior enrollment → run billing reconciliation.
* Create old-year enrollment → add fee assignment → generate future-month projection.

A successful individual operation is not sufficient. Verify the final database state after the **whole sequence**.

Do not run unsafe fixture creation, restores, schema changes or stress tests on the live database.

---

# PHASE 6 — SEARCH SPECIFICALLY FOR SILENT LOGIC FAILURES

These are more important than ordinary exceptions.

Prioritize cases where the application:

* Saves the wrong but valid-looking value.
* Selects the wrong enrollment or academic year.
* Uses an old counter or stale selected row.
* Calculates a valid-looking but incorrect fee amount.
* Silently excludes a student from an operation.
* Produces an incorrect financial total.
* Reports success despite partial completion.
* Uses a current value where a historical value is required.
* Commits an operation whose dependent records are inconsistent.
* Validates one set of inputs but saves another.
* Changes data between preview and confirmation.
* Assumes record creation order equals chronological order.
* Assumes the most recently generated number equals the highest previously allocated number.

Treat silent corruption, double charging, incorrect identifiers, and historical-data damage as top-priority release risks.

---

# PHASE 7 — EVALUATE EVERY FINDING

For each suspected defect, provide:

**1. Finding ID and title**

**2. Severity**

* Critical: plausible silent financial/data corruption, unauthorized modifications, or irreversible inconsistency.
* High: important business workflow can produce incorrect data or become unusable.
* Medium: operational inconsistency, inaccurate report, or recoverable workflow failure.
* Low: minor behavior that does not threaten data correctness.

**3. Confidence**

* Reproduced by executable test.
* Confirmed directly from code/SQL contract.
* Strongly suspected, requires runtime validation.
* Business-rule question rather than proven defect.

**4. Exact affected files, procedures and relevant line ranges**

**5. Preconditions and realistic example**

**6. Step-by-step reproduction**

**7. Expected result versus actual or predicted result**

**8. Root cause and violated business invariant**

**9. Impact on students, fees, historical data, reports and dependent modules**

**10. Whether it can be fixed safely in C# alone, requires SQL changes, or requires coordinated changes**

**11. Minimum-risk correction proposal**

**12. Potential regression risks of the proposed change**

**13. Regression test that must pass before accepting a fix**

**14. Whether this should block deployment**

Do not present speculative findings as proven defects.

Do not report generic style issues, minor code duplication or vague “could be improved” observations.

Do not artificially inflate the findings count.

---

# PHASE 8 — FINAL RELEASE READINESS REPORT

Produce a structured audit report containing:

## 1. Executive assessment

Explain whether the application is ready for a supervised pilot, ordinary single-school daily use, or unrestricted deployment, based only on evidence.

## 2. Coverage inventory

Show which modules were inspected, which scenarios were actually executed, and what could not be tested.

## 3. Critical and High findings

Put possible silent data corruption and financial inconsistencies first.

## 4. Medium and Low findings

Only meaningful business behavior, not cosmetics.

## 5. Cross-module risks

Explain failures caused by legitimate actions performed in unusual sequences.

## 6. Business invariants and enforcement

List important invariants and whether enforcement is implemented in SQL, C#, both or neither.

## 7. Automated test outcomes

Show test counts, passed, failed, skipped and blocked. Provide meaningful evidence for failures.

## 8. Manual Windows testing required

Identify UI, physical printer, simultaneous-session and other scenarios that cannot be reliably tested in the current environment.

## 9. C#-only vs SQL-required fixes

Separate these clearly to minimize unnecessary database alterations.

## 10. Deployment decision

State:

* **BLOCK RELEASE**
* **CONTROLLED PILOT ACCEPTABLE**
* **READY FOR DEPLOYMENT**

Justify the choice with concrete evidence, not a generic quality score.

## 11. Future regression protection

Recommend a compact, reusable collection of tests that would catch these classes of defects before every future release.

Do not create a new Audit folder or commit documentation unless I explicitly request it. Present the findings directly in the final response or use an existing approved output destination.

---

# FINAL INSTRUCTIONS

Think like a school operator who has used the software correctly for several years, not like a developer testing an empty database.

Assume:

* Old academic years exist.
* Numbers and counters have accumulated.
* Students have transferred, left and returned.
* Fees have been posted, partially paid, corrected and refunded.
* Records have been amended after their original creation.
* Administrators occasionally need legitimate historical corrections.
* Users may unknowingly repeat an operation after a timeout.
* Different modules may be individually correct but collectively inconsistent.

**Do not stop at “the code looks good” or “existing tests pass.”**

First investigate what the existing tests do **not** cover.

When a hidden scenario is discovered, trace it to the underlying rule and attempt safe reproduction.

When no defect is discovered, explain what was actually tested and what remains unverified.

Remember: **The goal is not to find more bugs for the sake of finding bugs. The goal is to reduce the possibility that a real user will silently create incorrect school records after deployment.**

Proceed with the deepest practical audit using the current project and database access available. Do not change application or database implementation without my approval.
