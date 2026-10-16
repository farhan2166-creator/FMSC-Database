# Help & FAQ source audit — 2026-10-16

## Scope and evidence

Baseline: latest fetched `main`, `ff48ec0568fa83760aaa0a9d447b8fd5327c1178`. Implementation branch: `help/source-verified-faq`. Review covers the existing PR #46 catalog, current dashboard workspaces, their C# forms/writers and checked-in SQL definitions, runtime compatibility requirements, permissions and current operational documentation. The runtime base schema requirement is **6**, with structural and recovery checks in addition to the version number. The optional communication contract is separately checked.

All **77 original questions were reviewed**. **22 original answers/destinations/questions were substantively corrected**, plus structured labels and evidence for all entries. **49 distinct missing scenarios were added**, giving **126 questions in 15 existing categories**. No questions were deleted or merged: the overlapping departure/history and partial-payment/balance questions have distinct operator intent. The initial catalog did not have stable IDs; `FAQ-001`–`FAQ-077` now preserve its original order, and additions use `FAQ-078` onward.

[Workspace and per-entry coverage matrix](FAQ_COVERAGE_MATRIX.md) records the C#, SQL, supported choices, labels, ownership and references for every entry. The catalog is read-only, offline and independent of user grants; it never changes a permission or executes the action it explains.

**Evidence limit:** source review is not a new SQL integration execution or confirmation of the owner's independently supplied database. The audit did not inspect exported school rows or attach any database files. No business procedures, contracts, permissions, version values, migrations, DACPACs or installer/release code was changed. The existing desktop CI gains one portable Help test step; its focused Windows suite also includes Help control checks. No existing check is removed. No live SMS/WhatsApp delivery was attempted. Historical audit records remain untouched.

## Corrections to the original catalog

| IDs | Original problem | Source-supported correction |
|---|---|---|
| 007–009 | Old assignment menu name; calendar editing conflated with confirmed student-year protection. | Use Campus Classes / Sections; programs precede classes; SaveAcademicYear permits authorized unique/non-overlapping calendar changes. GuardConfirmedAcademicYear protects the enrollment's year, not a blanket calendar freeze. Choosing a current year does not promote students. |
| 018–020 | Student Record Checks was an old destination label. | Student Management → Student Data Review is the current workspace. Legacy verification reset is backed by student/placement/fee triggers. |
| 021 | Admission guidance named a generic Check Save Outcome action. | Direct admission uses Check Saved Admission / Find Student. Quick Registration and fee adjustments have their own outcome checks. No universal retry guarantee is implied. |
| 016, 025, 027 | Family creation and departure paths were too broad. | Establish an admitted family in Admissions → Fees & Siblings; manage an existing family from Profile. Pending registration has a separate parent-link flow. Mark as Left is in Admissions and requires resolution of later-month charges, including collection access for reversal; Transfers / Re-Admission handles moves/returns. |
| 033–034 | Fee frequency and existing-rate application lacked scope detail. | Matrix row frequency differs from editing one cell. Update Student Fee Rates is a reviewed explicit operation; changing a rate is not a rewrite of all historical money. |
| 036 | Discount explanation was vague. | Assignment discounts are rupee amounts, not percentages; nonnegative and no greater than the fee, with Fees.Adjust access and separate paid-money correction. |
| 040, 044 | Charge reversal and payment recovery could be confused with payment refunds or universal SQL request idempotency. | Paid allocations protect cancellation; reversal retains history. Ordinary collection uses pending local recovery/history review. Fee-adjustment request markers are a different recovery path. |
| 050, 052 | Annual receipt naming and certificate eligibility were unclear. | Use Annual Fee Receipts; explain report differences. Clearance is optional, not a universal certificate prerequisite. Student/date/permission restrictions still apply. |
| 057 | Role configuration sounded open-ended. | Only the existing Accountant permissions are editable through the supported role workflow; new/renamed roles are disabled. Administrator retains full authority. No grants were changed. |
| 065–066 | Compatibility needed an explicit current requirement; DACPAC sounded necessarily schema-only. | Base schema 6 plus structural/recovery checks remain required. An export may contain data; it is neither the installed live database nor a SQL restore backup. |
| 072, 077 | Communication Settings label and deployment guidance were vague. | Use Gateway Settings / Templates / Automation Rules. Preserve installed data; only supplied supported updates can run, with verified backup and compatibility checks. Communication remains separate from base version 6. |

## Added practical coverage

The new questions explain setup dependencies and unavailable deactivation; programs/subjects; cutoff-based opening months; optional phone; first-time pending siblings; link/unlink after saving; opening-fee selection; temporary vs official identity; saved-state warnings; proportion-preserving photos with an 80 KiB ceiling; transfers, re-admission and placement corrections; Hold and promotion prerequisites; cell-specific frequency; transport stop/restart; paid assignment protection; stale previews and historical posting; purchases; manual and family allocation; correction/refund/reversal semantics and uncertain outcomes; daily closing revisions; blank marks/attendance sheets; optional clearance; receipt eligibility; local saved views; safe exports; staff departure; disabled actions and passwords; incompatible restore/connection; communication module availability, consent, offline scheduling, cancellation, reminders, budgets and simulation; large-task guidance; stale session permissions; Go Live preview; certificate issue vs reprint; report-measure differences; Urdu/template placeholders.

No attendance engine, examination-results system, online banking refund, automatic backup recovery guarantee or continuously hosted messaging service is invented.

## Conflicts, inconsistencies and unresolved boundaries

| Finding | Evidence | Treatment / remaining work |
|---|---|---|
| SQL says “Deactivate it instead” when class/section deletion is blocked, but those editors have no deactivation field/action. | DeleteClass / DeleteSection; ClassesPage / SectionsPage field specifications. | FAQ-078 explicitly avoids that unsupported alternative. SQL wording/feature policy is outside this patch. Reproduce the actual deletion message on a disposable Windows/SQL fixture before a separate correction. |
| Communication page advises running an installer setup prompt that no longer exists. | CommunicationCenterPage.InitializeAsync; current Installer/core.ps1; INSTALLATION.md and COMMUNICATION_CENTER.md retirement policy. | FAQ-114 says use an approved snapshot containing the module or seek a reviewed preservation/recovery path. The application message remains an independently actionable usability gap. Never replace an operational database just to enable communication. |
| Current architecture prose still described the removed optional installer module step; communication documentation also retained a paragraph about that old step. | Docs/ARCHITECTURE.md installer section; Docs/COMMUNICATION_CENTER.md deployment paragraph. | Correct only the current operational explanation, preserving historical audits and actual installer behavior. |
| ADMISSION_WORKFLOW.md said “confirmed operational academic years cannot be edited” and still ended with a v1 requirement. | SaveAcademicYear vs GuardConfirmedAcademicYear; DatabaseHelper.RequiredDatabaseSchemaVersion and compatibility manifests. | Clarify confirmed enrollment-year protection vs calendar setup; state current schema 6. No schema or version value changed. |
| Adjustment authorization is coordinated across layers rather than identical in each procedure. | RecoverableMutationWriter.Permission requires Fees.Adjust for writes; ProcessFeeTransaction itself initially requires Fees.Collect for all transaction types. | Document the combined production boundary. This read-only review does **not** prove that an independently callable SQL correction is denied to a collect-only actor. A restricted-execution SQL reproduction and grant review remain needed before calling this a confirmed security defect. Do not claim every SQL entry point checks every C# permission. |
| Fee Collection assignment action may lack a visible host after relocation. | FeeCollectionPage removes feeAssignmentsButton from Actions using RelocateFromActions; no later host attachment was found in the page. Admissions → Fees & Siblings → Fee Discounts & Transport is the confirmed alternative source path. | Help uses that Admissions path rather than promising a Profile fee editor or an unverified collection button. Treat the detached control as a source-observed candidate usability defect pending Windows reproduction; no action placement was changed here. |
| Calendar date edits are permitted, but downstream eligibility can depend on those dates. | SaveAcademicYear; promotion/posting date checks. | Warn to review used-year changes. Full effects on already-posted periods need disposable integration scenarios; do not invent a global freeze. |
| Visible controls, live deployment and external provider status require environment evidence. | UI layout code; backup/installer runtime paths; providers and optional webhook/worker contracts. | Windows DPI/Narrator/keyboard visual acceptance, independent MDF/LDF compatibility and real provider/hosting acceptance remain unverified here. |

No financial or lifecycle behavior was changed to resolve documentation conflicts. UI warnings that refer to unavailable setup/deactivation actions are recorded separately, not silently “fixed” through Help content.

## Verification results

Local environment: Linux x86-64 (kernel 6.18.44), .NET SDK 8.0.425, Gitleaks 8.30.1. Verification is recorded below; final delivery commit and PR link are recorded in the PR description.

| Check | Result / evidence |
|---|---|
| Production catalog, IDs, metadata, source paths/symbols, related graphs, ranked searches, spelling aliases, combined filters, no results, offline dependency contracts | PASS — `dotnet run --project Tests/HelpChecks/HelpChecks.csproj -c Release --no-restore --disable-build-servers`: 1,919 assertions; 2,000 filtered searches in 16 ms. |
| Release build, entire solution | PASS — `dotnet build FMSC_SMS.sln -c Release -p:EnableWindowsTargeting=true --no-restore --disable-build-servers -m:1 -nr:false`; zero warnings/errors. |
| Windows Help control suite | COMPILED — ThemeSmokeChecks Release build, zero warnings/errors. Execution NOT RUN: no Windows runtime/desktop in this Linux session. |
| Existing portable regressions | PASS — 832 assertions across money/TVP, permissions, recovery, photo/settings/export durability, registration, themes and synthetic DPI checks. |
| Existing security scanner | PASS — `python3 Tools/check_security.py --gitleaks /workspace/work/security-automation/gitleaks`: fetched history and staged changes, no detected secrets or forbidden database/sensitive-file names. |
| Synthetic performance/state suite | PASS — 32,183 assertions. Nonblocking statement simulation: 32 synthetic 8-ms reads, 508 ms, 49 caller heartbeats, maximum 1 concurrent read; excludes actual SQL/WinForms. |
| Desktop CI YAML / patch whitespace | PASS — existing workflow parses with one added Help step; `git diff --cached --check` passed. |
| New SQL integration / live school data | NOT RUN / NOT USED — help-only changes; no database dependency or financial implementation changes. Source inspection is not SQL execution. |
| GitHub hosted Actions | BLOCKED by the owner's exhausted monthly hosted allowance. No speculative workflow-triggering pushes; no green Actions claim. |
| Interactive DPI, theme contrast, Narrator, actual keyboard/mouse operator acceptance | NOT RUN — exact [Windows acceptance checklist](HELP_CENTER.md#windows-acceptance-still-required). |

Search timing is a small repeatable catalog smoke measurement, not a claim about production report or database performance. No production-scale dataset was needed. The FAQ can guide an operator but cannot replace the actual page's permission, eligibility and saved-outcome checks.
