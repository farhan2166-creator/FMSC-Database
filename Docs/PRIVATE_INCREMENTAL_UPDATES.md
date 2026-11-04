# FMSC direct private GitHub updater — agreed design

**Status: private distribution repository created, with signed packaging, bounded chunking,
non-disruptive staging and a two-version snapshot preparation CLI on the draft branch.
Actual package publishing, owner-approved signing key provisioning and installer activation remain pending.
The feature branch now includes a manual authorization/update-connection UI and protected
per-Windows-user SSH credentials, but it has not been deployed to school PCs.**

## Confirmed owner decisions

- Two separate GitHub repositories:
  - `FMSC-Database`: source, tests, migrations and build pipeline. **Currently public;
    owner will change its visibility to private later.** Do not assume that it is private.
  - `FMSC-Updates`: **created and verified private**. Reserved for signed update
    files, the version pointer and the current two approved versions only;
    NO application source or school data.
- **No hosted service, cloud VPS, broker, new API backend or additional database.**
- School staff do not require GitHub accounts. Each enrolled Windows user on
  a school computer generates an independent RSA-3072 SSH deploy key; only its
  **public key** is authorized as a read-only deploy key in the private
  distribution repository. The private half is sealed with Windows DPAPI
  CurrentUser under `%LOCALAPPDATA%\\FMSC\\Updates`. Another Windows login must
  enroll separately; no common school key is embedded in the application.
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
The staged `channel/stable.json` pointer identifies the current and previous
approved release. The final Git publisher must switch the complete validated
snapshot as **one atomic commit**, never upload partial files to the live feed.
Snapshot preparation exists; Git push and server-side retention/history pruning
have **not** been implemented.

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
- `Tools/UpdateDistribution`: validates the publisher signature and every
  chunk, prepares an independent snapshot containing the newest one or two
  approved versions, and removes older releases from the new **tree** without
  deleting predecessor snapshots. It copies only part files, not oversized ZIPs.
- `Core/Updates/UpdateDeviceKey.cs`: generates local RSA-3072 SSH keys,
  stores the private PEM as DPAPI CurrentUser-protected data, and prevents
  silent rekeying. Only the public SSH key is displayed.
- `Core/Updates/UpdateGitSession.cs`: per-session, private read-only Git transport
  with the published GitHub SSH host key pinned and a shallow partial clone.
  Reads signed metadata first; retrieves only individually requested release
  parts on user approval. It never checks out the entire update repository.
- `Core/Updates/UpdateGitReader.cs`: manual authorization/availability check
  over the shared Git session, now requiring publisher-signed manifest
  verification before reporting an installable version.
- `Core/Updates/UpdateReleasePreparer.cs`: verifies the signed latest manifest
  against a **public RSA signing key pinned in the application binary**;
  selects delta versus full using the installed file hashes and schema gate;
  downloads bounded Git parts, verifies each and the complete ZIP, and
  reconstructs the target into an isolated staging directory. A bad or stale
  delta may fall back to the separately verified full package.
  **No installer activation or SQL migration is implemented by this class.**
- The trusted publisher public key is intentionally **not yet provisioned**;
  production downloads are disabled until the owner approves a real signing
  key and its public half is embedded at build time. A public key fetched
  from the same Git repository is NOT a trust anchor.
- `Core/Updates/UpdateConnectionForm.cs` and `MainDashboardForm.cs`:
  Account → Check for Updates; generate/copy public key and test access.
  Reports the private feed version only as unverified metadata until the
  publisher verification key is configured. No startup checks are enabled.
- `Tests/UpdateAuthorizationChecks`: Windows-only DPAPI, uniqueness,
  key-format and corruption regression checks (CI).
- Existing desktop CI includes packaging, staging, distribution snapshot,
  protected authorization and signed-release preparation tests. The synthetic
  release tests cover delta/full staging, mutated installed files, damaged
  downloaded deltas, signature refusal and database schema gating.
- The mistakenly introduced hosted `Apps/UpdateBroker` was **removed**.
  No cloud service has been deployed.

## Remaining implementation

1. `FMSC-Updates` is created private with a README. Enroll each Windows
   user/computer by generating the public key in FMSC and having the repository
   owner add it in FMSC-Updates → Settings → Deploy keys with **Allow write
   access unchecked**. There is no GitHub account requirement for schools.
   The source repository remains public until the owner changes it.
2. Connect the snapshot preparer to a guarded private Git distribution
   publishing action. Signed chunked download/reconstruction code exists;
   test a REAL private Git checkout and interrupted large downloads on Windows
   before allowing a school update.
3. Provision owner-controlled publisher RSA signing keys (protect the PRIVATE
   half outside source/distribution) and embed the reviewed PUBLIC key in
   official FMSC builds. Read-only Git access and manifest/chunk verification
   now exist, but the manual checker requires this key before recognizing
   updates. Bundle reviewed Git/SSH runtime in the Windows installer so schools
   never need developer tools.
4. The manual Account → Check for Updates entry and authorization dialog are
   implemented. Signed update availability is wired behind the publisher-key
   gate; add user approval to download, progress/retries and
   Update Now / Remind Me Later. Do not interrupt active work.
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
