"""Refresh additive startup safeguards from the approved DACPAC, without SQL access.

Usage: python Tools/update_compatibility_contract.py FMSC.dacpac [--check]
Existing column, table-type and uniqueness contracts remain independently maintained.
Unknown SQL types fail rather than producing an incomplete parameter contract.
"""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile

NS = {"d": "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02"}
CONTRACT = Path(__file__).resolve().parents[1] / "Core/DatabaseCompatibilityRequirements.json"


def parts(name):
    return name.replace("[", "").replace("]", "").split(".")


def properties(element):
    return {p.get("Name"): p.get("Value") for p in element.findall("d:Property", NS)}


def refs(element, relationship):
    return [parts(r.get("Name")) for r in element.findall(
        f"d:Relationship[@Name='{relationship}']/d:Entry/d:References", NS)]


def type_shape(element, relationship="Type"):
    spec = element.find(f"d:Relationship[@Name='{relationship}']/d:Entry/d:Element", NS)
    type_name = refs(spec, "Type")[0]
    name = type_name[-1]
    p = properties(spec)
    if len(type_name) > 1:  # table-valued parameter
        return name, -1, 0, 0
    fixed = {"int": (4, 10, 0), "bigint": (8, 19, 0), "bit": (1, 1, 0),
             "date": (3, 10, 0), "uniqueidentifier": (16, 0, 0)}
    if name in fixed:
        return name, *fixed[name]
    if name in ("nvarchar", "varchar", "varbinary"):
        length = -1 if p.get("IsMax") == "True" else int(p["Length"])
        return name, length * 2 if name == "nvarchar" and length != -1 else length, 0, 0
    if name == "decimal":
        precision, scale = int(p.get("Precision", 18)), int(p.get("Scale", 0))
        length = 5 if precision <= 9 else 9 if precision <= 19 else 13 if precision <= 28 else 17
        return name, length, precision, scale
    if name == "datetime2":
        scale = int(p.get("Scale", 7))
        return name, 6 if scale <= 2 else 7 if scale <= 4 else 8, 20 + scale if scale else 19, scale
    raise ValueError(f"Unsupported SQL type: {name}")


def refresh(contract, model, artifact):
    elements = model.findall("d:Model/d:Element", NS)
    object_types = {"SqlTable": "U ", "SqlProcedure": "P ", "SqlDmlTrigger": "TR",
                    "SqlView": "V ", "SqlScalarFunction": "FN",
                    "SqlInlineTableValuedFunction": "IF", "SqlMultiStatementTableValuedFunction": "TF"}
    contract["objects"] = [dict(schema=parts(e.get("Name"))[0], name=parts(e.get("Name"))[1],
                                type=object_types[e.get("Type")])
                           for e in elements if e.get("Type") in object_types]
    # Keep existing function signatures, regenerate procedures, discard retired signatures.
    procedures = [e for e in elements if e.get("Type") == "SqlProcedure"]
    functions = {parts(e.get("Name"))[1] for e in elements if e.get("Type") in
                 ("SqlScalarFunction", "SqlInlineTableValuedFunction", "SqlMultiStatementTableValuedFunction")}
    parameters = [p for p in contract["parameters"] if p["objectName"] in functions]
    for e in procedures:
        schema, name = parts(e.get("Name"))
        for parameter in e.findall("d:Relationship[@Name='Parameters']/d:Entry/d:Element", NS):
            sql_type, length, precision, scale = type_shape(parameter)
            parameters.append(dict(schema=schema, objectName=name, name=parts(parameter.get("Name"))[-1],
                                   type=sql_type, maxLength=length, precision=precision, scale=scale,
                                   isOutput=properties(parameter).get("IsOutput") == "True"))
    contract["parameters"] = parameters
    # This table was missing from the original column inventory.
    table = next(e for e in elements if e.get("Name") == "[dbo].[CertificateNumberCounters]")
    contract["columns"] = [c for c in contract["columns"] if c["objectName"] != "CertificateNumberCounters"]
    for column in table.findall("d:Relationship[@Name='Columns']/d:Entry/d:Element", NS):
        sql_type, length, precision, scale = type_shape(column, "TypeSpecifier")
        contract["columns"].append(dict(schema="dbo", objectName="CertificateNumberCounters",
            name=parts(column.get("Name"))[-1], type=sql_type, maxLength=length,
            precision=precision, scale=scale, isNullable=properties(column).get("IsNullable", "True") == "True",
            isIdentity=False, isComputed=False))
    triggers, foreign_keys, checks = [], [], []
    for e in elements:
        kind, p = e.get("Type"), properties(e)
        if kind == "SqlDmlTrigger":
            schema, name = parts(e.get("Name"))
            parent_schema, parent = refs(e, "Parent")[0]
            triggers.append(dict(schema=schema, name=name, parentSchema=parent_schema, parentName=parent,
                events=",".join(event for event in ("DELETE", "INSERT", "UPDATE")
                                if p.get(f"Is{event.title()}Trigger") == "True"),
                insteadOf=p.get("SqlTriggerType") == "1"))
        elif kind == "SqlForeignKeyConstraint":
            schema, table_name = refs(e, "DefiningTable")[0]
            target_schema, target = refs(e, "ForeignTable")[0]
            foreign_keys.append(dict(schema=schema, objectName=table_name,
                referencedSchema=target_schema, referencedObject=target,
                columns=",".join(r[-1] for r in refs(e, "Columns")),
                referencedColumns=",".join(r[-1] for r in refs(e, "ForeignColumns")),
                deleteAction=int(p.get("OnDeleteAction", 0)), updateAction=int(p.get("OnUpdateAction", 0))))
        elif kind == "SqlCheckConstraint":
            schema, table_name = refs(e, "DefiningTable")[0]
            if e.get("Name") is None:
                raise ValueError("Unnamed check constraint needs an explicit contract strategy")
            checks.append(dict(schema=schema, objectName=table_name, name=parts(e.get("Name"))[-1]))
    contract["triggers"], contract["foreignKeys"], contract["checkConstraints"] = triggers, foreign_keys, checks
    for key in ("objects", "parameters", "columns", "triggers", "foreignKeys", "checkConstraints"):
        contract[key].sort(key=lambda item: json.dumps(item, sort_keys=True))
    contract["schemaArtifact"] = {"sha256": hashlib.sha256(artifact).hexdigest()}
    return contract


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dacpac", type=Path)
    parser.add_argument("--check", action="store_true", help="verify without writing")
    args = parser.parse_args()
    artifact = args.dacpac.read_bytes()
    with zipfile.ZipFile(args.dacpac) as archive:
        model = ET.fromstring(archive.read("model.xml"))
    original = json.loads(CONTRACT.read_text(encoding="utf-8-sig"))
    updated = refresh(json.loads(json.dumps(original)), model, artifact)
    if args.check:
        if original != updated:
            raise SystemExit("Compatibility contract differs from the approved DACPAC; review and refresh it.")
        print("Compatibility contract matches the approved DACPAC safeguards.")
    else:
        CONTRACT.write_text(json.dumps(updated, separators=(",", ":")) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
