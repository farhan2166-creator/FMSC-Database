import copy
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest

sys.path.insert(0, str(Path(__file__).parent))
from validate_report import validate
from review_layout import review

ROOT = Path(__file__).resolve().parents[2]


class Level2Safety(unittest.TestCase):
    def report(self):
        rows = json.loads(Path(__file__).with_name("scenarios.json").read_text())
        return dict(total=len(rows), scenarios=rows, counts={"Pass": 0, "Fail": 0, "Blocked": 0, "Not Run": len(rows)}, level3Ready=False)

    def test_unexecuted_report_is_not_ready(self):
        report = self.report()
        validate(report)
        report["level3Ready"] = True
        with self.assertRaises(ValueError):
            validate(report)

    def test_pass_requires_observed_and_verified_result(self):
        report = self.report()
        report["scenarios"][0]["Status"] = "Pass"
        report["counts"]["Pass"] = 1
        report["counts"]["Not Run"] -= 1
        with self.assertRaises(ValueError):
            validate(report)

    def test_artifact_path_escape_and_credential_disclosure_refused(self):
        for change in ({"Evidence": "../private/owner.txt"}, {"Actual": "Password=unpublishable"}):
            report = self.report()
            report["scenarios"][0].update(change)
            with self.assertRaises(ValueError):
                validate(report)

    def test_dishonest_totals_refused(self):
        report = self.report()
        report["counts"]["Pass"] = 48
        with self.assertRaises(ValueError):
            validate(report)

    def test_layout_review_requires_captures_and_observation(self):
        images={"L2-UX-05-1280x720.png", "L2-UX-05-maximized.png"}
        for available,notes in ((set(),"Readable labels"),(images,"")):
            with self.assertRaises(ValueError):
                review(self.report(),available,"Pass",notes)
        report=self.report()
        review(report,images,"Fail","Admission caption clipped at compact width")
        self.assertEqual(1,report["counts"]["Fail"])
        self.assertFalse(report["level3Ready"])

    @unittest.skipUnless(os.name == "nt", "Windows test binary required")
    def test_test_binary_rejects_operational_target_before_opening_sql(self):
        binary = Path(os.environ.get("FMSC_LEVEL2_PROBE_BINARY", str(ROOT / "bin/Level2/Release/net8.0-windows/FMSC_SMS.dll")))
        if not binary.exists():
            self.skipTest("Build the isolated test application in Release first")
        result = subprocess.run(["dotnet", str(binary), "--verify-level2-guards"], capture_output=True, timeout=30)
        self.assertEqual(0, result.returncode, "Operational catalog/instance, attach files and SQL credentials must be refused before connection")


if __name__ == "__main__":
    unittest.main()
