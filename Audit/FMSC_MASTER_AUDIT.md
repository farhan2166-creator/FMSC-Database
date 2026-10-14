# FMSC_SMS — Master Audit and Release Status

**Updated:** 14 October 2026  
**Reviewed source:** [`83bb0b6fe2720bb0ada6da7304dbeac4d4d3de34`](https://github.com/farhan2166-creator/FMSC-Database/commit/83bb0b6fe2720bb0ada6da7304dbeac4d4d3de34)  
**Historical evidence:** [FMSC_HISTORICAL_AUDITS.md](FMSC_HISTORICAL_AUDITS.md)

> **Release acceptance: PENDING.** A passing Windows CI build verifies automated tests, not a complete installer on a real school PC. Do not deploy as an unqualified production release until the deployment and operator checks below are recorded. This document is a consolidation of recorded evidence, not a new execution of all tests.

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
