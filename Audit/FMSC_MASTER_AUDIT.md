# FMSC_SMS — Master Audit and Release Status

**Updated:** 14 October 2026  
**Reviewed source:** [`14aba80a8751322e04aa028f960e0a68efaf08f2`](https://github.com/farhan2166-creator/FMSC-Database/commit/14aba80a8751322e04aa028f960e0a68efaf08f2) for the read-only migration-retirement verification below. Historical findings retain their original source freezes.
**Historical evidence:** [FMSC_HISTORICAL_AUDITS.md](Archive/FMSC_HISTORICAL_AUDITS.md)
**Current post-merge audit:** [FMSC_POST_MERGE_PROJECT_AUDIT_2026-10-14.md](Reviews/FMSC_POST_MERGE_PROJECT_AUDIT_2026-10-14.md) — read-only review of main 4c25103f4930; Windows desktop CI and security checks passed at this SHA, but current complete-installer and native/operator/database acceptance remain open. This supersedes only the older CI-status snapshot, not historical tests or recorded owner decisions.

> **Release acceptance: PENDING.** A passing Windows CI build verifies automated tests, not a complete installer on a real school PC. Do not deploy as an unqualified production release until the deployment and operator checks below are recorded. This document is a consolidation of recorded evidence, not a new execution of all tests.

## Consolidation index — 21 October 2026

- [Audit index](README.md) identifies the canonical owner-decision record, immutable investigations and historical verification.
- [Owner decisions and invariants](OWNER_DECISIONS_AND_INVARIANTS.md) is the cross-module locator for financial, admission, setup, data-preservation, access and UI safeguards. It does **not** replace current implementation or SQL checks.
- [UI guidance audit](Reviews/FMSC_UI_Guidance_Audit_2026-10-20.md) is an unimplemented wording/design assessment. Its G01–G17 findings require visual review and explicit approval before changes.
- This master preserves dated release evidence and outstanding gates. The 21 October consolidation organizes documentation and comments only; it did not rerun CI, installer, native GUI or database verification. In particular, the October 14 installer-policy observations must not be asserted as freshly reproduced against the October 21 source.

## End-user-flow corrections (historical reviewed baseline)

The merged [end-user-flow audit](Reviews/FMSC_END_USER_FLOW_AUDIT_2026-10-14.md) remains historical evidence at its reviewed source. [PR #8](https://github.com/farhan2166-creator/FMSC-Database/pull/8) was merged, as recorded by the later [post-merge audit](Reviews/FMSC_POST_MERGE_PROJECT_AUDIT_2026-10-14.md). The correction evidence below is pinned to its reviewed commits; a merge is not a production deployment approval.

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
| 64 historical BL/F/FR/IA/DP/RT/FQA findings | Preserved with original IDs, implementation history, owner decisions, caveats and links in the [historical archive](Archive/FMSC_HISTORICAL_AUDITS.md) | Preserve deliberate deferrals and accepted business policies. Recheck version-dependent historical claims before acting |
| FQA-01–25 | All 25 source-fixed or resolved by owner decisions in previous implementation pass | Native Windows and representative database release acceptance remains a separate gate |
| RFA-01 — Quick Registration tab guard | Source-fixed; 26 focused Windows page/host contract checks passed in recorded CI run | Real delayed SQL operations, mouse/keyboard navigation and unknown outcomes |
| RFA-02 — New Regular Certificate date draft | Source-fixed; focused Windows checks passed | End-user interactions with representative records |
| RFA-03 — Certificate New discards edits | Source-fixed; focused Windows checks passed | Real Windows save/busy/recovery routes |
| RVA-01 — Additional Fee Posting replay | 11 native disposable-SQL tests passed for identical request-ID replay and changed payload rejection | Actual network interruption and application restart where request ID may not survive |
| RVA-02 — Earlier snapshot upgrade | 8 disposable SQL version-4 to 5/6 upgrade, rollback and restore checks passed | Repeat against a verified disposable clone of the intended detached database pair |
| Schema 6 DACPAC | Reviewed and verified in recorded disposable SQL tests, including 273 SQL module fingerprints | Never publish DACPAC over existing school MDF/LDF; validate the actual target DB and schema |
| Atomic settings-write concurrency | Per-path named mutex correction documented; original 24-writer stress test and Windows preservation checks passed | Continue keeping regression enabled; actual multi-process/user testing when relevant |
| Desktop CI | Latest inspected Windows CI runs at `83bb0b6fe272` passed | Verify fresh run for each release candidate |
| Private complete-installer ZIP | [Run 38528371049](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38528371049) successfully validated and uploaded the private complete ZIP after the verifier fixes | This recorded package includes migrations 2–6. A package enforcing the owner's new no-upgrade policy has not been implemented or generated; see the verification gate below |

**Historical records:** The [implementation progress report](https://github.com/farhan2166-creator/FMSC-Database/blob/83bb0b6fe2720bb0ada6da7304dbeac4d4d3de34/Audit/FMSC_POST_FIX_IMPLEMENTATION_PROGRESS_2026-10-14.md) documents focused Windows [run 38516189607](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38516189607) with 653 portable checks, 32,183 performance/loading checks, 273 module fingerprints and 12 schema-artifact regression tests, plus 26 focused page/host checks. This is recorded automated testing, not a manual deployment sign-off.

## Prioritized release gates

1. **Installer policy and ZIP:** The previous `Get-FileHash` publish issue is resolved and a private complete ZIP was uploaded in the recorded successful run. Keep its evidence separate from installation acceptance. Migration retirement is currently blocked by the verification gate below; the current installer still supports older-schema upgrades. After that gate succeeds, implement strict pre-activation compatibility, remove obsolete deployment upgrades and verify a fresh private ZIP against the new asset policy without disabling checks.
2. **Owner-provided snapshot:** Missing MDF/LDF during CI is **intentional**. After extracting the private ZIP, the owner supplies the verified matching detached `FMSC_DB.mdf` and `FMSC_DB_log.ldf` under `Database/` for a fresh install. Never upload these to GitHub, substitute DACPAC, or replace a live school database during an upgrade.
3. **RR-03 — Installation Windows account:** Test elevated installer and application startup under the actual operator account. Installer ACLs under `C:\ProgramData\FMSC_Database` and per-user LocalDB identity can prevent a different standard account from running correctly. Do not weaken data ACLs broadly.
4. **RR-04 — Installation compatibility:** Recheck whether the installer's final “database compatible” message validates the same structural contract as production application startup. Do not assert complete compatibility from just the schema version and a few objects.
5. **RR-05 — Database integrity and rollback:** Run fresh install, schema upgrade, data preservation, backup/restore and failure recovery on a disposable verified school-like snapshot; verify actual detached-pair compatibility, numbered migration chain and recovery GUID.
6. **RR-06 — Native operator acceptance:** Verify representative registrations, admissions, account and fee workflows, save-outcome reconciliation, busy/discard behavior, printer/report output, display DPI, Windows user permissions, disconnected SQL, and application restart. Collect logs and sign-off.
7. **Accepted/deferred policy boundaries:** Preserve BL-03 local SQL trust boundary decision, FR-07 decoded-photo RAM measurement, FR-08 SQL photo-size policy, F-07 live-module semantic limits and any intentional owner business rules. These are not licenses for unrequested architectural changes.

## Owner policy and migration-retirement verification — 14 October 2026

**Owner-approved target policy:** Fresh installs use only the latest verified, matching detached MDF/LDF pair. Already compatible databases keep all existing records. Older, newer or structurally incompatible databases must be rejected without upgrades, reseeding or template replacement. The private GitHub ZIP continues to omit MDF/LDF; the owner adds them after extraction. **This target policy is not yet implemented in the current installer.**

**Decision: STOP before migration deletion or installer changes.** Final schema definitions are present, but the independent snapshot/compatibility evidence needed to retire deployment upgrades is incomplete. No migration files, installer functions, application logic, SQL permissions or compatibility requirements were removed or changed in this review.

### Verification matrix

| Migration | Original changes | Present in DACPAC | Verified | Safe to retire |
|---|---|---|---|---|
| `2.sql` | Eight trusted whole-rupee constraints covering fee configuration/assignment, charges, payments/allocations, manual posting, clearance balances and bank-certificate income; rejects fractional existing rows without rounding; records version 2 | All eight final expressions are present | Offline expression/fingerprint checks pass. Unchanged DACPAC-derived data has zero fractional violations across all eight tables; constraints are enabled/trusted. Actual detached snapshot remains unverified | **No: shared retirement gate is not satisfied** |
| `3.sql` | Retains Administrator/Accountant, normalizes primary-role/admin membership references, removes obsolete roles/grants, preserves supported Accountant grants, synchronizes 28 permission definitions, updates seven permission/user/family procedures, writes policy audit/version 3 | Seven final procedure bodies and signatures match; current exported role/catalog data is included | All seven bodies match migration SQL tokens while preserving string literals. Disposable export has exactly two supported roles, valid references/admin memberships, 28 matching permission codes/names/descriptions, six Accountant grants and one policy audit marker. Original pre-migration credential/grant preservation cannot be independently reconstructed from this restore alone | **No: detached data/preservation evidence and shared compatibility gate remain** |
| `4.sql` | Adds `QuickRegistrationRequests`, request/admission uniqueness, user/student FKs, 32-byte payload hash, UTC default, a database-specific `uniqueidentifier` recovery scope and version 4 | Table, columns, keys/FKs/default and named recovery property are present. The property model contains a GUID string, which does not prove its live SQL-variant base type | Request structure matches the manifest/reference. The unchanged DACPAC publish restores the recovery property's base type as **`varchar`**, so the strict production compatibility validator rejects it. No property repair was made. Actual MDF/LDF property type is unverified | **No: required recovery identity is not proven on the distributed snapshot** |
| `5.sql` | Updates `PostEnrollmentOpeningCharges`, `CompleteQuickAdmission` and `AddStudentFeeAssignment` for optional phone and safe pending-opening-fee posting/restoration; records version 5. The migration itself does not backfill student/payment/role records | All three final procedure bodies and signatures match | SQL-token comparison and final module fingerprints pass. Exported version history includes 5. This read-only review did not rerun fee-posting transactions or verify independently supplied MDF/LDF data | **No: shared retirement gate is not satisfied** |
| `6.sql` | Adds `MutationRequests`, request PK, user/payment/bank-certificate FKs, payload hash, operation-kind/JSON-result checks and UTC default; records version 6 | Final table/column/key/FK/check/default definitions are present | Production structural query reports no problems for these structures; exported version history ends at 6. The export has zero mutation/request rows, so zero marker violations are an empty-data invariant, not an exercised transaction-recovery test | **No: shared retirement gate is not satisfied** |

### Evidence actually executed

- Unchanged approved package SHA-256: `b66ec66f24f06fe1623445c0f433afb5cd8400270fd73612e273b7fa22f156a6`. `python Tools/verify_schema_artifact.py --check` passed ZIP/model integrity, approved provenance, contract inventory, all whole-rupee definitions and **273 module fingerprints**. `Tools/test_audit_schema.py` passed **12 tests**; `Tools/test_permission_policy.py` passed **4 tests**.
- Recompared the retained schema-6 reference extracted in the earlier disposable baseline-plus-migrations verification with the current approved package. There are **zero named-object differences**. Anonymous PK/FK/constraint elements match as an unordered set. A separate database-options serialization difference remains: the reference explicitly includes `QueryStoreDesiredState=0`, while the approved package omits that setting. It is not a migration 2–6 outcome; the entire model is not claimed byte-identical to this reference.
- Final inventory includes **60 tables, 232 procedures, 34 triggers, 60 PKs, 76 FKs, 19 unique constraints, 70 indexes, 52 check constraints, 97 defaults and 13 table types**. Named definitions, parameter/column metadata, triggers, recovery/request structures and constraint relationships match the final reference; all approved SQL module fingerprints remain unchanged.
- Loaded the **unmodified** approved DACPAC and its included data into a new isolated SQL Server 2022 Developer database on loopback. No migration scripts were executed on that database. Read-only queries confirmed version history **1,2,3,4,5,6**, current supported-role/permission invariants, whole-rupee data, trusted/enabled constraints and zero inconsistent request markers. Both request tables were empty. This is verification of exported data, not independent verification of the owner's distributed database files or historical before/after data preservation.
- A temporary .NET 8 probe linked the actual `Core/DatabaseCompatibility.cs` and both embedded compatibility manifests. It compiled with zero warnings/errors, then **rejected the unchanged DACPAC-derived database** solely for the recovery scope's SQL-variant base type. The probe deliberately returned a nonzero compatibility result. The valid nonzero GUID string did not override the required `uniqueidentifier` type. No DACPAC/property/contract alteration was used to produce a passing result.

### Blockers and required next evidence

1. No latest detached MDF/LDF pair or independently verified copy of that exact pair is available in this workspace. Verify a **disposable copy** using the production version/structural contract, whole-rupee and role/catalog invariants, including the recovery scope's nonzero GUID and actual `SQL_VARIANT_PROPERTY(value,'BaseType')='uniqueidentifier'`. Preserve the original files. The DACPAC round-trip type limitation does not establish whether the owner's MDF/LDF has the wrong type; do not substitute this restored DACPAC for the intended snapshot.
2. Original Accountant grants, credentials and school-record preservation require trustworthy before/after evidence if a retrospective claim is needed. Current exported state and recorded historical migration tests are useful but do not independently prove every historical data operation on an unrelated supplied pair.
3. The current installer activates a same-version database based on version before its limited four-object check; it does not invoke the full production structural/recovery contract before activation. It still discovers/runs numbered upgrades for older databases. After snapshot verification, replace that behavior with strict, read-only compatibility **before activation**, reject both version directions, and retain safe staging/rollback and existing-data protection. Test each branch and the resulting migration-free ZIP before retiring deployment files.
4. `Core/DatabaseMigrationRequirements.json` is an **active startup/restore compatibility manifest** for request tables, FKs/checks and typed recovery identity. Its name does not make it obsolete. Do not remove it or financial/registration request-recovery helpers when retiring deployment upgrades.

Missing snapshots during normal development are expected under [AGENTS.md](../AGENTS.md). This is a **migration-retirement evidence gate**, not a claim that their absence breaks the existing CI ZIP workflow. Interactive Windows attachment, operator-account/ACL behavior, installer rollback, and printer/application acceptance remain pending. No migration-free installer workflow/ZIP or revised no-upgrade behavior is claimed.

## How to interpret audit status

- **Source-fixed:** A committed correction exists; real usage may still require validation.
- **Verified in recorded tests:** The linked test report says the scenario passed at that commit, not every future commit.
- **Owner-accepted or deferred:** An intentional decision, not an unapproved defect fix.
- **Release-gate pending:** Missing operational evidence, not proof of database corruption.
- **Historical:** An original report's open finding may have been fixed in later commits; consult both before taking action.

## Full preservation and traceability

The second document, **[FMSC_HISTORICAL_AUDITS.md](Archive/FMSC_HISTORICAL_AUDITS.md)**, indexes all five exact original reports via immutable Git links. This includes all 64 legacy finding IDs, three RFA findings, two RVA verification tasks, the six RR release findings, original source evidence, SQL verification and subsequent Windows CI results. All originals and versions also remain at the [pre-consolidation source revision](https://github.com/farhan2166-creator/FMSC-Database/tree/83bb0b6fe2720bb0ada6da7304dbeac4d4d3de34/Audit).

For future work, update **this master** with date, reviewed commit, precise evidence and status. Keep original report text frozen in Git history and cite its commit. Do not create repeated progress files for the same findings. No application, installer, SQL, migration, or school database changes are authorized by this document.
