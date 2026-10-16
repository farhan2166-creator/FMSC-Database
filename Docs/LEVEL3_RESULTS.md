# Level 3 cloud reliability results — 16 October 2026

**45 acceptance scenarios: 24 PASS, 0 FAIL, 4 BLOCKED, 0 NOT RUN, 17 LOCAL REQUIRED.** All 45 appear in the [detailed scenario table](Level3Evidence/2026-10-16/summary.md) and [structured JSON](Level3Evidence/2026-10-16/report.json). Cloud SQL reliability is verified for the synthetic fixture. Full desktop/installation/deployment acceptance is **not established**.

Tested implementation: **`84d019cb3ec9a904f97312b16cf80d21f1ba7b67`**, branch `test/level3-cloud-acceptance`, [PR #48](https://github.com/farhan2166-creator/FMSC-Database/pull/48). The SQL run began with a clean working tree. Later changes add results/documentation only; application and test sources are unchanged. No merge is authorized or performed.

## Results by category

| Category | Planned | PASS | FAIL | BLOCKED | NOT RUN | LOCAL REQUIRED |
|---|---:|---:|---:|---:|---:|---:|
| Financial integrity | 8 | 8 | 0 | 0 | 0 | 0 |
| Failure/recovery | 7 | 6 | 0 | 0 | 0 | 1 |
| Database safety | 5 | 4 | 0 | 0 | 0 | 1 |
| Installation/updates | 5 | 0 | 0 | 4 | 0 | 1 |
| Security/permissions | 5 | 2 | 0 | 0 | 0 | 3 |
| Usability/layout | 5 | 0 | 0 | 0 | 0 | 5 |
| Performance/resources | 4 | 2 | 0 | 0 | 0 | 2 |
| Reports/printing | 4 | 0 | 0 | 0 | 0 | 4 |
| Optional communication | 2 | 2 | 0 | 0 | 0 | 0 |
| **Total** | **45** | **24** | **0** | **4** | **0** | **17** |

A partially completed scenario remains LOCAL REQUIRED where mandatory operator/display/printing assertions are outstanding. Those rows retain separate actual cloud sub-results. Four installer scenarios are BLOCKED because their Windows job never obtained a runner. The existing Level 2 47-pass/1-blocked historical catalog was read and not counted as fresh execution.

## Actual execution and environment

Codex Cloud: Linux `6.18.44` x86_64, .NET SDK `8.0.425`, Python `3.12.14`, Docker `28.4.0`, SQL Server 2022 `16.0.4295.3`, required schema **6**. Host: 3 logical CPUs, 9968 MiB reported RAM; SQL container limited to **2 CPUs / 3072 MiB**. Existing SQL image digest and SqlPackage pin were preserved. No Windows desktop or printer was available.

The [successful SQL evidence](Level3Evidence/2026-10-16/sql/summary.md) contains **65 underlying stages PASS, 0 FAIL, 0 BLOCKED**, including Level 1 suites, additional Level 3 checks, portable regressions, performance/state checks, fake communication tests and cleanup. It records **35 expected SQL refusals**. Underlying stages are not the same as 45 composite acceptance scenarios, and shared-stage assertion counts must not be summed as unique tests.

Commands actually run:

```bash
# Standard SDK/cache variables were set to the installed /workspace tool locations.
python3 -m unittest discover -s Tests/LifecycleSqlChecks -p 'test_*.py' -v
python3 -m unittest discover -s Tests/Level3Checks -p 'test_*.py' -v
python3 -m unittest discover -s Tests/Level2UiChecks -p 'test_*.py' -v
python3 Tests/LifecycleSqlChecks/run.py --level3 \
  --output /workspace/work/level3-84d019c/evidence \
  --state /workspace/work/level3-84d019c/owner.json \
  --dotnet /workspace/.dotnet/dotnet \
  --sqlpackage /workspace/work/sqlpackage/sqlpackage

dotnet restore FMSC_SMS.sln -p:EnableWindowsTargeting=true --disable-parallel --disable-build-servers
dotnet build FMSC_SMS.sln -c Release -p:EnableWindowsTargeting=true \
  --no-restore --disable-build-servers -m:1 -nr:false
dotnet restore Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -p:EnableWindowsTargeting=true --force --disable-parallel
dotnet build Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -c Release \
  -p:EnableWindowsTargeting=true --no-restore --disable-build-servers -m:1 -nr:false

python3 Tools/test_security.py --gitleaks /workspace/work/security-automation/gitleaks
python3 Tools/check_security.py --gitleaks /workspace/work/security-automation/gitleaks
/workspace/work/pwsh/pwsh -NoProfile -File Tools/TestInstallerMigrations.ps1
/workspace/work/pwsh/pwsh -NoProfile -File Tools/TestInstallerLocalDb.ps1
/workspace/work/pwsh/pwsh -NoProfile -File Tests/Level3Checks/TestActivation.ps1
```

The solution build compiles application, worker and host: **0 warnings / 0 errors**. Native Windows tests compile; they cannot execute on Linux. Initial solution attempts lacked restored optional-project assets; restoring them resolved the build failure. Initial cached NU1900 vulnerability-lookup warnings were addressed by a network-enabled restore without disabling audit checks. PowerShell used the installed .NET runtime and private XDG directories; mocked MSI tests required `TEMP=/tmp` to supply the Windows temporary-directory convention on Linux.

Other fresh results: fixture/ownership tests **10 pass**; Level 3 reporting tests **8 pass**; Level 2 evidence-safety tests **5 pass / 1 Windows-only skip**; security dummy tests **8 pass** and full history/forbidden-file scan passed. Portable checks **832**, synthetic state/performance checks **32,183**, fake-provider checks **42**, registration SQL **72**, mutation SQL **46**, administration SQL **67**. PowerShell migration checks passed 15 scenarios plus SQL-wrapper/cleanup/activation-order assertions. MSI tests used fake process results, not a real installer. YAML and PowerShell syntax validation passed. [Supplementary evidence](Level3Evidence/2026-10-16/supplementary-checks.json) preserves these distinctions.

## Financial integrity and recovery

- Admission creates the exact **7000** opening bill. Monthly posting adds **5000** once; **4000 + 8000** collections settle **12000** with exact SQL allocations. A **1200 − 200** additional charge settles **1000**.
- Historical collections remain **13000**; next-year collections remain **3000**. Promotion/return/new-year operations preserve the original canonical financial snapshot.
- A **200** refund is committed once despite lost acknowledgement, stored as negative **200**, and reopens exactly **200** of dues. Read-only reconciliation and exact retry return the original result.
- A distinct **100 → 80** correction persists negative **20**; a separate **200** collection/reversal restores the prior due **6920**, retains the reversed payment identity and reconciles completed payments/allocations to **80**.
- Four competing monthly requests create one charge. Two concurrent full-balance requests create one collection and reject the stale allocation. Separate SQL connections verify every committed result.
- Outer rollback, killed owned SQL session, command timeout and requested cancellation preserve dues after rollback. A deliberate retry occurs only after confirmed rollback.
- A separate process is killed after flushing the production collection journal. Another process reloads the same intent; three resubmission attempts are refused until explicit review. No server-side collection request ID or universal blind retry is claimed.
- Runtime SQL rejects revoked/restricted permissions. Tests exercise application-user permission procedures, not a claim that SQL `sa` itself is restricted from arbitrary DML.

## Database safety, backup and cleanup

A real engine **COPY_ONLY/CHECKSUM** backup is verified with `RESTORE VERIFYONLY`, then restored to an absent `FMSC_AuditChecks_Restore_<GUID>` catalog and absent container-private files. The owner marker, complete students/enrollments/charges/payments/allocations hash, production runtime contract and `DBCC CHECKDB` all pass after restore. Versions 5/7 and version 6 missing a required procedure are safely refused; restoring the test definition preserves all records.

Restored-catalog removal verifies ownership first. Container/volume removal was confirmed after successful execution. The existing `--inject-provision-failure` diagnostic was also executed: expected exit **1**, preserved intentional FAIL evidence, **Cleanup PASS**, no retained owner state/container. [Failure-cleanup evidence](Level3Evidence/2026-10-16/intentional-provision-failure.json).

No original school files or operational database were accessed. Main `fmsc.dacpac`, both compatibility manifests, runtime database connection logic, SQL/migrations, finance/security rules and `Installer/core.ps1` have an empty diff. Only the separate data-free fixture is used; its established test-only version/reference seeding and typed recovery-property correction remain explicit. This does not prove compatibility of an independently supplied MDF/LDF pair. No automatic DACPAC/provenance verification or approval was added.

## Performance baseline

| Measurement | Earlier `cbf6a40` run | Final `84d019c` run |
|---|---:|---:|
| 1000 production admissions | 7.664 s | 7.053 s |
| Production search median / p95, 20 samples | 7.586 / 9.290 ms | 7.172 / 9.149 ms |
| Register + statement median / p95, 10 samples | 16.385 / 51.701 ms | 17.384 / 52.932 ms |
| Total workload students | 1001 | 1002 |

Median fields use the upper middle observation for even sample counts; p95 uses the nearest-rank observation. These repeated bounded runs establish a rerunnable cloud reference, not a demonstrated optimization or production-scale SLA. The earlier revision had one fewer prerequisite student; host scheduling also varies. Future comparisons should fix revision, engine, fixture, runner class, workload and background activity. Full-day memory, DB-backed screen cycles and large real-world report layout remain local requirements.

## Defects and infrastructure corrections

No verified production business-logic defect was discovered in the executed scenarios. This is not a claim that unexecuted screens or installation cases are defect-free.

One test expectation defect was fixed: SqlClient 7.1.1 surfaces requested cancellation as SQL error **0** here rather than `OperationCanceledException`. The test accepts that error only after its cancellation token was requested and still independently checks rollback. The [before evidence](Level3Evidence/2026-10-16/cancellation-before.json) identifies the initial uncommitted diagnostic correctly; the final committed run passes. Missing restore assets and Linux PowerShell environment setup were test-environment issues. No financial logic or expected monetary values were changed to obtain passes.

## GitHub results and current blocker

The owner confirmed the **GitHub repository Actions limit is exhausted** and requested Windows execution on the local PC later. Both attempted PR revisions ended before allocation: every job has **runner_id 0, no runner name and zero steps**. These are GitHub conclusions **failure**, classified as **BLOCKED test execution**, not failed application assertions. No GitHub pass or private installer ZIP generation/upload is claimed. Installer artifact listing was empty; no database files were uploaded.

| Workflow at tested code revision | Actual run | Outcome |
|---|---|---|
| SQL lifecycle / Level 3 | [38848906564](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38848906564) | BLOCKED before runner; same SQL suite passed in Codex Cloud |
| Desktop CI | [38848906567](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38848906567) | BLOCKED before runner; native execution unverified |
| Disposable Windows installer | [38848906575](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38848906575) | BLOCKED before runner; four installation scenarios outstanding |
| Security | [38848906600](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38848906600) | BLOCKED before runner; scanner/tests passed locally in Codex Cloud |

The first attempt at `cbf6a40` had the same pre-run failure: [Desktop](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38848313560), [SQL](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38848313529), [Installer](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38848313582), [Security](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/38848313605). [Structured GitHub evidence](Level3Evidence/2026-10-16/workflow-runs.json) preserves the final attempted revision. Later documentation-only commits may also receive the same exhausted-limit result; they do not replace the recorded actual SQL evidence.

## Remaining acceptance and readiness

**17 LOCAL REQUIRED scenarios plus 4 Windows installer scenarios BLOCKED** remain incomplete. The [local plan](LEVEL3_LOCAL_TEST_PLAN.md) covers operator workflows, real DPI/resolution/mixed-monitor behavior, sustained memory, actual SQL-backed report rendering, printer output, LocalDB file faults and pristine-MSI/reboot acceptance. Native component tests and controlled activation rollback still need Windows execution despite their successful compilation / Linux filesystem subset.

Financial consistency, disposable SQL backup/restore/integrity and controlled recovery are verified for the tested fixture. Physical power loss, disk failure, genuine provider delivery, distributed school database compatibility and deployment acceptance are not established. Keep [PR #48](https://github.com/farhan2166-creator/FMSC-Database/pull/48) open and unmerged pending Windows/local results and owner approval.
