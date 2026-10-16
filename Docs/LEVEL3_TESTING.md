# Level 3 cloud reliability and acceptance

Level 3 reuses Level 1's owned Linux SQL container and Windows Desktop CI, plus the existing disposable Windows installer workflow. It does not rerun historical Level 2 outcomes as new passes. The 45-scenario catalog is [catalog.json](../Tests/Level3Checks/catalog.json). Composite scenarios retain `LOCAL REQUIRED` until every mandatory desktop/hardware assertion has been executed.

## Run the cloud SQL checks

Requirements: Linux/WSL, local Docker socket, .NET 8, Python 3.11+, registry/NuGet access and capacity for the existing 2-CPU/3-GiB SQL Server 2022 container. SqlPackage and the SQL image retain the existing pinned versions.

```bash
python -m unittest discover -s Tests/LifecycleSqlChecks -p 'test_*.py' -v
python -m unittest discover -s Tests/Level3Checks -p 'test_*.py' -v
python -m unittest discover -s Tests/Level2UiChecks -p 'test_*.py' -v
python Tests/LifecycleSqlChecks/run.py --level3 \
  --output /tmp/fmsc-level3-sql --state /tmp/fmsc-level3-owner.json
python Tests/Level3Checks/report.py --sql /tmp/fmsc-level3-sql/report.json \
  --output /tmp/fmsc-level3-matrix
```

`--dotnet` and `--sqlpackage` accept installed tools. The harness has no production connection/file argument. It uses only the existing separate data-free fixture, then unchanged production SQL/writers and runtime compatibility validation. The intentional repository `fmsc.dacpac` is neither read nor approved. Required base schema stays 6. Test-only fixture repair of the DacFx extended-property type is unchanged.

New fault tests live in `Tests/Level3Checks/ReliabilityChecks.cs`, linked only into the SQL test executable. Normal Level 1 execution remains independent. SQL interruption kills only the exact session with this run's application identity. Timeout/cancellation tests stage the real collection procedure inside an outer transaction and independently verify rollback. A separate process is killed after flushing the production collection journal; another process reloads it. These tests simulate narrow failures, not physical power loss.

Backup uses a previously absent container-private path, `COPY_ONLY`, checksums and `VERIFYONLY`. Restore requires an absent uniquely named catalog and absent destination files. The restored owner property, complete academic/financial hash, production compatibility and `CHECKDB` are checked before ownership-checked deletion. Container/volume removal always runs; backup files never leave the container.

The workload creates 1000 students through actual admission procedures, performs 20 production searches, and measures ten register/statement iterations. Results include median/p95, CPU/memory limits and actual host resources. No production-scale SLA is inferred. Compare equivalent runs on the same runner class and fixture rather than unrelated PCs.

If interrupted externally, repeat the existing owned cleanup command:

```bash
python Tests/LifecycleSqlChecks/run.py --cleanup \
  --output /tmp/fmsc-level3-sql --state /tmp/fmsc-level3-owner.json
```

## Windows checks and installation

Desktop CI retains all existing checks and adds `--level3` noninteractive production dialog/export checks. It creates/disposes 240 real dialog trees in six batches, records managed/native resource trends and generates a long-text 500-row multi-page PDF/CSV. This does not prove a full-day DB-backed operator session or visual report layout.

The existing **FMSC Disposable Windows Installation Acceptance** workflow accepts relevant PRs and manual dispatch. `Tools/RunDisposableWindowsInstall.ps1 -Level3` refuses execution outside an elevated, explicitly designated GitHub-hosted disposable Windows job. Do not try to bypass those guards on a school PC.

It runs the real installer for fresh installation, application update and reinstallation. Updates have no packaged MDF/LDF. It verifies SQL fixture records, database GUID and theme bytes, safely refuses a missing application executable, and exercises the unchanged production activation function against owned temporary folders with a narrowly injected partial move failure. A complete private ZIP is read back and checked for required assets and absence of database files, then deleted. Pristine MSI/wizard/reboot and physical interruption remain separate acceptance.

No installer runtime, compatibility, migration, financial or application workflow is changed. All failure injection lives in test code.

## Evidence

Actions upload only sanitized JSON/Markdown and numeric component measurements, retained for seven days. No databases, backups, credentials, raw child logs or generated PDFs/CSVs are uploaded. The SQL matrix artifact is partial until fresh Windows evidence is combined. Download the sanitized SQL, installer and component reports from the **same exact tested revision**; optional `--desktop` / `--security` accept normalized GitHub run metadata (`head_sha`, `status`, `conclusion`, `html_url`).

```bash
python Tests/Level3Checks/report.py --sql sql/report.json \
  --install install/report.json --windows windows/report.json \
  --desktop desktop-run.json --security security-run.json \
  --output combined
```

Mixed revisions, missing rows, dishonest counts, forged local passes and nonnumeric measurement payloads are rejected. The reporter preserves failed assertions and failed cleanup; it never turns local requirements into passes. Shared source stages support multiple acceptance rows, so per-row assertion counts are not additive unique totals. Reports are evidence, not schema approval or deployment authorization.

See [results](LEVEL3_RESULTS.md) and the [local checklist](LEVEL3_LOCAL_TEST_PLAN.md). Keep the PR unmerged until owner review.
