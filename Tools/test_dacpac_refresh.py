"""Disposable schema-only/synthetic packages; never reproduce or print school rows."""
import copy
import hashlib
import io
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zipfile
import warnings

import refresh_dacpac as refresh
import publish_dacpac_refresh as publisher


class RefreshChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.store = tempfile.TemporaryDirectory(prefix="fmsc-refresh-tests-")
        cls.seed = Path(cls.store.name) / "seed"; cls.seed.mkdir()
        with zipfile.ZipFile(refresh.ROOT / "Tests/LifecycleSqlChecks/Baseline/schema.dacpac") as fixture:
            cls.parts = {n: fixture.read(n) for n in fixture.namelist()}
        ns = "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02"
        origin = ET.Element("{" + ns + "}DacOrigin")
        properties = ET.SubElement(origin, "{" + ns + "}PackageProperties")
        ET.SubElement(properties, "{" + ns + "}ContainsExportedData").text = "true"
        operation = ET.SubElement(origin, "{" + ns + "}Operation")
        ET.SubElement(operation, "{" + ns + "}Identity").text = "11111111-1111-4111-8111-111111111111"
        ET.SubElement(operation, "{" + ns + "}Start").text = "2026-01-01T00:00:00Z"
        checks = ET.SubElement(origin, "{" + ns + "}Checksums")
        ET.SubElement(checks, "{" + ns + "}Checksum", Uri="/model.xml").text = refresh.sha(cls.parts["model.xml"])
        cls.parts["Origin.xml"] = ET.tostring(origin)
        cls.parts["Data/dbo.SyntheticFixture/rows.BCP"] = b"fictional-fixture-only\n"
        cls.old = cls.package(cls.parts)
        old_contents = refresh.archive(cls.old)
        _, _, _, cls.approval = refresh.fingerprint(old_contents, cls.old)
        cls.contract = json.loads((refresh.ROOT / refresh.CONTRACT).read_text())
        cls.contract["schemaArtifact"]["sha256"] = cls.approval["packageSha256"]
        for name, data in {refresh.APPROVAL: json.dumps(cls.approval).encode(), refresh.CONTRACT: json.dumps(cls.contract).encode(),
                          "DatabaseHelper.cs": b'const string RequiredDatabaseSchemaVersion = "6";',
                          "Core/DatabaseMigrationRequirements.json": (refresh.ROOT / "Core/DatabaseMigrationRequirements.json").read_bytes(),
                          "FMSC.dacpac": cls.old}.items():
            p = cls.seed / name; p.parent.mkdir(parents=True, exist_ok=True); p.write_bytes(data)
        # Strip exported data from the schema-only fixture using the actual harness.
        spec = refresh.importlib.util.spec_from_file_location("test_schema_derivation", refresh.ROOT / "Tests/LifecycleSqlChecks/run.py")
        harness = refresh.importlib.util.module_from_spec(spec); spec.loader.exec_module(harness); harness.ROOT = cls.seed
        (cls.seed / "Tools").mkdir()
        shutil.copyfile(refresh.ROOT / "Tools/verify_schema_artifact.py", cls.seed / "Tools/verify_schema_artifact.py")
        f = cls.seed / refresh.FIXTURE; f.parent.mkdir(parents=True, exist_ok=True)
        harness.schema_only(cls.seed / "FMSC.dacpac", f)
        cls.provenance = {"sourcePackageSha256": cls.approval["packageSha256"], "sourceModelSha256": cls.approval["modelSha256"],
                          "sourceGitBlob": refresh.git_blob(cls.old), "fixtureSha256": refresh.sha(f.read_bytes())}
        (cls.seed / refresh.PROVENANCE).write_text(json.dumps(cls.provenance))
        def git(*args):
            return subprocess.check_output(["git", "-C", str(cls.seed), *args], stderr=subprocess.DEVNULL).decode().strip()
        git("init", "-b", "main"); git("config", "user.name", "Synthetic Fixture"); git("config", "user.email", "fixture@example.invalid")
        git("add", "."); git("-c", "commit.gpgsign=false", "commit", "-m", "Synthetic approved baseline")
        cls.anchor = git("rev-parse", "HEAD")

    @classmethod
    def tearDownClass(cls): cls.store.cleanup()

    @staticmethod
    def package(parts, timestamp=(2026, 1, 1, 0, 0, 0)):
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as z:
            for name, data in parts.items():
                e = zipfile.ZipInfo(name, timestamp); e.compress_type = zipfile.ZIP_DEFLATED; z.writestr(e, data)
        return buffer.getvalue()

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="fmsc-refresh-case-"); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "repo"; shutil.copytree(self.seed, self.root)

    def candidate(self, parts=None):
        value = self.package(parts or self.parts, (2026, 2, 2, 0, 0, 0)); (self.root / "FMSC.dacpac").write_bytes(value); return value

    def check(self): return refresh.verify(self.root, self.anchor)

    def refuse(self, code=None):
        before = {p: (self.root / p).read_bytes() for p in refresh.SYNC_PATHS}
        report, updates = self.check()
        self.assertEqual("REVIEW_REQUIRED", report["result"]); self.assertFalse(updates)
        if code: self.assertIn(code, report["reasons"])
        self.assertEqual(before, {p: (self.root / p).read_bytes() for p in refresh.SYNC_PATHS})
        return report

    def model_change(self, kind):
        parts = self.parts.copy(); model = ET.fromstring(parts["model.xml"])
        ns = {"d": model.tag.split("}")[0][1:]}
        node = next(e for e in model.findall("d:Model/d:Element", ns) if e.get("Type") == kind)
        if kind in ("SqlProcedure", "SqlScalarFunction", "SqlDmlTrigger"):
            body = node.find(".//d:Property[@Name='BodyScript']/d:Value", ns)
            if body is None: body = node.find(".//d:Property[@Name='Script']/d:Value", ns)
            self.assertIsNotNone(body)
            body.text = (body.text or "") + "\nSELECT 987654; -- synthetic logic drift"
        elif kind == "SqlCheckConstraint":
            node.find("d:Property[@Name='CheckExpressionScript']/d:Value", ns).text = "1=1"
        elif kind == "SqlTable":
            prop = node.find(".//d:Property[@Name='IsNullable']", ns)
            self.assertIsNotNone(prop); prop.set("Value", "False" if prop.get("Value") == "True" else "True")
        elif kind == "SqlIndex":
            prop = node.find("d:Property[@Name='IsUnique']", ns)
            self.assertIsNotNone(prop); prop.set("Value", "False" if prop.get("Value") == "True" else "True")
        elif kind == "SqlForeignKeyConstraint":
            reference = node.find("d:Relationship[@Name='ForeignTable']/d:Entry/d:References", ns)
            self.assertIsNotNone(reference); reference.set("Name", "[dbo].[SyntheticWrongTarget]")
        parts["model.xml"] = ET.tostring(model)
        origin = ET.fromstring(parts["Origin.xml"])
        next(e for e in origin.iter() if e.get("Uri") == "/model.xml").text = refresh.sha(parts["model.xml"])
        parts["Origin.xml"] = ET.tostring(origin); self.candidate(parts)

    def test_missing_optional_relationships_part_reports_model_drift(self):
        # A real extracted DACPAC may omit the optional relationships part.
        # That should not conceal changed objects or qualify for auto-approval.
        parts = self.parts.copy()
        parts.pop("_rels/.rels")
        model = ET.fromstring(parts["model.xml"])
        ns = {"d": model.tag.split("}")[0][1:]}
        node = next(e for e in model.findall("d:Model/d:Element", ns) if e.get("Type") == "SqlTable")
        prop = node.find(".//d:Property[@Name='IsNullable']", ns)
        self.assertIsNotNone(prop)
        prop.set("Value", "False" if prop.get("Value") == "True" else "True")
        parts["model.xml"] = ET.tostring(model)
        origin = ET.fromstring(parts["Origin.xml"])
        next(e for e in origin.iter() if e.get("Uri") == "/model.xml").text = refresh.sha(parts["model.xml"])
        parts["Origin.xml"] = ET.tostring(origin)
        self.candidate(parts)
        report = self.refuse("CANDIDATE_RELATIONSHIPS_PART_ABSENT_REVIEW_REQUIRED")
        self.assertIn("FULL_SCHEMA_MODEL_CHANGED", report["reasons"])
        self.assertTrue(report["modelObjects"])

    def test_missing_relationships_with_unchanged_model_never_auto_approves(self):
        parts = self.parts.copy()
        parts.pop("_rels/.rels")
        self.candidate(parts)
        report = self.refuse("CANDIDATE_RELATIONSHIPS_PART_ABSENT_REVIEW_REQUIRED")
        self.assertNotIn("FULL_SCHEMA_MODEL_CHANGED", report["reasons"])

    def test_metadata_refresh_sync_and_idempotence(self):
        self.candidate(); report, updates = self.check()
        self.assertEqual("SAFE_METADATA_ONLY", report["result"])
        self.assertEqual({refresh.APPROVAL, refresh.CONTRACT, refresh.PROVENANCE}, set(updates))
        before = (self.root / refresh.FIXTURE).read_bytes(); refresh.synchronize(self.root, updates)
        again, pending = self.check(); self.assertEqual("ALIGNED", again["result"]); self.assertFalse(pending)
        self.assertEqual(before, (self.root / refresh.FIXTURE).read_bytes())

    def test_export_identity_and_start_are_safe(self):
        parts = self.parts.copy(); origin = ET.fromstring(parts["Origin.xml"])
        next(e for e in origin.iter() if e.tag.endswith("Identity")).text = "22222222-2222-4222-8222-222222222222"
        next(e for e in origin.iter() if e.tag.endswith("Start")).text = "2026-02-02T00:00:00Z"
        parts["Origin.xml"] = ET.tostring(origin); self.candidate(parts)
        self.assertEqual("SAFE_METADATA_ONLY", self.check()[0]["result"])

    def test_relationship_ids_order_only(self):
        parts = self.parts.copy(); rel = ET.fromstring(parts["_rels/.rels"])
        ET.SubElement(rel, "{" + rel.tag.split("}")[0][1:] + "}Relationship", Id="old", Type="fixture-internal", Target="model.xml")
        # Establish this relationship as part of a genuinely approved synthetic baseline.
        parts["_rels/.rels"] = ET.tostring(rel); baseline = self.package(parts)
        with tempfile.TemporaryDirectory() as folder:
            p = Path(folder) / "old"; p.write_bytes(baseline)
            a = refresh.archive(p.read_bytes())
        rel[0].set("Id", "new"); parts["_rels/.rels"] = ET.tostring(rel)
        b = refresh.archive(self.package(parts))
        self.assertEqual(refresh.canonical(ET.fromstring(a["_rels/.rels"]), True), refresh.canonical(ET.fromstring(b["_rels/.rels"]), True))

    def test_changed_sql_procedure(self): self.model_change("SqlProcedure"); self.refuse("SQL_MODULE_FINGERPRINTS_CHANGED")
    def test_changed_sql_function(self): self.model_change("SqlScalarFunction"); self.refuse("SQL_MODULE_FINGERPRINTS_CHANGED")
    def test_changed_sql_trigger(self): self.model_change("SqlDmlTrigger"); self.refuse("SQL_MODULE_FINGERPRINTS_CHANGED")
    def test_changed_constraint(self): self.model_change("SqlCheckConstraint"); self.refuse("FULL_SCHEMA_MODEL_CHANGED")
    def test_changed_table_column(self):
        self.model_change("SqlTable"); report = self.refuse("FULL_SCHEMA_MODEL_CHANGED")
        self.assertTrue(any(row["kind"] == "SqlSimpleColumn" and "IsNullable" in row["changedFields"] for row in report["modelObjects"]))
    def test_changed_index(self): self.model_change("SqlIndex"); self.refuse("FULL_SCHEMA_MODEL_CHANGED")
    def test_changed_relationship(self): self.model_change("SqlForeignKeyConstraint"); self.refuse("FULL_SCHEMA_MODEL_CHANGED")

    def test_changed_exported_data(self):
        parts = self.parts.copy(); parts["Data/dbo.SyntheticFixture/rows.BCP"] += b"new fictional row"
        self.candidate(parts); self.refuse("PAYLOAD_CHANGED")

    def test_new_exported_data(self):
        parts = self.parts.copy(); parts["Data/dbo.NewFixture/rows.BCP"] = b"new fictional fixture"; self.candidate(parts); self.refuse("PAYLOAD_CHANGED")

    def test_removed_exported_data(self):
        parts = self.parts.copy(); del parts["Data/dbo.SyntheticFixture/rows.BCP"]; self.candidate(parts); self.refuse("PAYLOAD_CHANGED")

    def test_added_deployment_script(self):
        parts = self.parts.copy(); parts["postdeploy.sql"] = b"SELECT 1;"; self.candidate(parts); self.refuse("PAYLOAD_CHANGED")

    def test_internal_model_checksum(self):
        parts = self.parts.copy(); parts["model.xml"] += b" "; self.candidate(parts); self.refuse("MODEL_CHECKSUM_MISMATCH")

    def test_corrupted_zip(self): (self.root / "FMSC.dacpac").write_bytes(b"invalid zip"); self.refuse("INVALID_PACKAGE_OR_PROVENANCE")

    def test_hidden_zip_comment(self):
        self.candidate()
        with zipfile.ZipFile(self.root / "FMSC.dacpac", "a") as z: z.comment = b"fictional extra payload"
        self.refuse("UNSUPPORTED_ZIP_METADATA_OR_HIDDEN_PAYLOAD")

    def test_hidden_trailing_payload(self):
        data = self.candidate(); (self.root / "FMSC.dacpac").write_bytes(data + b"fictional trailing payload")
        self.refuse("UNSUPPORTED_ZIP_METADATA_OR_HIDDEN_PAYLOAD")

    def test_hidden_entry_extra(self):
        data = io.BytesIO()
        with zipfile.ZipFile(data, "w") as z:
            for name, payload in self.parts.items():
                entry = zipfile.ZipInfo(name); entry.extra = b"\xff\xff\x04\x00demo"; z.writestr(entry, payload)
        (self.root / "FMSC.dacpac").write_bytes(data.getvalue()); self.refuse("UNSUPPORTED_ZIP_METADATA_OR_HIDDEN_PAYLOAD")

    def test_duplicate_entry(self):
        self.candidate()
        with warnings.catch_warnings(), zipfile.ZipFile(self.root / "FMSC.dacpac", "a") as z:
            warnings.simplefilter("ignore", UserWarning); z.writestr("model.xml", self.parts["model.xml"])
        self.refuse("DUPLICATE_OR_EXCESSIVE_ZIP_ENTRIES")

    def test_external_relationship(self):
        parts = self.parts.copy(); rel = ET.fromstring(parts["_rels/.rels"])
        ET.SubElement(rel, "Relationship", Id="external", Target="https://example.invalid/private", TargetMode="External")
        parts["_rels/.rels"] = ET.tostring(rel); self.candidate(parts); self.refuse("EXTERNAL_OR_MISSING_RELATIONSHIP_TARGET")

    def test_path_traversal(self):
        parts = self.parts.copy(); parts["../outside"] = b"synthetic"; self.candidate(parts); self.refuse("UNSAFE_ZIP_ENTRY")

    def test_incorrect_candidate_provenance(self):
        self.candidate(); value = copy.deepcopy(self.provenance); value["sourceGitBlob"] = "0" * 40
        (self.root / refresh.PROVENANCE).write_text(json.dumps(value)); self.refuse("INCORRECT_CANDIDATE_PROVENANCE")

    def test_incorrect_fixture_provenance(self):
        self.candidate(); value = copy.deepcopy(self.provenance); value["fixtureSha256"] = "0" * 64
        (self.root / refresh.PROVENANCE).write_text(json.dumps(value)); self.refuse("INCORRECT_FIXTURE_PROVENANCE")

    def test_incorrect_candidate_approval(self):
        self.candidate(); value = copy.deepcopy(self.approval); value["modelSha256"] = "0" * 64
        (self.root / refresh.APPROVAL).write_text(json.dumps(value)); self.refuse("INCORRECT_CANDIDATE_APPROVAL")

    def test_incorrect_anchored_provenance_stops(self):
        value = copy.deepcopy(self.provenance); value["sourcePackageSha256"] = "0" * 64
        (self.root / refresh.PROVENANCE).write_text(json.dumps(value))
        subprocess.run(["git", "-C", str(self.root), "add", refresh.PROVENANCE], check=True, capture_output=True)
        subprocess.run(["git", "-C", str(self.root), "-c", "commit.gpgsign=false", "commit", "-m", "Synthetic incorrect anchor"], check=True, capture_output=True)
        self.anchor = refresh.git(self.root, "rev-parse", "HEAD").decode().strip()
        self.candidate(); self.refuse("INCORRECT_BASELINE_PROVENANCE")

    def test_candidate_symlink_refused(self):
        source = self.root / "FMSC.dacpac"; source.rename(self.root / "other.dacpac"); source.symlink_to("other.dacpac")
        self.refuse("SYMLINK_CANDIDATE_REFUSED")

    def test_changed_production_contract(self):
        self.candidate(); value = copy.deepcopy(self.contract); value["objects"] = []
        (self.root / refresh.CONTRACT).write_text(json.dumps(value)); self.refuse("CANDIDATE_COMPATIBILITY_CONTRACT_CHANGED")

    def test_version_change(self):
        self.candidate(); (self.root / "DatabaseHelper.cs").write_text('const string RequiredDatabaseSchemaVersion = "7";')
        self.refuse("REQUIRED_SCHEMA_VERSION_CHANGED")

    def test_unreviewed_origin_metadata(self):
        parts = self.parts.copy(); origin = ET.fromstring(parts["Origin.xml"]); ET.SubElement(origin, "UnknownMetadata").text = "synthetic"
        parts["Origin.xml"] = ET.tostring(origin); self.candidate(parts); self.refuse()

    def test_metadata_serialization_regenerates_fixture_only_when_needed(self):
        parts = self.parts.copy(); parts["DacMetadata.xml"] += b"\n"; self.candidate(parts)
        report, updates = self.check(); self.assertEqual("SAFE_METADATA_ONLY", report["result"]); self.assertTrue(report["fixtureRegenerated"])
        self.assertIn(refresh.FIXTURE, updates); first = updates[refresh.FIXTURE]
        self.assertEqual(first, self.check()[1][refresh.FIXTURE])
        refresh.synchronize(self.root, updates); self.assertEqual("ALIGNED", self.check()[0]["result"])

    def test_sanitized_difference_report(self):
        parts = self.parts.copy(); parts["Data/dbo.SyntheticFixture/rows.BCP"] = b"FICTIONAL_PRIVATE_ROW_VALUE_DO_NOT_REPORT"
        self.candidate(parts); report = self.refuse(); folder = self.root / "evidence"; refresh.write_report(folder, report)
        content = "".join(p.read_text() for p in folder.iterdir()); self.assertNotIn("FICTIONAL_PRIVATE_ROW_VALUE", content)
        self.assertNotIn("fictional-fixture-only", content); self.assertIn("exported-data", content)

    def test_unknown_schema_names_are_not_echoed(self):
        parts = self.parts.copy(); model = ET.fromstring(parts["model.xml"]); ns = {"d": model.tag.split("}")[0][1:]}
        node = next(e for e in model.findall("d:Model/d:Element", ns) if e.get("Type") == "SqlTable")
        node.set("Name", "FICTIONAL_PRIVATE_NEW_NAME")
        parts["model.xml"] = ET.tostring(model); origin = ET.fromstring(parts["Origin.xml"])
        next(e for e in origin.iter() if e.get("Uri") == "/model.xml").text = refresh.sha(parts["model.xml"])
        parts["Origin.xml"] = ET.tostring(origin); self.candidate(parts); report = self.refuse()
        self.assertNotIn("FICTIONAL_PRIVATE_NEW_NAME", json.dumps(report))

    def test_unexpected_sync_paths(self):
        with self.assertRaises(refresh.Refusal): refresh.synchronize(self.root, {"DatabaseHelper.cs": b"unsafe"})

    def publisher_fixture(self, event="push", ref="refs/heads/main", permission="admin", private=True):
        self.candidate()
        subprocess.run(["git", "-C", str(self.root), "add", "FMSC.dacpac"], check=True, capture_output=True)
        subprocess.run(["git", "-C", str(self.root), "-c", "commit.gpgsign=false", "commit", "-m", "Synthetic metadata refresh"], check=True, capture_output=True)
        source = refresh.git(self.root, "rev-parse", "HEAD").decode().strip()
        calls = []
        def api(method, path, data=None):
            calls.append((method, path, data))
            if path == "/repos/fixture/private": return {"private": private, "default_branch": "main"}
            if path.endswith("/permission"): return {"permission": permission}
            if path.endswith("/git/ref/heads/main"): return {"object": {"sha": source}}
            if "/git/ref/heads/automation/" in path: return None
            if "/git/commits/" in path: return {"tree": {"sha": "1" * 40}}
            if path.endswith("/git/blobs") or path.endswith("/git/trees") or path.endswith("/git/commits"): return {"sha": "2" * 40}
            if "/pulls?" in path: return []
            if path.endswith("/pulls"): return {"html_url": "https://github.com/fixture/private/pull/1"}
            return {}
        def run(): return publisher.publish(self.root, source, source, "fixture/private", "maintainer", event, ref, api)
        return run, calls, api, source

    def test_publisher_creates_only_alignment_and_dispatches_checks(self):
        run, calls, _, source = self.publisher_fixture(); result = run()
        self.assertEqual("PR_PREPARED", result["result"])
        tree = next(data for method, path, data in calls if path.endswith("/git/trees"))
        self.assertEqual({refresh.APPROVAL, refresh.CONTRACT, refresh.PROVENANCE}, {e["path"] for e in tree["tree"]})
        commit = next(data for method, path, data in calls if path.endswith("/git/commits") and method == "POST")
        self.assertEqual([source], commit["parents"])
        self.assertEqual(3, sum(path.endswith("/dispatches") for _, path, _ in calls))
        self.assertFalse(any("merge" in path or "review" in path for _, path, _ in calls))

    def test_publisher_refuses_untrusted_pr_event(self):
        run, calls, _, _ = self.publisher_fixture(event="pull_request")
        with self.assertRaisesRegex(refresh.Refusal, "UNTRUSTED_EVENT"): run()
        self.assertFalse(any(method != "GET" for method, _, _ in calls))

    def test_publisher_refuses_non_default_dispatch(self):
        run, calls, _, _ = self.publisher_fixture(event="workflow_dispatch", ref="refs/heads/unreviewed")
        with self.assertRaisesRegex(refresh.Refusal, "UNTRUSTED_EVENT"): run()
        self.assertFalse(any(method != "GET" for method, _, _ in calls))

    def test_publisher_refuses_readonly_actor(self):
        run, calls, _, _ = self.publisher_fixture(permission="read")
        with self.assertRaisesRegex(refresh.Refusal, "TRUSTED_MAINTAINER"): run()
        self.assertFalse(any(method != "GET" for method, _, _ in calls))

    def test_publisher_refuses_public_repository(self):
        run, calls, _, _ = self.publisher_fixture(private=False)
        with self.assertRaisesRegex(refresh.Refusal, "UNTRUSTED_EVENT"): run()
        self.assertFalse(any(method != "GET" for method, _, _ in calls))

    def test_publisher_refuses_changed_default_before_writes(self):
        _, calls, api, source = self.publisher_fixture()
        def moved(method, path, data=None):
            if path.endswith("/git/ref/heads/main"): return {"object": {"sha": "0" * 40}}
            return api(method, path, data)
        with self.assertRaisesRegex(refresh.Refusal, "DEFAULT_BRANCH_MOVED"):
            publisher.publish(self.root, source, source, "fixture/private", "maintainer", "push", "refs/heads/main", moved)
        self.assertFalse(any(method != "GET" for method, _, _ in calls))

    def test_publisher_refuses_uncommitted_candidate(self):
        run, calls, _, _ = self.publisher_fixture(); self.candidate(self.parts | {"SyntheticEntry": b"review required"})
        with self.assertRaisesRegex(refresh.Refusal, "REFRESH_REQUIRES_REVIEW"): run()
        self.assertFalse(any(method != "GET" for method, _, _ in calls))

    def test_push_uses_previous_approval_not_self_declared_hashes(self):
        run, _, _, source = self.publisher_fixture()
        self.assertEqual(self.anchor, refresh.select_approval_anchor(self.root, source, "push", self.anchor))
        self.assertEqual(self.anchor, refresh.select_approval_anchor(self.root, source, "workflow_dispatch"))

    def test_material_data_cannot_self_approve_on_manual_dispatch(self):
        parts = self.parts.copy(); parts["Data/dbo.SyntheticFixture/rows.BCP"] += b"fictional added row"
        value = self.candidate(parts)
        _, _, _, actual = refresh.fingerprint(refresh.archive(value), value)
        contract = copy.deepcopy(self.contract); contract["schemaArtifact"]["sha256"] = actual["packageSha256"]
        provenance = self.provenance | {"sourceGitBlob": refresh.git_blob(value), "sourcePackageSha256": actual["packageSha256"]}
        for name, record in {refresh.APPROVAL: actual, refresh.CONTRACT: contract, refresh.PROVENANCE: provenance}.items():
            (self.root / name).write_text(json.dumps(record))
        subprocess.run(["git", "-C", str(self.root), "add", "."], check=True, capture_output=True)
        subprocess.run(["git", "-C", str(self.root), "-c", "commit.gpgsign=false", "commit", "-m", "Synthetic self-declared approval"], check=True, capture_output=True)
        source = refresh.git(self.root, "rev-parse", "HEAD").decode().strip()
        selected = refresh.select_approval_anchor(self.root, source, "workflow_dispatch")
        self.assertEqual(self.anchor, selected)
        report, updates = refresh.verify(self.root, selected)
        self.assertEqual("REVIEW_REQUIRED", report["result"]); self.assertFalse(updates); self.assertIn("PAYLOAD_CHANGED", report["reasons"])

    def test_invalid_push_anchor_refused(self):
        with self.assertRaisesRegex(refresh.Refusal, "PREVIOUS_MAIN_COMMIT_REQUIRED"):
            refresh.select_approval_anchor(self.root, self.anchor, "push", "0" * 40)

    def test_synchronization_rolls_back_on_write_failure(self):
        self.candidate(); _, updates = self.check()
        original = {p: (self.root / p).read_bytes() for p in updates}
        replace = refresh.os.replace; count = 0
        def fail_second(source, destination):
            nonlocal count; count += 1
            if count == 2: raise OSError("Synthetic interruption")
            return replace(source, destination)
        with patch.object(refresh.os, "replace", fail_second):
            with self.assertRaises(OSError): refresh.synchronize(self.root, updates)
        self.assertEqual(original, {p: (self.root / p).read_bytes() for p in updates})


if __name__ == "__main__": unittest.main()
