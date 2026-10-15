"""Export data-free DACPAC schema to deterministic, reviewable object files.

XML preserves each complete model element; .sql files preserve module bodies.
Never loads exported rows, approves schema drift, or connects to SQL.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
KIND = {
 "SqlTable":"Tables","SqlView":"Views","SqlProcedure":"StoredProcedures",
 "SqlScalarFunction":"Functions","SqlInlineTableValuedFunction":"Functions",
 "SqlMultiStatementTableValuedFunction":"Functions","SqlDmlTrigger":"Triggers",
 "SqlIndex":"Indexes","SqlPrimaryKeyConstraint":"Constraints",
 "SqlUniqueConstraint":"Constraints","SqlForeignKeyConstraint":"Constraints",
 "SqlCheckConstraint":"Constraints","SqlDefaultConstraint":"Constraints",
 "SqlTableType":"Types","SqlUserDefinedDataType":"Types",
 "SqlSequence":"Sequences","SqlSchema":"Schemas","SqlRole":"Security",
 "SqlUser":"Security","SqlPermissionStatement":"Security"}
MODULES = {"SqlView","SqlProcedure","SqlScalarFunction","SqlInlineTableValuedFunction",
           "SqlMultiStatementTableValuedFunction","SqlDmlTrigger"}

def digest(value):
    return hashlib.sha256(value).hexdigest()

def export(path):
    if path.stat().st_size > 128*1024*1024: raise ValueError("DACPAC too large")
    with zipfile.ZipFile(path) as z:
        infos=z.infolist(); names=[e.filename for e in infos]
        if len(names)!=len(set(names)) or len(names)>4096: raise ValueError("Duplicate/excessive archive entries")
        if any(not n or n.startswith("/") or "\\" in n or ":" in n or ".." in n.split("/") for n in names):
            raise ValueError("Unsafe DACPAC entry path")
        if sum(e.file_size for e in infos)>512*1024*1024: raise ValueError("DACPAC expansion limit")
        # Never read Data/*.BCP or other data-bearing export payloads.
        if "model.xml" not in names or "Origin.xml" not in names: raise ValueError("DACPAC lacks model/origin")
        if z.getinfo("model.xml").file_size>100*1024*1024: raise ValueError("Model too large")
        model=z.read("model.xml"); origin=z.read("Origin.xml")
    for xml in (model,origin):
        if b"<!DOCTYPE" in xml.upper() or b"<!ENTITY" in xml.upper():
            raise ValueError("Unsafe XML declaration")
    o=ET.fromstring(origin)
    checks=[e for e in o.iter() if e.tag.endswith("Checksum") and e.get("Uri")=="/model.xml"]
    if len(checks)!=1 or (checks[0].text or "").lower()!=digest(model):
        raise ValueError("DACPAC model checksum mismatch")
    root=ET.fromstring(model)
    ns={"d":root.tag.split("}")[0][1:]}
    node=root.find("d:Model",ns)
    if node is None: raise ValueError("No DACPAC model")
    elements=list(node)
    if len(elements)>20000: raise ValueError("Excessive schema objects")
    files={}
    inventory=[]
    for i,e in enumerate(elements):
        typ=e.get("Type") or "Unknown"; name=e.get("Name")
        folder=KIND.get(typ,"OtherObjects")
        safe=re.sub(r"[^A-Za-z0-9_.-]+","_",name or typ).strip("._")[:74] or "unnamed"
        stem=safe+"-"+digest((typ+"\0"+str(name)+"\0"+str(i if name is None else 0)).encode())[:12]
        target=folder+"/"+stem+".xml"
        raw=ET.tostring(e,encoding="utf-8",xml_declaration=True)+b"\n"
        files[target]=raw
        sql=None
        if typ in MODULES:
            for prop in ("BodyScript","Script"):
                body=e.find("d:Property[@Name='"+prop+"']/d:Value",ns)
                if body is not None and body.text and body.text.strip():
                    sql=folder+"/"+stem+".sql"
                    header="-- Module body only. See companion XML for signatures and relationships.\n"
                    files[sql]=(header+body.text.replace("\r\n","\n").rstrip()+"\n").encode()
                    break
        inventory.append({"type":typ,"name":name,"definition":target,"body":sql,"sha256":digest(raw)})
    manifest={"formatVersion":1,"modelSha256":digest(model),"objectCount":len(inventory),
              "sqlBodyCount":sum(bool(x["body"]) for x in inventory),
              "objects":sorted(inventory,key=lambda x:(x["type"],x["name"] or "",x["definition"]))}
    files["SchemaManifest.json"]=(json.dumps(manifest,indent=2,ensure_ascii=False)+"\n").encode()
    files["README.md"]=(
        "# FMSC DACPAC schema reference\n\nGenerated from model.xml only, never exported school rows.\n\n"
        "Each XML file holds the complete original DACPAC model element, including nested columns, "
        "constraints and relationships. Supported SQL modules also have body-only SQL files. "
        "These body files are **not guaranteed executable CREATE statements**; consult the paired "
        "XML for signatures. This reference folder is NOT a migration, schema approval, "
        "or live database deployment input. Review changes using existing DACPAC controls.\n").encode()
    return files

def synchronize(folder,files,check):
    old={p.relative_to(folder).as_posix():p.read_bytes() for p in folder.rglob("*") if p.is_file()} if folder.exists() else {}
    changed=sorted(k for k in set(old)|set(files) if old.get(k)!=files.get(k))
    if check:
        if changed: raise ValueError("Schema folder differs: "+str(len(changed))+" files; run export")
        print("PASS: generated DACPAC schema is synchronized")
        return
    for key in changed:
        dest=folder/key
        if key in files:
            dest.parent.mkdir(parents=True,exist_ok=True)
            dest.write_bytes(files[key])
        elif dest.is_file(): dest.unlink()
    print("Exported "+str(len(files))+" files; changes: "+str(len(changed)))

def main():
    p=argparse.ArgumentParser()
    p.add_argument("--dacpac",type=Path,default=ROOT/"FMSC.dacpac")
    p.add_argument("--output",type=Path,default=ROOT/"DatabaseSchema")
    p.add_argument("--check",action="store_true")
    a=p.parse_args()
    if a.output.resolve()==ROOT.resolve(): raise ValueError("Refusing to overwrite repository root")
    synchronize(a.output,export(a.dacpac),a.check)

if __name__=="__main__": main()
