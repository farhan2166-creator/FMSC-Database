# Documentation consolidation and authority map

**Correction — 5 November 2026:** Actual numbered Markdown files now appear in [Audit](../Audit/README.md) (`01_`–`05_`) and [Docs](README.md) (`01_`–`10_`). They consolidate explanations and references while preserving the original files and paths. The original inventory date and reviewed commit below remain historical.

**Reviewed:** 5 November 2026 against `FMSC-Source` main `906177db31`. **Scope:** organization, topic ownership and cross-references; no application/SQL changes, removal, test reruns or release certification.

## 01. Reading order and authority

1. **Implemented behavior:** inspect current C# source, embedded compatibility manifests and the installer; for example [DatabaseHelper.cs](../DatabaseHelper.cs) and [Installer/core.ps1](../Installer/core.ps1). A past document does not override current code.
2. **Approved business policy:** [business decisions](BUSINESS_DECISIONS.md) and [source-resident policy IDs](../Core/BusinessDecisions.cs). Do not silently promote an audit suggestion to an owner-approved decision.
3. **Current production baseline:** [first-release baseline](FIRST_RELEASE_BASELINE.md) and [production migration instructions](../Migrations/README.md); application 1.0.0, required database schema **1** as approved 5 November 2026. The live school database is not tested or modified by this indexing effort.
4. **How-to guidance:** the relevant operator, engineering, installation or feature guide below. Validate current behavior against source when version-sensitive.
5. **Dated evidence and investigations:** [audit master](../Audit/FMSC_MASTER_AUDIT.md), [review history](../Audit/REVIEW_HISTORY.md), focused reviews, testing results and evidence. They retain their original source revision and verification boundaries.

**Status meanings:** *Canonical* = first place to update the explanation; *supplement* = different audience or implementation detail; *historical* = valid for the cited snapshot only; *redirect* = compatibility path, not an independent copy of the full text.

## 02. Consolidated topics and overlapping documents

| Topic | Primary / canonical read | Complementary records and overlap explanation |
|---|---|---|
| Owner policies and invariants | [Business decisions](BUSINESS_DECISIONS.md), [C# rule record](../Core/BusinessDecisions.cs) | [Owner-invariants locator](../Audit/OWNER_DECISIONS_AND_INVARIANTS.md) and [Decisions compatibility folder](Decisions/README.md) point into the preserved decision history; do not rewrite policy three times. |
| Audit findings and follow-ups | [Audit index](../Audit/README.md), [review history](../Audit/REVIEW_HISTORY.md) | [Master audit](../Audit/FMSC_MASTER_AUDIT.md) summarizes dated release gates; [Reviews](../Audit/Reviews/) and [historical archive](../Audit/Archive/FMSC_HISTORICAL_AUDITS.md) preserve original entry points. |
| Credit and collections | [Business decisions](BUSINESS_DECISIONS.md), [billing implementation record](BillingImplementation.md), [family counter billing](FAMILY_COUNTER_BILLING.md) | [Credit lifecycle review, 4 Nov](../Audit/FMSC_CREDIT_LIFECYCLE_AUDIT_2026-11-04.md) reviewed `12f1d8f`; do not call its findings open or closed on a later commit without new verification. [Student fee account validation](StudentFeeAccountValidation.md) is focused testing detail. |
| Admissions / placement | [Admission workflow](Workflows/ADMISSION_WORKFLOW.md), [admission actions](ADMISSION_ACTIONS.md), [placement workspace](STUDENT_PLACEMENT_WORKSPACE.md) | [Posting holds](STUDENT_POSTING_HOLDS.md) covers a distinct condition; [admission/fee audit of 28 Oct](ADMISSION_FEE_FLOW_AUDIT_2026-10-28.md) is dated audit evidence, not a competing current guide. |
| Certificates | [Certificate workflow](CERTIFICATE_WORKFLOW.md) | [Certificate behavior review](CERTIFICATE_BEHAVIOR_REVIEW_2026-10-24.md) retains schema-49/50/51-era findings and testing; check current source before treating that workflow snapshot as present behavior. |
| Bills and receipts | [Fee documents](FEE_DOCUMENTS.md) | [Scenario review pack](FEE_DOCUMENT_REVIEW_PACK.md) is test coverage; [PDF presentation](PDF_REPORT_PRESENTATION.md) owns shared layout and previews; [historical PDF audit](../Audit/Reviews/FMSC_PDF_FINANCE_PRESENTATION_2026-11-02.md) is original investigation. |
| Reports, naming and exports | [Saved report views](SAVED_REPORT_VIEWS.md), [PDF presentation](PDF_REPORT_PRESENTATION.md) | [Report names](REPORT_DOCUMENT_NAMES.md), [report purpose improvements](REPORT_PURPOSE_IMPROVEMENTS.md), [release report improvements](RELEASE_REPORT_IMPROVEMENTS.md), [new reports](NEW_REPORTS.md) and [direct PDF samples](DIRECT_PDF_EXPORT_SAMPLES.md) address distinct behaviors. Consolidate shared rules at the primary page; retain feature-specific evidence. |
| Application guidance | [User guide](USER_GUIDE.md), [Help Center](HELP_CENTER.md), [operator control](OPERATOR_CONTROL.md) | [Keyboard shortcuts](KEYBOARD_SHORTCUTS.md), [global search](GLOBAL_RECORD_SEARCH.md) and [school operations](SCHOOL_OPERATIONS.md) are focused operator references; avoid copying entire guide sections into each. |
| UI rules | [UI style guide](Design/UI_STYLE_GUIDE.md), [UI tuning guide](Design/UI_TUNING_GUIDE.md) | [UI guidance review](../Audit/Reviews/FMSC_UI_Guidance_Audit_2026-10-20.md) is a dated assessment; not all proposed wording is approved. |
| Architecture / development | [Architecture](ARCHITECTURE.md), [development workflow](DEVELOPMENT_WORKFLOW.md), [project structure](Development/PROJECT_STRUCTURE.md) | [Restore development PC](RESTORE_DEVELOPMENT_PC.md) and [report-label editing](Design/EDITING_REPORT_LABELS.md) are task-specific guides. |
| First release, SQL and installation | [First-release baseline](FIRST_RELEASE_BASELINE.md), [Migrations readme](../Migrations/README.md), [installation](INSTALLATION.md) | [Release verification 1 Nov](RELEASE_VERIFICATION_2026-11-01.md), [release readiness](RELEASE_READINESS.md) and [release history](ReleaseHistory/1.1.0-rc.1.md) describe **earlier pre-baseline states**; do not use schema 20/21/69/75 statements from older records to override the schema-1 reset. |
| Distribution / safety | [Private incremental updates](PRIVATE_INCREMENTAL_UPDATES.md), [release checklist](RELEASE_CHECKLIST.md), [backup/recovery](BACKUP_RECOVERY.md) | [Security review](PUBLICATION_SECURITY_REVIEW.md) and [DACPAC history cleanup](DACPAC_HISTORY_CLEANUP.md) are separate risk assessments; one is not an approval to delete/rewrite old Git history. |
| Tests | [Level 1](LEVEL1_TESTING.md), [Level 2](LEVEL2_TESTING.md), [Level 3](LEVEL3_TESTING.md) | [Level 2 results](LEVEL2_RESULTS.md), [Level 3 results](LEVEL3_RESULTS.md), [local test plan](LEVEL3_LOCAL_TEST_PLAN.md), [Windows verification](WINDOWS_VERIFICATION_2026-10-16.md) and dated evidence capture different methods/times. Do not merge test plans into test results. |
| Help/FAQ coverage | [Help Center](HELP_CENTER.md), [FAQ coverage matrix](FAQ_COVERAGE_MATRIX.md) | [FAQ audit report](FAQ_AUDIT_REPORT.md) is a dated review; matrix entries and operator explanations serve different purposes. |

## 03. Exact matching files (verified by Git blob SHA)

The 10 groups below are **byte-identical evidence files**, not duplicate Markdown documents. They are chiefly Level 2 screenshots and UI control captures. No deletion or rename is warranted without tracing why two scenario IDs produced the same capture. The filenames and parent paths give each capture its test provenance.

| Group | Exact-match files | Why retained |
|---|---|---|
| E01 | [`L2-ADM-01.png`](Level2Evidence/2026-10-16/L2-ADM-01.png), [`L2-UX-05-maximized.png`](Level2Evidence/2026-10-16/L2-UX-05-maximized.png) | Same Git blob in 2 labeled capture paths; keep each scenario association until reviewed. |
| E02 | [`L2-ADM-03-controls.txt`](Level2Evidence/2026-10-16/L2-ADM-03-controls.txt), [`L2-ADM-13-controls.txt`](Level2Evidence/2026-10-16/L2-ADM-13-controls.txt) | Same Git blob in 2 labeled capture paths; keep each scenario association until reviewed. |
| E03 | [`L2-ADM-03.png`](Level2Evidence/2026-10-16/L2-ADM-03.png), [`L2-ADM-13.png`](Level2Evidence/2026-10-16/L2-ADM-13.png) | Same Git blob in 2 labeled capture paths; keep each scenario association until reviewed. |
| E04 | [`L2-ADM-06-controls.txt`](Level2Evidence/2026-10-16/L2-ADM-06-controls.txt), [`L2-ADM-07-controls.txt`](Level2Evidence/2026-10-16/L2-ADM-07-controls.txt), [`L2-ADM-11-controls.txt`](Level2Evidence/2026-10-16/L2-ADM-11-controls.txt) | Same Git blob in 3 labeled capture paths; keep each scenario association until reviewed. |
| E05 | [`L2-FIN-01-controls.txt`](Level2Evidence/2026-10-16/L2-FIN-01-controls.txt), [`L2-FIN-09-controls.txt`](Level2Evidence/2026-10-16/L2-FIN-09-controls.txt), [`L2-FIN-11-controls.txt`](Level2Evidence/2026-10-16/L2-FIN-11-controls.txt) | Same Git blob in 3 labeled capture paths; keep each scenario association until reviewed. |
| E06 | [`L2-FIN-05-controls.txt`](Level2Evidence/2026-10-16/L2-FIN-05-controls.txt), [`L2-FIN-08-controls.txt`](Level2Evidence/2026-10-16/L2-FIN-08-controls.txt) | Same Git blob in 2 labeled capture paths; keep each scenario association until reviewed. |
| E07 | [`L2-PRF-01.png`](Level2Evidence/2026-10-16/L2-PRF-01.png), [`L2-PRF-04.png`](Level2Evidence/2026-10-16/L2-PRF-04.png) | Same Git blob in 2 labeled capture paths; keep each scenario association until reviewed. |
| E08 | [`L2-SEC-01-controls.txt`](Level2Evidence/2026-10-16/L2-SEC-01-controls.txt), [`L2-SEC-03-controls.txt`](Level2Evidence/2026-10-16/L2-SEC-03-controls.txt), [`L2-SEC-06-controls.txt`](Level2Evidence/2026-10-16/L2-SEC-06-controls.txt) | Same Git blob in 3 labeled capture paths; keep each scenario association until reviewed. |
| E09 | [`L2-SEC-02-controls.txt`](Level2Evidence/2026-10-16/L2-SEC-02-controls.txt), [`L2-UX-02-controls.txt`](Level2Evidence/2026-10-16/L2-UX-02-controls.txt) | Same Git blob in 2 labeled capture paths; keep each scenario association until reviewed. |
| E10 | [`L2-UX-01-controls.txt`](Level2Evidence/2026-10-16/L2-UX-01-controls.txt), [`L2-UX-04-controls.txt`](Level2Evidence/2026-10-16/L2-UX-04-controls.txt) | Same Git blob in 2 labeled capture paths; keep each scenario association until reviewed. |


All original evidence remains under [Level 2 evidence](Level2Evidence/2026-10-16/summary.md) or [Level 3 evidence](Level3Evidence/2026-10-16/README.md). [Full catalog](FILE_CATALOG.md) indexes every original file, including binary assets.

## 04. Historical paths and non-destructive consolidation

- `Audit/REVIEW_HISTORY.md` holds the complete earlier investigations; short files under `Audit/Reviews/` and selected `Docs/` paths preserve headings and inbound links. Keep the redirect rather than copying an entire review back.
- `Docs/BUSINESS_DECISIONS.md` holds the dated owner-decision record; `Docs/Decisions/` entries are mostly compatibility locators. Preserve the historical wording and explicit supersession.
- No exact duplicate Markdown blobs were found in the inspected tree. **Subject overlap is intentional where one file is a policy, another a feature implementation, another a test result, and another a dated finding.**
- For a new issue, update the *one authoritative explanation* and link from nearby topic guides. For historical audit material, append a newly dated verification rather than rewriting what older checks proved.

## 05. Maintenance and safety rules

1. Add new files to [the complete file catalog](FILE_CATALOG.md) and link them from [Docs/README](README.md) or [Audit/README](../Audit/README.md) as appropriate; use numbered navigation sections, not compulsory numeric filename prefixes.
2. State the inspected commit, test type, and what was not checked. A source review is not a native form, disposable SQL, installer, operator or printer test.
3. The approved schema-1 baseline belongs in current installation/architecture guidance; do **not** silently modify old 1 November/October audit claims to describe November 5.
4. Protect sensitive school records and credentials; documentation organization alone does not establish repository privacy or release security.
5. Any actual file retirement, merge with removal, path rename, or Git history rewrite requires a separate explicit approval and reference/retention review.
