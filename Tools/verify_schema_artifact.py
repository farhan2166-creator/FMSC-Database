"""Offline release check; --approve is an explicit reviewed-baseline update.

Checks ZIP/model integrity, the exact package provenance hash, contract inventory,
whole-rupee constraints, and approved model/module fingerprints. Does not connect
to SQL Server and does not substitute for a native DacFx load or execution test.
"""
from pathlib import Path
import argparse
import hashlib
import json
import re
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def digest(data):
    return hashlib.sha256(data).hexdigest()


def inspect(path):
    with zipfile.ZipFile(path) as package:
        if package.testzip() is not None:
            raise ValueError('Invalid ZIP CRC')
        model = package.read('model.xml')
        origin = ET.fromstring(package.read('Origin.xml'))
        checksum = next(e.text for e in origin.iter() if e.tag.endswith('Checksum') and e.get('Uri') == '/model.xml')
        if checksum.lower() != digest(model):
            raise ValueError('DACPAC model checksum differs from Origin.xml')
    root = ET.fromstring(model)
    ns = {'d': root.tag.split('}')[0][1:]}
    model_node = root.find('d:Model', ns)
    if model_node is None:
        raise ValueError('Missing DACPAC Model')
    elements = list(model_node)
    # Include body text, signatures, dependencies, and annotations. Line-ending
    # normalization is safe; collapsing SQL whitespace could change string literals.
    modules = {e.get('Name'): digest(ET.tostring(e, encoding='utf-8').replace(b'\r\n', b'\n'))
               for e in elements if e.get('Type') in
               ('SqlProcedure', 'SqlDmlTrigger', 'SqlScalarFunction', 'SqlInlineTableValuedFunction', 'SqlMultiStatementTableValuedFunction')}
    return root, ns, elements, {'packageSha256': digest(path.read_bytes()),
                              'modelSha256': digest(model), 'modules': dict(sorted(modules.items()))}


def verify_contract(root, ns, elements, contract, fingerprint):
    errors = []
    if contract['schemaArtifact']['sha256'] != fingerprint['packageSha256']:
        errors.append('Contract package SHA-256 does not match the DACPAC')
    kinds = {'U': 'SqlTable', 'P': 'SqlProcedure', 'TR': 'SqlDmlTrigger',
             'FN': 'SqlScalarFunction', 'IF': 'SqlInlineTableValuedFunction',
             'TF': 'SqlMultiStatementTableValuedFunction', 'V': 'SqlView'}
    by_name = {e.get('Name'): e for e in elements if e.get('Name')}
    for item in contract['objects']:
        name = f"[{item['schema']}].[{item['name']}]"
        element = by_name.get(name)
        if element is None or element.get('Type') != kinds[item['type'].strip()]:
            errors.append(f'Missing/incompatible contract object {name}')
    for sql_type, contract_type in [('SqlTable','U'),('SqlProcedure','P'),('SqlDmlTrigger','TR')]:
        actual = {e.get('Name') for e in elements if e.get('Type') == sql_type}
        expected = {f"[{i['schema']}].[{i['name']}]" for i in contract['objects'] if i['type'].strip() == contract_type}
        if expected != actual:
            errors.append(f'{sql_type} contract inventory differs from DACPAC')
    all_named = {e.get('Name') for e in root.findall('.//d:Element',ns) if e.get('Name')}
    for section in ['columns','parameters','tableTypes']:
        for i in contract[section]:
            if section == 'parameters' and not i['name']:
                # sys.parameters represents scalar-function return types as name=''.
                if f"[{i['schema']}].[{i['objectName']}]" not in by_name:
                    errors.append(f"Missing return-type owner {i['objectName']}")
                continue
            name = f"[{i['schema']}].[{i['objectName']}].[{i['name']}]"
            if name not in all_named:
                errors.append(f'Missing {section} entry {name}')
    from apply_whole_rupee_schema import POLICY, expression
    for table, columns in POLICY.items():
        name = f'[dbo].[CK_{table}_WholeRupees]'
        element = by_name.get(name)
        if element is None:
            errors.append(f'Missing whole-rupee safeguard {name}')
            continue
        actual = element.find("d:Property[@Name='CheckExpressionScript']/d:Value",ns)
        if actual is None or actual.text != expression(columns):
            errors.append(f'Whole-rupee expression changed: {name}')
        if {'schema':'dbo','objectName':table,'name':f'CK_{table}_WholeRupees'} not in contract['checkConstraints']:
            errors.append(f'Whole-rupee safeguard absent from startup contract: {name}')
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('dacpac', nargs='?', default=str(ROOT/'FMSC.dacpac'))
    parser.add_argument('--check', action='store_true')
    parser.add_argument('--approve', action='store_true', help='Record an explicitly reviewed artifact baseline')
    args = parser.parse_args()
    if args.check and args.approve:
        parser.error('Choose --check or --approve')
    path = Path(args.dacpac)
    root, ns, elements, fingerprint = inspect(path)
    contract = json.loads((ROOT/'Core/DatabaseCompatibilityRequirements.json').read_text())
    errors = verify_contract(root,ns,elements,contract,fingerprint)
    manifest = ROOT/'Core/ApprovedSchemaArtifact.json'
    if args.approve:
        if errors:
            raise SystemExit('\n'.join(errors))
        manifest.write_text(json.dumps(fingerprint,indent=2)+'\n')
        print('Recorded reviewed package/model/module fingerprints.')
        return
    approved = json.loads(manifest.read_text())
    for field in ['packageSha256','modelSha256']:
        if approved[field] != fingerprint[field]:
            errors.append(f'Approved {field} differs from current artifact')
    for name in sorted(set(approved['modules']) | set(fingerprint['modules'])):
        if approved['modules'].get(name) != fingerprint['modules'].get(name):
            errors.append(f'SQL implementation/signature drift: {name}')
    if errors:
        raise SystemExit('\n'.join(errors))
    print(f"PASS: ZIP/model checksum, provenance, contract inventory, whole-rupee checks and {len(fingerprint['modules'])} SQL module fingerprints.")


if __name__ == '__main__':
    main()
