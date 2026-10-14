from pathlib import Path
import importlib.util
import json
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import uuid
import zipfile

spec = importlib.util.spec_from_file_location("lifecycle_runner", Path(__file__).with_name("run.py"))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class SafetyTests(unittest.TestCase):
    def test_approved_schema_only_model_and_modules(self):
        result = runner.verify_baseline()
        self.assertEqual(64, len(result["sourceModelSha256"]))

    def test_baseline_has_no_exported_data_or_script_relationship(self):
        with zipfile.ZipFile(runner.PROJECT / "Baseline/schema.dacpac") as package:
            self.assertFalse(any("Data/" in name or name.upper().endswith(".BCP") for name in package.namelist()))
            self.assertNotIn(b"Target=", package.read("_rels/.rels"))

    def test_cleanup_refuses_unowned_container(self):
        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder) / "owner.json"
            state.write_text(json.dumps({"owner": str(uuid.uuid4()), "container": "fmsc-level1-" + "a" * 32}))
            responses = [subprocess.CompletedProcess([], 0, "owned-name\n", ""), subprocess.CompletedProcess([], 0, str(uuid.uuid4()), "")]
            with patch.object(runner.subprocess, "run", side_effect=responses) as mocked:
                with self.assertRaisesRegex(ValueError, "ownership marker"):
                    runner.remove_owned(state)
                self.assertFalse(any("rm" in call.args[0] for call in mocked.call_args_list))

    def test_cleanup_refuses_school_or_arbitrary_container_name(self):
        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder) / "owner.json"
            state.write_text(json.dumps({"owner": str(uuid.uuid4()), "container": "FMSC_DB"}))
            with patch.object(runner.subprocess, "run") as mocked:
                with self.assertRaisesRegex(ValueError, "invalid container name"):
                    runner.remove_owned(state)
                mocked.assert_not_called()

    def test_cleanup_of_owned_container_is_confirmed_and_idempotent(self):
        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder) / "owner.json"; token = str(uuid.uuid4())
            state.write_text(json.dumps({"owner": token, "container": "fmsc-level1-" + "b" * 32}))
            responses = [subprocess.CompletedProcess([], 0, "id\n", ""), subprocess.CompletedProcess([], 0, token, ""),
                         subprocess.CompletedProcess([], 0, "removed\n", ""), subprocess.CompletedProcess([], 0, "", "")]
            with patch.object(runner.subprocess, "run", side_effect=responses) as mocked:
                runner.remove_owned(state); runner.remove_owned(state)
                self.assertFalse(state.exists()); self.assertEqual(4, mocked.call_count)

    def test_failure_evidence_is_not_a_pass_and_contains_no_payload(self):
        with tempfile.TemporaryDirectory() as folder:
            report = {"commit": "a" * 40, "runId": str(uuid.uuid4()), "result": "FAIL", "limitations": ["No UI acceptance"],
                      "scenarios": [{"name": "C: persisted payment", "result": "FAIL", "assertions": 2, "seconds": 1.0,
                                     "assertion": "Payment allocation does not reconcile.", "category": "Production assertion"}]}
            runner.write_evidence(Path(folder), report)
            text = (Path(folder) / "summary.md").read_text()
            self.assertIn("FAIL: 1", text); self.assertIn("Payment allocation does not reconcile.", text)
            self.assertNotIn("ConnectionString", text); self.assertNotIn("Password", text)
            runner.validate_evidence(Path(folder))

    def test_evidence_refuses_student_payload_and_extra_files(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            report = {"commit": "a" * 40, "runId": str(uuid.uuid4()), "result": "PASS", "limitations": [],
                      "scenarios": [{"name": "Ayaan Example", "result": "PASS", "assertions": 1, "seconds": 0}]}
            runner.write_evidence(root, report)
            with self.assertRaisesRegex(ValueError, "student payload"):
                runner.validate_evidence(root)
            report["scenarios"][0]["name"] = "Admission"
            runner.write_evidence(root, report); (root / "database.bak").write_text("Synthetic forbidden file")
            with self.assertRaisesRegex(ValueError, "exactly the two"):
                runner.validate_evidence(root)


if __name__ == "__main__":
    unittest.main()
