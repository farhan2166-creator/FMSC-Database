# Audit — numbered files

These are **actual numbered filenames** in the root of `Audit/`, not just section numbers. Each consolidates explanations and links while preserving all original reports and evidence.

- [01 — Audit status and authority](01_STATUS_AND_SCOPE.md)
- [02 — Finance and credit findings](02_FINANCE_AND_CREDIT.md)
- [03 — UI and workflow investigations](03_UI_AND_WORKFLOW.md)
- [04 — Release and SQL verification](04_RELEASE_AND_VERIFICATION.md)
- [05 — Historical audit retention](05_ARCHIVE_AND_RETENTION.md)

## Original audit references and guidance


**Updated:** 5 November 2026 · **Purpose:** navigation and provenance, not new verification. No audit records are deleted or rewritten by this organization.

## 01. Start with the evidence you need

| Question | Read | Interpretation |
|---|---|---|
| Which findings and release gates were recorded? | [Master audit and release-status history](FMSC_MASTER_AUDIT.md) | Contains dated baselines; its older CI/installer observations are **not** current certification. |
| What did prior reviews actually find? | [Consolidated review history](REVIEW_HISTORY.md) | Preserves the original report text, IDs, resolution evidence, limitations and commits. |
| What did the 4 November credit review identify? | [Credit lifecycle audit](FMSC_CREDIT_LIFECYCLE_AUDIT_2026-11-04.md) | Source reviewed at `12f1d8f`; recheck the later implementation before calling findings still outstanding. |
| What is the approved business rule? | [Business decision handbook](../Docs/BUSINESS_DECISIONS.md), [source rules](../Core/BusinessDecisions.cs) | Decisions are not equivalent to a verified UI/database behavior. |
| How are overlapping documents organized? | [Documentation consolidation map](../Docs/DOCUMENTATION_MAP.md) | Crosswalk by topic, canonical owner, dated context and exact evidence duplicates. |

## 02. Dated review entry points (preserved for existing links)

1. [End-user flow audit — 14 October](Reviews/FMSC_END_USER_FLOW_AUDIT_2026-10-14.md)
2. [Post-merge project audit — 14 October](Reviews/FMSC_POST_MERGE_PROJECT_AUDIT_2026-10-14.md)
3. [UI guidance review — 20 October](Reviews/FMSC_UI_Guidance_Audit_2026-10-20.md)
4. [Student posting hold/resume discussion — 21 October](Reviews/FMSC_Student_Hold_Resume_Discussion_2026-10-21.md)
5. [UI improvements review](Reviews/UI_IMPROVEMENTS_REVIEW.md)
6. [PDF and financial report presentation review — 2 November](Reviews/FMSC_PDF_FINANCE_PRESENTATION_2026-11-02.md)

Many review-path files are compatibility locators into [review history](REVIEW_HISTORY.md); read the preserved section there rather than treating an abbreviated locator as missing evidence.

## 03. Archive and decision locators

- [Historical audit archive](Archive/FMSC_HISTORICAL_AUDITS.md) and [audit-fix verification — 13 October](Archive/AUDIT_FIX_VERIFICATION_2026-10-13.md): past scope and dated outcomes.
- [Owner decisions and invariants](OWNER_DECISIONS_AND_INVARIANTS.md): preserved compatibility locator into the decision handbook.
- [Docs-side dated reviews](../Docs/FILE_CATALOG.md#09--docs-time-stamped-reviews-and-audit-requests): original standalone/redirect audit paths remain available.

## 04. Adding the next audit

1. Record exact branch/commit, DACPAC or database origin, and inspection date.
2. Separate **observed**, **proposed**, **approved**, **implemented**, and **verified**; do not mark a prior finding resolved from a merge alone.
3. Append or cross-link into [review history](REVIEW_HISTORY.md), preserving original IDs and links. Keep the [master audit](FMSC_MASTER_AUDIT.md) focused on release gates, not a duplicate narrative.
4. Link business-rule decisions to the [handbook](../Docs/BUSINESS_DECISIONS.md); list tests and limitations explicitly.
5. Never include real school data, keys, private backups or patient/student-identifying captures. Native Windows, SQL, installer and physical printing acceptance remain distinct.
