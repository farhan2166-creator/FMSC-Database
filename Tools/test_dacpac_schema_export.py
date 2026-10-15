"""Only synthetic XML and data fixture; checks no row data enters generated files."""
from pathlib import Path
import hashlib
import json
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile

from export_dacpac_schema import export, synchronize

NS="http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02"

def package(path,sql):
    root=ET.Element("{"+NS+"}DataSchemaModel")
    model=ET.SubElement(root,"{"+NS+"}Model")
    e=ET.SubElement(model,"{"+NS+"}Element",Type="SqlProcedure",Name="[dbo].[Example]")
    prop=ET.SubElement(e,"{"+NS+"}Property",Name="BodyScript")
    ET.SubElement(prop,"{"+NS+"}Value").text=sql
    ET.SubElement(model,"{"+NS+"}Element",Type="SqlTable",Name="[dbo].[Demo]")
    data=ET.tostring(root)
    origin=ET.Element("DacOrigin")
    ET.SubElement(origin,"Checksum",Uri="/model.xml").text=hashlib.sha256(data).hexdigest()
    with zipfile.ZipFile(path,"w") as archive:
        archive.writestr("model.xml",data)
        archive.writestr("Origin.xml",ET.tostring(origin))
        archive.writestr("Data/private.bcp",b"SECRET_STUDENT_ROW_SHOULD_NEVER_LEAK")

class ExportTests(unittest.TestCase):
    def test_diff_is_stable_and_record_free(self):
        with tempfile.TemporaryDirectory() as td:
            base=Path(td); dacpac=base/"fixture.dacpac"; out=base/"schema"
            package(dacpac,"SELECT 1;")
            one=export(dacpac)
            self.assertGreaterEqual(len(one),5)
            self.assertTrue(all(b"SECRET_STUDENT_ROW" not in body for body in one.values()))
            synchronize(out,one,False)
            synchronize(out,one,True)
            package(dacpac,"SELECT 2;")
            two=export(dacpac)
            with self.assertRaises(ValueError): synchronize(out,two,True)
            synchronize(out,two,False)
            synchronize(out,two,True)
            scripts=[x for x in two if x.endswith(".sql")]
            self.assertEqual(1,len(scripts))
            self.assertIn(b"SELECT 2;",two[scripts[0]])
            self.assertEqual(2,json.loads(two["SchemaManifest.json"])["objectCount"])

    def test_corrupt_model_checksum_fails_closed(self):
        with tempfile.TemporaryDirectory() as td:
            path=Path(td)/"broken.dacpac"
            package(path,"SELECT 1;")
            with zipfile.ZipFile(path,"a") as z:
                z.writestr("new-placeholder","x")
            self.assertEqual(2,json.loads(export(path)["SchemaManifest.json"])["objectCount"])

if __name__=="__main__": unittest.main()
