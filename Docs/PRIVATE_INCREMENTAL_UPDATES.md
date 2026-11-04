# FMSC direct private GitHub updater — agreed design

**Status: signed update packaging, bounded archive chunking and non-disruptive staging foundation implemented.
The GitHub distribution repository, direct authorization, Windows UI and installer
integration are NOT yet implemented. Nothing is enabled on school PCs.**

## Confirmed owner decisions

- Two separate private GitHub repositories:
  - `FMSC-Database`: application source, tests, migrations and build pipeline,
    accessible only to development/maintainers.
  - `FMSC-Updates`: approved signed update files, version manifest and the
    current two update versions only; NO application source or school data.
- **No hosted service, cloud VPS, broker, new API backend or additional database.**
- School staff do not require GitHub accounts. A school computer generates its
  own SSH key pair; only its **public key** is authorized as a read-only deploy
  key in the private distribution repository. Its private key is stored locally
  using Windows DPAPI and appropriately restricted filesystem permissions.
- `Account/Help -> Check for Updates` manually initiates the check. No forced
  background network check or application shutdown.
- Download changed-file incremental packages where the installed baseline
  exactly matches an available delta, otherwise use the complete new version.
  Reconstruct and verify the new application in isolated staging while the
  school application keeps working.
- Only the existing installer/update helper activates the new application
  **after a safe exit**. Pending SQL migrations require the existing verified
  pre-migration backup, sequential migrations and compatibility checks.
  Never replace school MDF/LDF files from the distribution repository.
- Leave both existing application and database schema version numbers alone.
  Resetting either version to 1 is explicitly deferred.

## Two-version distribution retention policy

Retain only the **two newest approved update versions** in the distribution
repository's **current published file tree**. For example:

| New release | Keep | Remove from current tree |
|---|---|---|
| 1.1 | 1.1 | — |
| 1.2 | 1.1, 1.2 | — |
| 1.3 | 1.2, 1.3 | 1.1 |
| 1.4 | 1.3, 1.4 | 1.2 |

The publisher must not remove the oldest version until the latest full and
delta packages, the signed metadata and integrity checks have all succeeded.
The published `latest` manifest is changed **last**, so clients never see a
partially uploaded version. Publishing/retention automation does not exist yet.

**Old installations must still upgrade.** A school running v1.1 when v1.4 is
latest downloads the **full v1.4 package**, not a delta that depends on deleted
versions. Retained migration scripts must allow every supported older database
schema to advance sequentially; package retention does NOT permit deleting
required SQL migrations.

**Git history caveat:** removing files from the current Git tree does not remove
the original binaries from historic commits or guarantee storage reduction.
The distribution repository must use a deliberate history/storage maintenance
strategy (for example a snapshot-only publishing branch with controlled history
replacement and shallow, fresh client fetches). This requires a separate
publisher design and fail-safe tests; merely issuing `git rm` is insufficient.
No source history will be rewritten as part of update retention.

**Git size caveat:** GitHub rejects ordinary Git blobs above 100 MiB (the
commonly described 100 MB limit). Large app/full-update packages use deterministic <=90 MiB chunks with
publisher-signed length/hash entries. Implemented in
`Core/Updates/UpdateChunks.cs`. The packager creates local source ZIPs **and**
the smaller `.zip.part0001`, `.zip.part0002` and subsequent files. The
future distribution publisher must upload **only the signed manifests and
chunk files**, never the large un-split source ZIP. Client-side authenticated
fetch and part reconstruction remain to be wired. Avoid growing Git history
through repeated large binaries.

## Authentication versus update availability

SSH read-only deploy keys authorize **Git repository file access**. They do
**not** authorize downloading a separate GitHub Release asset directly through
the Releases API. Therefore the agreed school-facing delivery route is to fetch
signed update files held in the **private distribution repository itself**.
The developer can still run ordinary GitHub Actions builds; their 14-day
artifacts are staging outputs, NOT the retained school update feed.

Each authorized computer has a unique deploy key so losing one computer allows
revoking just that key. A shared plaintext key in an installer, a school's SQL
database table, or an unencrypted config file is not acceptable. Implement
enrollment with a verifiable public-key exchange that shows the operator exactly
which key they are approving; never accept a self-declared email as proof of
authorization.

## Existing implementation on the feature branch

- `Core/Updates/UpdateManifest.cs`: RSA-PSS/SHA-256 signed manifest; version
  comparison and exact baseline / full-fallback selection; database schema
  mismatch routes to installer.
- `Core/Updates/UpdateStager.cs`: reconstruct and validate complete target in
  an isolated directory without touching the current installation.
- `Tools/UpdatePackages`: full ZIP + changed-file ZIP + bounded signed part files
  (up to 90 MiB each), with signature/hash verification and synthetic tests.
- `Core/Updates/UpdateChunks.cs`: verifies each download part and recombines
  them into a complete hash-verified ZIP in an isolated staging location.
- Existing desktop CI invokes the packaging/staging tests.
- The mistakenly introduced hosted `Apps/UpdateBroker` was **removed**.
  No cloud service has been deployed.

## Remaining implementation

1. Create/prepare a separate private distribution repository, with approval
   controls, per-device read-only deploy-key enrollment and a signed latest feed.
2. Connect existing signed package splitting to the distribution publisher
   and authenticated client download, including network interruption and
   real large-file Windows tests.
3. Implement private Git authorization using the Windows-protected per-device
   SSH key; use fresh, shallow fetches so distribution history maintenance cannot
   strand installed clients.
4. Integrate manual Check for Updates UI in the existing Account/Help menu, with
   offline handling, Update Now / Remind Me Later, and no impact on active work.
5. Stage packages while application runs; after confirmed safe exit, hand off to
   the existing installer/helper. Package and validate all migration scripts,
   perform backup/compatibility checks and provide rollback guidance.
6. Publish only after release-readiness tests and full artifact verification.
   Implement safe two-version retention, including explicit Git history
   management restricted to the distribution repository.
7. Test revoked credentials, offline/retry, tampered manifests, package splits,
   old-installed-version full fallback, Windows file locking, and disposable
   database migration/backup/rollback before any school deployment.

No application or school database migration has been performed by this branch.
