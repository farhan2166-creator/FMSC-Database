# FMSC private Git updater — manual publishing, no publisher signing keys

**Status:** The update-packaging, private-repository authorization, integrity
checks and staging foundations are on the DRAFT feature branch. No update feed
has been published and no school installation has been updated.

## Owner decisions

- `FMSC-Source` contains app source and the existing CI. It is **currently
  public**; the owner plans to make it private later.
- `FMSC-Updates` is a **separate private** GitHub repository used ONLY to
  distribute approved packages and release metadata.
- The owner **manually publishes** approved updates. There is **NO separate
  RSA publisher signature, signing private key, public-key pinning, encrypted
  signing-key backup or GitHub Actions signing secret** requirement.
- No VPS, hosted broker, licensing API, external SQL service or school GitHub
  user accounts.
- Each authorized school Windows user/computer generates a unique SSH key pair.
  Only its PUBLIC deploy key is added to `FMSC-Updates` with **Allow write
  access UNCHECKED**. Its private key is stored under
  `%LOCALAPPDATA%\\FMSC\\Updates` sealed by Windows DPAPI CurrentUser.
  Windows login changes/reinstalls may require re-enrollment.
- Account → **Check for Updates** is manual; no automatic program shutdown,
  replacement, or database migrations while the school works.
- Prefer a changed-file incremental download for an EXACT matching installed
  version and file inventory. Otherwise use the full package.
- A database schema mismatch routes to the existing guarded installer/migration
  workflow. App-only staging must never bypass the SQL backup/migration guards.
- Preserve school data/MDF/LDF, installation identity, recovery backups and
  all needed numbered SQL migrations. Application and schema versions are
  **unchanged**; resetting either to 1 is deferred.

## Trust and verification

GitHub private repository write permissions control **who may publish**.
Authorized school devices use **read-only SSH** with GitHub's known SSH host
key pinned. They access only the update distribution repository.

The manifest includes SHA-256 checksums and sizes for archive pieces, full
ZIP/delta ZIP and all installed application files. Clients verify these before
staging. These checks detect corrupted, incomplete or mismatched downloads.

**Limit:** Hashes in a manifest are NOT a digital signature. Someone who gains
write access to `FMSC-Updates` could replace a package AND its hashes.
Protect the owner's GitHub account (2FA) and repository write privileges.
This is an intentionally accepted security trade-off for manual distribution.

No source code, database files, application signing secret or school SSH private
key is published in `FMSC-Updates`.

## Update format and distribution layout

```text
channel/stable.json
versions/<version>/update-manifest.json
versions/<version>/FMSC_SMS-app-full.zip.part0001
versions/<version>/FMSC_SMS-app-full.zip.part0002
versions/<version>/FMSC_SMS-app-incremental.zip.part0001
...
```

`Tools/UpdatePackages` produces a complete ZIP, optional changed-file delta,
a JSON manifest with SHA-256 hashes and deterministic <=90 MiB chunk files.
Its local unchunked ZIP is a build intermediate and must NOT be pushed into Git.

Example local packaging on the approved build output:

```powershell
dotnet run --project Tools/UpdatePackages/UpdatePackages.csproj -c Release -- `
  --current-dir <approved-publish-directory> `
  --output-dir <new-empty-package-directory> `
  --version <approved-application-version> `
  --schema <approved-database-schema>
```

To prepare an incremental package also supply `--previous-dir` and
`--previous-version` from the last approved build. The latest FULL package
must always be available so schools can skip missed versions.

`Tools/UpdateDistribution` creates a checked, independent publishing snapshot:

```powershell
dotnet run --project Tools/UpdateDistribution/UpdateDistribution.csproj -c Release -- `
  --new-package-dir <package-directory> `
  --output-dir <new-snapshot-directory>
```

For a later release, also specify `--previous-snapshot-dir` pointing to
the last published verified snapshot. This tool checks metadata structure,
part sizes, per-part hashes and reconstructed ZIP hashes and retains only
the two latest versions in the NEW snapshot. It never performs a Git push.

The owner must publish a **fully validated snapshot atomically** to
`FMSC-Updates`. Automated Git push/atomic-promotion/history management is
not implemented yet; do NOT copy half a release into the live feed.

## Two-version retention

| New version | Keep in latest tree | Retire from latest tree |
|---|---|---|
| 1.1 | 1.1 | — |
| 1.2 | 1.1, 1.2 | — |
| 1.3 | 1.2, 1.3 | 1.1 |
| 1.4 | 1.3, 1.4 | 1.2 |

Never remove the older version before the newest complete package and manifest
pass every check. A school left on 1.1 when 1.4 is released must still be able
to install the full 1.4 release, with ALL required intervening SQL migrations.

Removing old update files from the current tree **does not reclaim historical
Git blobs**. A bounded distribution history strategy remains a separate
production gate, limited to `FMSC-Updates`; never rewrite the source history.

## Implemented on draft PR #84

- `Core/Updates/UpdateManifest.cs`: validated JSON manifest, path, version,
  inventory and SHA-256 structure, exact-baseline incremental selection,
  full fallback, database-schema installer gate.
- `Core/Updates/UpdateChunks.cs`: deterministic bounded chunking and
  independently checked chunk/whole-archive reconstruction.
- `Core/Updates/UpdateStager.cs`: isolated complete application staging;
  exact file inventory + hashes; never overwrites the live installation.
- `Core/Updates/UpdateDeviceKey.cs`: separate read-only SSH key for an enrolled
  Windows user, with DPAPI-protected private key.
- `Core/Updates/UpdateGitSession.cs`: private read-only Git transport with
  pinned GitHub SSH host key, shallow metadata fetch and bounded chunk streaming.
- `Core/Updates/UpdateGitReader.cs`: manually initiated connection and
  update-version check without a publisher-signature requirement.
- `Core/Updates/UpdateReleasePreparer.cs`: checks repository manifest
  metadata, selects a delta or full update, verifies downloaded chunks,
  reconstructs the ZIP, and prepares the target in an isolated directory.
- `MainDashboardForm.cs` + `UpdateConnectionForm.cs`: Account → Check for
  Updates, generate/copy computer's PUBLIC SSH key, test private Git access.
- `Tools/UpdatePackages`, `Tools/UpdateDistribution` and their regression
  fixtures: package creation and safe two-version snapshot preparation.
- Windows Git enrollment/DPAPI tests and existing desktop CI checks.

**Removed:** owner-only `Tools/UpdateSigning`, RSA signature generation,
`update-manifest.sig`, pinned publisher public-key resource, recovery backup
requirements, and all signing-key parameters from packaging and snapshot CLI.
The school's **SSH authorization key is separate** and is still required.

## Remaining production gates

1. Test real private Git downloads, revoked deploy keys, network interruption,
   oversized archives, Windows file-lock behavior and constrained disk space.
2. Bundle reviewed Git/SSH binaries or an approved equivalent with the FMSC
   installer. The current manual checker needs Git/SSH installed.
3. Complete persistent chunk resume/retry and acceptance of the connected
   Download / Prepare dialog. It now shows installed/latest versions, method,
   approximate size and chunk progress; Close cancels preparation. Remind Me
   Later dismisses this manual check (it does not schedule a notification).
4. Complete disposable Windows/LocalDB acceptance of the connected installer
   handoff, including actual pre-migration backup, migration and rollback recovery.
5. Accept the guarded manual publisher described below against the private
   distribution. Long-term history rotation still needs a concrete reviewed
   maintenance procedure and owner approval before any destructive operation.
6. Run release-readiness, Windows UI and full integration tests before asking
   for merge or installation at any school.

No GitHub source visibility change, database migration, update publication,
live installation change or app/schema version reset has been performed.

## Owner publication review and atomic push

### Normal developer path: one manual GitHub workflow

Use the existing **04 - Build and Publish Private Update** workflow on `main`.
`publish_update` defaults to true; disable it only for an artifact-only build.
After editing and testing locally, increase the application version in both
`FMSC_SMS.csproj` and `Installer/core.ps1`, push approved source to main, and
select Run workflow. The workflow uses that checked-in version; it does not
invent, reset or commit a version number. A duplicate published version is refused.

One-time credential setup: create a fine-grained GitHub personal access token
limited to **FMSC-Updates**, with **Contents: Read and write**, and store it as
the **FMSC_UPDATES_PUBLISH_TOKEN** Actions repository secret in **FMSC-Source**.
The ordinary source GITHUB_TOKEN cannot write a different private repository.
Do not paste the token into source, workflow inputs, school enrollment or the app.
School computers continue to use their individual read-only SSH deploy keys.

The workflow builds/tests the source, publishes the self-contained application,
fetches the current private snapshot, reconstructs and verifies its full package
as an incremental baseline when schemas match, and uses the existing packaging
and distribution tools. If schemas differ, it prepares a full update. It verifies
every chunk/ZIP and reconstructs the complete target from both new archives before
publication. The log/summary identifies version, schema, sizes, retained/retired
versions and expected Git parent before the final atomic push. Concurrent runs are
serialized; a concurrent external distribution update is rejected without force.
Failures before the push leave the current school feed unchanged. Source and school
databases are never modified by this workflow.

The owner publishing credential is provided only to the runner's Git processes,
never saved in a remote URL, application or downloadable artifact. The public source
workflow retains its existing current-application artifact. Private baseline packages,
history and distribution snapshots stay in runner temporary storage and are not
uploaded as public artifacts. GitHub no-reply commit identity avoids email-privacy
push rejection. Buffered HTTPS publication supports the approved large chunks.

On the school side, publication does nothing until someone manually selects
Account → Check for Updates. There is no automatic download or forced installation.

The new orchestration is covered by offline trigger refusal, real package reconstruction
and local atomic-publication regressions. Its first authenticated GitHub run still
requires the owner's one-time secret configuration and manual Run workflow; it was
not invoked to publish a real update during development.

### Local CLI alternative

After preparing a verified two-version snapshot with the existing CLI:

```powershell
dotnet run --project Tools/UpdateDistribution -c Release -- --prepare-publication <snapshot-directory> --workspace <new-review-directory>
```

This fetches distribution `main` into a new disposable Git workspace. It
requires the snapshot to advance and retain the remote's current latest
version. Review the printed additions/deletions (including the retired version),
stored-object size and expected parent commit. Nothing is pushed at this step.
The owner uses their own Git write authorization, never a school's deploy key.
Configure the owner's normal Git commit identity before the next command.

Only after approval of that exact snapshot, the owner runs:

```powershell
dotnet run --project Tools/UpdateDistribution -c Release -- --publish-reviewed <review-directory> --expected-parent <printed-commit>
```

The tool revalidates manifests, chunk sizes, chunk hashes, combined archive
hashes and allowed files. It commits and pushes one ordinary fast-forward
update of `main`. Concurrent publication is rejected without force pushing.
After a rejected push, prepare a fresh workspace against the current remote.
No source repository history or distribution history is rewritten.

History storage is bounded by a conservative **1 GiB local Git object budget**:
preparation refuses further publication above it. This is a stop condition,
not a claim that tree retention deletes historical binaries or that local
object size equals GitHub billing. Plan reviewed archival/rotation before
reaching it; no automatic history cleanup is authorized or performed.

## Verification on 4 November 2026

- Release solution build: passed, zero warnings/errors. Standalone tool sources
  are excluded from the desktop project, including stale removed signing-tool output.
- Packaging/preparation regressions: passed (matching delta, older full update,
  corrupted delta full fallback, schema gate, isolated staging).
- Snapshot/publication regressions: passed against disposable local Git remotes,
  including two-version retention, no push during review, fast-forward ancestry,
  competing-publish rejection, corrupt parts and unexpected files.
- Windows authorization: 21 checks passed after correcting inherited directory
  permissions. Credential and transient plaintext SSH directories now allow
  only the current Windows user before private key material is written.
- Installer migration and LocalDB recovery contract suites: passed. These are
  offline safeguard checks, not real SQL migration/recovery acceptance.
- Windows post-fix control suite: failed communication tab navigation focus
  assertion in `Program.Communication.cs`. The assertion checks deferred focus
  transfer in `Core/TabStrip.cs`; it remains unresolved. No communication or
  tab navigation code was changed for the updater.
- Python release checks could not run: Python is unavailable on this machine.

## Connected installer handoff (follow-up implementation)

- Published applications now include `UpdateSupport/core.ps1` and all numbered
  migrations. Their bytes are covered by the same full/incremental file manifest.
  The production packaging CLI refuses version/schema values that differ from
  the published application DLL and bundled installer, and refuses missing migrations.
- The existing update window opens without blocking dashboard work. Only one
  window is opened. Schema-changing releases can download a full package without
  running SQL during preparation.
- Install Update is enabled only after handoff validation of the staged inventory,
  installer version and schema. Dashboard unsaved/busy guards and explicit close
  confirmation precede launch; the normal close guard remains active.
- The existing installer runs in an elevated external PowerShell process, waits
  for the original process identity to exit, and refuses another Windows user.
  If closure is cancelled, it times out without replacing files or running SQL.
  Automatic handoff supports the existing standard installation directory only.
  Disposable Level 2 builds cannot initiate activation.
- Update mode requires the existing MDF and LDF. Its template paths are disabled;
  a missing school database fails instead of creating/replacing one. Existing
  database registration/identity checks, verified pre-migration backup, sequential
  numbered migrations, compatibility check and folder activation/rollback are reused.
- Hash/inventory checks run again after process exit and against the installer copy
  before migrations. An exclusive installer mutex prevents concurrent activation.
  Restart remains the existing installer's success-only user choice.
- New offline tests cover process timeout/identity/user refusal, actual process-exit
  waiting, installer version/schema mismatch, changed/unlisted files, path traversal
  and database-payload refusal. They never launch activation or connect to SQL.
- Release build, authorization/handoff fixtures, migration and LocalDB safeguard
  checks passed. The disposable full PortableRelease publish passed, including
  installer and migration content checks. The local post-fix control rerun passed
  all 1,973 checks; the earlier communication-focus failure did not reproduce.
- Desktop CI run `41854489676` for the preceding commit passed every step,
  including Python checks, post-fix controls and admission/finance regressions.

**Not production complete:** The handoff is implemented and offline checks pass;
real activation/migration/recovery acceptance remains unverified. Bundled Git/SSH,
transport failure/resume tests and private-distribution acceptance remain required.
No live installer, database or distribution package was changed in these tests.

## Local private-distribution acceptance and dialog corrections

The owner authorized publication of `1.1.0-rc.2.test.1` and installation on the
owner's test PC using its ProgramData database, with the original detached pair
untouched. Publication commit `2adfe6a47339a673dea3bbd8b2e5d6495488e5f6` in the
private distribution was verified against the approved archive hashes. The real
school transport downloaded the incremental archive, installed all 391 expected
files and preserved 391 verified baseline files for rollback. The owner confirmed
existing records open and the manual checker reports up to date. Schema remained
76: this did not test actual SQL migration, backup restoration or rollback activation.

The owner's test exposed modeless dismissal and owner-close reentrancy defects.
Release `1.1.0-rc.3` fixes Close and Remind Me Later with explicit close handlers,
replaces the update window's FormClosed installation callback with a guarded
request, and launches the installer only after the dashboard accepts closure.
Cancelled confirmation or closure clears the pending update; ordinary later
closure cannot start it. The work guard runs once and duplicate requests are
suppressed. The dialog now separates version/download status, optional computer
access details and dismissal actions. A focused SQL-free WinForms suite reproduces
the owned-window close path and tests exactly one inert installer callback.

The owner has approved merging the source after these corrections. Further package
publication remains owner controlled. Bundled Git/SSH, persistent download resume,
clean-machine acceptance, full transport acceptance and actual migration/recovery
tests remain production limitations; successful incremental testing does not close them.
