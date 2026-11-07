# Private MDF/LDF release acceptance (Sandbox-owned)

## Immutable fixture baselines

Private `farhan2166-creator/FMSC-Sandbox` retains exact synthetic-only SQL
LocalDB pairs extracted once, pinned at commit
`5a8c5130c9d7b76552bb73a753cd44c4d192d2d0`:

| Baseline | Permanent files | Acceptance |
| --- | --- | --- |
| schema 3 | `Database/v3/FMSC_DB.mdf`, `Database/v3/FMSC_DB_log.ldf` | Apply migrations 4–7, verify full contract, then test actual installer |
| schema 7 | `Database/v7/FMSC_DB.mdf`, `Database/v7/FMSC_DB_log.ldf` | Verify full contract, then test actual installer |

Check the pinned checkout's original SHA-256/size values against
`Tests/manifest.json`. Never attach or change the checked-in MDF/LDF:
**all writes, migrations, extended-property adjustments, and installer
operations must run against independent disposable copies**. Verify empty
student/user tables and a dedicated synthetic ownership marker; perform
`DBCC CHECKDB`.

These baselines are intentionally frozen as the application advances. New
SQL revisions require numbered `Migrations/N.sql` updates and a renewed
two-baseline migration/installer test. Never silently replace the fixtures.

The root `FMSC-Source/fmsc.dacpac` is for manual inspection only. Its
presence, freshness, content, checksum, and modification time are **not**
workflow prerequisites. The old, independently committed
`Tests/LifecycleSqlChecks/Baseline/schema.dacpac` is a static public smoke
fixture; it is **not** trusted release acceptance and is not refreshed by
the root DACPAC.

`FMSC-Sandbox/Latest.dacpac` and its instructions are retired and must
not be recreated as workflow dependencies.

## Trusted execution and publication

1. On **Sandbox main**, run the permanent **Private SQL + Windows Installer
   Acceptance** workflow. Its jobs check out a **specific public Source
   commit** and the exact pinned private fixture revision with Git LFS,
   with `persist-credentials: false`. The dedicated SQL jobs must pass
   schema-3 migrations 4–7, full `Core/DatabaseCompatibilityRequirements.json`
   checks (objects, columns, parameters, table types, unique indexes,
   triggers, trusted FKs, check constraints, typed recovery GUID) and
   schema-7 compatibility. Only then can the real Windows-installer jobs
   run against both independent fixture copies. No real school data,
   fixture bytes or database backups are uploaded as artifacts.
2. A distinct, trusted **attestation** job runs only after *both* SQL
   and *both* installer jobs pass. It verifies that the tested Source SHA
   is still the current `FMSC-Source/main` head, then writes a single
   commit status in FMSC-Source:
   `fmsc/sandbox-mdf-ldf/v1` = `success`, bound to that exact SHA.
   No attestation is issued for PR heads, moving main branches, skipped
   matrix jobs, failures, missing credentials, or cancelled runs.
3. **Workflow 04** in public Source checks the newest matching status
   for its exact `github.sha` and the expected attestor identity,
   description and Sandbox run target. It also verifies that `main`
   still points at that SHA. A missing/failing/untrusted status blocks
   **both build-only and publication** before packaging. The publishing
   step repeats the gate immediately before it uses the separate
   `FMSC_UPDATES_PUBLISH_TOKEN` credential.

The cross-repository flow is **Sandbox → public Source commit status**:
Source never needs permission to read the private Sandbox. Sandboxed
source scripts never have access to the attestation or publication token.
Public Source PRs may run independent synthetic smoke checks; those are
not release authorization.

## One-time least-privilege credential

In **FMSC-Sandbox → Settings → Secrets and variables → Actions**, configure
`FMSC_SOURCE_ATTEST_TOKEN`. Use a fine-grained GitHub token restricted to
**only `farhan2166-creator/FMSC-Source`**, with **Commit statuses:
Read and write** and automatic Metadata read. No Contents write,
Actions write, repository admin, Sandbox read, or FMSC-Updates access.
Keep this token only in the final Sandbox attestation job. If unavailable
or expired, do not write approval, and Workflow 04 stays blocked.

The public Source workflow continues using its existing, separate
`FMSC_UPDATES_PUBLISH_TOKEN` solely for final updates publication.
It does **not** require `FMSC_SANDBOX_READ_TOKEN`.

## Testing, limitations and release discipline

- A synthetic fixture may require resetting filesystem read-only
  flags/ACLs and converting `FMSC_QuickRegistrationScope` from a
  DacFx-persisted SQL `varchar` to a native `uniqueidentifier`.
  Apply these repairs **only to disposable copies**, never the master
  fixture. A valid GUID string alone is not sufficient.
- Installer success and matching `dbo.DatabaseVersion` are not enough:
  enforce the full production structural manifest after migrations and
  real installer execution.
- Test reports are sanitized; do not upload MDF, LDF, NDF, BAK,
  exported schools, credentials or source checkout archives.
- Baselines mostly lack user/student records. Migration *data-preservation*
  coverage beyond the deterministic synthetic records remains limited.
  Do not claim live-school upgrade, interactive GUI, physical-printer,
  reboot or clean-MSI coverage without independent evidence.
- Do not bump an application/database version or publish a release just
  to repair this gate. Retire temporary one-off diagnostic workflows
  after acceptance succeeds and commit their removal separately.
