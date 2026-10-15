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

This conservative policy does not normalize SQL or infer equivalence from similar object names. Even potentially harmless **model** serialization changes require review. There is no material-change override. Differences identify changed approved schema objects and hashed payload entries without exposing SQL bodies or row values.

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

## Current correction and limits

`91ec136` changed only export identity/start, relationship IDs/order and ZIP timestamps. The whole model, **273** SQL fingerprints and all **43** exported-data entries matched the prior approval. Generated [PR #17](https://github.com/farhan2166-creator/FMSC-Database/pull/17) updates three records and reuses the fixture, preserving the intentionally refreshed DACPAC.

Approval remains separate from migration/installation acceptance. This system does not run migrations, edit versions, modify original MDF/LDF or prove independently supplied detached-file compatibility. The existing DacFx recovery-scope type round-trip limitation is unchanged; Level 1 retains its test-only correction in owned disposable SQL containers. Real Windows/operator/installer acceptance remains separate.

## Readable schema-reference synchronization

A separate [DACPAC readable schema workflow](../.github/workflows/fmsc-dacpac-schema.yml) runs on changes to `FMSC.dacpac` on `main`, supports manual runs, and validates PR inputs without write permissions. It reads **only `model.xml` and `Origin.xml`** from the DACPAC, not exported row payloads. Generated `DatabaseSchema/` files consist of one lossless model XML element per object (including table columns, constraints, relationships and dependencies), SQL module **body** files when available, and a manifest. Body-only files are not necessarily runnable CREATE definitions; review the paired XML and DACPAC.

On the trusted default branch, changed generated files produce a **draft PR** (no auto-merge). No change means no commit. This is separate from approving the DACPAC: a schema change that existing provenance verification refuses remains unapproved even if the readable files are generated. Never deploy or migrate from this reference folder. GitHub Actions PR creation must be permitted by repository settings. The initial sync PR will populate the folder after this workflow is merged into `main`; it does not happen while the workflow itself is under review.
