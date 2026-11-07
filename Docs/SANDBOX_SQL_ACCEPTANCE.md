# Private sandbox MDF/LDF release acceptance

## Fixed, one-time baselines

The owner-approved, **private** `farhan2166-creator/FMSC-Sandbox` repository contains independently extracted, synthetic-only SQL Server LocalDB pairs:

| Baseline | Checked-in Git LFS paths | Intended test |
| --- | --- | --- |
| Schema 3 | `Database/v3/FMSC_DB.mdf`, `Database/v3/FMSC_DB_log.ldf` | Real installer upgrades through all pending migrations |
| Schema 7 | `Database/v7/FMSC_DB.mdf`, `Database/v7/FMSC_DB_log.ldf` | Real installer accepts an already current schema, or runs later migrations |

Both baselines are permanently pinned to private sandbox commit `5a8c5130c9d7b76552bb73a753cd44c4d192d2d0`. They must not be replaced or edited during release runs. New migrations change source SQL only; these master fixtures remain frozen. A future deliberate fixture rotation requires reviewing the SQL schema/data, manifest checksums, pin SHA, and a fresh two-version Windows acceptance.

`Schema/Latest.dacpac` in FMSC-Sandbox is an optional *inspection* model that the owner may refresh. It is not the source of real release-acceptance databases, nor does it replace the historical DACPAC in this repository.

## One-time owner secret setup

The public `FMSC-Source` repository's default `GITHUB_TOKEN` **cannot** read a separate private repository. This must not be bypassed by making the fixture repository public or by reusing the private-update publishing token.

1. Create a **fine-grained personal access token** under GitHub account settings → Developer settings → Personal access tokens → Fine-grained tokens. Set repository owner to the owner of FMSC-Sandbox; choose **Only select repositories → FMSC-Sandbox**. Grant repository **Contents: Read-only** (Metadata read is automatic). Do not grant write, Issues, Actions, Administration, or FMSC-Updates access. Apply reasonable expiration and rotate when necessary.
2. In **FMSC-Source → Settings → Secrets and variables → Actions → New repository secret**, create exactly `FMSC_SANDBOX_READ_TOKEN` containing that value. Never put a token in scripts, workflow YAML, source files, or chat messages.
3. After the secret is configured, execute **02 - Release - Windows Installation Acceptance** with `test_scope=complete` on `main`. Check BOTH Windows matrix jobs (`schema 3` and `schema 7`) pass, then and only then use workflow 04 for build/publication.

A missing or expired token blocks both trusted full-install matrix jobs and therefore **blocks workflow 04**. Do not add a skip/fallback for trusted release runs.

## Security and coverage

- Trusted full release acceptance on `main`: GitHub Actions checks out the private fixture repository at an exact commit using read-only token, downloads its Git LFS content, validates SHA-256/byte counts from `Tests/manifest.json`, copies the pair, attaches a **second isolated copy** in LocalDB, checks database version and disposable marker, rejects student/user data, runs `DBCC CHECKDB`, drops the probe, then hands an **unattached copy** to the real Windows installer. Actual installer applies pending numbered SQL migrations. The installed app, recovery launcher, optional module behavior, backups and synthetic Level 3 preservation are tested.
- Public/untrusted pull requests: independent DACPAC/LocalDB regression **without** access to the private sandbox token. This is independent early smoke testing; only trusted release runs can certify private fixture-upgrade acceptance. The release workflow never publishes from a PR.
- **No** MDF/LDF/NDF/BAK/ZIP database artifacts or SQL connection credentials are uploaded to public workflow artifacts or included in private app updates; the workflow only uploads sanitized evidence. Run failures block publication.
- The current snapshots are *mostly empty* synthetic schemas; Level 3 adds minimal fictional role/user/school records after migrations. Rich data-**preservation during migration** coverage requires reviewed deterministic synthetic-data seed scripts on disposable copies before the migration step. Do not claim that full production-like data coverage exists yet.
- Real school databases must never be attached to, pushed into, or anonymized in this repository; deleting records is not safe sanitization.

## Migration and release policy

The original schema-1 DACPAC continues to support public PR regression tests but is **not required by trusted workflow 04 publication acceptance**. Application-only versions still bump the app version separately from the database version; a new database revision requires the next numbered `Migrations/N.sql`. Version labels do not themselves establish physical schema equivalence: the trusted gate checks the attached database's actual `dbo.DatabaseVersion` and runs migration/compatibility checks.

Do not merge or publish on a failed or cancelled full SQL/Windows acceptance.
