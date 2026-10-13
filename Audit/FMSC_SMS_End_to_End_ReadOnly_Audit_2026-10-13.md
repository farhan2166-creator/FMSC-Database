# FMSC_SMS — End-to-End C# / DACPAC Business, Workflow and End-User Audit

**Report date:** 13 October 2026 (Pakistan time)
**Repository:** `farhan2166-creator/FMSC-Database`
**Reviewed branch and head:** `main` — [`063e4e69c8bd82bbad154252fc2a99d8cb8c92f3`](https://github.com/farhan2166-creator/FMSC-Database/commit/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3)
**Schema artifact:** `FMSC.dacpac` (Git blob `e98af9460d69095458b880bbee74cd4995711653`, 253,120 bytes). Its compressed `model.xml` was decoded and inspected.
**Mode:** **READ-ONLY.** No GitHub files, application files, SQL database, commits, branches, or schema artifacts were modified. This report is a new local output, **not** a commit to the repository.

## Executive judgment

The application has meaningful database-side controls and thoughtful workflows: direct admission creates an active placement and opening bills in a single transaction; Quick Registration deliberately creates a pending record and allocates official numbers only when completed; family collections are designed to be atomic; pending placements are guarded against normal later-month fee posting; cancellation and leaving have restrictions; and posting and review operations have several stale-preview safeguards. These protections are **real source-level findings**, not an assumption that nothing has been built.

**However, the static evidence does not justify declaring the system fully deployment-safe.** Priority areas are fractional-money handling across fee entry and collection; stale fee previews; concurrent edits; an unsaved-form navigation path; and a DACPAC-to-contract fingerprint mismatch. These do not all imply corrupt data has already occurred. Several require controlled two-user or Windows UI tests to establish operational impact. No actual SQL Server restore, Windows WinForms interaction, SQL integration script execution, concurrency harness or production-style load test was performed in this session.

### Evidence and scope

- Inventoried **333 tracked files** at the reviewed commit, including principal student, finance, security, setup, reports and administrative modules. Inspected selected source and workflow entry points rather than asserting that every line of all files or all SQL routines was exhaustively reviewed.
- Extracted the DACPAC ZIP and decompressed `model.xml` (about 3.22 MB). Identified **58 SQL tables, 232 procedures and 34 DML triggers**, along with functions, views and constraints. Compared important SQL procedure behavior with C# callers.
- Examined the embedded [`DatabaseCompatibilityRequirements.json`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Core/DatabaseCompatibilityRequirements.json): 332 listed executable/schema objects, 974 procedure parameters, 34 trigger requirements, 71 FK shapes and 42 check-constraint entries.
- Cross-checked **62 literal procedure calls across seven central C# workflow files** against the embedded procedure inventory: **all 62 referenced existing procedure names**. This is a targeted consistency check, not a validation of all call argument combinations or result-column contracts across the entire application.
- Read the current [`ADMISSION_WORKFLOW.md`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/ADMISSION_WORKFLOW.md) and existing [`2026-10-12 audit`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Audit/2026-10-12_Business_Logic_and_Schema_CSharp_Cross_Audit.md) so previously approved business choices are not mistakenly reopened as defects.
- The source contains two substantial PowerShell integration-test scripts, [`AdmissionWorkflowChecks.ps1`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Validation/AdmissionWorkflowChecks.ps1) and [`StudentCorrectionChecks.ps1`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Validation/StudentCorrectionChecks.ps1). Their test scenarios were **read, not executed**.

**Confidence labels:** **CONFIRMED** = exact source behavior or artifact fact; **CONFIRMED WINDOW** = competing events can cause the stated outcome, but a timed runtime demonstration is still required; **CONDITIONAL** = depends on permissions, school policy or data; **TEST NEEDED** = concern identified but not established as a current defect. Severity is the risk if the condition occurs, not a count of observed production incidents.

## Prioritized findings register

| ID | Priority | Confidence | Concern | Proposed ownership |
|---|---|---|---|---|
| F-01 | **P1 High** | CONFIRMED cross-module mismatch | Decimal fees/discounts permitted but collection allows only whole-rupee amounts | Business decision + C# + SQL |
| F-02 | **P1 High** | CONFIRMED WINDOW | Quick Registration fee preview may not match rates actually billed when fee configurations change | C# + SQL |
| F-03 | **P1 High** | CONFIRMED WINDOW | Pending-registration details permit last-writer-wins overwrites between operators | C# + SQL |
| F-04 | **P1 High** | CONFIRMED | Quick Registration can silently discard unsaved form edits when opening/reviewing a record | C# UI |
| F-05 | **P2 Medium** | CONFIRMED artifact fact | DACPAC SHA-256 and compatibility-contract provenance hash differ | Build/release process |
| F-06 | **P2 Medium** | CONDITIONAL | `Students.Admit` alone exposes Quick Registration but cannot satisfy the Pending list's separate `Students.View` check | Role design + C# / SQL |
| F-07 | **P2 Medium** | CONFIRMED limitation | Startup compatibility checks object/signature/safeguard *shape* but not procedure/trigger body semantics | Compatibility verification |
| F-08 | **P2 Medium** | CONFIRMED implementation; impact unmeasured | Pending/legacy search reloads and copies whole tables on each keystroke; per-record balance work | C# + SQL/index profiling |
| F-09 | **P3 Low/Medium** | CONDITIONAL | Pending list filters on student status alone, not explicitly on a Quick Registration discriminator | SQL query / workflow policy |
| F-10 | **P3 Low/Medium** | TEST NEEDED | Modal size and DPI behavior at constrained Windows working areas | C# WinForms QA |
| F-11 | **P2 Release gate** | CONFIRMED coverage gap | Existing scripts do not demonstrate two-operator UI races and whole-vs-fractional fee behavior | Integration/acceptance tests |
| F-12 | **P3 Maintainability** | CONFIRMED size; impact qualitative | Several critical UI controllers are very large and mix business orchestration, SQL and presentation | C# architecture |

---

## Detailed findings and proposed remedies

### F-01 — Fractional fees can become impossible to collect exactly

**Evidence.** [`QuickRegistrationPage.cs:76-85`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Students/Admissions/QuickRegistrationPage.cs#L76-L85) represents fees and discounts as decimals, validates a range but not integer rupees, and displays money in `N0` format. Its SQL call uses decimal discounts, and the DACPAC models `BillingCharges`/`StudentFeeAssignments` monetary values as decimal-scale columns. Conversely, [`FeeCollectionPage.cs:142-153`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Finance/Collection/FeeCollectionPage.cs#L142-L153) accepts payment text containing digits **only**; [`UseFullAmount` at 750-769](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Finance/Collection/FeeCollectionPage.cs#L750-L769) refuses auto-fill when payable is fractional; [`Process Collection` at 2682-2685](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Finance/Collection/FeeCollectionPage.cs#L2682-L2685) rejects decimal payment input.

**Example.** An opening fee of Rs. 1,000 with a discount of Rs. 0.50 produces a collectible balance of Rs. 999.50. Quick Registration permits the discount and may display a rounded whole-rupee amount, but normal Fee Collection cannot receive **999.50** precisely. Taking Rs. 999 leaves Rs. 0.50 outstanding; recording Rs. 1,000 may over-allocate or be refused. The normal user is pushed into unnecessary corrections to settle a legitimate bill.

**Root cause.** Inconsistent money precision policy across configuration, charge construction, discount entry, display and collection—not a missing arithmetic operator in one place.

**Proposed correction.** Decide centrally whether the school supports paisa. If the policy is **whole rupees only**, reject non-integral amounts in every fee configuration, opening discount, assignment discount, adjustment and posting path, and add SQL safeguards at the authoritative write procedures; do not silently round existing data. If two decimals are required, accept and format two-decimal payments end-to-end, including allocation and receipt logic. Migration/cleanup of already-fractional balances should be reviewed separately rather than automatically rounding historical records.

**Re-test:** Rs. 1000.00, Rs. 999.50, Rs. 0.01, sibling payment across one fractional and one whole charge, discount-only adjustment, receipt and outstanding reports. Ensure sum of paid plus discounts plus balance reconciles to gross with no stranded fractions.

### F-02 — Stale fee quotation between form and SQL save

**Evidence.** [`QuickRegistrationPage.cs:146-175`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Students/Admissions/QuickRegistrationPage.cs#L146-L175) loads the current `FeeConfigurations.Amount` into a local `DataTable`. [`SaveAsync:218-245`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Students/Admissions/QuickRegistrationPage.cs#L218-L245) sends fee-type IDs and discount values, **not the quoted fee rates or a rate-version token**. The DACPAC's `dbo.AdmitStudent` builds `StudentFeeAssignments` from then-current `FeeConfigurations` and posts the opening charges. The C# success path retains its original displayed rates instead of reloading the saved bill.

**Two-user reproduction.** Clerk A opens QR for a class showing tuition Rs. 2,000. Admin B changes the class rate to Rs. 2,500 and saves. Clerk A saves the still-open QR. SQL can charge Rs. 2,500 while Clerk A's form still displays Rs. 2,000. Different discounts may cause save failure when based on the old rate.

**Impact.** Unexpected invoices, disagreement between operator/parent and stored opening bills, avoidable reversals. **Requires the rate to change during the form's lifetime; not reproduced against a running SQL instance here.**

**Proposed correction.** Include an optimistic quote signature/version or the expected fee amounts in the save contract and reject/re-preview if underlying fees changed. At a minimum, reload persisted assignments and bills after save and show a prominent differences confirmation; the proper prevention is to compare the quote **inside the same SQL transaction** that creates the assignments. Do not freeze all fee editing for a school just to prevent this rare collision.

### F-03 — Concurrent pending edits can overwrite more recent data

**Evidence.** [`QuickRegistrationPage.cs:248-289`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Students/Admissions/QuickRegistrationPage.cs#L248-L289) loads name, parent, contact and address, but `SavePendingAsync` checks only that the record remains `Pending`, re-reads the **parent** from the database, and sends displayed name/contact/address plus changed fee discounts to `SaveStudentDetailsWithFirstEnrollment`. No original record `rowversion`, `UpdatedAt` expectation or old-field comparison is sent. The SQL procedure does use a transaction and locks, which makes an individual write atomic, but cannot detect whether an operator's form is out of date without an expected version/value.

**Reproduction.** Clerk A and Clerk B load the same QR. B changes phone number and saves. A changes only the name and saves the older form: A's unchanged but stale phone can overwrite B's new phone. A similar collision can affect unpaid fee discounts. Current code protects parent-name copying better than it protects these other basics.

**Proposed correction.** Add a rowversion/expected state token, compare it when acquiring SQL update locks, and return a structured conflict requiring a fresh review rather than silently overwriting. Prefer an explicit merge/reload experience for non-overlapping basic-field edits. Protect fee-assignment changes with their own version or expected discount when updating them in the same transaction. Keep existing payment/paid-charge guards intact.

**Re-test:** two windows/roles editing the same pending QR, both save orders, simultaneous confirmation, simultaneous payment vs discount change; verify either a safe merge or explicit conflict without lost updates.

### F-04 — Unsaved Quick Registration form edits can be discarded silently

**Evidence.** [`QuickRegistrationPage.cs:13-21`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Students/Admissions/QuickRegistrationPage.cs#L13-L21) changes to the form tab and calls `form.OpenRegistrationAsync(id)` from Pending Registrations. [`StudentDataReviewPage.cs:58-65`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Students/Maintenance/StudentDataReviewPage.cs#L58-L65) also raises `RegistrationReviewed` **after every dialog close**, even without confirmation, and the container reloads the current form when IDs match. [`OpenRegistrationAsync:291-295`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Students/Admissions/QuickRegistrationPage.cs#L291-L295) does not check `HasPendingChanges`; [`LoadRegistrationAsync:248-273`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Students/Admissions/QuickRegistrationPage.cs#L248-L273) replaces form fields and fees.

**Reproduction.** Open a pending QR; change the contact number without saving; move to Pending Registrations, select the same QR and open Review / Confirm, then simply close the dialog. The form is reloaded and the unsaved contact disappears. Opening a different pending QR is another overwrite path.

**Proposed correction.** Guard *cross-record opening* and *review-triggered reload* when `HasPendingChanges` is true, offering Save / Discard / Stay or blocking with a clear save-first message; refresh only after a successful completion/cancellation when appropriate. The user's separate product policy that **New/Clear** need not prompt can remain unchanged—this finding concerns implicit navigation/reload.

### F-05 — DACPAC provenance hash disagrees with embedded contract

**Evidence.** The exact current `FMSC.dacpac` package was decoded (253,120 bytes). A SHA-256 implementation verified against a standard known hash test returned:

- **Actual package SHA-256:** `872597ce4a56dd34c72c06a7a5bee02e2149c5e61f959f76bec39727e2ad3eef`
- **Embedded contract `schemaArtifact.sha256`:** `52661aa414251a9b46440912903b158907eb96bf8a6d7dea2ab18e3c59fb7a1e`

The extracted DACPAC model and contract list the same **headline object counts** (58 tables, 232 procedures, 34 triggers); this is **not evidence** of a broken Quick Registration procedure. It may be a genuine model revision or a ZIP/package repack; the discrepancy alone cannot decide. [`DatabaseCompatibility.cs`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Core/DatabaseCompatibility.cs) checks schema structure at startup, but does not use this package-level SHA to fingerprint current SQL module bodies.

**Proposed correction.** Confirm whether `schemaArtifact.sha256` is intentionally defined as the ZIP package's hash or a canonicalized source representation. If ZIP bytes, regenerate/verify the contract from this exact approved DACPAC and fail a read-only release check when they differ. If canonical, change the field name and document the canonicalization to avoid mistaken assumptions. **Do not rewrite the DACPAC or schema as a first response**; establish why the hashes differ and compare model elements/procedure definitions first.

### F-06 — Admission-only role has a Quick Registration access inconsistency

**Evidence.** [`WelcomePage.cs:503-509`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Dashboard/WelcomePage.cs#L503-L509) shows Quick Registration for `Students.Admit`. The registration form's fee query also gates on `Students.Admit`. But the Pending Registrations list query in [`StudentDataReviewPage.cs:86-96`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Students/Maintenance/StudentDataReviewPage.cs#L86-L96) explicitly calls `dbo.RequirePermission @UserId, N'Students.View'`. [`CurrentUser.cs`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Security/CurrentUser.cs) tests permission codes as distinct set members except for administrators.

**Conditional impact.** If a school configures a legitimate admission clerk with `Students.Admit` but without `Students.View`, the dashboard offers Quick Registration but the pending list may fail to load. Existing roles may already contain both rights; SQL role policy could also grant an implication not visible in C#.

**Proposed correction.** Decide the intended minimum role. Either require both rights visibly before offering the feature, ensure the role setup assigns the required rights, or change only the pending QR list authorization to allow an appropriately scoped admission permission. **Do not bypass general Students.View for unrelated student data.** Test with a custom limited role and one administrator.

### F-07 — Compatibility checks do not prove business behavior matches the DACPAC

**Evidence.** [`DatabaseCompatibility.cs:24-148`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Core/DatabaseCompatibility.cs#L24-L148) compares version, required objects, parameter signatures, column shapes, trigger enabled/event/parent status, foreign keys, check constraints and unique indexes. It does **not** compare SQL procedure or trigger *definitions*. An older/modified stored procedure with unchanged name and parameters can pass that check even if it implements different fee/registration rules.

**Scope.** This is a verified limitation, not proof the current packaged DACPAC contains an incorrect body. A large structural contract is already a strong safeguard.

**Proposed correction.** Add an offline release-side semantic-drift comparison of important stored procedures/triggers (normalized `model.xml` against the approved schema source, or explicit module hashes). Optionally check known critical module definitions in a disposable database. A SQL body hash may be too brittle for every whitespace change at application startup; prefer deterministic build/release checks plus targeted runtime canaries.

### F-08 — Pending and legacy lists scale by copying all records for each keystroke

**Evidence.** [`StudentDataReviewPage.cs:86-116`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Students/Maintenance/StudentDataReviewPage.cs#L86-L116) fetches all matching SQL records, computes an outstanding-balance aggregate using a scalar charge-balance function, and locally `Clone`+`ImportRow`s a second `DataTable` for *each* search change; `Grid.DataSource` is rebound after every keystroke. The entire result is retained as `records` while creating another filtered result.

**Impact.** Possible UI latency, temporary memory churn and redraw cost with thousands of students and large billing histories. No benchmark was run, so avoid asserting a particular slowdown.

**Proposed correction.** First measure using realistic 2k/10k+ student and charge fixtures. If slow, debounce 150–250 ms, use a `DataView` where filtering semantics match, defer expensive balances until needed, page/search server-side and ensure relevant indexes. Preserve existing keyboard/search behavior and result counts; do not replace it with more complex caching unless profiling shows a benefit.

### F-09 — Pending list selection is broader than its label

**Evidence.** SQL in [`StudentDataReviewPage.cs:86-96`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Students/Maintenance/StudentDataReviewPage.cs#L86-L96) uses `(@Pending=1 AND S.Status=N'Pending')`. It does not require `QuickReference IS NOT NULL`, whereas `dbo.CompleteQuickAdmission` explicitly requires a pending record **with** a QuickReference and without an official AdmissionNumber.

**Conditional impact.** If another workflow or legacy record can have `Students.Status='Pending'` without being a QR, the Pending Registrations screen may show it even though confirmation will reject it. Normal new direct admission appears to become Active immediately, so this may not occur in current clean data.

**Proposed correction.** If the list's business definition is *uncompleted Quick Registrations*, use the same discriminator as the completion procedure and test historic/legacy records; if it intentionally includes all Pending states, rename the screen and supply relevant actions. Check the actual data distribution before altering SQL.

### F-10 — Modal DPI and working-area handling requires actual Windows verification

**Evidence.** [`StudentDataReviewPage.cs:62`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Students/Maintenance/StudentDataReviewPage.cs#L62) opens a registration review dialog using sizes scaled from 1280×900 and a minimum scaled from 1000×700. [`AppScale.cs`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Core/AppScale.cs) adapts geometry to a floor of 82%, so a relatively large minimum remains. A form may still be usable after Windows resizing or clipping; static code alone does not prove actual failure.

**Proposed correction.** Test 100%, 125% and 150% on physically constrained monitor working areas; ensure the entire Save/Confirm/Cancel action footer remains reachable. If not, cap dialog bounds to `Screen.WorkingArea` and make the content, not the window chrome or primary actions, scroll as necessary. Record screenshots and tab-order observations.

### F-11 — Release testing leaves the new high-value edge cases unproved

**Evidence.** The current [`AdmissionWorkflowChecks.ps1`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Validation/AdmissionWorkflowChecks.ps1) includes good isolated cases for direct admission, QR numbers, family links, payment-before-completion, double completion, cancellation limits, departure and Go Live. [`StudentCorrectionChecks.ps1`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Validation/StudentCorrectionChecks.ps1) checks correction/placement and paid-charge protections. The scripts don't themselves prove WinForms form reloading, two-clerk races or fractional-amount collection. They were not executed in this session.

**Proposed correction.** Keep these tests and add *targeted* tests for F-01–F-06. Run only on disposable restored data, assert invariants and compare financial balances before/after. See the runbook below.

### F-12 — Large high-risk WinForms controllers increase regression risk

**Evidence.** [`StudentAdmissionPage.cs`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Students/Admissions/StudentAdmissionPage.cs) is about **4,790 lines**; [`FeeCollectionPage.cs`](https://github.com/farhan2166-creator/FMSC-Database/blob/063e4e69c8bd82bbad154252fc2a99d8cb8c92f3/Pages/Finance/Collection/FeeCollectionPage.cs) about **4,159 lines**. Both contain UI orchestration, validation, SQL preparation, status rules, calculations and report interactions. Size alone is not a runtime bug; changing these monolithic screens is nevertheless harder to test and reason about, especially around event handling.

**Proposed correction.** Avoid speculative broad rewrites. After fixing behavior, extract only independently testable business plans: fee preview and save reconciliation, QR dirty-state transitions, amount/discount policy, receipt allocation, and concurrency tokens. Retain public page behavior and reuse existing core helpers instead of creating parallel duplicate rules.

---

## Database and workflow protections **verified**, not falsely reported as defects

| Workflow | Current protective behavior observed | Remaining caveat |
|---|---|---|
| Direct admission | DACPAC `dbo.AdmitStudent` validates placement, dates, mandatory class fees and discounts, then creates Student, Enrollment, assignments and opening charges under `XACT_ABORT`/transaction | Still needs actual concurrent save/billing run |
| Quick registration | Same procedure marks student/enrollment Pending, assigns `QR-` reference, does not allocate official admission/registration number yet | UI rate/stale edit issues above |
| Completion | `dbo.CompleteQuickAdmission` locks/checks eligible pending QR, verifies full details, reserves both official numbers and activates the **same** enrollment without reposting bills | Run race against concurrent edits and payments |
| Provisional placement | DACPAC `GuardProvisionalPlacement` prevents direct promotion to active; `GuardClosedEnrollmentCharges` rejects later-month pending bills | Inspect every unusual direct-SQL entry point under live permissions |
| Cancellation / departure | `dbo.CloseStudentAdmission` has preview/snapshot, blocks cancelling formally admitted students, checks payment allocations and future charges | Confirm actual operator permissions and reason/date UX |
| Fee posting | `PostSelectedMonthlyFees` selects explicitly ticked rows, uses transaction and checks already-active charges; C# reports counts | Test two concurrent posters / reversed charges |
| Family collection | `ProcessFamilyFeeTransaction` validates charge/student/enrollment ownership and aggregates family rows under one transaction; `CollectionDiscountWriter` uses serializable C# transaction and interrupted-commit recovery journal | Simulate ambiguous commit acknowledgement and simultaneous payment |
| Bulk promotion | Matching-section mapping uses a serializable transaction and `BulkPromoteStudents` | Test conflicting destination mappings and batches |
| Legacy Go Live | `FinishLegacyAdmissionSetup` checks verified records, dates, fee readiness, stale preview and repeat execution | Test against disposable legacy fixtures |
| Database startup | Compatibility check verifies version and many structural integrity objects | Does not fingerprint SQL body semantics; see F-05/F-07 |

## Important owner-approved policies not reclassified as defects

The prior repository audit documents decisions to retain: manual valid admission-number overrides rather than mandatory chronological ordering; optional unknown departure date for eligible historical Left records; prospective monthly discount changes versus controlled nonmonthly unpaid-charge updates; partial application of bulk fee-rate changes with explicit results; daily collection closing as a revisable snapshot when late adjustments occur; assignment-level auditing for designated fee operations; and the current local SQL trust model. Those decisions should not be overridden by an audit without a new business requirement. Security risk from another local SQL client spoofing a caller-supplied application `UserId` remains **conditional on treating the local SQL connection as untrusted**; the user previously deferred a trust-model change.

## Detailed acceptance/scenario test runbook (disposable data only)

**P0 — must pass or be resolved before trusting the new release path**

1. **FIN-01 Fractional money:** Create/discount fees with `.01`, `.50`, `.99`; prove they are rejected at creation under whole-rupee policy *or* can be collected exactly without residuals under decimal policy. Check all receipts and reports.
2. **QR-01 Two-clerk stale quotation:** Clerk A opens fee preview, B edits the class rate, A saves; expect an explicit re-preview or reconciled amount with clear acknowledgment; no misleading initial quote.
3. **QR-02 Concurrent student edits:** Two windows edit same QR; ensure one save cannot silently undo the other operator's contact/address/name/fee changes.
4. **QR-03 Dirty navigation:** Change unsaved QR details; open another QR and open/close review of the same QR. Confirm no silent loss. Confirm that normal deliberate New/Clear behavior follows the chosen UX policy.
5. **SEC-01 Limited role:** Grant `Students.Admit` only and test opening QR, searching pending, linking family, saving, reviewing and confirming; verify permission failures are understandable and permissions are not broadened unintentionally.
6. **DB-01 Schema fingerprint:** Compare approved DACPAC byte hash, contract hash, procedure bodies and release database definitions; document any intentionally different package generation.

**P1 — high-value regression and integrity coverage**

7. QR saved with new family, then second sibling; inspect family identity, parent name lock, contact edits, bill grouping and whether one child can be cancelled without changing the other.
8. Register, collect part-payment, change unpaid discount, complete registration; expect one official admission number and one registration number, no duplicate bill and unchanged legitimate allocations.
9. Attempt second completion, cancel after completion, cancel partly paid QR, reverse/refund before cancellation. Verify consistent SQL refusals and correct operator text.
10. Concurrent confirmation versus cancellation, plus confirmation versus payment; no orphan charge, duplicate official number or half-activated enrollment.
11. Direct admissions with historical dates, old academic year, manual number override, sibling names duplicated, and number uniqueness under simultaneous save.
12. Promote/transfer/re-admit across year boundaries, missing destination sections, already confirmed years, history corrections and status transition collisions.
13. Two fee posters select the same month and rows; prove no double charge. Test reversed charge reposting, fee assignments whose rates changed and posting after departure.
14. Two collectors pay the same charge; test over-allocation prevention, family receipt number uniqueness, charge balance reconciliation and interrupted-commit recovery review.
15. Leave a student with current dues and future unpaid/paid charges; preview/close after another operator posts or collects; ensure stale preview rejected and historical dues remain collectible.
16. Go Live preview then edit a legacy record in another session; attempt applying stale snapshot, retry after completion, and test required fee/placement blockers.

**P2 — experience and capacity checks**

17. 100%, 125% and 150% DPI across at least 1024×768, 1366×768 and a normal large desktop; inspect QR review, fee collection, Go Live and confirmation dialogs for clipped action buttons.
18. 100, 2,000 and 10,000 student records with realistic bill counts; profile pending search responsiveness, repeated grid binding, SQL query plan and memory/GC peaks.
19. Backup and restore on a disposable database followed by startup compatibility validation and representative post-restore workflow.
20. Role matrix across Students.View, Students.Admit, Students.Administer, Fees.View, Fees.Collect, Fees.Adjust and administrators; ensure SQL and C# authorization outcomes align.

For each destructive test, assert:

- Exactly one canonical active/pending enrollment where policy requires it; no conflicting active placement.
- Official admission and registration numbers allocated exactly once, unique, and never allocated to merely pending QR records.
- Every posted charge has a valid assignment/student/enrollment; no duplicate active charge for the same assignment/period/kind.
- Payment allocations reconcile with payment records; `GrossAmount - DiscountAmount - completed allocations = outstanding` within the chosen precision policy.
- No paid-charge reversal without the designated adjustment/refund path.
- Audit/history rows exist for completed operations and remain intact after cancellation/closure.

## Recommended order (not implemented)

1. **Decide and enforce one monetary precision policy** (F-01). This removes an operator-facing financial inconsistency that can arise without a second user.
2. **Protect multi-user QR workflows** (F-02, F-03) with SQL-enforced optimistic verification, rather than relying on visual warnings alone.
3. **Guard QR implicit reloading/navigation** (F-04) without changing deliberately accepted New/Clear UX.
4. **Resolve artifact hash provenance and role behavior** (F-05, F-06); test before modifying anything.
5. Run focused **two-user and SQL-clone regression tests**, then profile pending list and DPI behavior (F-07–F-12).

## Final assessment and limits

- **No data corruption or fatal runtime exception was reproduced.** Several failures are grounded in exact C# logic and credible concurrent event sequences, not observed school incidents.
- **There are meaningful financial and workflow risks to resolve before a confident release verdict**, especially if school operators enter fractional discounts or multiple clerks/admins use the app concurrently.
- Current SQL-side atomicity, pending/paid-charge guards and validation scripts are strengths; preserving them should be an explicit requirement of every fix.
- This was a **static audit of high-risk end-to-end workflows and the actual DACPAC model**. It is **not** a line-by-line certified review of all 333 tracked files, all 232 SQL procedure bodies or an executed production workload. Additional runtime verification is necessary to make a trustworthy deployment claim.
- The repository was left untouched. No changes have been made or committed.
