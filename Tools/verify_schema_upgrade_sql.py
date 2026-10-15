"""Disposable migration acceptance; reuse Level 1 ownership, fixtures and writers.

No production endpoint is accepted. Private definitions and fictional row snapshots
are deleted; only a verified catalog is returned to the approval planner.
"""
import collections
import importlib.util
import json
import os
from pathlib import Path
import re
import secrets
import signal
import subprocess
import tempfile
import time
import uuid

import refresh_dacpac as refresh
from export_dacpac_schema import prepare_model_only


def preserve_rows(before, after):
    if before["recoveryScope"] != after["recoveryScope"]:
        raise refresh.Refusal("MIGRATION_CHANGED_RECOVERY_IDENTITY")
    for table, original in before["tables"].items():
        current = after["tables"].get(table)
        if current is None or current["columns"] != original["columns"]:
            raise refresh.Refusal("MIGRATION_REMOVED_OR_CHANGED_EXISTING_COLUMNS")
        if collections.Counter(original["rows"]) - collections.Counter(current["rows"]):
            raise refresh.Refusal("MIGRATION_CHANGED_OR_LOST_EXISTING_RECORDS")


def verify_sql(project, report, raw):
    spec = importlib.util.spec_from_file_location("upgrade_level1", project / "Tests/LifecycleSqlChecks/run.py")
    level1 = importlib.util.module_from_spec(spec); spec.loader.exec_module(level1)
    token = uuid.uuid4(); container = "fmsc-level1-" + token.hex
    env = level1.docker_env()
    secret = "A!" + secrets.token_urlsafe(32) + "9z"
    env.update(SQLCMDPASSWORD=secret, FMSC_TEST_RUN_ID=str(token), DOTNET_CLI_TELEMETRY_OPTOUT="1")
    if os.environ.get("GITHUB_ACTIONS") == "true": print("::add-mask::" + secret, flush=True)
    dotnet = os.environ.get("FMSC_UPGRADE_DOTNET", "dotnet")
    cli = project / "Tests/LifecycleSqlChecks/LifecycleSqlChecks.csproj"
    tools = project / "Tests/LifecycleSqlChecks"
    docker = level1.docker_command()
    with tempfile.TemporaryDirectory(prefix="fmsc-upgrade-sql-") as directory:
        private = Path(directory)
        state = Path(os.environ.get("RUNNER_TEMP", directory)) / "fmsc-schema-upgrade-owner.json"
        if state.exists(): raise refresh.Refusal("PREVIOUS_DISPOSABLE_OWNER_STATE_REQUIRES_CLEANUP")
        env.update(FMSC_TEST_PRIVATE_ROOT=str(private), XDG_DATA_HOME=str(private / "data"))
        def execute(command, timeout=300, cwd=project):
            result = subprocess.run(command, cwd=cwd, env=env, capture_output=True, text=True, timeout=timeout)
            if result.returncode:
                if os.environ.get("FMSC_UPGRADE_DIAGNOSTICS"):
                    Path(os.environ["FMSC_UPGRADE_DIAGNOSTICS"]).write_text(stage + "\n" + result.stdout + "\n" + result.stderr)
                # No SQL bodies, connections, fictional personal data or credentials in CI logs.
                raise refresh.Refusal("DISPOSABLE_SQL_COMMAND_FAILED:" + stage)
            return result.stdout
        def sql(db, body):
            script = private / "query.sql"
            if script.exists(): script.chmod(0o600)
            script.write_text(body); script.chmod(0o444)
            execute(docker + ["cp", str(script), container + ":/tmp/upgrade.sql"])
            return execute(docker + ["exec", "--env", "SQLCMDPASSWORD", container, "/opt/mssql-tools18/bin/sqlcmd",
                "-S", "localhost", "-U", "sa", "-C", "-b", "-d", db, "-i", "/tmp/upgrade.sql"], timeout=180)
        def run(db, mode):
            env["FMSC_AUDIT_CONNECTION"] = f"Server=127.0.0.1,{port};Database={db};User ID=sa;Password={secret};Encrypt=False;TrustServerCertificate=True;Connect Timeout=15"
            path = private / (db + (mode or "lifecycle") + ".json")
            env["FMSC_TEST_REPORT_PATH"] = str(path)
            env["FMSC_SCHEMA_UPGRADE_PRIVATE_OUTPUT"] = str(path)
            execute([dotnet, "run", "--project", str(cli), "-c", "Release", "--no-build", "--"] + ([mode] if mode else []))
            result = json.loads(path.read_text())
            if not mode and any(s["Result"] != "PASS" for s in result["scenarios"]):
                raise refresh.Refusal("PRODUCTION_LIFECYCLE_ASSERTION_FAILED")
            return result
        def interrupted(signum, _): raise SystemExit(128 + signum)
        previous_handlers = {sig: signal.signal(sig, interrupted) for sig in (signal.SIGTERM, signal.SIGINT)}
        stage = "tooling"
        try:
            execute([dotnet, "build", str(cli), "-c", "Release"])
            package_exe = os.environ.get("FMSC_UPGRADE_SQLPACKAGE")
            if not package_exe: execute([dotnet, "tool", "restore"], cwd=tools)
            prefix = [package_exe] if package_exe else [dotnet, "tool", "run", "sqlpackage", "--"]
            credentials = private / "sql.env"; credentials.write_text("ACCEPT_EULA=Y\nMSSQL_SA_PASSWORD=" + secret + "\n"); credentials.chmod(0o600)
            state.write_text(json.dumps({"owner": str(token), "container": container}))
            stage = "owned-container-provisioning"
            execute(docker + ["run", "-d", "--name", container, "--label", level1.LABEL + "=" + str(token),
                "--env-file", str(credentials), "--memory", "3g", "--cpus", "2", "-p", "127.0.0.1::1433", level1.IMAGE])
            deadline = time.monotonic() + 150
            while True:
                ready = subprocess.run(docker + ["exec", "--env", "SQLCMDPASSWORD", container, "/opt/mssql-tools18/bin/sqlcmd",
                    "-S", "localhost", "-U", "sa", "-C", "-b", "-Q", "SELECT 1;"], env=env, capture_output=True, timeout=10)
                if ready.returncode == 0: break
                if time.monotonic() >= deadline: raise refresh.Refusal("DISPOSABLE_ENGINE_READINESS_TIMEOUT")
                time.sleep(1)
            binding = json.loads(execute(docker + ["inspect", container]))[0]["NetworkSettings"]["Ports"]["1433/tcp"]
            if len(binding) != 1 or binding[0]["HostIp"] != "127.0.0.1" or not re.fullmatch(r"[1-9][0-9]{0,4}", binding[0]["HostPort"]):
                raise refresh.Refusal("UNSAFE_DISPOSABLE_PORT_BINDING")
            port = binding[0]["HostPort"]
            for name in ("previous", "candidate"):
                path = private / (name + ".dacpac")
                if name == "previous":
                    path.write_bytes(refresh.git(project, "show", report["sourceCommit"] + ":" + refresh.FIXTURE))
                else:
                    source = private / "candidate-private.dacpac"; source.write_bytes(raw); prepare_model_only(source, path)
                db = "FMSC_AuditChecks_" + name + "_" + token.hex
                stage = name + "-fresh-publish"
                connection = f"Server=127.0.0.1,{port};Database={db};User ID=sa;Password={secret};Encrypt=False;TrustServerCertificate=True"
                execute(prefix + ["/Action:Publish", "/SourceFile:" + str(path), "/TargetConnectionString:" + connection, "/p:CreateNewDatabase=True"], cwd=tools)
                sql(db, "DECLARE @Owner uniqueidentifier='" + str(token) + "'; EXEC sys.sp_addextendedproperty @name=N'FMSC_Level1Owner',@value=@Owner;")
                stage = name + "-production-lifecycle"
                run(db, "")
                if name == "previous":
                    previous_db = db
                    before = run(db, "--upgrade-snapshot")
                    stage = "reviewed-migrations-on-populated-fixture"
                    for item in report["migrations"]:
                        migration = refresh.git(project, "show", report["sourceCommit"] + ":" + item["path"])
                        if refresh.sha(migration) != item["sha256"]: raise refresh.Refusal("MIGRATION_HASH_CHANGED")
                        sql(db, migration.decode("utf-8-sig"))
                    env["FMSC_SCHEMA_UPGRADE_SNAPSHOT_BASE"] = str(private / (db + "--upgrade-snapshot.json"))
                    after = run(db, "--upgrade-snapshot"); preserve_rows(before, after)
                    env.pop("FMSC_SCHEMA_UPGRADE_SNAPSHOT_BASE")
                    upgraded = run(db, "--upgrade-catalog")
                else:
                    fresh = run(db, "--upgrade-catalog")
            stage = "upgrade-target-schema-equivalence"
            for section in ("modules", "checks", "defaults", "indexes", "permissions", "roleMembership"):
                if upgraded[section] != fresh[section]:
                    raise refresh.Refusal("MIGRATION_TARGET_SCHEMA_DIFFERS:" + section)
            # Catalogs also cover every table/column/type, signature, key and relationship.
            def normalize(value):
                if isinstance(value, dict): return {k: normalize(v) for k, v in value.items() if v is not None}
                if isinstance(value, list): return [normalize(v) for v in value]
                return value
            fresh_contract = normalize(fresh["contract"])
            migrated_contract = normalize(upgraded["contract"])
            # Generated SQL constraint names may vary, but their definition and table cannot.
            def semantic(contract):
                value = normalize(contract)
                for item in value["checkConstraints"]:
                    if item.get("isSystemNamed"): item.pop("name", None)
                for item in value["uniqueIndexes"]: item.pop("name", None)
                return {k: sorted((json.dumps(v, sort_keys=True) for v in values)) for k, values in value.items()}
            if semantic(fresh_contract) != semantic(migrated_contract):
                raise refresh.Refusal("MIGRATED_STRUCTURAL_CONTRACT_DIFFERS_FROM_FRESH_CANDIDATE")
            report["sqlChecks"] = ["Previous production lifecycle", "Candidate production lifecycle", "Fresh production compatibility",
                "Reviewed migration on populated database", "Existing record preservation", "Recovery identity preservation",
                "Complete target catalog/module/permission equivalence", "Owned SQL cleanup"]
            return fresh_contract
        finally:
            try: level1.remove_owned(state)
            finally:
                for sig, handler in previous_handlers.items(): signal.signal(sig, handler)
