# Documentation authority — current rules

**Updated 5 November 2026.** This map describes active policy and authoritative document locations. Prior decision narratives are not retained here.

## 01. Authority order

1. **Owner-approved rules:** [BD-01–BD-38](BUSINESS_DECISIONS.md), mirrored in [C# policy comments](../Core/BusinessDecisions.cs).
2. **Actual current implementation:** C#, SQL compatibility manifests and installer — use tests to confirm behavior.
3. **Release baseline:** [first-production decision](FIRST_RELEASE_BASELINE.md) — application 1.0.0, schema 1, next production SQL migration `2.sql`.
4. **Focused operation and engineering:** [numbered subject guides](README.md) and their linked working manuals.
5. **Dated evidence:** [Audit](../Audit/README.md) and test results; historical findings do not certify a later commit.

## 02. Superseded guidance resolved

| Domain | Use this latest rule | Earlier instruction no longer applicable |
|---|---|---|
| Credit | [BD-12](BUSINESS_DECISIONS.md#bd-12) | Monthly posting must **not** automatically use advance; credit may be applied at collection only when its transaction date is today. |
| Pending registration | [BD-01](BUSINESS_DECISIONS.md#bd-01) | The loaded child is not in the Siblings panel; separate Family Admission navigation is retired. |
| Certificates | [BD-21](BUSINESS_DECISIONS.md#bd-21) | No cancel/void action or cancelled-copy replacement workflow. |
| Receipt paper trail | [BD-18](BUSINESS_DECISIONS.md#bd-18) | Saved visit-based snapshots supersede the earlier cumulative receipt presentation. |
| Release | [Baseline](FIRST_RELEASE_BASELINE.md) | Pre-production schema 20, 21, 69, 70, 75 instructions are not the schema-1 production upgrade path. |

## 03. Single owner for each subject

| Area | Canonical starting point |
|---|---|
| Admission and placement | [02 — Admission](02_ADMISSIONS_AND_LIFECYCLE.md) |
| Fees and collection | [03 — Finance](03_FINANCE_AND_FEES.md) |
| Certificates and messaging | [04 — Certificates](04_CERTIFICATES_AND_COMMUNICATION.md) |
| Reports, receipts, print | [05 — Reports](05_REPORTS_AND_PRINTING.md) |
| Operator and UI | [06 — User and UI](06_USER_GUIDES_AND_UI.md) |
| System development | [07 — Architecture](07_ARCHITECTURE_AND_DEVELOPMENT.md) |
| Installation, updates, recovery | [08 — Release](08_INSTALLATION_AND_UPDATES.md) |
| Test evidence | [09 — Testing](09_TESTING_AND_EVIDENCE.md) |
| Dated technical audits | [Audit](../Audit/README.md) |

## 04. Cleanup limits

The dated decision transcript was removed from `BUSINESS_DECISIONS.md`; its BD-01–BD-38 current rules remain. Obsolete redirect files and redundant early release/audit stubs are removed from the branch. Two short source-referenced compatibility pointers under `Docs/Decisions/` remain. Technical audit findings and actual test-evidence files are retained because they are evidence, not alternative business decisions. Git history remains untouched. **No application, SQL, migration, workflow or real school data changes are included.**
