"""DacFx SQL schema export tests use only fictional objects and BCP bytes."""
import hashlib
import json
import tempfile
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET
import zipfile

from export_dacpac_schema import export, prepare_model_only, synchronize

NS = "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02"
PRIVATE_ROW = b"SECRET_SYNTHETIC_STUDENT_ROW_NEVER_EXPORT"

def package(path):
    root = ET.Element("{" + NS + "}DataSchemaModel")
    model = ET.SubElement(root, "{" + NS + "}Model")
    table = ET.SubElement(model, "{" + NS + "}Element", Type="SqlTable", Name="[dbo].[Example]")
    ET.SubElement(table, "{" + NS + "}Relationship", Name="Columns")
    proc = ET.SubElement(model, "{" + NS + "}Element", Type="SqlProcedure", Name="[dbo].[ExampleProc]")
    body = ET.SubElement(proc, "{" + NS + "}Property", Name="BodyScript")
    ET.SubElement(body, "{" + NS + "}Value").text = "SELECT 7;"
    ET.SubElement(model, "{" + NS + "}Element", Type="SqlCheckConstraint", Name="[dbo].[CK_Example]")
    xml = ET.tostring(root)
    origin = ET.Element("{" + NS + "}DacOrigin")
    properties = ET.SubElement(origin, "{" + NS + "}PackageProperties")
    ET.SubElement(properties, "{" + NS + "}ContainsExportedData").text = "true"
    checks = ET.SubElement(origin, "{" + NS + "}Checksums")
    ET.SubElement(checks, "{" + NS + "}Checksum", Uri="/model.xml").text = hashlib.sha256(xml).hexdigest()
    with zipfile.ZipFile(path, "w") as z:
        z.writestr("model.xml", xml)
        z.writestr("Origin.xml", ET.tostring(origin))
        z.writestr("DacMetadata.xml", b"<DacMetadata/>")
        z.writestr("[Content_Types].xml", b"<Types/>")
        z.writestr("Data/dbo.Students/data.BCP", PRIVATE_ROW)
    return xml

def scripts(version=1):
    return [
        {"Type": "Table", "Name": "[dbo].[Example]", "Sql":
         "CREATE TABLE [dbo].[Example] (\n    [ExampleId] int NOT NULL,\n"
         "    CONSTRAINT [CK_Example] CHECK ([ExampleId] > 0)\n);"},
        {"Type": "Procedure", "Name": "[dbo].[ExampleProc]", "Sql":
         "CREATE PROCEDURE [dbo].[ExampleProc] AS SELECT " + str(version) + ";"}
    ]

class ExportChecks(unittest.TestCase):
    def test_sql_only_and_synthetic_bcp_excluded(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "test.dacpac"
            xml = package(path)
            out = Path(temp) / "Schema"
            extracted = export(path, scripts())
            sql_files = [k for k in extracted if k.endswith(".sql")]
            self.assertEqual(2, len(sql_files))
            self.assertTrue(any(k.startswith("Tables/") for k in sql_files))
            self.assertTrue(any(k.startswith("StoredProcedures/") for k in sql_files))
            self.assertFalse(any(k.endswith(".xml") for k in extracted))
            self.assertTrue(all(PRIVATE_ROW not in blob for blob in extracted.values()))
            data = json.loads(extracted["SchemaManifest.json"])
            self.assertEqual(3, data["modelObjects"])
            self.assertEqual(2, data["scriptedObjects"])
            self.assertEqual(1, data["unscriptedObjects"])
            synchronize(out, extracted, False)
            synchronize(out, extracted, True)
            changed = export(path, scripts(2))
            with self.assertRaises(ValueError): synchronize(out, changed, True)
            synchronize(out, changed, False)
            synchronize(out, changed, True)
            self.assertEqual(2, len(list(out.rglob("*.sql"))))

    def test_temporary_model_only_contains_no_row_payload(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "source.dacpac"
            package(source)
            derived = Path(temp) / "safe.dacpac"
            prepare_model_only(source, derived)
            with zipfile.ZipFile(derived) as z:
                self.assertEqual({"model.xml", "Origin.xml", "DacMetadata.xml",
                                  "[Content_Types].xml", "_rels/.rels"}, set(z.namelist()))
                self.assertNotIn(PRIVATE_ROW, b"".join(z.read(n) for n in z.namelist()))
                origin = ET.fromstring(z.read("Origin.xml"))
                self.assertIn("false", [e.text for e in origin.iter()
                                      if e.tag.endswith("ContainsExportedData")])

    def test_no_fabricated_required_definitions(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "test.dacpac"
            package(path)
            with self.assertRaisesRegex(ValueError, "CREATE TABLE"):
                export(path, scripts()[1:])
            fallback = export(path, scripts()[:1])
            self.assertEqual(1, json.loads(fallback["SchemaManifest.json"])["bodyOnlyReferences"])
            self.assertIn(b"BODY ONLY:", b"".join(fallback.values()))

    def test_model_checksum_tamper_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "bad.dacpac"
            package(path)
            with zipfile.ZipFile(path, "a") as z:
                z.writestr("model.xml", b"<tampered/>")
            with self.assertRaises(ValueError): export(path, scripts())

if __name__ == "__main__":
    unittest.main()
