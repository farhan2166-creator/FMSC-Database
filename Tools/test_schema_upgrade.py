"""Synthetic approval attacks and real disposable migration acceptance. No school rows."""
import copy
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile

import refresh_dacpac as refresh
import schema_upgrade as upgrade
import test_dacpac_refresh as refresh_tests
from verify_schema_upgrade_sql import preserve_rows, verify_sql


def added_table(parts):
    parts = parts.copy()
    for name in list(parts):
        if name not in refresh.FIXTURE_ENTRIES: del parts[name]
    root = refresh.xml(parts["model.xml"]); ns = root.tag.split("}")[0] + "}"
    model = root.find(ns + "Model")
    table = ET.SubElement(model, ns + "Element", Type="SqlTable", Name="[dbo].[FMSC_UpgradeFixture]")
    schema = ET.SubElement(ET.SubElement(table, ns + "Relationship", Name="Schema"), ns + "Entry")
    ET.SubElement(schema, ns + "References", Name="[dbo]", ExternalSource="BuiltIns")
    column = ET.SubElement(ET.SubElement(ET.SubElement(table, ns + "Relationship", Name="Columns"), ns + "Entry"),
        ns + "Element", Type="SqlSimpleColumn", Name="[dbo].[FMSC_UpgradeFixture].[Id]")
    ET.SubElement(column, ns + "Property", Name="IsNullable", Value="True")
    typ = ET.SubElement(ET.SubElement(ET.SubElement(column, ns + "Relationship", Name="TypeSpecifier"), ns + "Entry"), ns + "Element", Type="SqlTypeSpecifier")
    entry = ET.SubElement(ET.SubElement(typ, ns + "Relationship", Name="Type"), ns + "Entry")
    ET.SubElement(entry, ns + "References", Name="[int]", ExternalSource="BuiltIns")
    parts["model.xml"] = ET.tostring(root)
    origin = refresh.xml(parts["Origin.xml"])
    next(e for e in origin.iter() if e.get("Uri") == "/model.xml").text = refresh.sha(parts["model.xml"])
    parts["Origin.xml"] = ET.tostring(origin)
    return parts


class UpgradeChecks(unittest.TestCase):
    package = staticmethod(refresh_tests.RefreshChecks.package)
    @classmethod
    def setUpClass(cls):
        refresh_tests.RefreshChecks.setUpClass.__func__(cls)
        for name in upgrade.PROTECTED:
            path = cls.seed / name
            if not path.exists():
                path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes((refresh.ROOT / name).read_bytes())
        subprocess.run(["git", "-C", str(cls.seed), "add", "."], check=True, capture_output=True)
        subprocess.run(["git", "-C", str(cls.seed), "-c", "commit.gpgsign=false", "commit", "-m", "Synthetic trusted contracts"], check=True, capture_output=True)
        cls.anchor = refresh.git(cls.seed, "rev-parse", "HEAD").decode().strip()
        cls.original_anchor = upgrade.LEGACY_APPROVAL_REF
        upgrade.LEGACY_APPROVAL_REF = cls.anchor
    @classmethod
    def tearDownClass(cls):
        upgrade.LEGACY_APPROVAL_REF = cls.original_anchor
        cls.store.cleanup()
    def setUp(self): refresh_tests.RefreshChecks.setUp(self)
    def fixture(self, migration=True):
        raw = self.package(added_table(self.parts)); (self.root / "FMSC.dacpac").write_bytes(raw)
        if migration:
            path = self.root / "Migrations/99.sql"; path.parent.mkdir(); path.write_text("CREATE TABLE dbo.FMSC_UpgradeFixture(Id int NULL);")
        subprocess.run(["git", "-C", str(self.root), "add", "."], check=True, capture_output=True)
        subprocess.run(["git", "-C", str(self.root), "-c", "commit.gpgsign=false", "commit", "-m", "Synthetic candidate"], check=True, capture_output=True)
        source = refresh.git(self.root, "rev-parse", "HEAD").decode().strip()
        return source, raw
    def test_missing_upgrade_path_is_explicit(self):
        source, _ = self.fixture(False)
        report, _ = upgrade.inspect_candidate(self.root, source, source, "")
        self.assertEqual("UPGRADE_PATH_REQUIRED", report["status"])
        self.assertFalse(report["deploymentReady"]); self.assertFalse(report["upgradePathVerified"])
    def test_unchanged_inspection_needs_no_approval_or_generated_proposal(self):
        report,_=upgrade.inspect_candidate(self.root,self.anchor,self.anchor,'')
        self.assertEqual('VERIFIED_UNCHANGED',report['status'])
        self.assertFalse(report['ownerApprovalRequired']);self.assertNotIn('reviewDigest',report)
    def test_structural_review_is_not_approval(self):
        source, _ = self.fixture()
        report, _ = upgrade.inspect_candidate(self.root, source, source, "Migrations/99.sql")
        self.assertEqual("STRUCTURAL_CHANGE_REVIEW_REQUIRED", report["status"])
        self.assertTrue(report["ownerApprovalRequired"]); self.assertFalse(report["compatibilityVerified"])
    def test_migration_path_traversal_and_nested_scripts_refused(self):
        source, _ = self.fixture()
        for path in ("../secret.sql", "Migrations/nested/99.sql", "Tools/exploit.py", "Migrations/99.sql,Migrations/99.sql"):
            with self.assertRaises(refresh.Refusal): upgrade.inspect_candidate(self.root, source, source, path)
    def test_stale_candidate_refused(self):
        source, _ = self.fixture()
        with self.assertRaisesRegex(refresh.Refusal, "STALE_CANDIDATE"): upgrade.inspect_candidate(self.root, source, self.anchor, "")
    def test_missing_full_commit_refused(self):
        with self.assertRaises(refresh.Refusal): upgrade.inspect_candidate(self.root, "main", "HEAD", "")
    def test_owner_only_default_branch_dispatch(self):
        info = {"private": True, "owner": {"login": "owner", "type": "User"}, "default_branch": "main"}
        upgrade.authorize("owner/private", "owner", "owner", "workflow_dispatch", "refs/heads/main", "a"*40, info)
        for actor, trigger, event, ref in (("writer","writer","workflow_dispatch","refs/heads/main"),
            ("owner","writer","workflow_dispatch","refs/heads/main"), ("owner","owner","pull_request","refs/heads/main"),
            ("owner","owner","workflow_dispatch","refs/heads/untrusted")):
            with self.assertRaises(refresh.Refusal): upgrade.authorize("owner/private", actor, trigger, event, ref, "a"*40, info)
    def test_public_or_organization_repository_refused(self):
        for info in ({"private":False,"owner":{"login":"owner","type":"User"}}, {"private":True,"owner":{"login":"owner","type":"Organization"}}):
            with self.assertRaises(refresh.Refusal): upgrade.authorize("owner/private","owner","owner","workflow_dispatch","refs/heads/main","a"*40,info)
    def test_requirements_cannot_be_removed_or_weakened(self):
        new = copy.deepcopy(self.contract); new["columns"][0]["maxLength"] += 1
        with self.assertRaisesRegex(refresh.Refusal,"PROTECTED_COMPATIBILITY"): upgrade.preserve_contract(self.contract,new)
    def test_preserving_existing_records_allows_reference_additions(self):
        before = {"recoveryScope":"fixture", "tables":{"dbo.fixture":{"columns":"[Id]","rows":["a","b"]}}}
        after = copy.deepcopy(before); after["tables"]["dbo.fixture"]["rows"].append("c")
        preserve_rows(before,after)
        after["tables"]["dbo.fixture"]["rows"].remove("a")
        with self.assertRaisesRegex(refresh.Refusal,"LOST_EXISTING_RECORDS"): preserve_rows(before,after)
    def test_recovery_scope_change_refused(self):
        with self.assertRaisesRegex(refresh.Refusal,"RECOVERY_IDENTITY"):
            preserve_rows({"recoveryScope":"a","tables":{}},{"recoveryScope":"b","tables":{}})
    def test_edited_hashes_without_owner_receipt_cannot_approve(self):
        source,_=self.fixture(); (self.root/refresh.APPROVAL).write_text('{}')
        subprocess.run(["git","-C",str(self.root),"add","."],check=True,capture_output=True)
        subprocess.run(["git","-C",str(self.root),"-c","commit.gpgsign=false","commit","-m","Synthetic forged approval"],check=True,capture_output=True)
        with self.assertRaisesRegex(refresh.Refusal,"OWNER_APPROVAL_RECEIPT_REQUIRED"):
            upgrade.audit_approval(self.root,self.anchor,lambda *args:None,"owner/private")
    def test_replaced_complete_baseline_cannot_bless_itself_on_main(self):
        _,folder,_,_,_,_=self.prepared()
        for name in refresh.SYNC_PATHS:(self.root/name).write_bytes((folder/name).read_bytes())
        subprocess.run(['git','-C',str(self.root),'add','.'],check=True,capture_output=True)
        subprocess.run(['git','-C',str(self.root),'-c','commit.gpgsign=false','commit','-m','Synthetic self-declared baseline'],check=True,capture_output=True)
        source=refresh.git(self.root,'rev-parse','HEAD').decode().strip()
        with self.assertRaisesRegex(refresh.Refusal,'BASELINE_STRUCTURE_HAS_NO_VERIFIED_OWNER_APPROVAL'):
            upgrade.inspect_candidate(self.root,source,source,'')
    def test_sanitized_report(self):
        source,_=self.fixture(); report,_=upgrade.inspect_candidate(self.root,source,source,"")
        folder=self.root/'report';upgrade.write_report(folder,report)
        self.assertNotIn('fictional-fixture-only',(folder/'report.json').read_text())
        self.assertNotIn('FMSC_UpgradeFixture',(folder/'summary.md').read_text())

    def prepared(self):
        source, raw = self.fixture()
        report, _ = upgrade.inspect_candidate(self.root, source, source, 'Migrations/99.sql')
        catalog = copy.deepcopy(self.contract)
        catalog['objects'].append({'schema':'dbo','name':'FMSC_UpgradeFixture','type':'U '})
        catalog['columns'].append({'schema':'dbo','objectName':'FMSC_UpgradeFixture','name':'Id','type':'int',
            'maxLength':4,'precision':10,'scale':0,'isNullable':True,'isIdentity':False,'isComputed':False})
        folder=self.root/'proposal';folder.mkdir()
        digest=upgrade.proposal(self.root,report,raw,catalog,folder)
        context={'repository':'fixture/private','source':source,'actor':'fixture','triggeringActor':'fixture',
            'event':'workflow_dispatch','ref':'refs/heads/main','runId':'123','runAttempt':'1'}
        calls=[]
        def api(method,path,data=None):
            calls.append((method,path,data))
            if path=='/repos/fixture/private':return {'private':True,'default_branch':'main','owner':{'login':'fixture','type':'User'}}
            if path.endswith('/git/ref/heads/main'):return {'object':{'sha':source}}
            if '/git/ref/heads/automation/' in path:return None
            if '/git/commits/' in path:return {'tree':{'sha':'1'*40}}
            if method=='POST' and path.endswith(('/git/blobs','/git/trees','/git/commits')):return {'sha':'2'*40}
            if '/pulls?' in path:return []
            if path.endswith('/pulls'):return {'html_url':'https://github.com/fixture/private/pull/1'}
            if '/actions/runs/' in path:return {'actor':{'login':'fixture'},'triggering_actor':{'login':'fixture'},
                'event':'workflow_dispatch','head_branch':'main','head_sha':source,'path':upgrade.WORKFLOW,
                'conclusion':'success','display_title':'Schema approve '+source+' '+digest}
            return {}
        return source,folder,digest,context,calls,api

    def test_verified_proposal_is_deterministic_and_uses_no_new_school_payload(self):
        _,folder,digest,_,_,_=self.prepared()
        manifest=refresh.read_json((folder/'manifest.json').read_bytes())
        self.assertEqual(digest,refresh.sha(upgrade.fixed_json(manifest)))
        self.assertEqual(set(refresh.SYNC_PATHS),set(manifest['updates']))
        self.assertEqual(refresh.FIXTURE_ENTRIES,set(refresh.archive((folder/refresh.FIXTURE).read_bytes())))

    def test_owner_confirmation_creates_only_draft_and_dispatches_all_checks(self):
        _,folder,digest,context,calls,api=self.prepared()
        upgrade.publish(self.root,folder,digest,'APPROVE:'+digest,api,context)
        request=next(data for method,path,data in calls if method=='POST' and path.endswith('/pulls'))
        self.assertTrue(request['draft'])
        tree=next(data for method,path,data in calls if path.endswith('/git/trees'))
        self.assertEqual(set(upgrade.PATHS),{f['path'] for f in tree['tree']})
        self.assertEqual(4,sum(path.endswith('/dispatches') for _,path,_ in calls))
        self.assertFalse(any('/merge' in path for _,path,_ in calls))

    def test_wrong_confirmation_and_substituted_proposal_refuse_before_writes(self):
        _,folder,digest,context,calls,api=self.prepared()
        with self.assertRaisesRegex(refresh.Refusal,'EXACT_REVIEW_DIGEST'):
            upgrade.publish(self.root,folder,digest,'APPROVE:'+'0'*64,api,context)
        (folder/refresh.APPROVAL).write_text('{}')
        with self.assertRaisesRegex(refresh.Refusal,'SUBSTITUTED_APPROVAL_PAYLOAD'):
            upgrade.publish(self.root,folder,digest,'APPROVE:'+digest,api,context)
        self.assertFalse(any(method!='GET' for method,_,_ in calls))

    def test_forged_receipt_and_wrong_actor_run_cannot_authorize(self):
        source,folder,digest,context,calls,api=self.prepared()
        upgrade.publish(self.root,folder,digest,'APPROVE:'+digest,api,context)
        tree=next(data for method,path,data in calls if path.endswith('/git/trees'))
        blobs=[data for method,path,data in calls if path.endswith('/git/blobs')]
        import base64
        for entry,blob in zip(tree['tree'],blobs):
            path=self.root/entry['path'];path.write_bytes(base64.b64decode(blob['content']))
        subprocess.run(['git','-C',str(self.root),'add','.'],check=True,capture_output=True)
        subprocess.run(['git','-C',str(self.root),'-c','commit.gpgsign=false','commit','-m','Synthetic owner proposal'],check=True,capture_output=True)
        self.assertEqual('STRUCTURAL_CHANGE_APPROVED',upgrade.audit_approval(self.root,source,api,'fixture/private'))
        def inspection_only(method,path,data=None):
            value=api(method,path,data)
            if '/actions/runs/' in path:value['display_title']='Schema inspect '+source+' '+digest
            return value
        with self.assertRaisesRegex(refresh.Refusal,'TRUSTED_APPROVAL_RUN'):
            upgrade.audit_approval(self.root,source,inspection_only,'fixture/private')
        def forged(method,path,data=None):
            value=api(method,path,data)
            if '/actions/runs/' in path:value['actor']['login']='writer'
            return value
        with self.assertRaisesRegex(refresh.Refusal,'OWNER_APPROVAL'):
            upgrade.audit_approval(self.root,source,forged,'fixture/private')
        (self.root/refresh.APPROVAL).write_text('{}')
        with self.assertRaisesRegex(refresh.Refusal,'PAYLOAD_DIFFERS'):
            upgrade.audit_approval(self.root,source,api,'fixture/private')


def sql_self_test():
    # Copy code only, never the operational/exported database. Seed with the already
    # reviewed data-free fixture, then add one wholly fictional table/migration.
    with tempfile.TemporaryDirectory(prefix="fmsc-upgrade-integration-") as folder:
        root = Path(folder) / "repo"
        root.mkdir()
        # Only tracked source/contract files enter the synthetic repository.
        # Untracked settings, images, exports and database files are never copied.
        allowed = {'.cs','.csproj','.sln','.json','.props','.targets','.sql','.py','.config','.toml'}
        for name in refresh.git(refresh.ROOT,'ls-files','-z').decode().split('\0'):
            if not name: continue
            relative=Path(name); source=refresh.ROOT/relative
            if relative.parts[0] in ('Schema','Audit','Reports','Resources') or relative.suffix.lower() not in allowed:
                continue
            if source.is_symlink(): raise AssertionError('Test source symlink is unsupported')
            destination=root/relative;destination.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,destination)
        raw = (refresh.ROOT / refresh.FIXTURE).read_bytes()
        parts = refresh.archive(raw); actual=refresh.fingerprint(parts,raw)[3]
        approved=refresh.read_json((refresh.ROOT/refresh.APPROVAL).read_bytes())
        recorded=refresh.read_json((refresh.ROOT/refresh.PROVENANCE).read_bytes())
        refresh.fixture_valid(raw,recorded,approved)
        (root/refresh.FIXTURE).write_bytes(raw)
        (root/'FMSC.dacpac').write_bytes(raw)
        (root/refresh.APPROVAL).write_text(json.dumps(actual))
        contract=refresh.read_json((root/refresh.CONTRACT).read_bytes());contract['schemaArtifact']['sha256']=actual['packageSha256']
        (root/refresh.CONTRACT).write_text(json.dumps(contract))
        provenance=refresh.read_json((root/refresh.PROVENANCE).read_bytes());provenance.update(sourceGitBlob=refresh.git_blob(raw),sourcePackageSha256=actual['packageSha256'])
        (root/refresh.PROVENANCE).write_text(json.dumps(provenance))
        def git(*args):return refresh.git(root,*args)
        git('init','-b','main');git('config','user.name','Synthetic schema fixture');git('config','user.email','fixture@example.invalid')
        git('add','.');git('-c','commit.gpgsign=false','commit','-m','Synthetic data-free baseline')
        # Only this isolated test process substitutes its fictional trust root.
        upgrade.LEGACY_APPROVAL_REF=git('rev-parse','HEAD').decode().strip()
        candidate=refresh_tests.RefreshChecks.package(added_table(parts));(root/'FMSC.dacpac').write_bytes(candidate)
        migration=root/'Migrations/99.sql';migration.parent.mkdir(exist_ok=True);migration.write_text('CREATE TABLE dbo.FMSC_UpgradeFixture(Id int NULL);')
        git('add','.');git('-c','commit.gpgsign=false','commit','-m','Synthetic additive migration')
        source=git('rev-parse','HEAD').decode().strip()
        report,raw=upgrade.inspect_candidate(root,source,source,'Migrations/99.sql')
        if report['reasons']:raise AssertionError(report['reasons'])
        catalog=verify_sql(root,report,raw)
        digest=upgrade.proposal(root,report,raw,catalog,root/'proposal')
        if not digest or not report['upgradePathVerified']:raise AssertionError('No verified proposal')
        print('PASS: real SQL old/fresh production lifecycles, additive migration, row/recovery preservation, schema equivalence, proposal and cleanup.')
        # A destructive script is tested ONLY on the next owned fictional fixture.
        bad=root/'Migrations/100.sql';bad.write_text("UPDATE dbo.Students SET StudentName=N'Fictional invalid migration rewrite';")
        git('add','.');git('-c','commit.gpgsign=false','commit','-m','Synthetic rejected data rewrite')
        source=git('rev-parse','HEAD').decode().strip()
        rejected,raw=upgrade.inspect_candidate(root,source,source,'Migrations/99.sql,Migrations/100.sql')
        try: verify_sql(root,rejected,raw)
        except refresh.Refusal as ex:
            if str(ex)!='MIGRATION_CHANGED_OR_LOST_EXISTING_RECORDS':raise
        else:raise AssertionError('A rewritten original student record was accepted')
        print('PASS: real SQL migration that rewrites existing fictional records is rejected; owned cleanup completed.')


if __name__=='__main__':
    if '--sql' in sys.argv: sql_self_test()
    else: unittest.main()
