# Changelog

Notable user-facing, database, installer, and operational changes will be tracked here. This file begins as a **forward-looking** changelog; it is not a fabricated complete history of previous builds.

## [Unreleased]

### Report improvements

- Add Monthly Fee Reconciliation by billing month and school/campus/class/section/fee type, comparing current signed money allocations, credit applied and unpaid fees; include excess settlement and missing-period charge-date fallback.
- Add the consolidated Certificate Issue Register with permitted student/staff sources, date/type/recipient/entry/collection/search filters, recorded issue history and explicitly distinguished saved-only records.
- Add Account Credit filtering and data-only CSV exports for Collections & Receipts; count receipt entries separately from headings/totals and repeat printed group context with numeric subtotals.
- Add overdue/balance/day-range filters and permission-checked Open Statement to Outstanding & Aging; keep filtered output distinct from the full-location A4 report.
- Add a read-only Student Credit Balance Register with student/family views and current/latest placement filters; preserve signed ledger sums and avoid duplicate balances.
- Add a Concession Summary separating assigned discount rates by billing frequency, with no mixed-cycle money grand total.
- Capture finance report generation/aging dates separately from print time. Preserve financial writers, saved documents, database versions and migrations.


- Lock the academic year in Admission's placement/setup actions. New placements use the configured current year; existing placement history retains its recorded year.

- Complete the optional quick counter journey with the existing Fee Collection screen opened over the retained family. Carry only selected children into payment, retain editable suggested allocations, print established receipts and refresh remaining dues without retaining submitted amounts.
- Isolate registration recovery from loaded child editors, reject mismatched recovered identities and identify progress when preparation stops after earlier children have saved. New children remain pending for full admission later.
- Add migration 27 for read-only current/future fully waived bill evidence. Preserve established admission, financial writes, receipt numbering and RDLC templates; no school or financial records are rewritten by the migration.


### Fixed

- Align certificate inputs directly beneath their labels, let certificate grids size date/dropdown fields consistently, and use compact wide-screen layouts for student certificate details. Match disabled/read-only editors to their surrounding input background.
- Separate student account statements from the family/group statement workflow; expose annual receipts and outstanding bills as related reports, and leave balances unshown until a statement loads.
- Let the school issue student character certificates based on current or earlier Class 10/12 placement, without a passed-exam checkbox or an extra print-wording acknowledgement.
- Fit Record Departure fields and explanations within the dialog, show the waiver month only when selected, and show the school cutoff's actual waiver month in the review.
- Verify a backup's recovery identity and compatibility in a temporary database before replacing live school records; retain backup-file protection and verify identity again after restore or safety recovery.
- Remove main-database version checks from the communication worker and webhook callback; retain communication API, structural, permission and delivery safeguards (migration 41).
- Make the database-file release helper stop a verified lingering LocalDB engine gracefully and require repeated process-exit and exclusive MDF/LDF checks, even when the controller incorrectly reports Stopped.
- Validate each issued bill line against its saved charge balance and student, rejecting offsetting stale amounts even when the grand total matches (migration 40). Existing saved bills remain available for reprint.
- Retain additional-fee posting intent across interrupted saves and reopening; both individual and bulk entry require a read-only outcome check before another posting can proceed.
- Restrict bulk additional-fee posting to eligible As Needed fees, excluding admission, tuition and transport in both the picker and database writer (migration 39).
- Allow the family payment counter to open for students on hold and collect retained posted dues while new fee posting remains paused.
- Return Fee Collection focus to Amount Received after closing its student fee, advance, history, family or print dialogs.
- Allocate received cash after each bill's discount and selected student advance, so mixed advance/cash collections do not assign cash to already covered fees.
- Show staff employment validation messages below their inputs instead of squeezing them beside field captions, and reject a Hire Date later than today.
- Define payment-detail grid columns before binding so Student Fee Corrections can open View Details without accessing missing auto-generated columns.

### Changed

- Show saved pre-payment fee amounts on collection receipts, followed by Total, Received and Balance; retain credit/adjustment details and remove the duplicate settlement total for saved collection receipts.
- Open staff Add Job and Edit Job directly in an editor-only dialog; keep employment history on the staff page.
- Remove the redundant top Employment button from Staff Management; use the existing Edit Job, Add Job and employment-history Edit actions.
- Prefill the bank account opening certificate’s editable Designation from the selected staff member’s employment record; preserve saved certificate wording when editing.
- Remove the trailing “here.” after Subject Taught on lecturer/teacher certificates.
- Prefill new lecturer/teacher certificates with the selected staff member’s employment designation and latest saved certificate subject, keeping both fields editable and existing certificate values intact.
- Move bill/receipt dates below the closing footer in small italic text: bills show the current printed-on date/time; receipts retain their recorded payment date/time.
- Add Whole School, Campus, Class and Section grouping to Collections & Receipts receipt lists, with monetary subtotals and a grand total on screen, print and export; preserve mixed-location family receipts intact.
- Give fee descriptions more width on student/family bills and receipts by narrowing the amount column, preserving paper width and month alignment.
- Clarify Billing Summary headings with the selected charge-date/grouping label, Charge Count, Gross Billed, Discounts and Net Billed.
- Show exact Days Overdue beside the stored due date in Outstanding & Aging student, charge and family views, while retaining aging buckets.
- Center serial numbers, sections and student counts in the Class & Section Summary, including headers and totals; preserve left-aligned names and right-aligned balances.
- Widen Update Student Fee Rates, align its fields in one row when space permits, and fit preview columns to the available width. Smaller windows retain a two-row field layout.

## [1.1.0-rc.2] - 2026-10-18

- Make Billing Center titles and guidance concise and consistent; use posting/reversal labels instead of generic report instructions.
- Show additional-fee periods as month/year in Fee Collection and display charge remarks in their own column.
- Show zero-value numeric inputs as focus-aware placeholders. Use optional Remarks for additional fees, student setup and ordinary fee-configuration notes; retain contextual reasons for corrections and reversals.
- Add Manual / Advance Posting with a reviewed month range and optional student filter for vacation fees; post the range in one transaction using existing monthly eligibility and duplicate checks.
- Show admission number, student, father and recorded class/section in Billing Status with natural admission-number sorting; leave missing placements blank.
- Move Fee Rate Setup to School Setup and student fee corrections, daily collection closing, student promotion and student data review to Daily Work; remove the Fees & Billing navigation group.
- Fill the campus/class/section picker from the highlighted list row during keyboard navigation; Enter accepts the choice and closes the list.
- Let the operator choose concession periods, issued-bill treatment and credit/refund, with a financial preview and save-outcome recovery.
- Offer admission-month or following-month concessions in direct admission and Quick Registration.
- Let leaving retain issued monthly bills, apply the school cutoff or waive from a chosen month; preview full correction chains.
- Upgrade schema 20 to 21 without rewriting school records; remove audit-only preparation from revised procedures.
- Review fee rates and concessions together for monthly, annual and individual purchase bills, including closed placements. Retain credit unless its use is explicitly selected; show credit before/use/remaining in the review.
- Make carrying monthly concessions through promotion an explicit choice; remove the unused amount editor while retaining its SQL compatibility API.
- Document the operator choices and remaining school-PC acceptance in Docs/OPERATOR_CONTROL.md.

## [1.1.0-rc.1] - 2026-10-18

### Changed

- Group direct admission, pending registration and existing-student activation in Student Admissions. Billing Center exceptions now open the selected student's setup or collection account; remove duplicate navigation paths.
- Align application, installer and optional communication runtimes on schema 20. The transactional 19→20 migration preserves school and financial rows and retires four unused read procedures.

### Fixed

- Stop offering credit deposits/applications as ordinary payment corrections, and exclude amounts already released to credit from the adjustable balance.
- Label Quick Registration fees as a plan, separating monthly and yearly/one-time amounts from actual posted dues.
- Support release-candidate installer versions with downgrade protection and validate schema package checksums. Remove the revised callback’s audit insert while preserving inbox/delivery recovery records.
- Restore deleted verification sources/tools, correct outdated test/schema assumptions, repair Help Center source links, and verify release branches through security, Windows, SQL, installation and database-free packaging checks.
- Remove a date-prefixed maintenance patch from release migration discovery; its guard is already in the baseline.

### Added

- Add outstanding student/family bills and a Family / Returns shortcut to Fee Collection. Family cards show placement/year and billing readiness; each return uses the existing guarded confirmation dialog. Quick Registration warns when linked returning siblings still lack new-year charges.
- Level 3 cloud reliability checks for guarded SQL faults, backup/restore, concurrency and measured fictional workloads, plus Windows component/update acceptance and a complete 45-scenario evidence/local-requirement matrix.


### Documentation

- Organize loose workflow, design and developer documents under Docs; group audit reviews and historical records under Audit, with navigation indexes and updated relative links.
- Shorten C# comments and record accepted business policies beside their implementations. Historical audits, executable code, SQL, database artifacts and installer behavior are unchanged.

### Changed

- Keep direct desktop, SQL, Level 2 and disposable installer workflow triggers manual. Release branches run the coordinated security, desktop, SQL, installation and packaging checks; security also runs on main pushes and pull requests.

- Temporarily hide Admission's Save & New button to leave more room for Print. Use Save followed by Clear / New for the next student.
- Distinguish New Record from Clear Search in shared setup editors. Setup Readiness disables unavailable destinations and navigation during checks, with inline permission guidance.
- Clarified payment-adjustment and placement confirmations with aligned details, and shortened Communication Center guidance while preserving warnings and confirmation defaults.

- Replace the XML-heavy `DatabaseSchema/` reference export with real DacFx-generated SQL `CREATE` scripts in `Schema/`. The generated review PR removes the old files only after replacement scripts are produced and reports inline/unsupported model objects in a JSON manifest; no school data or database changes.

- Pause routine Dependabot NuGet and GitHub Actions version-update PRs until the owner chooses to resume. Existing PRs, security-alert settings, security updates and secret scanning remain unchanged.

- [PR #19](https://github.com/farhan2166-creator/FMSC-Database/pull/19) makes Student Account Statement start with one student and full-width account details; group/family selection remains a secondary mode. Annual Receipts opens for the selected student. Family / Siblings opens from Student Profile in an owned window and refreshes the profile on return. Financial calculations, SQL, permissions and installer behavior are unchanged.

- [PR #15](https://github.com/farhan2166-creator/FMSC-Database/pull/15) renames Bulk Promotion to **Student Promotion** and moves Promotion History into a window opened from the promotion-details header. History starts with the selected source filters, remains independently filterable and leaves the promotion draft intact. Existing permissions, promotion processing and SQL contracts are unchanged.

### Added

- Enhance the existing offline Help & FAQ Center: 126 source-linked questions across 15 topics, plain-language rule labels, ranked synonym search, topic/rule filters, related questions and responsive themed reading. Clarify calendar vs enrollment protection, supported role editing, payment recovery and retired communication setup guidance; application business rules remain unchanged.

- Added an opt-in disposable Windows installation acceptance workflow: approved data-free schema fixture, real signed LocalDB prerequisite, validated complete installer, synthetic MDF/LDF, and installed-app verification. It reports pristine-MSI gaps and uploads sanitized evidence only; it does not test real school data or approve production deployment.

- Added a manually dispatchable, DACPAC-triggered, schema-only reference extractor. It stores complete per-object model XML and SQL module bodies, tracks changed definitions and proposes a draft synchronization PR without importing data or approving changes.

- Optional Communication Center with explicit guardian/staff consent, reviewed English/Urdu templates, draft/approval/scheduling controls, a durable SQL queue, safe simulation, Twilio SMS and official Meta template adapters. Live mode defaults disabled; the additive database module requires separate owner review/deployment. No Accountant grants, financial procedures, installer upgrades or approved DACPAC changes are included. See [setup and remaining external acceptance](Docs/COMMUNICATION_CENTER.md).

- [PR #18](https://github.com/farhan2166-creator/FMSC-Database/pull/18) adds conservative DACPAC refresh comparison and trusted default-branch synchronization PR automation. Exact full-model/module/data matching, sanitized refusal reports and synthetic safety regressions preserve existing approval/security/SQL-isolation rules; no automatic merge or material-change override.
- Generated [PR #17](https://github.com/farhan2166-creator/FMSC-Database/pull/17) aligns the verified metadata-only `91ec136` refresh across three approval/provenance records, preserving the refreshed DACPAC and reusing the schema-only fixture.

### Testing

- Added an isolated Level 1 SQL lifecycle harness in [PR #14](https://github.com/farhan2166-creator/FMSC-Database/pull/14), with schema-only approved-model provenance, fictional school fixtures, persisted admission/fees/promotion/recovery assertions, existing-suite integration and sanitized CI evidence. Production application/database/installer behavior is unchanged; real Windows and deployment acceptance remain separate.

### Fixed

- Keep report-preview captions above the print toolbar and preserve the content viewport's docking order. Compact Admission finder/action sizing keeps Print beside the other actions at ordinary widths while narrow windows still wrap whole buttons.

- Bound Help Center splitter transitions during compact layout changes to prevent the reproduced Windows startup/layout exception.

- Refresh Setup Readiness navigation after the current grid cell changes, so permission guidance follows the selected destination.
- Remove obsolete installer setup instructions from the missing Communication Center module warning; direct operators to administrator-reviewed setup and recovery guidance.

- Center message-dialog buttons with a readable gap. Show clear decision headings, prominent consequences, aligned fields and read-only record tables for payment, promotion, backup and duplicate-student confirmations. Preserve confirmation defaults, keyboard choices, exact values and recovery payloads; long details scroll while buttons remain visible.

- Shorten promotion, admission, registration, transfer and collection feedback. Shared status areas stay within two readable lines, with full hover/accessibility text; unknown promotion outcomes remain visible after refresh. Counts, receipt references, recovery guidance and database rules are preserved.

- Communication Center distinguishes inactive-tab keyboard focus from selection and adds per-tab instructions, actionable empty/loading/failure states, bounded paging and explicit save/next-step feedback. Changed filters block stale selections, template paging preserves the chosen template, New Message clears the previous schedule, and dispatcher errors remain visible. SQL, permissions, provider transports and installer behavior are unchanged.

- Offer explicit Communication Center setup for a compatible schema-6 database before application activation. Approval creates a verified backup, checks packaged `7.sql` against the embedded definition, applies it transactionally and verifies base/module contracts before commit. Existing valid modules are preserved; partial or incompatible modules are refused. Normal startup, messaging defaults, base version and reviewed DACPAC remain unchanged.

- Package Communication Center SQL directly as `Migrations/7.sql`, include its deployment guide, and verify that the published SQL and LocalDB recovery helper match the checked-out source. The approved base schema remains version 6; the optional add-on still requires explicit reviewed deployment. Regenerate installer ZIPs after merging fixes; rerunning an older run retains its older source revision.

- Improve LocalDB installation on clean Windows computers: MSI verbose logging, explicit wizard fallback after silent failure, restart-required exit handling, executable verification and PATH discovery. No school data or database schema changes.

- Fixed a fresh-install failure: the installer no longer requires `dbo.ReorderSchoolOfficials`, which is absent from the approved schema. A regression check now keeps the remaining compatibility probes aligned with the reviewed DACPAC; no SQL, migration or school-data changes.

- [PR #16](https://github.com/farhan2166-creator/FMSC-Database/pull/16) restores date-field height after shared width limits are applied, preventing date controls from disappearing while their captions remain visible in admissions and other field layouts. Date values, optional-date handling and validation are unchanged.

- [PR #30](https://github.com/farhan2166-creator/FMSC-Database/pull/30): Find Student clears placement/status filters when an admission number is typed, and clears the admission number when a filter changes. Changed criteria immediately remove old rows and their open/new-admission actions; delayed replies cannot restore stale results. Name searches retain combined filtering.

- Shared DPI and responsive-layout corrections are proposed in [PR #13](https://github.com/farhan2166-creator/FMSC-Database/pull/13): explicit per-monitor awareness, logical client-area density, monitor-relative fonts and grid/tab dimensions, preserved window minima, scrollable small-screen dialogs and bounded fixed-purpose field widths. Appearance and business workflows are preserved. Physical DPI/monitor and ReportViewer acceptance remains manual.

The following end-user-flow corrections are proposed in [PR #8](https://github.com/farhan2166-creator/FMSC-Database/pull/8).

- Dashboard Student Reports and profile shortcuts follow destination permissions. Read-only record review remains available; unavailable photo/family actions are clearly disabled. Administrator and Accountant grants are unchanged (UFA-09).
- Daily closing rejects fractional new actual amounts without rounding; printed summaries match the on-screen precision and preserve historical stored fractions (UFA-08).
- Daily closing summaries print with intentionally blank Refund/Correction actuals, retaining recorded net totals and leaving unchecked methods blank (UFA-07).
- Ctrl+K searches permitted destinations independently of collapsed or compact sidebar visibility and invokes navigation directly (UFA-06).
- Admission and fee collection hold submitted inputs through awaited writes and refreshes, preventing post-submit typing from being lost. Validation remains editable before submission (UFA-05).
- [PR #31](https://github.com/farhan2166-creator/FMSC-Database/pull/31): Admission finalizes a successfully reloaded record's clean baseline after its submitted inputs are enabled again. Mark Left and Correct Placement no longer mistake a successful save for unsaved edits; real edits, failed reloads and pending photos remain protected.
- New sports and provisional certificate content participates in existing leave/discard guards. Date-only untouched dialogs remain freely abandonable (UFA-04).
- Classes & Sections forwards active-save state without adding setup-draft prompts. Bulk fee-rate drafts retain their existing discard protection when embedded, with a clean baseline after a successful apply (UFA-03).
- Admission closure and legacy go-live hold review inputs, reject obsolete previews, and prevent closing during pending operations (UFA-02).
- Certificate creation keeps inputs locked and refuses repeat submission until the pending write finishes (UFA-01).

### Security
- Added weekly Dependabot proposals for application/test NuGet packages and GitHub Actions, with grouped minor/patch updates, bounded open PRs and human review.
- Added read-only Gitleaks CLI and sensitive-file checks on pull requests and pushes to `main`, with checksum-pinned downloads, temporary redacted reports and exact synthetic-fixture exceptions. No paid service or automatic merging is enabled.
- Added disposable detection/redaction regression tests and guidance for security failures and dependency review. Application, SQL, database compatibility and installer behavior are unchanged.

### Documentation
- Added a project overview and operator, installation, recovery, architecture, release-readiness, and development-workflow guides.
- Added a pull-request template prompting contributors and Codex to explain behavior changes, safety implications, tests, and manual acceptance requirements.
- Established a single current [master audit](Audit/FMSC_MASTER_AUDIT.md) with a [historical index](Audit/Archive/FMSC_HISTORICAL_AUDITS.md) for earlier investigations.

### Build and delivery
- Existing Windows desktop CI and manually triggered private application-only and complete-installer artifact workflows are documented. **Workflow existence does not mean a complete installer has passed deployment acceptance.**
- Fresh-install database MDF/LDF files remain owner-supplied after downloading the private full-installer ZIP; school data is not included in source control or Actions artifacts.

## Versioning policy

The application assembly version in `FMSC_SMS.csproj` and the database's required schema version are **different concepts**. The current runtime contract requires **database schema 6**. A version should not be tagged or described as a production-approved school release until the tests and checks in [RELEASE_CHECKLIST.md](Docs/RELEASE_CHECKLIST.md) are recorded.

When a pull request changes behavior, add an Unreleased entry describing the operator-visible result and a PR link. Move entries to a dated version section only after an actual release approval.

For the original fixes, implementation passes, schema verification and historical decisions, see the [historical audit index](Audit/Archive/FMSC_HISTORICAL_AUDITS.md) and commit history.

- Legacy student setup applies the School Profile cutoff to the selected billing start and shows the resulting monthly fee period. Historical class placement stays independent. Migration 29 corrects the earlier placement-based rule in migration 28 without rewriting existing charges.

- Correct the placement cutoff change with migration 29: use the selected billing start for the cutoff, preserve historical class placement independently, and use the billing date for opening one-time fees. Existing student charges are unchanged.

- Align individual/family bill and receipt headers: show the school name followed by the address and campus on one line, widen detail labels slightly, and reduce document-title font to avoid wrapping. Preserve available campus names in grouped documents; mixed-campus families use an explicit summary. Financial amounts and records are unchanged.

- Rename the payment Correction action to Reduce Recorded Payment. Explain that the entered amount is the amount to retain and show the resulting increase in outstanding dues in the confirmation and success message. Preserve transaction codes, calculations and recovery behavior.

- Clarify the leaving-fee review: show each charge's reversal restriction, explain that kept dues remain payable and reversal decisions save with the certificate, name the Fee Collection destination, and count or disable the view-only settled/zero-amount history checkbox. Preserve financial eligibility and invalidate a reviewed decision when its placement changes.

- Simplify financial screen wording and add practical rupee-example tooltips across collection, student advance balances, payment adjustments, fee/discount changes, posting, rate setup, transfers, readmission, leaving and reports. Explain new money versus existing advance, amounts to keep as paid, fee cancellation versus payment reversal, and excess paid money. Preserve financial calculations, permissions and transaction codes.
