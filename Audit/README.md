# Audit index

**Start here:** [Master audit and release status](FMSC_MASTER_AUDIT.md) for outstanding release gates, then [Owner decisions and invariants](OWNER_DECISIONS_AND_INVARIANTS.md) for business-rule boundaries. Both link to dated evidence. **No single report is proof that the current application or installer is release-ready.**

## Current reference documents

| File | Role | Update policy |
|---|---|---|
| [Master audit and release status](FMSC_MASTER_AUDIT.md) | Consolidated finding status, reviewed baselines and release gates | Append dated, evidenced status changes rather than create competing status reports |
| [Owner decisions and invariants](OWNER_DECISIONS_AND_INVARIANTS.md) | Business rules and approved-but-not-yet-implemented policies, with code references | Update when decisions or source-enforced boundaries change |

## Dated reviews and verification

| Record | Purpose | Interpretation |
|---|---|---|
| [End-user flow audit — 14 Oct 2026](Reviews/FMSC_END_USER_FLOW_AUDIT_2026-10-14.md) | UFA-01–09 discovery and full source coverage | Original findings at their reviewed baseline; consult master for later corrections |
| [Post-merge project audit — 14 Oct 2026](Reviews/FMSC_POST_MERGE_PROJECT_AUDIT_2026-10-14.md) | PMA release/installer findings and evidence | Baseline-specific release assessment, not a new verification |
| [UI improvements review](Reviews/UI_IMPROVEMENTS_REVIEW.md) | Earlier UI changes, portable tests and Windows acceptance checklist | Implementation history; native acceptance remains separate |
| [UI guidance audit — 20 Oct 2026](Reviews/FMSC_UI_Guidance_Audit_2026-10-20.md) | G01–G17 wording and consistency proposals | **Read-only review:** no UI changes authorized |
| [Audit fix verification — 13 Oct 2026](Archive/AUDIT_FIX_VERIFICATION_2026-10-13.md) | Dated test/verification execution | Historical evidence, not current-build certification |

## Immutable historical evidence

[Historical audit archive](Archive/FMSC_HISTORICAL_AUDITS.md) indexes the original full reports, IDs, accepted decisions, test evidence and Git revisions. Preserve frozen evidence and original finding identifiers; do not overwrite a historic observation to make it sound currently resolved.

## Related documentation

- [UI style guide](../Docs/Design/UI_STYLE_GUIDE.md), [finance wording review](../Docs/FINANCE_WORDING_REVIEW_2026-10-20.md), and [UI tuning guide](../Docs/Design/UI_TUNING_GUIDE.md)
- [Help source audit](../Docs/FAQ_AUDIT_REPORT.md) and [coverage matrix](../Docs/FAQ_COVERAGE_MATRIX.md)
- [Windows verification](../Docs/WINDOWS_VERIFICATION_2026-10-16.md), [release checklist](../Docs/RELEASE_CHECKLIST.md), and [documentation index](../Docs/README.md)

## Filing and maintenance rules

1. Keep `Audit/FMSC_MASTER_AUDIT.md` as the one status register; record new evidence with the inspected commit, date, and still-open acceptance gates. Do not equate a historical CI pass with a current runtime result.
2. Keep business rules summarized in `Audit/OWNER_DECISIONS_AND_INVARIANTS.md` and write concise invariant comments next to their implementations where a future edit could silently change semantics. Comments do not replace SQL constraints and automated tests.
3. Put new independent investigations in `Reviews/` with a descriptive date and scope; put superseded verification evidence in `Archive/` only when its original links remain intact.
4. Preserve existing report filenames and evidence links unless a rename is demonstrably needed; their current descriptive names are stable and changing them would break cross-repository references.
5. Never add school database MDF/LDF files, personal records or credentials to GitHub.
