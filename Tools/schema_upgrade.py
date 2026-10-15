"""Owner-bound schema approval. No SQL or candidate code executes in publication.

Inspection and SQL verification are read-only with respect to repository approval.
Only a trusted default-branch dispatch with an exact review digest may prepare a PR.
"""
import argparse
import base64
import copy
import io
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import zipfile

import refresh_dacpac as refresh
from publish_dacpac_refresh import GitHub
from export_dacpac_schema import prepare_model_only

ROOT = refresh.ROOT
RECEIPT = "Core/SchemaUpgradeApproval.json"
WORKFLOW = ".github/workflows/fmsc-schema-upgrade-approval.yml"
PATHS = (*refresh.SYNC_PATHS, RECEIPT)
VALIDATION = ("fmsc-desktop-ci.yml", "fmsc-security.yml", "fmsc-lifecycle-sql-ci.yml")
PROTECTED = ("Core/DatabaseMigrationRequirements.json", "Core/PermissionCatalog.json", "DatabaseHelper.cs", "Installer/core.ps1")


def fixed_json(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":")).encode()


def full_sha(value):
    if not re.fullmatch(r"[a-f0-9]{40}", value or ""):
        raise refresh.Refusal("FULL_COMMIT_SHA_REQUIRED")
    return value


def ancestor(project, older, newer):
    try:
        refresh.git(project, "merge-base", "--is-ancestor", full_sha(older), full_sha(newer))
    except subprocess.SubprocessError:
        raise refresh.Refusal("CANDIDATE_OR_APPROVAL_NOT_IN_TRUSTED_HISTORY") from None


def baseline(project, source):
    """Validate approval records and the actual anchored package, not a new hash."""
    provenance = refresh.read_json(refresh.git(project, "show", source + ":" + refresh.PROVENANCE))
    approved = refresh.read_json(refresh.git(project, "show", source + ":" + refresh.APPROVAL))
    previous = refresh.git(project, "cat-file", "blob", provenance["sourceGitBlob"])
    parts = refresh.archive(previous, candidate=True)
    _, _, _, actual = refresh.fingerprint(parts, previous)
    if actual != approved or provenance["sourcePackageSha256"] != actual["packageSha256"] or provenance["sourceModelSha256"] != actual["modelSha256"]:
        raise refresh.Refusal("INCORRECT_BASELINE_PROVENANCE")
    history = refresh.git(project, "rev-list", "--objects", source, "--", "FMSC.dacpac").decode().splitlines()
    if not any(line.split()[0] == provenance["sourceGitBlob"] for line in history):
        raise refresh.Refusal("APPROVED_BLOB_NOT_IN_TRUSTED_HISTORY")
    refresh.fixture_valid(refresh.git(project, "show", source + ":" + refresh.FIXTURE), provenance, approved)
    return approved, parts, provenance


def inspect_candidate(project, source, candidate, migrations):
    full_sha(source); ancestor(project, candidate, source)
    approved, old, _ = baseline(project, source)
    # A historical commit is allowed only while its artifact is still the latest one.
    candidate_blob = refresh.git(project, "rev-parse", candidate + ":FMSC.dacpac").decode().strip()
    if candidate_blob != refresh.git(project, "rev-parse", source + ":FMSC.dacpac").decode().strip():
        raise refresh.Refusal("STALE_CANDIDATE_ARTIFACT")
    raw = refresh.git(project, "cat-file", "blob", candidate_blob)
    new = refresh.archive(raw, candidate=True)
    root, ns, elements, actual = refresh.fingerprint(new, raw)
    contract = refresh.read_json(refresh.git(project, "show", source + ":" + refresh.CONTRACT))
    contract["schemaArtifact"]["sha256"] = actual["packageSha256"]
    errors = refresh.verify_contract(root, ns, elements, contract, actual)
    # Inventory-only additions can be verified against an expanded contract in SQL.
    hard_errors = [error for error in errors if not error.endswith("contract inventory differs from DACPAC")]
    paths = [p.strip() for p in migrations.split(",") if p.strip()]
    if len(paths) > 12 or len(paths) != len(set(paths)):
        raise refresh.Refusal("INVALID_MIGRATION_SELECTION")
    selected = []
    for path in paths:
        if not re.fullmatch(r"Migrations/[1-9][0-9]*\.sql", path):
            raise refresh.Refusal("ONLY_REVIEWED_FLAT_NUMBERED_MIGRATIONS_ALLOWED")
        entry = refresh.git(project, "ls-tree", source, "--", path).decode().split()
        if len(entry) < 3 or entry[0] != "100644":
            raise refresh.Refusal("MIGRATION_NOT_A_REGULAR_FILE_ON_TRUSTED_MAIN")
        selected.append({"path": path, "sha256": refresh.sha(refresh.git(project, "show", source + ":" + path))})
    if paths != sorted(paths, key=lambda p: int(Path(p).stem)):
        raise refresh.Refusal("MIGRATIONS_MUST_BE_IN_VERSION_ORDER")
    changes = refresh.model_differences(old, new)
    report = {"status": "STRUCTURAL_CHANGE_REVIEW_REQUIRED", "sourceCommit": source, "candidateCommit": candidate,
        "previousPackageSha256": approved["packageSha256"], "previousModelSha256": approved["modelSha256"],
        "candidatePackageSha256": actual["packageSha256"], "candidateModelSha256": actual["modelSha256"],
        "candidateBlob": candidate_blob, "modelObjects": changes,
        "objectChanges": {kind: sum(c["change"] == kind for c in changes) for kind in ("added", "modified", "removed")},
        "structuralChangesDetected": old["model.xml"] != new["model.xml"],
        "compatibilityVerified": False, "ownerApprovalRequired": True, "upgradePathVerified": False,
        "requiredSchemaVersion": refresh.required_version(refresh.git(project, "show", source + ":DatabaseHelper.cs")),
        "migrations": selected, "reasons": [], "deploymentReady": False}
    if not report["structuralChangesDetected"]:
        report["reasons"].append("USE_EXISTING_METADATA_REFRESH_WORKFLOW")
    if set(new) - refresh.FIXTURE_ENTRIES:
        report["reasons"].append("STRUCTURAL_APPROVAL_REQUIRES_SCHEMA_ONLY_EXPORT_NO_DATA_OR_SCRIPTS")
    if hard_errors:
        # Values/SQL/XML from a candidate must never enter reports.
        report["reasons"].append("PROTECTED_APPLICATION_CONTRACT_FAILED")
        report["status"] = "COMPATIBILITY_FAILED"
    if not selected:
        report["reasons"].append("REVIEWED_DATA_PRESERVING_MIGRATION_REQUIRED")
        if not hard_errors: report["status"] = "UPGRADE_PATH_REQUIRED"
    report["nextAction"] = "Select reviewed main-branch migrations and run disposable SQL verification. No baseline has been approved."
    if report["requiredSchemaVersion"] != "6":
        report["status"] = "COMPATIBILITY_FAILED"
        report["reasons"].append("VERSION_CHANGE_REQUIRES_REVIEWED_APPLICATION_INSTALLER_AND_FIXTURES")
    return report, raw


def preserve_contract(old, new):
    """Never drop/weaken existing requirements when collecting an expanded catalog."""
    result = copy.deepcopy(new)
    for section, rows in old.items():
        if section == "schemaArtifact": continue
        if not isinstance(rows, list) or section not in new:
            raise refresh.Refusal("UNSUPPORTED_COMPATIBILITY_CONTRACT_SECTION")
        for row in rows:
            # SQL catalog JSON may omit an optional null (e.g. an unfiltered
            # unique index). Keep the old row verbatim after semantic matching.
            nonnull = lambda value: {k: v for k, v in value.items() if v is not None}
            matches = [item for item in new[section] if nonnull(item) == nonnull(row)]
            if not matches and section == "uniqueIndexes":
                matches = [item for item in new[section] if {k: v for k, v in nonnull(item).items() if k != "name"} ==
                    {k: v for k, v in nonnull(row).items() if k != "name"}]
            if not matches and section == "checkConstraints" and row.get("isSystemNamed"):
                matches = [item for item in new[section] if item.get("schema") == row["schema"] and
                    item.get("objectName") == row["objectName"] and item.get("definition") in
                    (row["definition"], "(" + row["definition"] + ")") and item.get("isSystemNamed")]
            if not matches:
                raise refresh.Refusal("PROTECTED_COMPATIBILITY_REQUIREMENT_CHANGED:" + section)
            if matches[0] != row:
                result[section].remove(matches[0]); result[section].append(row)
    return result


def write_report(folder, report):
    folder.mkdir(parents=True, exist_ok=True)
    (folder / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    text = ["# FMSC database schema upgrade", "", "Status: **" + report["status"] + "**", ""]
    for key in ("sourceCommit", "candidateCommit", "previousPackageSha256", "previousModelSha256", "candidatePackageSha256",
                "candidateModelSha256", "objectChanges", "compatibilityVerified", "ownerApprovalRequired", "upgradePathVerified",
                "requiredSchemaVersion", "deploymentReady", "reviewDigest", "reasons", "nextAction"):
        if key in report: text.append(f"- {key}: `{report[key]}`")
    text += ["", "| Type | Object identifier | Change |", "|---|---|---|"]
    text += [f"| {c['kind']} | {c['object']} | {c['change']} |" for c in report.get("modelObjects", [])]
    text += ["", "New identifiers are hashed. No SQL bodies, exported rows or connections are included.",
        "SQL verification uses synthetic records only. Independently supplied MDF/LDF, interactive installation and real-school upgrade acceptance remain manual."]
    (folder / "summary.md").write_text("\n".join(text) + "\n")


def proposal(project, report, raw, catalog, folder):
    actual = refresh.fingerprint(refresh.archive(raw, candidate=True), raw)[3]
    old = refresh.read_json(refresh.git(project, "show", report["sourceCommit"] + ":" + refresh.CONTRACT))
    contract = preserve_contract(old, catalog)
    contract["schemaArtifact"] = {"sha256": actual["packageSha256"]}
    parts = refresh.archive(raw, candidate=True)
    root, ns, elements, _ = refresh.fingerprint(parts, raw)
    if refresh.verify_contract(root, ns, elements, contract, actual):
        raise refresh.Refusal("EXPANDED_COMPATIBILITY_CONTRACT_INVALID")
    with tempfile.TemporaryDirectory(prefix="fmsc-upgrade-fixture-") as temporary:
        source, target = Path(temporary) / "source.dacpac", Path(temporary) / "fixture.dacpac"
        source.write_bytes(raw); prepare_model_only(source, target); fixture = target.read_bytes()
    provenance = {"sourcePackageSha256": actual["packageSha256"], "sourceModelSha256": actual["modelSha256"],
        "sourceGitBlob": report["candidateBlob"], "fixtureSha256": refresh.sha(fixture)}
    refresh.fixture_valid(fixture, provenance, actual)
    updates = {refresh.APPROVAL: json.dumps(actual, indent=2).encode() + b"\n",
        refresh.CONTRACT: json.dumps(contract, separators=(",", ":")).encode() + b"\n",
        refresh.PROVENANCE: json.dumps(provenance, indent=2).encode() + b"\n", refresh.FIXTURE: fixture}
    manifest = {key: report[key] for key in ("sourceCommit", "candidateCommit", "previousPackageSha256", "previousModelSha256",
        "candidatePackageSha256", "candidateModelSha256", "candidateBlob", "migrations", "requiredSchemaVersion")}
    manifest["updates"] = {name: refresh.sha(value) for name, value in sorted(updates.items())}
    manifest["protectedContracts"] = {name: refresh.sha(refresh.git(project, "show", report["sourceCommit"] + ":" + name)) for name in PROTECTED}
    digest = refresh.sha(fixed_json(manifest))
    for name, value in updates.items():
        path = folder / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(value)
    (folder / "manifest.json").write_bytes(fixed_json(manifest))
    report.update(reviewDigest=digest, compatibilityVerified=True, upgradePathVerified=True,
        status="STRUCTURAL_CHANGE_REVIEW_REQUIRED", nextAction="Owner: dispatch on main with confirmation APPROVE:" + digest + ". A draft PR, never an automatic merge, will be prepared.")
    return digest


def authorize(repository, actor, triggering_actor, event, ref, source, info):
    owner = info.get("owner", {}).get("login")
    if not info.get("private") or info.get("owner", {}).get("type") != "User" or not owner:
        raise refresh.Refusal("PRIVATE_USER_OWNED_REPOSITORY_REQUIRED")
    if actor != owner or triggering_actor != owner or repository.split("/")[0] != owner:
        raise refresh.Refusal("EXPLICIT_REPOSITORY_OWNER_APPROVAL_REQUIRED")
    if event != "workflow_dispatch" or ref != "refs/heads/" + info["default_branch"]:
        raise refresh.Refusal("TRUSTED_DEFAULT_BRANCH_DISPATCH_REQUIRED")
    full_sha(source)


def publish(project, folder, expected_digest, confirmation, api, context):
    base = "/repos/" + context["repository"]
    info = api("GET", base)
    authorize(context["repository"], context["actor"], context["triggeringActor"], context["event"], context["ref"], context["source"], info)
    manifest = refresh.read_json((folder / "manifest.json").read_bytes())
    digest = refresh.sha(fixed_json(manifest))
    if digest != expected_digest or confirmation != "APPROVE:" + digest:
        raise refresh.Refusal("EXACT_REVIEW_DIGEST_CONFIRMATION_REQUIRED")
    if manifest["sourceCommit"] != context["source"] or refresh.git(project, "rev-parse", "HEAD").decode().strip() != context["source"]:
        raise refresh.Refusal("TRUSTED_SOURCE_CHANGED")
    if api("GET", base + "/git/ref/heads/" + info["default_branch"])["object"]["sha"] != context["source"]:
        raise refresh.Refusal("MAIN_MOVED_REINSPECT_AND_REAPPROVE")
    report, raw = inspect_candidate(project, context["source"], manifest["candidateCommit"], ",".join(m["path"] for m in manifest["migrations"]))
    if report["reasons"] or report["candidatePackageSha256"] != manifest["candidatePackageSha256"] or report["migrations"] != manifest["migrations"]:
        raise refresh.Refusal("CANDIDATE_OR_MIGRATION_CHANGED")
    if set(manifest["updates"]) != set(refresh.SYNC_PATHS):
        raise refresh.Refusal("UNEXPECTED_APPROVAL_PATH")
    updates = {}
    for name, checksum in manifest["updates"].items():
        path = folder / name
        if path.is_symlink(): raise refresh.Refusal("SYMLINK_PROPOSAL_REFUSED")
        value = path.read_bytes()
        if refresh.sha(value) != checksum: raise refresh.Refusal("SUBSTITUTED_APPROVAL_PAYLOAD")
        updates[name] = value
    # Revalidate the data-free fixture and preserve every old compatibility requirement.
    approval = refresh.read_json(updates[refresh.APPROVAL])
    if approval != refresh.fingerprint(refresh.archive(raw, candidate=True), raw)[3]:
        raise refresh.Refusal("SUBSTITUTED_APPROVAL_FINGERPRINT")
    refresh.fixture_valid(updates[refresh.FIXTURE], refresh.read_json(updates[refresh.PROVENANCE]), approval)
    old = refresh.read_json(refresh.git(project, "show", context["source"] + ":" + refresh.CONTRACT))
    preserve_contract(old, refresh.read_json(updates[refresh.CONTRACT]))
    receipt = {"format": 1, "status": "STRUCTURAL_CHANGE_APPROVED", "reviewDigest": digest,
        "manifest": manifest, "runId": str(context["runId"]), "runAttempt": int(context["runAttempt"]), "actor": context["actor"]}
    updates[RECEIPT] = json.dumps(receipt, indent=2).encode() + b"\n"
    from publish_dacpac_refresh import create_review_pr
    branch = "automation/schema-upgrade-" + digest[:16]
    pr = create_review_pr(api, context["repository"], context["source"], branch, updates,
        "Review explicitly approved database schema upgrade", "Owner approval is bound to `" + digest + "`.\n\n"
        "Candidate: `" + manifest["candidateCommit"] + "`; package: `" + manifest["candidatePackageSha256"] + "`.\n\n"
        "Synthetic fresh/migration, production compatibility, record-preservation and lifecycle checks passed in run " + str(context["runId"]) +
        ". Review all dispatched Desktop/Security/Level 1 results before merging. No school database was modified. No automatic merge or production deployment approval.", True)
    for workflow in VALIDATION:
        api("POST", base + "/actions/workflows/" + workflow + "/dispatches", {"ref": branch})
    api("POST", base + "/actions/workflows/fmsc-schema-upgrade-approval.yml/dispatches", {"ref": branch, "inputs": {"operation": "validate"}})
    return pr


def audit_approval(project, anchor, api, repository):
    """A manually edited approval JSON or unrelated PR cannot authorize a schema."""
    changed = refresh.git(project, "diff", "--name-only", anchor, "HEAD", "--", *PATHS).decode().splitlines()
    if not changed: return "NO_APPROVAL_RECORD_CHANGE"
    report, _ = refresh.verify(project, anchor)
    if report["result"] in ("ALIGNED", "SAFE_METADATA_ONLY"): return "VERIFIED_METADATA_ALIGNMENT"
    path = project / RECEIPT
    if not path.is_file() or path.is_symlink(): raise refresh.Refusal("OWNER_APPROVAL_RECEIPT_REQUIRED")
    receipt = refresh.read_json(path.read_bytes()); manifest = receipt["manifest"]
    digest = refresh.sha(fixed_json(manifest))
    if receipt["reviewDigest"] != digest or receipt["status"] != "STRUCTURAL_CHANGE_APPROVED":
        raise refresh.Refusal("INVALID_APPROVAL_RECEIPT")
    for name, checksum in manifest["updates"].items():
        if name not in refresh.SYNC_PATHS or refresh.sha((project / name).read_bytes()) != checksum:
            raise refresh.Refusal("APPROVAL_PAYLOAD_DIFFERS_FROM_OWNER_REVIEW")
    if set(manifest["updates"]) != set(refresh.SYNC_PATHS): raise refresh.Refusal("INCOMPLETE_APPROVAL_PAYLOAD")
    if set(manifest["protectedContracts"]) != set(PROTECTED): raise refresh.Refusal("INCOMPLETE_PROTECTED_CONTRACTS")
    for name, checksum in manifest["protectedContracts"].items():
        if refresh.sha((project / name).read_bytes()) != checksum:
            raise refresh.Refusal("PROTECTED_APPLICATION_OR_INSTALLER_CONTRACT_CHANGED")
    if refresh.sha((project / "FMSC.dacpac").read_bytes()) != manifest["candidatePackageSha256"]:
        raise refresh.Refusal("APPROVAL_DACPAC_SUBSTITUTED")
    ancestor(project, manifest["sourceCommit"], anchor)
    previous = refresh.read_json(refresh.git(project, "show", manifest["sourceCommit"] + ":" + refresh.APPROVAL))
    anchored = refresh.read_json(refresh.git(project, "show", anchor + ":" + refresh.APPROVAL))
    if previous != anchored or previous["packageSha256"] != manifest["previousPackageSha256"]:
        raise refresh.Refusal("STALE_BASELINE_APPROVAL")
    if not re.fullmatch(r"[1-9][0-9]*", receipt["runId"]) or receipt["runAttempt"] < 1:
        raise refresh.Refusal("INVALID_APPROVAL_RUN")
    base = "/repos/" + repository; info = api("GET", base)
    run = api("GET", base + "/actions/runs/" + receipt["runId"] + "/attempts/" + str(receipt["runAttempt"]))
    import time
    for _ in range(20):
        if run.get("status") not in ("queued", "in_progress"): break
        time.sleep(2)
        run = api("GET", base + "/actions/runs/" + receipt["runId"] + "/attempts/" + str(receipt["runAttempt"]))
    authorize(repository, run["actor"]["login"], run["triggering_actor"]["login"], run["event"],
        "refs/heads/" + run["head_branch"], run["head_sha"], info)
    if receipt["actor"] != run["actor"]["login"] or run["path"] != WORKFLOW or run["head_sha"] != manifest["sourceCommit"] or run["conclusion"] != "success" or \
        run["display_title"] != "Schema approve " + manifest["candidateCommit"] + " " + digest:
        raise refresh.Refusal("TRUSTED_APPROVAL_RUN_NOT_VERIFIED")
    for name, checksum in ((m["path"], m["sha256"]) for m in manifest["migrations"]):
        if refresh.sha(refresh.git(project, "show", manifest["sourceCommit"] + ":" + name)) != checksum:
            raise refresh.Refusal("APPROVAL_MIGRATION_SUBSTITUTED")
    return "STRUCTURAL_CHANGE_APPROVED"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("inspect", "publish", "audit"))
    parser.add_argument("--project", type=Path, default=ROOT)
    parser.add_argument("--candidate")
    parser.add_argument("--source")
    parser.add_argument("--migrations", default="")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--proposal", type=Path)
    parser.add_argument("--sql", action="store_true")
    parser.add_argument("--anchor")
    args = parser.parse_args()
    try:
        if args.operation == "audit":
            print(audit_approval(args.project, args.anchor, GitHub(os.environ["GH_TOKEN"]), os.environ["GITHUB_REPOSITORY"]))
            return 0
        if args.operation == "publish":
            context = {key: os.environ[env] for key, env in {"repository": "GITHUB_REPOSITORY", "source": "GITHUB_SHA",
                "actor": "GITHUB_ACTOR", "triggeringActor": "GITHUB_TRIGGERING_ACTOR", "event": "GITHUB_EVENT_NAME", "ref": "GITHUB_REF",
                "runId": "GITHUB_RUN_ID", "runAttempt": "GITHUB_RUN_ATTEMPT"}.items()}
            print(json.dumps(publish(args.project, args.proposal, os.environ["REVIEW_DIGEST"], os.environ["CONFIRMATION"], GitHub(os.environ["GH_TOKEN"]), context)))
            return 0
        report, raw = inspect_candidate(args.project, args.source, args.candidate, args.migrations)
        if args.sql and not report["reasons"]:
            from verify_schema_upgrade_sql import verify_sql
            catalog = verify_sql(args.project, report, raw)
            proposal(args.project, report, raw, catalog, args.proposal)
        write_report(args.output, report)
        print(report["status"] + ": " + report["nextAction"])
        if os.environ.get("GITHUB_OUTPUT"):
            with open(os.environ["GITHUB_OUTPUT"], "a") as output:
                output.write("ready=" + str(report.get("upgradePathVerified", False)).lower() + "\n")
                output.write("digest=" + report.get("reviewDigest", "") + "\n")
        return 0 if not report["reasons"] else 2
    except (refresh.Refusal, OSError, ValueError, KeyError, subprocess.SubprocessError, zipfile.BadZipFile) as ex:
        code = str(ex) if isinstance(ex, refresh.Refusal) else "UPGRADE_VERIFICATION_FAILED_SEE_PRIVATE_DIAGNOSTICS"
        if args.output:
            write_report(args.output, {"status": "COMPATIBILITY_FAILED", "reasons": [code],
                "deploymentReady": False, "nextAction": "Resolve the reported blocker; no approval files were changed."})
        print("Schema upgrade refused: " + code)
        return 1


if __name__ == "__main__": raise SystemExit(main())
