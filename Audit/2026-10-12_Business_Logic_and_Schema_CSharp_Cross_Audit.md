# FMSC_SMS — Business Logic and SQL Schema vs C# Cross-Audit

**Report date:** 2026-10-12  
**Repository:** farhan2166-creator/FMSC-Database  
**Audited main HEAD (before creating this report):** 04647e7ecee3510cc614331f76bdb5d3709c221c  
**Schema artifact:** FMSC.dacpac, Git blob 337893be7a46fdc155ab9efb85b2b2ade5ce705d, 241,968 bytes  
**Assessment type:** Read-only static code/schema cross-audit. **No application/database code changed. No live database modified. No build, execution, or integration test performed.**  
**Confidence guide:** CONFIRMED = directly established behavior/coverage gap in source or DACPAC; CONDITIONAL = consequence depends on threat model or explicitly chosen business policy; TEST NEEDED = plausible runtime scenario, not demonstrated as a defect.

## Revalidation and approved fixes — 12 October 2026

This section supersedes the original open findings below where noted. Revalidation used GitHub main `6159807d385bdfe5d91d6ce97ac6f3967a418655` and the same DACPAC blob. The application source was unchanged from the original audited SHA; the later commits added audit documents only.

### Fixes implemented

- **BL-01 — fixed in C#.** `AdmissionNumberPolicy.LoadAsync` now requires `Students.Admit`, matching new-admission SQL and number preview. `LoadExistingAsync` retains `Students.Administer`. Historical admission still requires `Enrollment.Manage` at its existing SQL boundary; discount authorization is unchanged.
- **BL-04 — resolved by the owner's policy and implemented in C#.** Chronological date/number ordering is suggestion assistance, not a save rule. A manually entered valid, unused number can override the proposal for an ordinary admission. Conflicting legacy date order suppresses a chronological suggestion without blocking manual entry. Existing-record chronology review remains optional. Mandatory format, normalized uniqueness, future-date and academic-year checks remain. SQL continues enforcing uniqueness; no SQL chronology rule was added.
- **BL-02 — compatibility coverage gap addressed.** The embedded contract now includes all 57 tables, 228 procedures, 22 triggers and every procedure parameter from the approved DACPAC, including both transfer arguments. The three CertificateNumberCounters columns are explicitly checked. Startup/restore validation additionally checks each trigger's enabled state, parent, event set and AFTER/INSTEAD OF mode; all 71 foreign-key shapes and referential actions, including unnamed keys; and all 40 named check constraints. Foreign keys and checks must be enabled and trusted.
- `Tools/update_compatibility_contract.py` refreshes this coverage deterministically from the approved DACPAC, records its SHA-256, and supports `--check` without writing. It preserves the independently maintained existing column, table-type, function-signature and uniqueness requirements. No existing procedure parameter changed its expected type/length/precision/scale/output shape. Unknown new parameter types fail generation.
- These changes detect schema drift; they do not repair live databases, change business procedure definitions or require a new schema version. They do not fingerprint procedure/trigger bodies or compare check-expression semantics. Objects with the correct structure but altered implementations remain outside this compatibility check's protection.

### Revalidation corrections

- **BL-02 qualification:** CertificateNumberCounters was absent from the original object/column inventory but its primary-key shape was already covered by the uniqueness contract. It was not wholly unchecked.
- **BL-06 scope narrowed:** Monthly UI discounts use `FeeDiscountPeriod.Save`, which rejects already-billed months and changes future assignment periods. The September/October monthly example below is a direct-procedure-call possibility, not a reproduced normal UI path. The no-date-boundary procedure remains reachable for nonmonthly UI discounts and direct calls. Closed-enrollment triggers still protect historical closed records.
- **BL-07:** Partial results are intentional; current C# already retains confirmed group counts, identifies interrupted results as unknown and requires fresh review before retrying. Further atomicity/retry changes require a policy decision.
- **BL-10:** No current C# caller of `GetStudentAnnualFeeLedger` or its legacy template was found. `AnnualFeeReceiptsPage` uses `GetStudentAnnualFeeReceipts` and receipt output instead. A current application rendering defect is not established.
- Correct DACPAC counts are **71 foreign keys and 57 primary keys**. The original counts excluded two unnamed foreign keys and five unnamed primary keys.

### Verification of this fix pass

- Windows-targeted application cross-build passed with **zero warnings and errors**: `DOTNET_CLI_HOME=/workspace NUGET_PACKAGES=/workspace/.nuget/packages /workspace/.dotnet/dotnet build --no-restore -p:EnableWindowsTargeting=true -v minimal`.
- `python Tools/update_compatibility_contract.py FMSC.dacpac --check` passed against the exact approved artifact.
- `python Tools/test_compatibility_contract.py FMSC.dacpac` passed **seven offline regression tests**: table/procedure coverage; all procedure parameter names; transfer parameter shapes; certificate counter columns; trigger/constraint counts including unnamed foreign keys; deterministic generation; and schema drift detection by the generator.
- Existing expected parameter shapes were compared before/after and none changed. `git diff --check` passed.
- **Limits:** Offline tests verify the contract and generator, not SQL execution of startup/restore validation. No SQL Server clone was available in this workspace; the container daemon was inaccessible. Removing/disabling safeguards on a disposable database and exercising the limited-admission Windows role path still require runtime verification. No school data, SQL definitions, DACPAC, installer or migration files were changed.

### BL-04 verification — 12 October 2026

The Windows-targeted application cross-build passed with zero warnings and errors. `Tools/test_admission_number_policy.py` compiled the production `AdmissionNumberPolicy` and `AdmissionNumbers` against synthetic records and passed **15 checks**: unchanged chronological suggestions; valid overrides below/above bounds; optional review; normalized duplicates; malformed numbers; valid in-order entry; conflicting legacy bounds without an unsafe suggestion; manual entry under conflicting bounds; out-of-year/future dates; historical exceptions; and case-insensitive suffix duplicates. No SQL connection was opened. Interactive Windows admission saves and concurrent SQL execution were not exercised.

### Policy decisions still pending

1. **BL-03 — trust boundary:** Are Windows/LocalDB access permissions the accepted security boundary, or must application roles resist another local process connecting to SQL?
2. **BL-05 — historical withdrawal:** Require a known withdrawal date, or permit Unknown with an explicit reason/source?
3. **BL-06 — nonmonthly/direct-call discounts:** Should an edited discount update already-posted unpaid charges, or require prospective effective dates/explicit adjustments? The monthly UI already preserves billed months.
4. **BL-07 — rate application:** Keep best-effort per-assignment progress, or require all-or-nothing application for a selected scope? If partial progress stays, decide whether durable failed-ID tracking is required.
5. **BL-08 — daily closing:** Permit late/backdated entries with stale detection and revised approval, or seal approved dates until an administrator reopens them?
6. **BL-09 — audit detail:** Keep assignment-level auditing, or add correlated per-charge before/after details? The user explicitly deferred this item to policy discussion.
7. **BL-10 — legacy ledger:** Is that unused ledger retained for an external consumer requiring nonstandard academic-year lengths? No current UI defect is established.

## Executive summary

The current DACPAC was successfully decoded and its compressed model.xml read. It reports no exported data. Its schema model contains **57 tables, 228 stored procedures, 22 DML triggers, 71 foreign keys, 40 check constraints, 57 primary-key constraints, 18 unique constraints, and 69 index objects** (plus functions and one view). The complete 354-entry repository tree was inventoried. Representative high-risk WinForms C# modules were inspected and traced into actual SQL bodies, parameters, triggers, indexes, and the embedded schema compatibility specification.

**No conclusion of safe/unsafe deployment can be made from static review alone.** The most actionable confirmed issues are (1) a contradictory authorization check during ordinary new admission, and (2) the application compatibility contract omitting procedures and trigger safeguards that the current application relies upon. Other points involve business policy, financial traceability, and multi-user behavior, and are explicitly classified below instead of being called proven corruptions.

### Priority register

| ID | Priority | Confidence | Topic | Where remedy belongs |
|---|---|---|---|---|
| BL-01 | P1 / High | CONFIRMED authorization-path mismatch | New admission planner requires a stronger permission than admission itself | C# only, if intended role policy remains unchanged |
| BL-02 | P1 / High | CONFIRMED compatibility coverage gap | Required procedures/arguments and integrity triggers omitted from startup schema contract | C#-embedded JSON/generator and compatibility checks; no business SQL change necessarily needed |
| BL-03 | P1 / High if local SQL is an untrusted boundary | CONDITIONAL architecture risk | SQL procedures trust caller-supplied application UserId | Security architecture + OS/database access policy; not simply a one-line C# fix |
| BL-04 | P2 / Medium | CONFIRMED cross-layer rule gap | Chronological ordinary admission-number ordering exists in C# but not in SQL | Both C# and SQL if it must be authoritative under concurrent entry |
| BL-05 | P2 / Medium | CONFIRMED permitted state; POLICY DECISION | Historical Left student may have no verified withdrawal date | C# + SQL if a recorded date/reason is mandatory |
| BL-06 | P2 / Medium | CONFIRMED effect; POLICY DECISION | Changing a standard fee discount rewrites unpaid historical charges | SQL + UI contract/preview |
| BL-07 | P2 / Medium | CONFIRMED design; operational risk | Bulk application of new rates can partially succeed | SQL and C# workflow, depending on desired atomicity |
| BL-08 | P2 / Medium | CONDITIONAL financial-close policy | Backdated postings may make an approved daily closing stale | SQL transaction policy + UI; stale detection already exists |
| BL-09 | P2 / Medium | CONFIRMED traceability gap | Some fee-assignment changes alter charge rows without per-charge before/after audit detail | SQL (with an optional C# preview) |
| BL-10 | P3 / Low | TEST NEEDED | Academic years longer than 12 months and annual-ledger/report output shape | SQL/RDLC/C# only if this period length is supported |

## Findings

### BL-01 — Ordinary admission workflow checks Students.Administer instead of Students.Admit

**Severity:** P1 / High. **Evidence:** CONFIRMED static mismatch.  
**C#:** [Core/AdmissionNumberPolicy.cs:45–54](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Core/AdmissionNumberPolicy.cs#L45-L54), [Pages/Students/Admissions/StudentAdmissionPage.cs:849–854](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Students/Admissions/StudentAdmissionPage.cs#L849-L854), [Pages/Students/Admissions/StudentAdmissionPage.cs:4090–4102](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Students/Admissions/StudentAdmissionPage.cs#L4090-L4102), [Pages/Students/Admissions/StudentAdmissionPage.cs:4221–4233](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Students/Admissions/StudentAdmissionPage.cs#L4221-L4233).  
**SQL:** DACPAC model.xml: dbo.GetNextAdmissionNumber and dbo.AdmitStudent, and dbo.AdmitHistoricalStudent.

**Observation.** The navigation/save action explicitly supports an operator having Students.Admit. The stored procedure dbo.AdmitStudent authorizes Students.Admit (and Fees.Adjust when discounts are supplied). The preview procedure dbo.GetNextAdmissionNumber also authorizes Students.Admit. But AdmissionNumberPolicy.LoadAsync executes an explicit dbo.RequirePermission for **Students.Administer**, even for ordinary new admissions. SaveNewAdmissionAsync calls LoadAsync before validation and again before its final insert. Therefore a person granted Students.Admit but not Students.Administer may be able to open/attempt the admission workflow yet fail the required numbering-plan query, before the authorized SQL admission procedure can be called.

**Example.** A clerk with Students.Admit and relevant view/setup permissions, but not Students.Administer, tries to register a new student. The planner's SELECT is refused, and the save cannot progress even though the underlying admission procedure is intended to accept that role.

**Remedy.** For **new** admission planning use the same Students.Admit permission as dbo.AdmitStudent; retain the stronger Students.Administer check for **editing existing records** in LoadExistingAsync. Historical admission still needs Enrollment.Manage as enforced by its SQL procedure. Check any associated GetStudents/lookup permissions separately. Make the role matrix an automated integration test. A targeted C# permission-check change is sufficient for this specific mismatch; do not relax dbo.AdmitStudent authorization.

**Regression tests.** Separate users with (a) Students.Admit only, (b) Students.Admit plus Fees.Adjust, (c) Students.Administer but not Students.Admit, (d) historical admission combination. Verify empty/nonempty discounts, new vs edit, and expected SQL denials. **Do not grant stronger administrator permissions just to make the UI work.**

### BL-02 — Embedded schema compatibility requirements lag behind actual DACPAC

**Severity:** P1 / High for restore/upgrade protection. **Evidence:** CONFIRMED missing coverage; no claim that the current DACPAC itself is missing objects.  
**C#:** [Core/DatabaseCompatibility.cs:23–110](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Core/DatabaseCompatibility.cs#L23-L110), [Core/DatabaseCompatibilityRequirements.json](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Core/DatabaseCompatibilityRequirements.json), [Pages/Students/Enrollment/StudentTransferReadmissionPage.cs:1280–1347](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Students/Enrollment/StudentTransferReadmissionPage.cs#L1280-L1347), [Pages/Students/Maintenance/StudentRecordChecksPage.cs:1242–1251](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Students/Maintenance/StudentRecordChecksPage.cs#L1242-L1251).  
**SQL:** DACPAC model.xml: dbo.ReAdmitStudentWithNumberChoice; dbo.PreviewLifecycleDateRepair; dbo.TransferEnrollmentClass, dbo.TransferEnrollmentCampus; triggers dbo.GuardClosedEnrollmentCharges, dbo.GuardFeeAssignmentPeriods.

**Observation.** The DACPAC contains 57 tables and 228 procedures. The compatibility JSON enumerates only 56 tables and 211 procedures; the differences are not simply type-label differences. Comparing dbo object names identifies **one table and 17 procedures omitted**:

- Table: CertificateNumberCounters.
- Procedures: CreateReviewedLeaveCertificate, EndStaffEmployment, GetAdmissionsReport, GetLifecycleRepairSnapshot, GetNextCertificateNumber, GetStudentAnnualFeeLedger, GetStudentStrengthTrendReport, MoveAcademicProgram, NormalizeSetupOrder, PreviewLifecycleDateRepair, ReAdmitStudentWithNumberChoice, ReconcileEnrollmentDates, ReorderCampuses, ReorderSections, ReorderSubjects, RepairLeaveCertificateEnrollmentLinks, RepairLifecycleDates.

Furthermore, the current DACPAC declares **@FeeEffectiveMonth** and **@ReplaceScheduledFees** on both dbo.TransferEnrollmentClass and dbo.TransferEnrollmentCampus; C# passes these arguments but the embedded JSON lists neither argument for either procedure. All **22 DML trigger definitions** are absent from the JSON object inventory, and Validate does not compare foreign-key or check-constraint definitions, trigger availability, or procedure implementation bodies. Its safeguards are a defined subset: objects, columns, parameters, table-type columns, unique indexes, and one extra admission-number uniqueness check.

**Why this matters.** The current archive and C# agree on the named transfer arguments: this is **not** an assertion that transfers currently fail. The actual risk is that a restored, stale, manually repaired, or otherwise drifted database can retain the expected DatabaseVersion and pass compatibility validation despite lacking an application-used routine/parameter or an essential trigger. For example, a version-3 database without dbo.ReAdmitStudentWithNumberChoice could pass today's required-object check, and fail only when an operator tries the readmission-number-choice workflow. A database missing GuardClosedEnrollmentCharges could lack an important paid-charge cancellation safeguard, because that trigger is not checked.

**Remedy.** Generate compatibility metadata deterministically from the **same approved schema artifact** used to build the application (or maintain a source-controlled contract with enforced diff checks). Include all called procedures, actual parameters, table types, mandatory triggers, FK/check constraints, and definition fingerprints for critical business logic. Check schema version **and** these safeguards. A C#/embedded-contract-only remedy can detect missing objects, but a missing database object must ultimately be supplied by the correct approved schema/migration or a compatible restored database; never silently manufacture/patch unknown live objects.

**Regression tests.** On disposable cloned databases with the expected version unchanged, deliberately remove one required wrapper/trigger or alter a transfer signature, confirm that startup/restore compatibility rejects the clone *before* an operator reaches the affected workflow. Include positive test with the exact current DACPAC.

### BL-03 — Application UserId is not cryptographically bound to the connected SQL principal

**Severity:** P1 / High **only where untrusted local processes/users can connect to the same LocalDB**; otherwise document as the accepted on-device trust model. **Evidence:** CONFIRMED architecture; exploitability depends on OS/database access policy.  
**C#:** [DatabaseHelper.cs:1–28](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/DatabaseHelper.cs#L1-L28), [DatabaseHelper.cs:175–230](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/DatabaseHelper.cs#L175-L230), [Pages/Security/AuthenticationService.cs:1–140](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Security/AuthenticationService.cs#L1-L140).  
**SQL:** DACPAC model.xml: dbo.RequirePermission and dbo.GetUserPermissions.

**Observation.** Connections use Windows integrated security to a LocalDB instance. Business stored procedures receive an integer @UserId supplied by the WinForms client. dbo.RequirePermission verifies that this ID exists and has permission in dbo.Users/UserRoles/RolePermissions; it does not prove that the database connection belongs to the account that just authenticated in the WinForms UI. Any other process with adequate local SQL access could in principle invoke a procedure with a different existing UserId. The C# login, password derivation, throttling and UI authorization cannot by themselves establish a trust boundary against another process that can run SQL on the database.

**Example.** A shared Windows account can launch a second program with the same LocalDB connection privileges. An operator who cannot access a management page in FMSC_SMS may nevertheless be able to submit a procedure directly using an administrator's numerical @UserId. This is a **conditional local threat-model risk**, not an assertion of remote network exposure.

**Remedy.** Decide and document whether Windows/LocalDB account/file permissions are the security boundary. If adversarial per-operator isolation is required, move privileged data operations behind a trusted process/service with independently authenticated user identity and least-privilege SQL credentials. Do not consider a client-set SESSION_CONTEXT alone an adequate fix if arbitrary clients can connect under the same DB principal. If this is intentionally a single trusted Windows workstation/account, explicitly accept and mitigate via OS ACLs, workstation protection, backups, and restricted developer/SQL tools.

**Validation.** Test using a separately authorized local Windows identity and a controlled disposable database; verify actual SQL principal and file/instance ACL behavior without attempting against real user data.

### BL-04 — Chronological admission-number ordering is a C# preflight, not an atomic database invariant

**Severity:** P2 / Medium. **Evidence:** CONFIRMED control-location mismatch; a simultaneous-user violation is a scenario to test.  
**C#:** [Core/AdmissionNumberPolicy.cs:9–19](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Core/AdmissionNumberPolicy.cs#L9-L19), [Core/AdmissionNumberPolicy.cs:67–109](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Core/AdmissionNumberPolicy.cs#L67-L109), [Pages/Students/Admissions/StudentAdmissionPage.cs:4221–4232](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Students/Admissions/StudentAdmissionPage.cs#L4221-L4232).  
**SQL:** DACPAC model.xml: dbo.AdmitStudent, dbo.AdmitHistoricalStudent, dbo.ReserveAdmissionNumber, dbo.SetStudentAdmissionNumber.

**Observation.** AdmissionNumberPolicy.Calculate and Validate compute lower/upper surrounding register numbers from the current students list, enforcing ordinary admission-date order. A deliberately verified historical register entry can bypass chronological bounds, while keeping uniqueness. The SQL allocator/insert enforces normalization/uniqueness and serializes the counter, **but does not re-check the ordinary date-order bounds** that the C# planner validated. The planner rereads just before submitting, but the date/number plan and SQL admission are separate database operations; a later concurrent admission can change the bounds before insert. This is a trust-boundary/TOCTOU gap for the **chronology** invariant, not a known duplicate-number gap.

**Example.** Two desks open overlapping historical dates, each selects a unique yet order-dependent number. Both pass separate snapshots; one commits before the other's SQL insert. SQL may accept a final ordering that a fresh C# validation would reject, because uniqueness alone does not detect the resulting chronological inversion.

**Remedy.** If the chronological rule is mandatory, enforce it atomically within the admission transaction under appropriate serialization/range locking, with an explicit, auditable exception for verified historical-register imports. Keep C# advice/preview for UX but let SQL make the final decision. If it is deliberately only operator guidance (because physical registers may not be monotonic), document that as a business decision and ensure an explicit exception reason/review trail. The SQL layer must participate if concurrency-safe enforcement is desired.

**Tests.** Parallel admissions on earlier/later dates with different numbers, same-date suffixes, imports of legitimately out-of-order physical-register numbers, readmission with a new number, and manual correction of old admission numbers.

### BL-05 — Historical Left status does not demand a withdrawal date or an explicit unknown-date reason

**Severity:** P2 / Medium **if departure date is mandatory for historical records**. **Evidence:** CONFIRMED allowed input; requires business-policy decision.  
**C#:** [Pages/Students/Admissions/StudentAdmissionPage.cs:2640–2656](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Students/Admissions/StudentAdmissionPage.cs#L2640-L2656), [Pages/Students/Admissions/StudentAdmissionPage.cs:4213–4221](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Students/Admissions/StudentAdmissionPage.cs#L4213-L4221).  
**SQL:** DACPAC model.xml: dbo.AdmitHistoricalStudent.

**Observation.** A historical admission permits VerifiedStatus=Left with VerifiedWithdrawalDate=NULL. SQL validates a withdrawal date **if supplied** but does not require one when Left; C# similarly checks its date only when the picker is checked. Thus an explicit Left status can be stored without a known effective withdrawal date. This may be intentional for damaged or incomplete historical registers, so it should **not** be changed until the school chooses its policy.

**Impact.** Year-wise enrollment/exit reports, charge cutoffs, period reconciliation, and certificate timelines may have to interpret a Left record with no departure date. A dashboard that assumes Left implies a date could mislead.

**Remedy.** Either (A) require and validate the leaving date in both layers; or (B) deliberately allow unknown departure dates, but record an explicit reason/source/uncertainty flag and ensure downstream report filters display Unknown rather than invent a date. Do not silently replace missing historical dates with today.

**Tests.** Historical Active/Left with known/unknown withdrawal, date before enrollment, date after today, multiple years, and transition to later readmission.

### BL-06 — Standard discount changes rewrite already-posted but unpaid bills, regardless of period

**Severity:** P2 / Medium; **confirmed behavior, business-policy approval needed**.  
**C#:** [Pages/Finance/StudentAccounts/FeeAssignmentsDialog.cs:845–868](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Finance/StudentAccounts/FeeAssignmentsDialog.cs#L845-L868).  
**SQL:** DACPAC model.xml: dbo.SaveStudentFeeDiscount.

**Observation.** SaveStudentFeeDiscount changes the assignment's DiscountAmount, then updates DiscountAmount for **all non-cancelled BillingCharges belonging to that assignment without completed allocations**, capped by each charge's gross amount. It takes **no effective-date parameter** and does not scope the update to future bills. A past unpaid month can consequently show a newly edited net amount even though the bill was issued before the decision.

**Example.** September bill was posted for 1,000 and remains unpaid. In October a clerk edits the standard discount from 0 to 200 to apply prospectively. The September bill's discount can also change to 200. The statement total changes retrospectively.

**Remedy.** Decide whether discounts are intentionally retroactive. If prospective, add an effective-from scope and show a preview of affected bill IDs/months; preserve historic issued amounts and express retroactive adjustments as explicit credit/adjustment entries when necessary. If retroactivity is intentional, require a reason and record affected charges with before/after amounts in the financial audit trail. This requires SQL behavior plus C# UI/preview changes.

**Tests.** Posted/unpaid earlier month, partially paid month, completed fully paid month, cancelled bill, zero-net bill, closed enrollment, and month boundaries.

### BL-07 — Apply saved fee configurations intentionally permits partial success

**Severity:** P2 / Medium operational consistency risk; **not labeled a corruption bug**.  
**C#:** [Pages/Finance/Configuration/ApplyFeeConfigurationPage.cs:520–577](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Finance/Configuration/ApplyFeeConfigurationPage.cs#L520-L577).  
**SQL:** DACPAC model.xml: dbo.ApplyFeeConfigurationToExistingAssignments and dbo.ChangeStudentFeeAssignmentAmount_Internal.

**Observation.** The outer SQL procedure obtains a candidate list and loops per assignment, catching a failure, counting Failed, and continuing to other students. XACT_ABORT is OFF in the outer procedure, with inner assignment transactions. The UI also iterates through scopes and accumulates Total/Succeeded/Failed, displaying errors. This is clear **designed best-effort semantics**, and the UI does not misrepresent the operation as all-or-nothing.

**Impact.** Two students in the same class can have different rates after one application attempt. If an operator overlooks or fails to resolve partial failures, subsequent billing may vary unexpectedly.

**Remedy.** Retain partial progress if appropriate, but make failed IDs durably retrievable and offer an explicit **retry only failed / reconcile affected assignments** workflow, with a mandatory post-apply discrepancy report. If the business requires synchronized cohort rates, move to a staged plan plus one transaction for the entire approved scope; weigh lock duration and contention. This cannot be declared a defect without choosing the intended atomicity.

**Tests.** Introduce one invalid/locked candidate in a disposable database; confirm affected vs unaffected changes, failure visibility, retry idempotency, and post-application preview/charge behavior.

### BL-08 — Daily collection approval is a snapshot/revision process, not a hard accounting-period seal

**Severity:** P2 / Medium, conditional on financial-control policy. **Evidence:** The backdating pathway and stale detection are CONFIRMED; unrestricted approval validity over time is a POLICY QUESTION.  
**C#:** [Pages/Finance/Collection/FeeCollectionPage.cs:2690–2703](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Finance/Collection/FeeCollectionPage.cs#L2690-L2703), [Pages/Finance/Collection/FeeCollectionPage.cs:3014–3049](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Finance/Collection/FeeCollectionPage.cs#L3014-L3049), [Pages/Finance/Collection/DailyCollectionClosingPage.cs:72–90](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Finance/Collection/DailyCollectionClosingPage.cs#L72-L90), [Pages/Finance/Collection/DailyCollectionClosingPage.cs:192–205](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Finance/Collection/DailyCollectionClosingPage.cs#L192-L205).  
**SQL:** DACPAC model.xml: dbo.ProcessFeeTransaction, dbo.ProcessFamilyFeeTransaction, dbo.DailyClosingSnapshot, dbo.SaveDailyCollectionClosing, dbo.ApproveDailyCollectionClosing, dbo.GetDailyCollectionClosing.

**Observation.** Fee Collection permits an operator-specified transaction date up to today (subject to enrollment/payment validations). The transaction procedures do not consult an approved daily closing to categorically block a later posting into that date. In contrast, the closing procedures hash and version payment snapshots, verify the hash at submit/approve, and the UI already displays **Needs review** and rechecks before printing if data changes. Thus it is **incorrect** to claim the application silently presents stale closings as current; a meaningful detection mechanism exists. The open question is whether post-approval backdating should be possible at all.

**Scenario.** Monday's closing is approved. On Tuesday, an operator records a permitted payment dated Monday. The old approval's payment set is no longer current; the day must be reviewed and possibly closed/approved again.

**Remedy.** Choose one explicit policy: (A) hard-close the approved date and require a privileged reopen with reason and audit, blocking backdated writes at SQL transaction entry; or (B) allow audited late corrections and make stale/reopened/reapproved revisions first-class status records. Ensure all entry points—single collection, family collection, correction, refund, reversal—follow the same policy, and reports clearly distinguish historic approved revision from currently reconciled day.

**Tests.** Late receipt after approval, backdated refund/correction, replacement revision and administrator reapproval, print/export of old revisions, concurrent payment vs approval.

### BL-09 — Some charge mutations have less granular audit data than collection-time discount edits

**Severity:** P2 / Medium financial traceability. **Evidence:** CONFIRMED differences in recorded audit payloads.  
**C#:** [Pages/Finance/Collection/CollectionDiscountWriter.cs:19–51](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Finance/Collection/CollectionDiscountWriter.cs#L19-L51).  
**SQL:** DACPAC model.xml: dbo.SaveStudentFeeDiscount, dbo.DeleteStudentFeeAssignment, dbo.WriteAuditLog.

**Observation.** SaveStudentFeeDiscount can update many BillingCharges, but writes an audit entry describing the **StudentFeeAssignments** old/new discount, not a separate old/new value for each changed BillingCharge. DeleteStudentFeeAssignment cancels associated BillingCharges while recording the assignment's CancelledAt. In contrast, the collection-time discount writer logs before/after against a specific BillingCharges ID. The audit history therefore has varying granularity for operations that change the financial ledger's charge rows. This does **not** mean there is no audit log; it means the charge-level delta is not captured consistently.

**Impact.** A reviewer trying to reconstruct exactly which unpaid months changed and by how much may have to infer it from surviving current state and assignment audits; later actions can make that reconstruction difficult.

**Remedy.** Capture changed row IDs with OUTPUT inserted/deleted into table variables and write correlated old/new charge amounts and statuses **inside the same transaction** as the change. Avoid excessive payload bloat: one operation ID with a structured list of affected rows is acceptable. Make reason codes and actor IDs mandatory for retroactive financial changes. This is primarily a SQL/audit design change.

**Tests.** One assignment affecting many posted months, zero vs partly paid, cancellation and discount changes; verify that every financial-row mutation maps to an identifiable audit operation and reconcile sums.

### BL-10 — Annual fee ledger dynamically supports longer periods; consumer compatibility should be tested

**Severity:** P3 / Low. **Evidence:** TEST NEEDED, not a confirmed failure.  
**SQL:** DACPAC model.xml: dbo.GetStudentAnnualFeeLedger.  
**C#/report context:** [Pages/Reports/Finance/AnnualFeeReceiptsPage.cs](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Reports/Finance/AnnualFeeReceiptsPage.cs), [Core/RdlcReportPreview.cs](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Core/RdlcReportPreview.cs), templates under Pages/Reports/Templates.

**Observation.** The ledger SQL explicitly computes month count from stored academic-year dates and dynamically generates at least twelve M1..M12 columns, adding further month columns for longer saved years. Report templates and consumers may be based on a fixed twelve-month layout; the two shapes should be tested together if nonstandard year ranges are supported. Dynamic SQL uses constructed numeric index and parameterized values; the observation itself does not establish SQL injection or a broken report.

**Remedy.** Define a supported academic-year duration contract. For variable years, use row-oriented month output or a well-defined templating strategy; for fixed twelve-month years, validate duration on save and report generation. Avoid changes unless a tested discrepancy appears.

**Tests.** Calendar academic year, April–March, short year, 13- and 15-month year (only if allowed), no bills, partial receipts, and mixed monthly/annual charges.

## Cross-layer safeguards verified and not reported as defects

1. **Paid charge cancellation protection:** dbo.GuardClosedEnrollmentCharges explicitly checks completed payment allocations before allowing a charge to transition to Cancelled, with locking. Therefore the pre-transaction eligibility check in dbo.DeleteStudentFeeAssignment **must not be described as a confirmed paid-charge cancellation bug in the current schema**. The trigger is an important dependency that strengthens BL-02.
2. **Fee period integrity:** dbo.GuardFeeAssignmentPeriods blocks overlapping active assignment periods and modifications of closed-enrollment fee assignments.
3. **Enrollment uniqueness:** UX_Enrollments_OneActivePerAdmission is a filtered unique index on AdmissionId for Active records.
4. **Admission uniqueness:** UX_Students_AdmissionNumber and counter-row UPDLOCK/HOLDLOCK protect unique number allocation. This is distinct from BL-04's chronological ordering rule.
5. **Billing duplicate protection:** filtered unique monthly and annual charge indexes are present, including enrollment/fee/month scope.
6. **Transactional protection:** core admission, transfer, re-admission, monthly posting, payment, and closing paths use transactions; several important paths also take explicit locks. The family payment path uses a group transaction and calls the individual transaction procedure inside it.
7. **Ambiguous payment acknowledgement:** [Pages/Finance/Collection/CollectionDiscountWriter.cs:10–91](https://github.com/farhan2166-creator/FMSC-Database/blob/04647e7ecee3510cc614331f76bdb5d3709c221c/Pages/Finance/Collection/CollectionDiscountWriter.cs#L10-L91) keeps a local recovery journal and distinguishes failed commit acknowledgement from confirmed rollback, rather than blindly retrying.
8. **Closing stale-data defense:** approval and printing compare current snapshot data, and the UI surfaces Needs review. BL-08 is a decision about how strictly to *seal* dates, not absence of stale-detection logic.
9. **Partial fee configuration progress:** C# reports success/failure counts rather than treating partial failures as global success.
10. **Deployment scope:** User-established MDF/LDF packaging responsibility is documented in AGENTS.md and is intentionally **excluded** as a finding. This review is about schema and C# behavior, not missing local release assets.

## Prioritized validation / scenario-test plan

Use a **disposable restored database and disposable test users**; do not run these destructive cases against real students.

| Order | Test | Expected result / decision criterion |
|---|---|---|
| 1 | Admit student with Students.Admit but without Students.Administer | If role is intended to admit, numbering and save both work (BL-01) |
| 2 | Tamper a throwaway same-version schema by removing an application-used procedure | Compatibility check fails at startup before workflow begins (BL-02) |
| 3 | Remove GuardClosedEnrollmentCharges from throwaway schema | Compatibility check rejects missing trigger before paid-charge workflows (BL-02) |
| 4 | Change transfer-procedure arguments in throwaway same-version schema | Compatibility check detects missing @FeeEffectiveMonth/@ReplaceScheduledFees (BL-02) |
| 5 | Two concurrent admissions with unique register numbers and different dates | Chronology and historical exception behavior adhere to chosen rule (BL-04) |
| 6 | Historical Left record with missing withdrawal date | Explicit reject or explicit Unknown-with-reason, per chosen policy (BL-05) |
| 7 | Change fee discount after earlier bill has been posted but unpaid | User-facing effective-date/retroactivity policy is honored and audit matches (BL-06/09) |
| 8 | Discount change when a bill is partially paid | Paid allocation and outstanding net remain consistent; no accidental past paid rewrites |
| 9 | Concurrent fee collection vs removal of fee assignment | Current trigger refuses cancelling paid charge; only one valid operation wins, no orphan data |
| 10 | Bulk fee-rate application with one invalid row | Partial-result and retry policy is followed; totals reconcile (BL-07) |
| 11 | Enter backdated single/family payment after administrator has approved that day | Old approval does not masquerade as current; decision on re-open/lockout enforced (BL-08) |
| 12 | Correct/reverse past payment following daily approval | All adjustments use the same closing policy; ledger and receipts reconcile |
| 13 | Transfer across class/campus while monthly bills exist, including partially paid future charges | Reviewed fee sync/replacement semantics and transaction rollback are correct |
| 14 | Re-admit student keeping number vs allotting new number | Registration identity is stable; chronology/uniqueness, prior accounts, fees, and history remain correct |
| 15 | Promote multiple students with one invalid decision and year boundary | No unintended partial promotion or double fee posting; recorded reason/history consistent |
| 16 | Issue/collect/reissue certificates after enrollment corrections | Certificate numbering, immutable fields, history links, and access rules survive |
| 17 | Reconcile financial statement vs charges/allocations/refunds | Beginning balance + charges – payments + valid reversals/corrections equals ending balance |
| 18 | Run with >12-month academic-year only if allowed | Dynamic SQL output and report rendering agree (BL-10) |

### Suggested runtime invariants to compare after each destructive test

- One Active enrollment per student; status and leaving dates follow the documented lifecycle model.
- Admission numbers are unique; registration numbers remain stable where policy demands.
- For each non-cancelled charge, gross – discount – net paid equals the displayed outstanding balance; no forbidden negative balance.
- No completed paid charge is cancelled, and there are no allocations pointing to the wrong admission/enrollment/charge.
- Monthly and annual posting remains idempotent under double-click/retry/concurrency.
- Family receipt grouping maps each student payment correctly; correction/refund/reversal totals balance.
- A valid action is traceable by actor, operation, time, affected IDs, and before/after material amounts.
- A closing marked Approved corresponds to its documented snapshot revision; later change becomes visible and requires chosen process.

## Scope, assumptions and limitations

- This was a **static** review using the actual latest repository SHA given at the top, source files fetched directly from that SHA, and **the updated schema-only DACPAC**. No cached prior database model was substituted.
- The archive contained model.xml, DacMetadata.xml, [Content_Types].xml and Origin.xml; Origin.xml reported ContainsExportedData=false. No customer/student rows were accessible.
- The project contains 267 C# source files; the tree was completely enumerated, with representative high-risk files read and procedure calls traced. The audit did **not** establish line-by-line review of every source file, every procedure path, every index shape, every report expression, or every UI transition.
- The DACPAC contains 228 procedure definitions; a broad inventory/permission/transaction scan was performed, with targeted full-body checks for admissions, enrollment/transfer, fee assignments, collections, closing, compatibility, and integrity triggers. More specialized workflows still deserve runtime scenario testing.
- C# was **not compiled**, the WinForms UI was **not launched**, there was **no SQL connection to a live or clone database**, and no concurrency test or performance measurement was executed. Risk scenarios are not results of executed tests.
- A schema-only DACPAC does not establish real-world data consistency, query-plan performance, storage permissions, real OS principal access, or migration correctness.
- Finding IDs BL-05, BL-06, BL-07, BL-08 and BL-10 involve policy/design questions or testing, and must **not** be presented as confirmed production incidents.
- The only repository write requested/authorized for this task is **creating this Markdown report under Audit**. No C# source, SQL artifacts, installer files, or existing audit documents were changed.

## Recommendation for the next step (not performed)

First correct/test the limited-admission role path (BL-01) and make the runtime schema compatibility contract complete (BL-02), then decide the locally trusted workstation/database threat boundary (BL-03). Before any financial schema changes, approve explicit policies for historical date uncertainty, retroactive discounts, partial bulk rate changes, and reopening approved closing periods. Execute the prioritized scenarios on a disposable clone and compare financial/lifecycle invariants after each operation. Make small, separately verified fixes only after these decisions; this report itself makes no changes.

