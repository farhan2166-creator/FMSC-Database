# Development, Code Review, and Documentation Workflow

These instructions are for maintainers and Codex working on FMSC_SMS.

## One change at a time

1. Begin at the latest `main`, check that the workspace is clean, and read [AGENTS.md](../AGENTS.md), [architecture](ARCHITECTURE.md), and [master audit](../Audit/FMSC_MASTER_AUDIT.md).
2. Create a descriptive branch for the task (example: `fix/fee-save-recovery`, `docs/operator-guide`). Do not combine unrelated refactors with a behavior fix.
3. Reproduce the reported scenario and locate existing validation, busy/dirty, transaction or recovery helpers before adding new code.
4. Implement the **smallest compatible change**. Preserve student data, finance logic, SQL security, and UI navigation/discard semantics; explain any migration explicitly.
5. Add focused regression tests that reproduce the original problem; do not disable failures or claim tests were executed when they were merely compiled.
6. Update the [user guide](USER_GUIDE.md), [installation](INSTALLATION.md), [recovery](BACKUP_RECOVERY.md), and [architecture](ARCHITECTURE.md) **only where the behavior actually changed**. Add a concise entry to [CHANGELOG.md](../CHANGELOG.md) under Unreleased.
7. Open a pull request using [.github/pull_request_template.md](../.github/pull_request_template.md) and record actual command output, CI links and remaining manual checks.
8. Review the diff and Windows CI status before merging. Branch-protection/ruleset enforcement, if desired, must be configured separately in GitHub Settings; a template alone does not enforce checks.
9. After merging, re-run the complete installer workflow when the release candidate is frozen. Only a verified artifact and test-install outcome can advance release readiness.

## Pull-request description expectations

Every PR should answer: **Why is the change needed? What behavior changed? Which screens/database operations are affected? Why will existing workflows/data remain safe? What tests passed (exact commands/results)? What remains untested?**

For SQL changes, include schema version, affected procedures/tables/migrations, transaction boundaries, idempotency and rollback implications; attach evidence from a disposable database only.

For UI changes, include the before/after operator sequence, dirty-state/busy-state handling, permissions and relevant Windows screen or DPI acceptance.

For installer changes, distinguish a passing publish regression from a successful private installer ZIP, and a successful ZIP from real-school installation approval.

## Changelog conventions

Use headings `Added`, `Changed`, `Fixed`, `Security`, and `Documentation` as appropriate under `## [Unreleased]`. Link changes to PRs or commits. When tagging a version, move release entries into `## [x.y.z] - YYYY-MM-DD` only after release acceptance; avoid claiming historical release dates or production readiness from CI alone.

## Data boundaries

Never commit or upload real `.mdf`, `.ldf`, `.bak`, screenshots of student records, staff passwords, live connection secrets, or sensitive logs. Use synthetic/disposable SQL fixtures. Never replace an installed school database with a development DACPAC.

## Lightweight local checks (Windows)

```powershell
dotnet restore FMSC_SMS.sln
dotnet build FMSC_SMS.sln -c Release --no-restore
dotnet build Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -c Release
dotnet run --project Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -c Release --no-build -- --post-fix
dotnet run --project Tests/AuditFixChecks/AuditFixChecks.csproj -c Release
dotnet run --project Tests/PerformanceChecks/PerformanceChecks.csproj -c Release
python Tools/verify_schema_artifact.py --check
python Tools/test_audit_schema.py
```

For Windows PowerShell provenance and disposable publish tests, see [CI workflow](../.github/workflows/fmsc-desktop-ci.yml). SQL integration must target disposable clones, not the production database.

Avoid direct merges that bypass review for sensitive changes. A Codex PR is a proposal until reviewed and merged; neither tests nor documentation automatically imply production sign-off.
