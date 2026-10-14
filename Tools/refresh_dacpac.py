"""Compare a refresh with an anchored Git approval. Never approve model/data drift.

Only reports and explicitly verified approval records are written. No SQL is run,
no archive is extracted, and no exported row or SQL body enters the report.
"""
import argparse
import collections
import copy
import datetime
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import uuid
import xml.etree.ElementTree as ET
import zipfile

from verify_schema_artifact import inspect, verify_contract

ROOT = Path(__file__).resolve().parents[1]
APPROVAL = "Core/ApprovedSchemaArtifact.json"
CONTRACT = "Core/DatabaseCompatibilityRequirements.json"
PROVENANCE = "Tests/LifecycleSqlChecks/Baseline/provenance.json"
FIXTURE = "Tests/LifecycleSqlChecks/Baseline/schema.dacpac"
SYNC_PATHS = (APPROVAL, CONTRACT, PROVENANCE, FIXTURE)
FIXTURE_ENTRIES = {"model.xml", "Origin.xml", "DacMetadata.xml", "[Content_Types].xml", "_rels/.rels"}


class Refusal(Exception):
    """A fixed diagnostic code, never a parser exception containing private text."""


def sha(data):
    return hashlib.sha256(data).hexdigest()


def git(root, *args):
    return subprocess.check_output(["git", "-C", str(root), *args], stderr=subprocess.DEVNULL, timeout=60)


def git_blob(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()


def read_json(data):
    def pairs(items):
        value = {}
        for key, item in items:
            if key in value:
                raise Refusal("DUPLICATE_JSON_KEY")
            value[key] = item
        return value
    return json.loads(data, object_pairs_hook=pairs)


def xml(data):
    declarations = data.replace(b"\x00", b"").upper()
    if b"<!DOCTYPE" in declarations or b"<!ENTITY" in declarations:
        raise Refusal("UNSUPPORTED_XML_DECLARATION")
    return ET.fromstring(data)


def canonical(node, relationship_ids=False, volatile_origin=False, path=""):
    path += "/" + node.tag.split("}")[-1]
    attrs = dict(node.attrib)
    if relationship_ids:
        attrs.pop("Id", None)
    text = node.text if node.text and node.text.strip() else None
    if volatile_origin and path in ("/DacOrigin/Operation/Identity", "/DacOrigin/Operation/Start"):
        text = "<export-metadata>"
    children = [canonical(c, relationship_ids, volatile_origin, path) for c in node]
    if relationship_ids:
        children.sort(key=repr)
    return (node.tag, tuple(sorted(attrs.items())), text, tuple(children))


def archive(data):
    if len(data) > 128 * 1024 * 1024:
        raise Refusal("PACKAGE_SIZE_LIMIT")
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        entries = z.infolist()
        names = [e.filename for e in entries]
        if len(entries) > 4096 or len(names) != len(set(names)):
            raise Refusal("DUPLICATE_OR_EXCESSIVE_ZIP_ENTRIES")
        if sum(e.file_size for e in entries) > 512 * 1024 * 1024:
            raise Refusal("UNCOMPRESSED_SIZE_LIMIT")
        for e in entries:
            parts = e.filename.split("/")
            if not e.filename or e.filename.startswith("/") or "\\" in e.filename or ":" in e.filename or ".." in parts or e.flag_bits & 1:
                raise Refusal("UNSAFE_ZIP_ENTRY")
            if e.file_size > 128 * 1024 * 1024 or e.file_size > max(1, e.compress_size) * 2000:
                raise Refusal("ZIP_EXPANSION_LIMIT")
        if not FIXTURE_ENTRIES.issubset(names):
            raise Refusal("MISSING_PACKAGE_METADATA")
        # Reading every entry checks CRC, including exported data. Keep it private.
        contents = {name: z.read(name) for name in names}
    origin = xml(contents["Origin.xml"])
    for e in origin:
        if e.tag.split("}")[-1] == "Operation":
            for item in e:
                if item.tag.split("}")[-1] == "Identity": uuid.UUID(item.text or "")
                if item.tag.split("}")[-1] == "Start": datetime.datetime.fromisoformat((item.text or "").replace("Z", "+00:00"))
    checks = [e for e in origin.iter() if e.tag.split("}")[-1] == "Checksum"]
    model_checks = [e for e in checks if e.get("Uri") == "/model.xml"]
    if len(model_checks) != 1 or (model_checks[0].text or "").lower() != sha(contents["model.xml"]):
        raise Refusal("MODEL_CHECKSUM_MISMATCH")
    for e in checks:
        name = (e.get("Uri") or "").lstrip("/")
        if name not in contents or (e.text or "").lower() != sha(contents[name]):
            raise Refusal("INTERNAL_CHECKSUM_MISMATCH")
    xml(contents["model.xml"])
    relationships = xml(contents["_rels/.rels"])
    ids = [e.get("Id") for e in relationships]
    if len(ids) != len(set(ids)) or any(not value for value in ids):
        raise Refusal("INVALID_RELATIONSHIP_ID")
    for e in relationships:
        target = (e.get("Target") or "").lstrip("/")
        if e.get("TargetMode", "Internal") != "Internal" or target not in contents:
            raise Refusal("EXTERNAL_OR_MISSING_RELATIONSHIP_TARGET")
    xml(contents["DacMetadata.xml"])
    xml(contents["[Content_Types].xml"])
    return contents


def fingerprint(contents, data):
    # Reuse the production module fingerprint algorithm without reading data rows.
    with tempfile.TemporaryDirectory(prefix="fmsc-refresh-model-") as folder:
        path = Path(folder) / "model.dacpac"
        with zipfile.ZipFile(path, "w") as z:
            z.writestr("model.xml", contents["model.xml"])
            z.writestr("Origin.xml", contents["Origin.xml"])
        root, ns, elements, result = inspect(path)
    result["packageSha256"] = sha(data)
    return root, ns, elements, result


def object_inventory(contents):
    root = xml(contents["model.xml"])
    ns = {"d": root.tag.split("}")[0][1:]}
    result = {}
    for i, node in enumerate(root.findall("d:Model/d:Element", ns)):
        kind = node.get("Type", "Unknown")
        name = node.get("Name")
        # Anonymous definitions are still checked; the full model is byte-strict.
        key = (kind, name or "anonymous:" + str(i))
        result[key] = sha(ET.tostring(node, encoding="utf-8"))
    return result


def model_differences(old, new):
    a, b = object_inventory(old), object_inventory(new)
    approved_kinds = {kind for kind, _ in a}
    rows = []
    for key in sorted(set(a) | set(b)):
        if a.get(key) == b.get(key):
            continue
        kind, name = key
        if kind not in approved_kinds or not re.fullmatch(r"Sql[A-Za-z]{1,60}", kind): kind = "unknown-kind:" + sha(kind.encode())[:16]
        safe_name = name if key in a and re.fullmatch(r"[A-Za-z0-9_.$@\[\] :\-]{1,160}", name) else "object:" + sha(name.encode())[:16]
        rows.append({"kind": kind, "object": safe_name, "change": "added" if key not in a else "removed" if key not in b else "modified",
                     "beforeSha256": a.get(key), "afterSha256": b.get(key)})
    return rows


def fixture_valid(data, provenance, approved):
    if sha(data) != provenance["fixtureSha256"]:
        raise Refusal("INCORRECT_FIXTURE_PROVENANCE")
    contents = archive(data)
    if set(contents) != FIXTURE_ENTRIES:
        raise Refusal("EXPORTED_DATA_OR_SCRIPT_IN_FIXTURE")
    origin = xml(contents["Origin.xml"])
    for e in origin.iter():
        if e.tag.split("}")[-1] in ("ExportStatistics", "Server", "DataPhaseTables") or e.tag.endswith("ContainsExportedData") and e.text != "false":
            raise Refusal("SENSITIVE_FIXTURE_METADATA")
    _, _, _, actual = fingerprint(contents, data)
    if actual["modelSha256"] != approved["modelSha256"] or actual["modules"] != approved["modules"]:
        raise Refusal("INCORRECT_FIXTURE_MODEL")
    return contents


def required_version(data):
    found = re.search(rb'RequiredDatabaseSchemaVersion\s*=\s*"(\d+)"', data)
    if not found:
        raise Refusal("MISSING_REQUIRED_SCHEMA_VERSION")
    return found.group(1).decode()


def verify(project, anchor):
    report = {"result": "REVIEW_REQUIRED", "approvalCommit": anchor, "policy": "exact-model-and-data-v1",
              "differences": [], "reasons": [], "changedApprovalFiles": [], "fixtureRegenerated": False}
    try:
        if not re.fullmatch(r"[a-f0-9]{40}", anchor):
            raise Refusal("FULL_APPROVAL_COMMIT_REQUIRED")
        def at(path):
            return git(project, "show", anchor + ":" + path)
        approved = read_json(at(APPROVAL))
        contract = read_json(at(CONTRACT))
        provenance = read_json(at(PROVENANCE))
        if not re.fullmatch(r"[a-f0-9]{40}", provenance["sourceGitBlob"]):
            raise Refusal("INCORRECT_BASELINE_PROVENANCE")
        reachable = git(project, "rev-list", "--objects", anchor, "--", "FMSC.dacpac").decode().splitlines()
        if not any(line.split()[0] == provenance["sourceGitBlob"] for line in reachable):
            raise Refusal("APPROVED_BLOB_NOT_IN_TRUSTED_HISTORY")
        previous = git(project, "cat-file", "blob", provenance["sourceGitBlob"])
        old = archive(previous)
        old_root, old_ns, old_elements, old_print = fingerprint(old, previous)
        if old_print != approved or provenance["sourcePackageSha256"] != approved["packageSha256"] or provenance["sourceModelSha256"] != approved["modelSha256"]:
            raise Refusal("INCORRECT_BASELINE_PROVENANCE")
        if verify_contract(old_root, old_ns, old_elements, contract, old_print):
            raise Refusal("APPROVED_COMPATIBILITY_CONTRACT_INVALID")
        old_fixture = at(FIXTURE)
        fixture_valid(old_fixture, provenance, approved)
        current_path = project / "FMSC.dacpac"
        if current_path.is_symlink():
            raise Refusal("SYMLINK_CANDIDATE_REFUSED")
        candidate = current_path.read_bytes()
        new = archive(candidate)
        root, ns, elements, actual = fingerprint(new, candidate)
        report.update(previousPackageSha256=approved["packageSha256"], candidatePackageSha256=actual["packageSha256"],
                      sourceGitBlob=git_blob(candidate), modelSha256=actual["modelSha256"], moduleCount=len(actual["modules"]),
                      requiredSchemaVersion=required_version(at("DatabaseHelper.cs")), exportedEntryCount=sum(n.startswith("Data/") for n in old))
        if report["requiredSchemaVersion"] != required_version((project / "DatabaseHelper.cs").read_bytes()):
            raise Refusal("REQUIRED_SCHEMA_VERSION_CHANGED")
        if read_json(at("Core/DatabaseMigrationRequirements.json")) != read_json((project / "Core/DatabaseMigrationRequirements.json").read_bytes()):
            raise Refusal("RECOVERY_COMPATIBILITY_REQUIREMENTS_CHANGED")
        for name in sorted(set(old) | set(new)):
            if name in ("Origin.xml", "_rels/.rels", "DacMetadata.xml", "[Content_Types].xml"):
                continue
            if old.get(name) == new.get(name):
                continue
            label = "model.xml" if name == "model.xml" else "entry:" + sha(name.encode())[:16]
            report["differences"].append({"entry": label, "category": "exported-data" if name.startswith("Data/") else "schema-model" if name == "model.xml" else "other-payload",
                "change": "added" if name not in old else "removed" if name not in new else "modified",
                "beforeSha256": sha(old[name]) if name in old else None, "afterSha256": sha(new[name]) if name in new else None})
        if old["model.xml"] != new["model.xml"]:
            report["modelObjects"] = model_differences(old, new)
            report["reasons"].append("FULL_SCHEMA_MODEL_CHANGED")
        if actual["modules"] != approved["modules"]:
            report["reasons"].append("SQL_MODULE_FINGERPRINTS_CHANGED")
        if report["differences"]:
            report["reasons"].append("PAYLOAD_CHANGED")
        for name in ("Origin.xml", "DacMetadata.xml", "[Content_Types].xml", "_rels/.rels"):
            a, b = xml(old[name]), xml(new[name])
            if canonical(a, name == "_rels/.rels", name == "Origin.xml") != canonical(b, name == "_rels/.rels", name == "Origin.xml"):
                report["reasons"].append("UNREVIEWED_METADATA_CHANGE:" + name)
        current_contract = copy.deepcopy(contract)
        current_contract["schemaArtifact"]["sha256"] = actual["packageSha256"]
        if verify_contract(root, ns, elements, current_contract, actual):
            report["reasons"].append("PRODUCTION_COMPATIBILITY_CONTRACT_FAILED")
        if report["reasons"]:
            return report, {}
        candidate_fixture = (project / FIXTURE).read_bytes()
        candidate_provenance = read_json((project / PROVENANCE).read_bytes())
        fixture = fixture_valid(candidate_fixture, candidate_provenance, approved)
        # Regenerate only when retained schema-package metadata actually changes.
        regenerate = any(fixture[n] != new[n] for n in ("DacMetadata.xml", "[Content_Types].xml"))
        new_fixture = candidate_fixture
        if regenerate:
            spec = importlib.util.spec_from_file_location("refresh_schema_only", ROOT / "Tests/LifecycleSqlChecks/run.py")
            harness = importlib.util.module_from_spec(spec); spec.loader.exec_module(harness)
            with tempfile.TemporaryDirectory(prefix="fmsc-refresh-derived-") as directory:
                staging = Path(directory); (staging / "Core").mkdir()
                (staging / APPROVAL).write_text(json.dumps(actual))
                (staging / "Tools").mkdir()
                (staging / "Tools/verify_schema_artifact.py").write_bytes((ROOT / "Tools/verify_schema_artifact.py").read_bytes())
                harness.ROOT = staging
                destination = staging / "schema.dacpac"
                harness.schema_only(current_path, destination)
                new_fixture = destination.read_bytes()
        new_provenance = {"sourcePackageSha256": actual["packageSha256"], "sourceModelSha256": actual["modelSha256"],
                          "sourceGitBlob": git_blob(candidate), "fixtureSha256": sha(new_fixture)}
        # Do not repair altered contracts or invented approval/provenance by replacing them.
        if read_json((project / APPROVAL).read_bytes()) not in (approved, actual):
            raise Refusal("INCORRECT_CANDIDATE_APPROVAL")
        if read_json((project / CONTRACT).read_bytes()) not in (contract, current_contract):
            raise Refusal("CANDIDATE_COMPATIBILITY_CONTRACT_CHANGED")
        if candidate_provenance not in (provenance, new_provenance):
            raise Refusal("INCORRECT_CANDIDATE_PROVENANCE")
        fixture_valid(new_fixture, new_provenance, actual)
        payloads = {APPROVAL: actual, CONTRACT: current_contract, PROVENANCE: new_provenance}
        updates = {}
        for name, value in payloads.items():
            if read_json((project / name).read_bytes()) != value:
                # Preserve the compact production manifest's serialization convention.
                updates[name] = (json.dumps(value, separators=(",", ":")) if name == CONTRACT else json.dumps(value, indent=2)).encode() + b"\n"
        if regenerate:
            updates[FIXTURE] = new_fixture
        report.update(result="SAFE_METADATA_ONLY" if updates else "ALIGNED", changedApprovalFiles=sorted(updates), fixtureRegenerated=regenerate,
                      modelAndDataUnchanged=True, compatibilityVerified=True)
        return report, updates
    except Refusal as ex:
        report["reasons"].append(str(ex))
    except (OSError, ValueError, KeyError, TypeError, StopIteration, ET.ParseError, zipfile.BadZipFile, subprocess.SubprocessError):
        report["reasons"].append("INVALID_PACKAGE_OR_PROVENANCE")
    return report, {}


def synchronize(project, updates):
    if not set(updates).issubset(SYNC_PATHS):
        raise Refusal("UNEXPECTED_SYNCHRONIZATION_PATH")
    original = {name: (project / name).read_bytes() for name in updates}
    try:
        for name, data in updates.items():
            path = project / name
            if path.is_symlink():
                raise Refusal("SYMLINK_APPROVAL_REFUSED")
            with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as stream:
                temporary = Path(stream.name)
                stream.write(data); stream.flush(); os.fsync(stream.fileno())
            try: os.replace(temporary, path)
            finally: temporary.unlink(missing_ok=True)
    except Exception:
        for name, data in original.items(): (project / name).write_bytes(data)
        raise


def write_report(folder, report):
    folder.mkdir(parents=True, exist_ok=True)
    (folder / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    lines = ["# FMSC DACPAC refresh verification", "", "Result: **" + report["result"] + "**", "",
        "Policy: exact full model, all SQL module fingerprints and all exported payload bytes must match the anchored approval.",
        "No SQL bodies, row values, database archives or connection metadata are included.", ""]
    for key in ("approvalCommit", "previousPackageSha256", "candidatePackageSha256", "modelSha256", "requiredSchemaVersion", "moduleCount", "exportedEntryCount"):
        if key in report: lines.append(f"- {key}: `{report[key]}`")
    lines += ["", "Reasons: " + (", ".join(report["reasons"]) or "none"), "",
              "Synchronization files: " + (", ".join(report["changedApprovalFiles"]) or "none"),
              "Schema-only fixture regenerated: " + str(report["fixtureRegenerated"]), "",
              "| Category | Object / entry | Change |", "|---|---|---|"]
    for row in report.get("differences", []) + report.get("modelObjects", []):
        lines.append(f"| {row.get('category',row.get('kind'))} | {row.get('entry',row.get('object'))} | {row['change']} |")
    lines += ["", "Exact-MDF/LDF compatibility and live data outcomes remain outside this offline check. Existing isolated SQL/Windows/security checks must pass on the proposed revision."]
    (folder / "summary.md").write_text("\n".join(lines) + "\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, default=ROOT)
    parser.add_argument("--approval-ref", required=True, help="Full trusted default/base commit; approvals and baseline come from its Git history.")
    parser.add_argument("--report-dir", type=Path, required=True)
    parser.add_argument("--sync", action="store_true", help="Write only alignment proved safe by this verification.")
    args = parser.parse_args()
    report, updates = verify(args.project.resolve(), args.approval_ref)
    if args.sync and report["result"] != "REVIEW_REQUIRED":
        original = {name: (args.project / name).read_bytes() for name in updates}
        try:
            if sha((args.project / "FMSC.dacpac").read_bytes()) != report["candidatePackageSha256"]:
                raise Refusal("CANDIDATE_CHANGED_BEFORE_SYNCHRONIZATION")
            synchronize(args.project.resolve(), updates)
            confirmed, _ = verify(args.project.resolve(), args.approval_ref)
            if confirmed["result"] != "ALIGNED":
                raise Refusal("SYNCHRONIZATION_POSTCHECK_FAILED")
        except Exception:
            synchronize(args.project.resolve(), original)
            raise
        report["synchronizationPostcheck"] = "PASS"
    write_report(args.report_dir, report)
    print("DACPAC refresh: " + report["result"] + "; reasons: " + (", ".join(report["reasons"]) or "none"))
    print("See sanitized report.json/summary.md; no package/data is uploaded.")
    return 1 if report["result"] == "REVIEW_REQUIRED" else 0


if __name__ == "__main__":
    raise SystemExit(main())
