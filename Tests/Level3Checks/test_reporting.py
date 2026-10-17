import copy
import importlib.util
import json
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("level3_report", Path(__file__).with_name("report.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ReportingSafety(unittest.TestCase):
    def evidence(self):
        catalog = json.loads(module.CATALOG.read_text())
        names = sorted({name for row in catalog for name in row[2]} | {"Cleanup"})
        return {"commit": "a" * 40, "runId": "fictional-report-unit-test", "engine": "16.0.0", "schemaVersion": "6", "scenarios": [
            {"name": name, "result": "PASS", "assertions": 1, "seconds": .1} for name in names]}

    def test_unexecuted_catalog_keeps_all_45_rows(self):
        result = module.build()
        self.assertEqual(45, len(result["scenarios"]))
        self.assertEqual(45, sum(result["counts"].values()))
        self.assertEqual(0, result["counts"]["PASS"])
        self.assertFalse(result["ready"])

    def test_passing_cloud_checks_do_not_complete_physical_acceptance(self):
        result = module.build(sql=self.evidence())
        for row in result["scenarios"]:
            if row["localRequired"]:
                self.assertEqual("LOCAL REQUIRED", row["status"])
        self.assertFalse(result["ready"])

    def test_failed_sql_assertion_remains_failed(self):
        source = self.evidence()
        source["scenarios"].append({"name": "L3-FIN-07 | concurrent production posting and competing collections", "result": "FAIL"})
        row = next(r for r in module.build(sql=source)["scenarios"] if r["id"] == "L3-FIN-07")
        self.assertEqual("FAIL", row["status"])

    def test_failed_cleanup_cannot_create_sql_pass(self):
        source = self.evidence()
        next(r for r in source["scenarios"] if r["name"] == "Cleanup")["result"] = "FAIL"
        result = module.build(sql=source)
        self.assertEqual(0, result["counts"]["PASS"])

    def test_mixed_revision_evidence_is_refused(self):
        with self.assertRaisesRegex(ValueError, "one exact"):
            module.build(sql=self.evidence(), install={"sourceCommit": "b" * 40})

    def test_omitted_row_or_dishonest_totals_are_refused(self):
        result = module.build(sql=self.evidence())
        broken = copy.deepcopy(result)
        broken["scenarios"].pop()
        with self.assertRaises(ValueError):
            module.validate(broken)
        broken = copy.deepcopy(result)
        broken["counts"]["PASS"] += 1
        with self.assertRaises(ValueError):
            module.validate(broken)

    def test_local_pass_and_readiness_forgery_are_refused(self):
        result = module.build(sql=self.evidence())
        result["ready"] = True
        with self.assertRaisesRegex(ValueError, "Incomplete"):
            module.validate(result)
        result["ready"] = False
        row = next(row for row in result["scenarios"] if row["localRequired"])
        result["counts"][row["status"]] -= 1
        row["status"] = "PASS"
        result["counts"]["PASS"] += 1
        with self.assertRaisesRegex(ValueError, "mandatory"):
            module.validate(result)

    def test_credentials_cannot_enter_measurement_evidence(self):
        source = self.evidence()
        source["measurements"] = {"Unsafe": "Password=" + "synthetic-noncredential"}
        with self.assertRaises(ValueError):
            module.build(sql=source)


if __name__ == "__main__":
    unittest.main()
