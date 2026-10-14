# FMSC_SMS — Master Audit and Release Status

**Updated:** 14 October 2026  
**Reviewed source:** [`83bb0b6fe2720bb0ada6da7304dbeac4d4d3de34`](https://github.com/farhan2166-creator/FMSC-Database/commit/83bb0b6fe2720bb0ada6da7304dbeac4d4d3de34)  
**Historical evidence:** [FMSC_HISTORICAL_AUDITS.md](FMSC_HISTORICAL_AUDITS.md)

> **Release acceptance: PENDING.** A passing Windows CI build verifies automated tests, not a complete installer on a real school PC. Do not deploy as an unqualified production release until the deployment and operator checks below are recorded. This document is a consolidation of recorded evidence, not a new execution of all tests.

## End-user-flow corrections under review

The merged [end-user-flow audit](FMSC_END_USER_FLOW_AUDIT_2026-10-14.md) remains historical evidence at its reviewed source. [PR #8](https://github.com/farhan2166-creator/FMSC-Database/pull/8) proposes the corrections below on a separate branch; it is not an automatic merge or deployment approval.

**Reviewed implementation:** [`5b664f3952feeef339a0af1b9ca4c39c553357a2`](https://github.com/farhan2166-creator/FMSC-Database/commit/5b664f3952feeef339a0af1b9ca4c39c553357a2). [Windows CI run 38554464212](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38554464212) completed successfully: Release compilation (zero warnings/errors), 148 focused Windows control checks, 657 portable checks, 32,183 synthetic performance/state checks, 273 SQL-module fingerprints, 12 schema-artifact tests, Windows PowerShell verification, full disposable publication and altered-DACPAC rejection. These are focused automated checks, not an interactive deployment acceptance.

| Finding | Current correction / owner decision | Automated evidence and remaining acceptance |
|---|---|---|
| UFA-01 | Sports/Provisional/NOC creation rejects re-entry, holds all inputs and preserves save-result feedback | Actual delayed injected writes, forced field events and duplicate/direct-action attempts; live SQL/network outcome acceptance remains |
| UFA-02 | Lifecycle dialogs refuse closing during review/apply, hold review inputs and discard obsolete responses | Delayed preview/apply, stale review, failure/retry and Changed-before-close fixtures; actual closure/go-live on disposable representative SQL remains |
| UFA-03 | Classes & Sections forwards busy saves. **Owner accepts abandoning small setup drafts without a new prompt.** Embedded bulk fee drafts reach the shell and reset after successful apply | Selective workspace contract, revert/commit/new-edit baseline fixtures; real bulk apply and navigation acceptance remains |
| UFA-04 | Meaningful new sports/provisional content is dirty; date-only untouched dialogs remain abandonable | Content/subject, clear/revert and modal Stay fixtures; normal operator flow acceptance remains |
| UFA-05 | Submitted admission/collection inputs stay held across asynchronous writes and refreshes | Native input-scope restoration and actual delayed photo-only admission Save; full fee transaction SQL and failed-refresh acceptance remains |
| UFA-06 | Ctrl+K uses permitted destinations, independent of hidden/captionless sidebar buttons | Compact/hidden destination search, dispatch and permission recheck fixtures; keyboard/DPI acceptance remains |
| UFA-07 | Closing print skips intentionally blank adjustment actuals without inventing counted values | Portable net-total, unchecked-method, source-preservation and historical precision fixtures; physical print acceptance remains |
| UFA-08 | New closing actuals require whole rupees; printing preserves existing two-decimal history | Native cell-validation and Submit-before-SQL rejection; historical fractional SQL records remain unchanged and should be reviewed manually |
| UFA-09 | Report/photo/family/receipt shortcuts follow destination requirements; read-only review stays accessible | Native viewing-only, Accountant access subsets and full-authority Administrator fixtures; real account acceptance remains |

No SQL, migration, reviewed DACPAC, installer, financial transaction/recovery implementation or role-grant definitions changed in this patch. Schema provenance and all 273 SQL-module fingerprints remain verified. Earlier installer/release statuses below retain their original evidence; this UI patch does not revalidate a production installation.

## Active status

| Finding or area | Current disposition | What is still needed |
|---|---|---|
| 64 historical BL/F/FR/IA/DP/RT/FQA findings | Preserved with original IDs, implementation history, owner decisions, caveats and links in the [historical archive](FMSC_HISTORICAL_AUDITS.md) | Preserve deliberate deferrals and accepted business policies. Recheck version-dependent historical claims before acting |
| FQA-01–25 | All 25 source-fixed or resolved by owner decisions in previous implementation pass | Native Windows and representative database release acceptance remains a separate gate |
| RFA-01 — Quick Registration tab guard | Source-fixed; 26 focused Windows page/host contract checks passed in recorded CI run | Real delayed SQL operations, mouse/keyboard navigation and unknown outcomes |
| RFA-02 — New Regular Certificate date draft | Source-fixed; focused Windows checks passed | End-user interactions with representative records |
| RFA-03 — Certificate New discards edits | Source-fixed; focused Windows checks passed | Real Windows save/busy/recovery routes |
| RVA-01 — Additional Fee Posting replay | 11 native disposable-SQL tests passed for identical request-ID replay and changed payload rejection | Actual network interruption and application restart where request ID may not survive |
| RVA-02 — Earlier snapshot upgrade | 8 disposable SQL version-4 to 5/6 upgrade, rollback and restore checks passed | Repeat against a verified disposable clone of the intended detached database pair |
| Schema 6 DACPAC | Reviewed and verified in recorded disposable SQL tests, including 273 SQL module fingerprints | Never publish DACPAC over existing school MDF/LDF; validate the actual target DB and schema |
| Atomic settings-write concurrency | Per-path named mutex correction documented; original 24-writer stress test and Windows preservation checks passed | Continue keeping regression enabled; actual multi-process/user testing when relevant |
| Desktop CI | Latest inspected Windows CI runs at `83bb0b6fe272` passed | Verify fresh run for each release candidate |
| Private complete-installer ZIP | **Not yet demonstrated successfully produced** | Latest inspected [installer workflow run](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38517224885) failed in PortableRelease publication because the nested `VerifySchemaArtifact.ps1` reported `Get-FileHash` unrecognized (MSB3073); investigate, correct and rerun |

**Historical records:** The [implementation progress report](https://github.com/farhan2166-creator/FMSC-Database/blob/83bb0b6fe2720bb0ada6da7304dbeac4d4d3de34/Audit/FMSC_POST_FIX_IMPLEMENTATION_PROGRESS_2026-10-14.md) documents focused Windows [run 38516189607](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38516189607) with 653 portable checks, 32,183 performance/loading checks, 273 module fingerprints and 12 schema-artifact regression tests, plus 26 focused page/host checks. This is recorded automated testing, not a manual deployment sign-off.

## Prioritized release gates

1. **Installer ZIP (active CI issue):** Get a successful run of [FMSC Complete Windows Installer ZIP](https://github.com/farhan2166-creator/FMSC-Database/actions/workflows/fmsc-complete-installer.yml). Investigate the nested Windows PowerShell `Get-FileHash` error and any additional failures independently. Preserve validation instead of bypassing tests. Confirm the actual downloadable artifact includes `FMSC_SMS/`, `Install FMSC_SMS.bat`, `core.ps1`, `Tools/SqlLocalDB.msi` signed by Microsoft, pinned `Tools/sqlcmd.exe`, all `Migrations/2.sql` through `6.sql`, and checksums.
2. **Owner-provided snapshot:** Missing MDF/LDF during CI is **intentional**. After extracting the private ZIP, the owner supplies the verified matching detached `FMSC_DB.mdf` and `FMSC_DB_log.ldf` under `Database/` for a fresh install. Never upload these to GitHub, substitute DACPAC, or replace a live school database during an upgrade.
3. **RR-03 — Installation Windows account:** Test elevated installer and application startup under the actual operator account. Installer ACLs under `C:\ProgramData\FMSC_Database` and per-user LocalDB identity can prevent a different standard account from running correctly. Do not weaken data ACLs broadly.
4. **RR-04 — Installation compatibility:** Recheck whether the installer's final “database compatible” message validates the same structural contract as production application startup. Do not assert complete compatibility from just the schema version and a few objects.
5. **RR-05 — Database integrity and rollback:** Run fresh install, schema upgrade, data preservation, backup/restore and failure recovery on a disposable verified school-like snapshot; verify actual detached-pair compatibility, numbered migration chain and recovery GUID.
6. **RR-06 — Native operator acceptance:** Verify representative registrations, admissions, account and fee workflows, save-outcome reconciliation, busy/discard behavior, printer/report output, display DPI, Windows user permissions, disconnected SQL, and application restart. Collect logs and sign-off.
7. **Accepted/deferred policy boundaries:** Preserve BL-03 local SQL trust boundary decision, FR-07 decoded-photo RAM measurement, FR-08 SQL photo-size policy, F-07 live-module semantic limits and any intentional owner business rules. These are not licenses for unrequested architectural changes.

## How to interpret audit status

- **Source-fixed:** A committed correction exists; real usage may still require validation.
- **Verified in recorded tests:** The linked test report says the scenario passed at that commit, not every future commit.
- **Owner-accepted or deferred:** An intentional decision, not an unapproved defect fix.
- **Release-gate pending:** Missing operational evidence, not proof of database corruption.
- **Historical:** An original report's open finding may have been fixed in later commits; consult both before taking action.

## Full preservation and traceability

The second document, **[FMSC_HISTORICAL_AUDITS.md](FMSC_HISTORICAL_AUDITS.md)**, indexes all five exact original reports via immutable Git links. This includes all 64 legacy finding IDs, three RFA findings, two RVA verification tasks, the six RR release findings, original source evidence, SQL verification and subsequent Windows CI results. All originals and versions also remain at the [pre-consolidation source revision](https://github.com/farhan2166-creator/FMSC-Database/tree/83bb0b6fe2720bb0ada6da7304dbeac4d4d3de34/Audit).

For future work, update **this master** with date, reviewed commit, precise evidence and status. Keep original report text frozen in Git history and cite its commit. Do not create repeated progress files for the same findings. No application, installer, SQL, migration, or school database changes are authorized by this document.
