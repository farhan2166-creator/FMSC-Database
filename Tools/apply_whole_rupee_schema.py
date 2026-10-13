"""Generate the whole-rupee constraints in the DACPAC, contract and v2 migration.

No database connection is made. Existing module bodies and model elements are
preserved byte-for-byte; only named check constraints are appended to model.xml.
Run once with the reviewed DACPAC, then run verify_schema_artifact.py --approve.
"""
from pathlib import Path
import hashlib
import json
import re
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
POLICY = {
    'FeeConfigurations': ['Amount'],
    'StudentFeeAssignments': ['Amount', 'DiscountAmount'],
    'BillingCharges': ['GrossAmount', 'DiscountAmount'],
    'FeePayments': ['TotalAmount'],
    'FeePaymentAllocations': ['Amount'],
    'ManualFeePostingRequests': ['Amount', 'DiscountAmount'],
    'StudentClearanceRecords': ['OutstandingBalance'],
    'BankAccountOpeningCertificates': ['MonthlyIncome'],
}


def expression(columns):
    return ' AND '.join(f'[{c}]=FLOOR([{c}])' for c in columns)


def main():
    path = ROOT / 'FMSC.dacpac'
    with zipfile.ZipFile(path) as package:
        entries = [(info, package.read(info.filename)) for info in package.infolist()]
    content = dict((info.filename, data) for info, data in entries)
    model = content['model.xml'].decode('utf-8-sig')
    root = ET.fromstring(model)
    ns = {'d': root.tag.split('}')[0][1:]}
    names = {e.get('Name') for e in root.findall('.//d:Element', ns)}
    additions = []
    for table, columns in POLICY.items():
        name = f'[dbo].[CK_{table}_WholeRupees]'
        if name in names:
            continue
        for column in columns:
            if f'[dbo].[{table}].[{column}]' not in names:
                raise ValueError(f'Missing policy column: {table}.{column}')
        dependencies = ''.join(f'<Entry><References Name="[dbo].[{table}].[{c}]" /></Entry>' for c in columns)
        additions.append(f'''<Element Type="SqlCheckConstraint" Name="{name}">
            <Property Name="CheckExpressionScript"><Value>{expression(columns)}</Value></Property>
            <Relationship Name="CheckExpressionDependencies">{dependencies}</Relationship>
            <Relationship Name="DefiningTable"><Entry><References Name="[dbo].[{table}]" /></Entry></Relationship>
        </Element>''')
    if additions:
        if model.count('</Model>') != 1:
            raise ValueError('Unexpected model layout')
        model = model.replace('</Model>', '\n'.join(additions) + '\n</Model>')
        content['model.xml'] = model.encode('utf-8')
        origin = content['Origin.xml'].decode('utf-8-sig')
        count = len(ET.fromstring(model).findall('.//d:Element[@Type="SqlCheckConstraint"]', ns))
        origin = re.sub(r'<CheckConstraint>\d+</CheckConstraint>', f'<CheckConstraint>{count}</CheckConstraint>', origin)
        digest = hashlib.sha256(content['model.xml']).hexdigest().upper()
        origin = re.sub(r'(<Checksum Uri="/model.xml">)[A-Fa-f0-9]+(</Checksum>)', rf'\g<1>{digest}\2', origin)
        content['Origin.xml'] = origin.encode('utf-8-sig')
        content['DacMetadata.xml'] = content['DacMetadata.xml'].replace(b'<Version>1.0.0.0</Version>', b'<Version>2.0.0.0</Version>')
        temporary = path.with_suffix('.dacpac.tmp')
        with zipfile.ZipFile(temporary, 'w') as package:
            for info, _ in entries:
                package.writestr(info, content[info.filename])
        temporary.replace(path)

    contract_path = ROOT / 'Core/DatabaseCompatibilityRequirements.json'
    contract = json.loads(contract_path.read_text())
    for table in POLICY:
        row = {'schema': 'dbo', 'objectName': table, 'name': f'CK_{table}_WholeRupees'}
        if row not in contract['checkConstraints']:
            contract['checkConstraints'].append(row)
    contract['checkConstraints'].sort(key=lambda r: (r['schema'], r['objectName'], r['name']))
    contract['schemaArtifact']['sha256'] = hashlib.sha256(path.read_bytes()).hexdigest()
    contract_path.write_text(json.dumps(contract, separators=(',', ':')) + '\n')

    sql = ['''-- Whole-rupee policy approved on 13 October 2026.
-- Apply to a disposable restored copy first. No historical amounts are rounded.
-- Any fractional existing row aborts the complete migration before DDL/version changes.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @Version nvarchar(20)=(SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion WITH(UPDLOCK,HOLDLOCK) ORDER BY VersionId DESC);
    IF @Version NOT IN(N'1',N'2') OR @Version IS NULL
        THROW 53200,'Whole-rupee migration requires schema version 1 or 2.',1;''']
    for table, columns in POLICY.items():
        condition = ' OR '.join(f'[{c}]<>FLOOR([{c}])' for c in columns)
        sql.append(f'''    IF EXISTS(SELECT 1 FROM dbo.[{table}] WITH(TABLOCKX,HOLDLOCK) WHERE {condition})
        THROW 53201,'Fractional amounts exist in {table}. Review and correct them explicitly; no values have been rounded.',1;''')
    for table, columns in POLICY.items():
        name = f'CK_{table}_WholeRupees'
        sql.append(f'''    IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.{table}') AND name=N'{name}')
        ALTER TABLE dbo.[{table}] DROP CONSTRAINT [{name}];
    ALTER TABLE dbo.[{table}] WITH CHECK ADD CONSTRAINT [{name}] CHECK ({expression(columns)});
    ALTER TABLE dbo.[{table}] WITH CHECK CHECK CONSTRAINT [{name}];''')
    sql.append('''    IF @Version=N'1'
        INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
        VALUES(N'2',SYSDATETIME(),SUSER_SNAME(),N'Whole-rupee financial constraints; no historical rounding.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
''')
    (ROOT / 'Migrations').mkdir(exist_ok=True)
    (ROOT / 'Migrations/2.sql').write_text('\n'.join(sql))
    print(f'Added {len(additions)} constraints; generated Migrations/2.sql and refreshed package hash.')


if __name__ == '__main__':
    main()
