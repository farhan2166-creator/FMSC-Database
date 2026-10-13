"""Regression checks for artifact provenance and semantic drift detection."""
import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
import xml.etree.ElementTree as ET

from verify_schema_artifact import ROOT, inspect, verify_contract, whole_rupee_expression_matches
from apply_whole_rupee_schema import POLICY


class ArtifactChecks(unittest.TestCase):
    def setUp(self):
        self.path = ROOT/'FMSC.dacpac'
        self.root, self.ns, self.elements, self.fingerprint = inspect(self.path)
        self.contract = json.loads((ROOT/'Core/DatabaseCompatibilityRequirements.json').read_text())

    def test_current_contract(self):
        self.assertEqual([],verify_contract(self.root,self.ns,self.elements,self.contract,self.fingerprint))

    def test_provenance_mismatch_detected(self):
        self.contract['schemaArtifact']['sha256']='0'*64
        self.assertTrue(verify_contract(self.root,self.ns,self.elements,self.contract,self.fingerprint))

    def test_missing_procedure_contract_entry_detected(self):
        self.contract['objects']=[x for x in self.contract['objects'] if x['name']!='AdmitStudent']
        self.assertTrue(verify_contract(self.root,self.ns,self.elements,self.contract,self.fingerprint))

    def test_all_whole_rupee_checks(self):
        checks={e.get('Name'):e for e in self.elements if e.get('Type')=='SqlCheckConstraint'}
        for table, columns in POLICY.items():
            e=checks[f'[dbo].[CK_{table}_WholeRupees]']
            self.assertTrue(whole_rupee_expression_matches(e.find("d:Property[@Name='CheckExpressionScript']/d:Value",self.ns).text, columns))
            parent=e.find("d:Relationship[@Name='DefiningTable']/d:Entry/d:References",self.ns)
            self.assertEqual(f'[dbo].[{table}]',parent.get('Name'))

    def test_weakened_constraint_detected(self):
        e=next(e for e in self.elements if e.get('Name')=='[dbo].[CK_BillingCharges_WholeRupees]')
        e.find("d:Property[@Name='CheckExpressionScript']/d:Value",self.ns).text='1=1'
        self.assertTrue(verify_contract(self.root,self.ns,self.elements,self.contract,self.fingerprint))

    def test_server_floor_keyword_casing_accepted(self):
        for keyword in ('FLOOR', 'floor', 'Floor'):
            self.assertTrue(whole_rupee_expression_matches(f'[Amount]={keyword}([Amount])', ['Amount']))

    def test_column_casing_and_wrong_column_stay_strict(self):
        for actual in ('[amount]=floor([amount])', '[Amount]=floor([DiscountAmount])'):
            self.assertFalse(whole_rupee_expression_matches(actual, ['Amount']))

    def test_changed_operator_or_extra_sql_rejected(self):
        for actual in ('[Amount]>=floor([Amount])', '[Amount]=floor([Amount])+0.5',
                       '[Amount]=floor([Amount]) OR 1=1', None):
            self.assertFalse(whole_rupee_expression_matches(actual, ['Amount']))

    def test_generated_check_definition_detected(self):
        row=next(r for r in self.contract['checkConstraints'] if r.get('isSystemNamed'))
        row['definition']='[SetupId]>=(1)'
        self.assertTrue(verify_contract(self.root,self.ns,self.elements,self.contract,self.fingerprint))

    def test_sql_body_drift_despite_matching_signature(self):
        e=next(e for e in self.elements if e.get('Name')=='[dbo].[AdmitStudent]')
        body=e.find("d:Property[@Name='BodyScript']/d:Value",self.ns)
        self.assertIsNotNone(body)
        body.text += '\n-- changed body, unchanged parameter contract'
        changed=hashlib.sha256(ET.tostring(e,encoding='utf-8').replace(b'\r\n',b'\n')).hexdigest()
        self.assertNotEqual(changed,self.fingerprint['modules']['[dbo].[AdmitStudent]'])

    def test_approved_model_and_modules_match(self):
        self.assertEqual(json.loads((ROOT/'Core/ApprovedSchemaArtifact.json').read_text()),self.fingerprint)

    def test_model_checksum_corruption_detected(self):
        with tempfile.TemporaryDirectory() as directory:
            dest=Path(directory)/'bad.dacpac'
            with zipfile.ZipFile(self.path) as original,zipfile.ZipFile(dest,'w') as output:
                for name in original.namelist():
                    data=original.read(name)
                    output.writestr(name,data+b' ' if name=='model.xml' else data)
            with self.assertRaises(ValueError):inspect(dest)


if __name__=='__main__':
    unittest.main()
