"""Generate real DacFx CREATE scripts from a data-free copy of FMSC.dacpac.

Reads only model/package metadata. Never loads Data/*.BCP or changes a database.
No XML files are generated: unsupported inline objects are inventoried in JSON.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
CATEGORIES = {
    "SqlTable": "Tables", "SqlView": "Views", "SqlProcedure": "StoredProcedures",
    "SqlScalarFunction": "Functions", "SqlInlineTableValuedFunction": "Functions",
    "SqlMultiStatementTableValuedFunction": "Functions", "SqlDmlTrigger": "Triggers",
    "SqlIndex": "Indexes", "SqlPrimaryKeyConstraint": "Constraints",
    "SqlUniqueConstraint": "Constraints", "SqlForeignKeyConstraint": "Constraints",
    "SqlCheckConstraint": "Constraints", "SqlDefaultConstraint": "Constraints",
    "SqlTableType": "Types", "SqlUserDefinedDataType": "Types",
    "SqlSequence": "Sequences", "SqlSchema": "Schemas", "SqlRole": "Security",
    "SqlUser": "Security", "SqlPermissionStatement": "Security",
    "SqlSynonym": "Synonyms", "SqlFullTextIndex": "Indexes",
    "SqlFullTextCatalog": "OtherObjects", "SqlXmlIndex": "Indexes",
}
REQUIRED_SCRIPT_TYPES = {
    "SqlTable", "SqlView", "SqlProcedure", "SqlScalarFunction",
    "SqlInlineTableValuedFunction", "SqlMultiStatementTableValuedFunction",
    "SqlDmlTrigger",
}
RETAIN = {"model.xml", "Origin.xml", "DacMetadata.xml", "[Content_Types].xml"}

def sha(data):
    return hashlib.sha256(data).hexdigest()

def read_model(path):
    if path.stat().st_size > 128 * 1024 * 1024:
        raise ValueError("DACPAC exceeds supported package size")
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        names = [e.filename for e in entries]
        if (len(names) != len(set(names)) or len(entries) > 4096 or
            sum(e.file_size for e in entries) > 512 * 1024 * 1024 or
            any(not n or n.startswith("/") or "\\" in n or ":" in n or ".." in n.split("/") for n in names)):
            raise ValueError("Unsafe or oversized DACPAC entries")
        if not RETAIN.issubset(names) or archive.getinfo("model.xml").file_size > 100 * 1024 * 1024:
            raise ValueError("Missing/oversized DACPAC schema metadata")
        # Intentionally do not read BCP or other exported row payloads.
        contents = {name: archive.read(name) for name in RETAIN}
    for name in ("model.xml", "Origin.xml", "DacMetadata.xml", "[Content_Types].xml"):
        if b"<!DOCTYPE" in contents[name].upper() or b"<!ENTITY" in contents[name].upper():
            raise ValueError("Unsupported XML entity/declaration")
    origin = ET.fromstring(contents["Origin.xml"])
    checks = [e for e in origin.iter() if e.tag.endswith("Checksum") and e.get("Uri") == "/model.xml"]
    if len(checks) != 1 or (checks[0].text or "").lower() != sha(contents["model.xml"]):
        raise ValueError("DACPAC model SHA256 does not match internal checksum")
    model = ET.fromstring(contents["model.xml"])
    ns = {"d": model.tag.split("}")[0][1:]}
    root = model.find("d:Model", ns)
    if root is None or len(root) > 20000:
        raise ValueError("Missing or excessive DACPAC model definitions")
    return contents, origin, list(root)

def prepare_model_only(source, destination):
    contents, origin, _ = read_model(source)
    # Preserve the same schema-only package layout exercised by Level 1 SQL tests.
    for node in list(origin):
        if node.tag.split("}")[-1] not in ("PackageProperties", "Checksums"):
            origin.remove(node)
    for node in origin.iter():
        if node.tag.endswith("ContainsExportedData"):
            node.text = "false"
    for node in origin.iter():
        for child in list(node):
            if child.tag.endswith("Checksum") and child.get("Uri") != "/model.xml":
                node.remove(child)
    contents["Origin.xml"] = ET.tostring(origin, encoding="utf-8", xml_declaration=True)
    destination.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED) as output:
        for name in sorted(RETAIN):
            entry = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            output.writestr(entry, contents[name])
        entry = zipfile.ZipInfo("_rels/.rels", (1980, 1, 1, 0, 0, 0))
        entry.compress_type = zipfile.ZIP_DEFLATED
        output.writestr(entry, '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships" />')
    return sha(contents["model.xml"])

def key(kind, name):
    return (re.sub(r"^Sql", "", kind, flags=re.I).lower(),
            re.sub(r"[\[\]\s]", "", name or "").lower())

def stem(kind, name, index):
    safe = re.sub(r"[^A-Za-z0-9_.-]+", "_", name or kind).strip("._")[:72] or "unnamed"
    token = sha((kind + "\0" + str(name) + "\0" + str(index if not name else 0)).encode())[:12]
    return safe + "-" + token

def export(path, scripts):
    contents, _, nodes = read_model(path)
    if not isinstance(scripts, list):
        raise ValueError("Expected DacFx JSON object array")
    mapped = {}
    for item in scripts:
        if not isinstance(item, dict) or not isinstance(item.get("Type"), str) or not isinstance(item.get("Name"), str):
            raise ValueError("Malformed DacFx object record")
        identity = key(item["Type"], item["Name"])
        if identity in mapped:
            raise ValueError("Duplicate DacFx object identity")
        sql = item.get("Sql")
        if sql is not None and not isinstance(sql, str):
            raise ValueError("Malformed DacFx SQL definition")
        mapped[identity] = sql
    files = {}
    manifest = []
    supported = 0
    for index, node in enumerate(nodes):
        kind = node.get("Type") or "Unknown"
        name = node.get("Name")
        folder = CATEGORIES.get(kind, "OtherObjects")
        identity = key(kind, name)
        sql = mapped.get(identity) if name else None
        script_file = None
        if sql and sql.strip():
            script_file = folder + "/" + stem(kind, name, index) + ".sql"
            files[script_file] = ("-- Generated by Microsoft DacFx from schema-only FMSC.dacpac.\n"
                "-- Reference only; do not execute directly on a school database.\n\n"
                + sql.replace("\r\n", "\n").rstrip() + "\n").encode("utf-8")
            supported += 1
        elif kind in REQUIRED_SCRIPT_TYPES and name is not None:
            raise ValueError("DacFx could not create required SQL definition for " + kind + " " + name)
        manifest.append({"type": kind, "name": name, "script": script_file,
                         "modelElementSha256": sha(ET.tostring(node, encoding="utf-8"))})
    if not supported:
        raise ValueError("DacFx did not generate any SQL CREATE scripts")
    unscripted = [m for m in manifest if m["script"] is None]
    files["SchemaManifest.json"] = (json.dumps({
        "formatVersion": 2, "sourceModelSha256": sha(contents["model.xml"]),
        "modelObjects": len(manifest), "scriptedObjects": supported,
        "unscriptedObjects": len(unscripted),
        "notes": "Unscriptable inline constraints/model-only objects are represented within their parent DDL and/or source DACPAC model.",
        "objects": sorted(manifest, key=lambda m: (m["type"], m["name"] or ""))
    }, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    files["README.md"] = (
        "# FMSC database SQL definitions\n\n"
        "These SQL scripts are generated by Microsoft DacFx from a **data-free, temporary** DACPAC model.\n"
        "Table scripts include their columns and inline constraints; supported other objects have CREATE scripts.\n"
        "Not every model element has a standalone SQL statement: see SchemaManifest.json for coverage.\n"
        "The authoritative schema remains FMSC.dacpac. Never deploy scripts from this reference folder "
        "or treat schema generation as review/approval of a DACPAC change.\n"
    ).encode("utf-8")
    return files

def synchronize(folder, files, check):
    if folder.exists() and folder.is_symlink():
        raise ValueError("Refusing symlink output directory")
    existing = {p.relative_to(folder).as_posix(): p.read_bytes() for p in folder.rglob("*") if p.is_file()} if folder.exists() else {}
    changed = sorted(n for n in set(existing) | set(files) if existing.get(n) != files.get(n))
    if check:
        if changed:
            raise ValueError("Generated SQL schema differs in " + str(len(changed)) + " files")
        print("PASS: SQL definitions fully synchronized")
        return
    for name in changed:
        dest = folder / name
        if name in files:
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(files[name])
        elif dest.is_file():
            dest.unlink()
    print("SQL scripts: " + str(len(files) - 2) + "; changed/deleted: " + str(len(changed)))

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dacpac", type=Path, default=ROOT / "FMSC.dacpac")
    parser.add_argument("--output", type=Path, default=ROOT / "Schema")
    parser.add_argument("--scripts-json", type=Path)
    parser.add_argument("--prepare-model-only", type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    if args.prepare_model_only:
        print("Data-free model prepared:", prepare_model_only(args.dacpac, args.prepare_model_only))
        return
    if not args.scripts_json:
        parser.error("--scripts-json is required; SQL must come from Microsoft DacFx")
    folder = args.output.resolve()
    if folder == ROOT.resolve() or not folder.is_relative_to(ROOT.resolve()) and args.output == ROOT:
        raise ValueError("Refusing repository root as destination")
    content = json.loads(args.scripts_json.read_text(encoding="utf-8"))
    synchronize(folder, export(args.dacpac, content), args.check)

if __name__ == "__main__":
    main()
