"""Check that migration 3, the embedded catalog and actual access consumers agree."""
import json
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]


class PermissionPolicyChecks(unittest.TestCase):
    def setUp(self):
        self.catalog = json.loads((ROOT/'Core/PermissionCatalog.json').read_text())
        self.migration = (ROOT/'Migrations/3.sql').read_text()
        self.codes = {entry['Code'] for entry in self.catalog}

    def test_same_catalog_in_sql_and_csharp(self):
        match = re.search(r"DECLARE @Catalog nvarchar\(max\)=N'((?:[^']|'')*)';", self.migration)
        self.assertIsNotNone(match)
        self.assertEqual(self.catalog, json.loads(match.group(1).replace("''", "'")))
        self.assertEqual(28, len(self.codes))
        self.assertEqual(len(self.codes), len(self.catalog))

    def test_catalog_covers_actual_consumers_without_dead_options(self):
        with zipfile.ZipFile(ROOT/'FMSC.dacpac') as package:
            root = ET.fromstring(package.read('model.xml'))
        ns = {'d': root.tag.split('}')[0][1:]}
        modules = {}
        for element in root.find('d:Model', ns):
            if element.get('Type') != 'SqlProcedure':
                continue
            body = element.find("d:Property[@Name='BodyScript']/d:Value", ns)
            modules[element.get('Name')] = body.text or ''
        updates = re.findall(r"EXEC sys.sp_executesql N'((?:[^']|'')*)';", self.migration)
        self.assertEqual(7, len(updates))
        for update in updates:
            update = update.replace("''", "'")
            name = re.search(r'ALTER PROCEDURE dbo\.(\w+)', update, re.I).group(1)
            modules[f'[dbo].[{name}]'] = update
        sql_codes = set()
        for body in modules.values():
            for statement in re.findall(r'EXEC(?:UTE)?\s+dbo\.Require(?:Any)?Permission\b[^;]*;', body, re.I):
                sql_codes.update(re.findall(r"N?'([A-Za-z]+\.[A-Za-z.]+)'", statement))
        self.assertLessEqual(sql_codes, self.codes)
        old_catalog = {'Students.Edit', 'Students.Photo.Manage', 'System.Permissions',
                       'System.Roles.Deactivate', 'Students.Siblings'} | self.codes
        cs_codes = set()
        for path in ROOT.rglob('*.cs'):
            if any(part in ('Tests', 'bin', 'obj', '.git') for part in path.relative_to(ROOT).parts):
                continue
            cs_codes.update(set(re.findall(r'"([A-Za-z]+\.[A-Za-z.]+)"', path.read_text())) & old_catalog)
        self.assertLessEqual(cs_codes, self.codes)
        self.assertEqual(self.codes, sql_codes | cs_codes)

    def test_application_and_installer_require_migration_version(self):
        application = re.search(r'RequiredDatabaseSchemaVersion = "(\d+)"', (ROOT/'DatabaseHelper.cs').read_text())
        installer = re.search(r"\$DatabaseVersion = '(\d+)'", (ROOT/'Installer/core.ps1').read_text())
        self.assertIsNotNone(application)
        self.assertIsNotNone(installer)
        required = int(application.group(1))
        self.assertGreaterEqual(required, 3)
        self.assertEqual(int(installer.group(1)), required)
        self.assertEqual(max(int(path.stem) for path in (ROOT/'Migrations').glob('*.sql') if path.stem.isdigit()), required)
        self.assertIn("VALUES(N'3',SYSDATETIME()", self.migration)

    def test_metadata_fits_sql_columns(self):
        for entry in self.catalog:
            self.assertTrue(entry['Code'] and len(entry['Code']) <= 100)
            self.assertTrue(entry['Name'] and len(entry['Name']) <= 100)
            self.assertTrue(entry['Description'] and len(entry['Description']) <= 250)


if __name__ == '__main__':
    unittest.main()
