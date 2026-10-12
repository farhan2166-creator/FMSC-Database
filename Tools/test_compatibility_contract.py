"""Offline regression checks: python Tools/test_compatibility_contract.py approved.dacpac.

These compare the embedded contract to actual DACPAC metadata. They do not execute
DatabaseCompatibility.Validate against SQL Server or test Windows role workflows.
"""
import copy
import json
from pathlib import Path
import sys
import unittest
import xml.etree.ElementTree as ET
import zipfile

from update_compatibility_contract import CONTRACT, NS, parts, refresh

ARTIFACT_PATH = Path(sys.argv.pop(1))
ARTIFACT = ARTIFACT_PATH.read_bytes()
with zipfile.ZipFile(ARTIFACT_PATH) as archive:
    MODEL = ET.fromstring(archive.read("model.xml"))
ELEMENTS = MODEL.findall("d:Model/d:Element", NS)
CONTRACT_DATA = json.loads(CONTRACT.read_text())


class ContractChecks(unittest.TestCase):
    def test_all_procedures_and_tables_are_required(self):
        for model_type, sql_type in (("SqlProcedure", "P"), ("SqlTable", "U")):
            expected = {tuple(parts(e.get("Name"))) for e in ELEMENTS if e.get("Type") == model_type}
            actual = {(r["schema"], r["name"]) for r in CONTRACT_DATA["objects"] if r["type"].strip() == sql_type}
            self.assertEqual(expected, actual)

    def test_every_procedure_parameter_is_required(self):
        expected = {tuple(parts(p.get("Name"))) for e in ELEMENTS if e.get("Type") == "SqlProcedure"
                    for p in e.findall("d:Relationship[@Name='Parameters']/d:Entry/d:Element", NS)}
        actual = {(p["schema"], p["objectName"], p["name"]) for p in CONTRACT_DATA["parameters"]
                  if (p["schema"], p["objectName"]) in
                  {tuple(parts(e.get("Name"))) for e in ELEMENTS if e.get("Type") == "SqlProcedure"}}
        self.assertEqual(expected, actual)

    def test_transfer_parameters_have_correct_shapes(self):
        for procedure in ("TransferEnrollmentClass", "TransferEnrollmentCampus"):
            rows = {p["name"]: p for p in CONTRACT_DATA["parameters"] if p["objectName"] == procedure}
            self.assertEqual(("date", 3, 10, 0), tuple(rows["@FeeEffectiveMonth"][k]
                             for k in ("type", "maxLength", "precision", "scale")))
            self.assertEqual(("bit", 1, 1, 0), tuple(rows["@ReplaceScheduledFees"][k]
                             for k in ("type", "maxLength", "precision", "scale")))

    def test_certificate_counter_columns_are_required(self):
        columns = {c["name"]: c for c in CONTRACT_DATA["columns"] if c["objectName"] == "CertificateNumberCounters"}
        self.assertEqual({"Prefix", "CounterYear", "LastNumber"}, set(columns))
        self.assertEqual(16, columns["Prefix"]["maxLength"])
        self.assertFalse(any(c["isNullable"] for c in columns.values()))

    def test_all_triggers_and_constraint_counts_include_unnamed_constraints(self):
        for model_type, key in (("SqlDmlTrigger", "triggers"), ("SqlForeignKeyConstraint", "foreignKeys"),
                                ("SqlCheckConstraint", "checkConstraints")):
            self.assertEqual(sum(e.get("Type") == model_type for e in ELEMENTS), len(CONTRACT_DATA[key]))
        triggers = {t["name"]: t for t in CONTRACT_DATA["triggers"]}
        self.assertEqual("BillingCharges", triggers["GuardClosedEnrollmentCharges"]["parentName"])
        self.assertEqual("DELETE,INSERT,UPDATE", triggers["GuardClosedEnrollmentCharges"]["events"])
        fks = [f for f in CONTRACT_DATA["foreignKeys"] if f["objectName"] == "DailyCollectionClosings"]
        self.assertTrue({"SubmittedBy", "ApprovedBy"}.issubset({f["columns"] for f in fks}))

    def test_regeneration_is_deterministic(self):
        self.assertEqual(CONTRACT_DATA, refresh(copy.deepcopy(CONTRACT_DATA), MODEL, ARTIFACT))

    def test_schema_drift_requires_a_contract_change(self):
        for name in ("[dbo].[ReAdmitStudentWithNumberChoice]", "[dbo].[GuardClosedEnrollmentCharges]"):
            model = copy.deepcopy(MODEL)
            parent = model.find("d:Model", NS)
            element = next(e for e in parent if e.get("Name") == name)
            parent.remove(element)
            self.assertNotEqual(CONTRACT_DATA, refresh(copy.deepcopy(CONTRACT_DATA), model, ARTIFACT))


if __name__ == "__main__":
    unittest.main()
