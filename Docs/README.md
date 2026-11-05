# Docs — numbered navigation

**Updated:** 5 November 2026. This index links existing files; no historical documentation or test evidence is removed. For every original path see the [full file catalog](FILE_CATALOG.md). For overlapping explanations, canonical ownership and exact duplicate evidence see the [consolidation map](DOCUMENTATION_MAP.md).

## 01. Governing rules and current baseline

- [Business decisions and full dated decision record](BUSINESS_DECISIONS.md) · [implemented C# policy IDs](../Core/BusinessDecisions.cs)
- [First production baseline: application 1.0.0 / database 1](FIRST_RELEASE_BASELINE.md) · [production migration rules](../Migrations/README.md)
- [Decision-path index and compatibility locators](Decisions/README.md)

## 02. Everyday operation and navigation

- [User guide](USER_GUIDE.md) · [Help Center](HELP_CENTER.md) · [School operations](SCHOOL_OPERATIONS.md) · [Operator control](OPERATOR_CONTROL.md)
- [Keyboard shortcuts](KEYBOARD_SHORTCUTS.md) · [Global record search](GLOBAL_RECORD_SEARCH.md) · [Communication Center](COMMUNICATION_CENTER.md)

## 03. Admission, student lifecycle and certificates

- [Admission workflow](Workflows/ADMISSION_WORKFLOW.md) · [Admission actions](ADMISSION_ACTIONS.md) · [Placement workspace](STUDENT_PLACEMENT_WORKSPACE.md)
- [Student posting holds](STUDENT_POSTING_HOLDS.md) · [Certificate workflow](CERTIFICATE_WORKFLOW.md)
- Historical review: [certificate behavior — 24 October](CERTIFICATE_BEHAVIOR_REVIEW_2026-10-24.md) · [admission/fees — 28 October](ADMISSION_FEE_FLOW_AUDIT_2026-10-28.md)

## 04. Fees, collections, bills and receipts

- [Billing implementation](BillingImplementation.md) · [Family counter billing](FAMILY_COUNTER_BILLING.md) · [Student fee account validation](StudentFeeAccountValidation.md)
- [Bills and receipts](FEE_DOCUMENTS.md) · [Bill/receipt review scenarios](FEE_DOCUMENT_REVIEW_PACK.md)
- [Quick counter verification](QUICK_COUNTER_VERIFICATION_2026-10-20.md) · [Finance wording review](FINANCE_WORDING_REVIEW_2026-10-20.md)
- [Credit lifecycle audit — 4 November](../Audit/FMSC_CREDIT_LIFECYCLE_AUDIT_2026-11-04.md) (findings are pinned to the reviewed commit)

## 05. Reports and presentation

- [PDF and print presentation](PDF_REPORT_PRESENTATION.md) · [Saved report views](SAVED_REPORT_VIEWS.md) · [Report/document names](REPORT_DOCUMENT_NAMES.md)
- [New reports](NEW_REPORTS.md) · [Report purpose improvements](REPORT_PURPOSE_IMPROVEMENTS.md) · [Release report improvements](RELEASE_REPORT_IMPROVEMENTS.md)
- [Direct PDF samples](DIRECT_PDF_EXPORT_SAMPLES.md) · [Report label editing](Design/EDITING_REPORT_LABELS.md)

## 06. Architecture, design and engineering

- [Architecture](ARCHITECTURE.md) · [Project structure](Development/PROJECT_STRUCTURE.md) · [Development workflow](DEVELOPMENT_WORKFLOW.md) · [Restore development PC](RESTORE_DEVELOPMENT_PC.md)
- [UI style guide](Design/UI_STYLE_GUIDE.md) · [UI tuning guide](Design/UI_TUNING_GUIDE.md)

## 07. Release, installation, updates and data recovery

- [Installation](INSTALLATION.md) · [Release checklist](RELEASE_CHECKLIST.md) · [Backup and recovery](BACKUP_RECOVERY.md)
- [Private incremental updates](PRIVATE_INCREMENTAL_UPDATES.md) · [Publication/security review](PUBLICATION_SECURITY_REVIEW.md) · [DACPAC history cleanup proposal](DACPAC_HISTORY_CLEANUP.md)
- Pre-baseline / historical: [Release readiness](RELEASE_READINESS.md) · [1 November verification](RELEASE_VERIFICATION_2026-11-01.md) · [release history](ReleaseHistory/1.1.0-rc.1.md)

## 08. Tests, evidence and coverage

- [Level 1 testing](LEVEL1_TESTING.md) · [Level 2 plan](LEVEL2_TESTING.md) · [Level 2 results](LEVEL2_RESULTS.md) · [Level 2 evidence](Level2Evidence/2026-10-16/summary.md)
- [Level 3 testing](LEVEL3_TESTING.md) · [Local Level 3 plan](LEVEL3_LOCAL_TEST_PLAN.md) · [Level 3 results](LEVEL3_RESULTS.md) · [Level 3 evidence](Level3Evidence/2026-10-16/README.md)
- [FAQ audit](FAQ_AUDIT_REPORT.md) · [FAQ coverage matrix](FAQ_COVERAGE_MATRIX.md) · [Windows verification — 16 October](WINDOWS_VERIFICATION_2026-10-16.md)

## 09. Audit, review and cleanup history

- [Audit index](../Audit/README.md) · [Master audit](../Audit/FMSC_MASTER_AUDIT.md) · [Review history](../Audit/REVIEW_HISTORY.md)
- [Code cleanup verification — 2 November](VERIFIED_CODE_CLEANUP_2026-11-02.md) · [Redundancy review — 28 October](REDUNDANCY_AUDIT_2026-10-28.md) · [Cloud billing audit brief](CODEX_CLOUD_BILLING_AUDIT.md)

**Document status:** Current-facing guides must be checked against implementation; dated reviews and results retain their original evidence scope. This navigation work does not run tests or certify a release.
