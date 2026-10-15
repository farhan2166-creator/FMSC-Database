# Verified DACPAC refreshes

A re-export can change ZIP/export metadata without changing schema or data. Application, installer and SQL-test approvals deliberately pin the reviewed artifact. A new hash alone must never bypass those checks.

## Owner workflow

1. Commit the refreshed `FMSC.dacpac` to the private repository as usual. Keep original MDF/LDF, backups and credentials off GitHub. Do not edit approval hashes manually.
2. **FMSC DACPAC refresh verification** compares it with the previously approved Git blob. Pushes use the previous main commit, PRs use their trusted base, and manual default-branch runs use the parent of the latest artifact change. A refresh cannot approve itself by changing all hashes together. PR comparison is read-only; writing starts only on the trusted default branch.
3. A safe `main` refresh prepares `automation/dacpac-refresh-<source-commit>` and a focused synchronization PR. It preserves `FMSC.dacpac` and updates only the alignment records below.
4. Review **Desktop CI**, **Security Checks** and **Level 1 lifecycle SQL** on that exact revision, then merge manually. GITHUB_TOKEN-created PRs suppress ordinary PR events, so the publisher explicitly dispatches those existing workflows on the generated branch.
5. Update older feature branches from the corrected `main` before retesting. A later export is compared with the newly approved artifact.

Existing exact-hash checks may remain red until the alignment PR is merged; they are never weakened to hide that intermediate mismatch. A safe DACPAC comparison does not approve other changes in a source PR. The automation itself must first be reviewed and merged into `main` to activate future push/manual publication; its initial installation PR uses read credentials and cannot run the writer job.

## Trusted manual operation and settings

In **Actions → FMSC DACPAC refresh verification → Run workflow**, select default `main`. Leave **synchronize** false for a report, or set it true to prepare a PR after verification. The publisher requires `write`, `maintain` or `admin` access and refuses PR events, arbitrary dispatch refs, public repositories and stale default-branch snapshots.

Enable **Settings → Actions → General → Workflow permissions → Allow GitHub Actions to create and approve pull requests** if PR creation is disabled. Despite GitHub's setting name, this workflow never approves or merges a PR. No PAT, repository secret, paid service or visibility change is needed. Missing permissions fail publication clearly. Normal private Actions minutes/storage limits apply.

## Automatic approval boundary

- ZIP CRC, safe unique entry paths, size/expansion limits and internal model checksum must pass. XML declarations/entities, external relationship targets, ZIP comments/extra fields/trailing payloads and missing package entries are refused.
- The **entire `model.xml` must be byte-identical** to the anchored reviewed model, and every existing SQL module fingerprint must match. This includes all tables, nested columns/types, constraints, indexes, keys, dependencies/relationships, permissions, extended properties and model options, plus procedure/function/trigger bodies and signatures.
- Every exported-data entry and other deployment payload must be byte-identical, with the same inventory. New, modified or removed school data always stops automatic alignment, even with unchanged SQL. No row data is parsed, printed, reseeded or uploaded.
- Production compatibility/recovery requirements and schema version must stay unchanged, except for the package hash in the compatibility manifest. Existing inventory and whole-rupee checks must pass; the current required version remains **6**.
- Previous and candidate approval/provenance records must be valid. Arbitrary edited hashes/contracts are refused instead of silently repaired. The approved blob must be reachable in trusted artifact history.
- Allowed differences are ZIP timestamps/compression/order, export operation identity/start, relationship IDs/order and semantically identical package-metadata XML formatting. Unknown metadata, deployment identity/version changes and model reserialization require review.

This conservative metadata policy does not normalize SQL or infer equivalence from similar object names. Even potentially harmless **model** serialization changes require review. Structural changes use the separate owner-confirmed path below; they never become metadata refreshes. Differences identify changed approved schema objects and hashed payload entries without exposing SQL bodies or row values.

## Synchronized files

| File | Allowed update |
|---|---|
| `Core/ApprovedSchemaArtifact.json` | Verified new package SHA; model/module fingerprints unchanged |
| `Core/DatabaseCompatibilityRequirements.json` | Only `schemaArtifact.sha256`; structural/financial/recovery requirements unchanged |
| `Tests/LifecycleSqlChecks/Baseline/provenance.json` | Verified source package/Git blob and checked fixture digest |
| `Tests/LifecycleSqlChecks/Baseline/schema.dacpac` | Regenerate only if retained package metadata changed; identical model, no data/scripts/server statistics |

Schema-only generation has deterministic ZIP timestamps. Normal export identity/timestamp refreshes reuse the fixture. Corrupted or invented fixture provenance remains a blocker, rather than being erased by regeneration.

## Credentials and evidence

The comparison job has `contents: read`, no secrets and no persisted checkout credentials. Future PR comparisons execute the trusted base verifier; PR regression code receives no write token. There is no `pull_request_target` workflow.

The separate publisher runs only for a safe default-branch push or explicit synchronization dispatch. It re-verifies independently of uploaded reports, checks actor/current-main identity, restricts generated commits to four paths and refuses unexpected existing branches. Its short-lived `contents`, `pull-requests` and `actions` writes create the review PR and dispatch only the three reviewed validation workflows. It executes default-branch Python, never PR code, SQL or tests with write credentials.

Only sanitized `report.json` and `summary.md` are uploaded to private Actions artifacts for seven days. **REVIEW_REQUIRED** fails comparison and generates no alignment. Data-entry names and unknown/new schema identifiers are hashed; contents and connection metadata are omitted. An incomplete checkout/tooling run claims no approval or SQL success.

## Local checks

Use a full trusted default/base commit SHA from a checkout with artifact history:

```powershell
python Tools/refresh_dacpac.py --approval-ref <full-main-sha> --report-dir <private-temporary-directory>
python Tools/refresh_dacpac.py --approval-ref <full-main-sha> --report-dir <private-temporary-directory> --sync
python Tools/verify_schema_artifact.py --check
python Tools/test_audit_schema.py
python Tools/test_dacpac_refresh.py
python -m unittest discover -s Tests/LifecycleSqlChecks -p "test_*.py" -v
```

`--sync` writes only proved alignment, refuses unrelated changes and checks the result. Failed writes/post-checks restore original records. Tests use temporary repositories, the schema-only model and fictional payload bytes; they never copy school rows into fixtures. Windows/security/disposable SQL checks remain mandatory on the reviewed revision.

## Deliberate structural upgrade approval

**FMSC Database Schema Upgrade Approval** distinguishes inspection, explicit owner authorization and deployment acceptance. A readable candidate without the optional OPC relationships part still produces object differences; it never silently becomes a metadata refresh. Inspection changes no approval records.

| Status | Meaning / next action |
|---|---|
| `VERIFIED_UNCHANGED` | Already matches; no new approval PR |
| `SAFE_METADATA_REFRESH` | Model/modules/exported bytes unchanged; existing metadata synchronization applies |
| `STRUCTURAL_CHANGE_REVIEW_REQUIRED` | Review material changes and test a migration path |
| `COMPATIBILITY_FAILED` | Required contract or disposable acceptance failed; resolve before approval |
| `UPGRADE_PATH_REQUIRED` | No reviewed migration selection; deployment readiness remains blocked |
| `STRUCTURAL_CHANGE_APPROVED` | Exact owner decision is backed by a successful trusted Actions attempt; review the draft PR and all CI before merging |

1. Use a **schema-only** export for a structural upgrade. New/modified data entries, deployment scripts and unknown payloads are refused. Removing old exported data is reported and never deletes installed records. Metadata refreshes still require all existing exported bytes to remain identical.
2. Review the separate `Schema/` extraction PR. Extraction is not approval. Add/review any necessary data-preserving SQL at flat numbered `Migrations/<number>.sql` paths on main. An empty folder, matching version or already upgraded personal database is not migration evidence. Do not manufacture SQL/version changes to make a check green.
3. After this implementation is reviewed and merged, dispatch **inspect** on default `main`, supplying the full candidate commit and explicitly selected migration paths in ascending order (comma-separated). The candidate must be in main's history and still be main's current artifact. Inspection verifies the previous approved Git blob and compatibility contracts, runs real production lifecycle writers against old and fresh candidate schema-only fixtures, migrates the populated old fixture, and checks original records, recovery identity and full target catalog/module/permission equivalence. Owned SQL databases use loopback-only endpoints and are removed on success/failure.
4. Only successful inspection produces a `reviewDigest`. Review its artifact/model/migration fingerprints and test results. Dispatch **approve** on the same main revision with the same candidate/migrations, that digest, and exact `APPROVE:<reviewDigest>` confirmation. Verification runs again. Changed main, candidate, scripts or protected contracts require a new inspection and confirmation.
5. Only the **personal repository owner**, as both original and rerun actor, may publish. Trusted default-branch code rechecks live main and the complete proposal, then prepares a **draft PR** containing the four baseline files and `Core/SchemaUpgradeApproval.json`. The receipt binds the exact candidate, protected contracts, migration hashes, generated-file hashes and Actions run/attempt. No automatic merge occurs. Desktop, Security, Level 1 and approval checks are dispatched on that generated branch.
6. Changed baseline records are audited against the trusted owner-run identity and review digest. Merely editing hashes, forging a receipt or merging an unrelated PR cannot satisfy approval. Existing exact artifact/schema checks stay red until legitimate alignment; never disable them to hide an intermediate mismatch.

The implemented safeguard is owner-only default-branch dispatch plus exact digest confirmation. **No protected environment or branch protection is claimed to be active.** It requires no paid environment feature. Keep repository administration and workflow editing trusted. For optional additional protection, use **Settings → Environments**, choose the owner as required reviewer, restrict deployments to `main`, then separately review adding that environment to the publisher job. Availability depends on GitHub plan; it is not silently configured. Organization-owned repositories are refused until an explicit reviewer policy is implemented.

If needed, enable **Settings → Actions → General → Allow GitHub Actions to create and approve pull requests**. This workflow creates drafts only. Its short publisher needs contents/PR writes and Actions writes for the four allowlisted validation workflows. PR jobs have read credentials; no `pull_request_target`, PAT, or candidate script/SQL execution with write credentials is used.

Fresh acceptance and existing-database acceptance are separate: the latter requires a reviewed, tested migration reaching the candidate structure while retaining records. The production installer is unchanged. Actions never upgrades a school database, attaches original MDF/LDF, resets data or replaces an existing database. Operator backups, independently supplied database-pair checks and interactive installation remain manual acceptance.

The current application/fixture requirement remains **6**. The system never increments it or regenerates recovery/permission contracts automatically. Version changes, incompatible signatures, removed safeguards or intended transformations of existing cell values require separately reviewed application/installer/test changes. Strict preservation permits new columns/reference rows but rejects changed/deleted original values and recovery identity. Missing evidence blocks readiness rather than creating a false green.

Seven-day report artifacts contain only sanitized `report.json`/`summary.md`; new identifiers are hashed and SQL/row values are omitted. The one-day internal proposal artifact contains only the four baseline files, including a **data-free** test DACPAC, and a hashed manifest. Synthetic row snapshots/private diagnostics are deleted and never uploaded. Existing `Schema/` automation remains separate and unchanged.

Local tests: `python Tools/test_dacpac_refresh.py`, `python Tools/test_schema_upgrade.py`, and `python Tools/test_schema_upgrade.py --sql`. SQL testing needs Docker and .NET 8, uses the existing Level 1 fictional fixtures/writers and ownership guards, and demonstrates a synthetic additive upgrade without approving the uploaded database. Optional `FMSC_UPGRADE_DOTNET`/`FMSC_UPGRADE_SQLPACKAGE` select installed tools. `FMSC_UPGRADE_DIAGNOSTICS` is a local private failure log; never upload it.

## Current correction and limits

`91ec136` changed only export identity/start, relationship IDs/order and ZIP timestamps. The whole model, **273** SQL fingerprints and all **43** exported-data entries matched the prior approval. Generated [PR #17](https://github.com/farhan2166-creator/FMSC-Database/pull/17) updates three records and reuses the fixture, preserving the intentionally refreshed DACPAC.

Approval remains separate from migration/installation acceptance. This system does not run migrations, edit versions, modify original MDF/LDF or prove independently supplied detached-file compatibility. The existing DacFx recovery-scope type round-trip limitation is unchanged; Level 1 retains its test-only correction in owned disposable SQL containers. Real Windows/operator/installer acceptance remains separate.

## Readable SQL schema synchronization

The [DACPAC readable schema workflow](../.github/workflows/fmsc-dacpac-schema.yml) runs when `FMSC.dacpac` changes on `main` (also manually) and verifies contributions in a read-only PR job. The SQL extractor checks `model.xml` checksums, constructs a temporary **schema-only DACPAC**, and uses Microsoft's pinned **DacFx 170.5.96** library to generate SQL `CREATE` definitions. No exported BCP/table rows are read. It does **not** connect to SQL Server.

Generated definitions are placed in **`Schema/`**, grouped into Tables, StoredProcedures, Views, Functions, Triggers, Indexes, Types, Security, and other supported categories. A generated `SchemaManifest.json` accounts for every model object and flags those with no separate SQL statement. For example, column definitions and inline constraints are part of table `CREATE TABLE` DDL and may not have independent scripts. The original DACPAC remains the complete source of truth; no XML extraction files need be committed.

After verification, the trusted default-branch workflow opens a draft PR containing BOTH the new `Schema/` scripts and deletion of the old `DatabaseSchema/` XML export, so the old reference is never removed before a replacement is ready. On GitHub-generated PRs, CI/security/schema checks are explicitly dispatched. If any script extraction fails, the workflow fails without deleting the old folder. No automatic merges, schema approvals, SQL migrations, database writes or school data uploads occur.

An existing DACPAC refresh may still fail the separate approved-schema fingerprint policy. Generating readable SQL does not approve that change; keep the provenance validation and independent review. The first SQL replacement PR is generated **after this workflow change has been reviewed and merged into main**.
