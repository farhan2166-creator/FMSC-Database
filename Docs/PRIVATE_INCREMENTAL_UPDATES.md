# Private, non-disruptive application updates — implementation plan

Status: **private broker backend source implemented; integration pending. NOT enabled for school PCs.**
This is a new delivery capability, not permission to deploy. Keep the existing
`FMSC_SMS.csproj` application version and `Installer/core.ps1` database version as
they are. A future reset to version 1 is an explicitly separate change.

## Owner decisions

- Both source and release packages must remain private.
- Prefer **changed-file incremental downloads**. Always retain a verified complete
  application ZIP for fallback. Do not confuse this with binary patching.
- Check silently without blocking application startup or normal school operations.
- Ask before installing; users can postpone. Never terminate the running WinForms
  application or discard unsaved forms.
- Installation is deferred until the user closes the program or explicitly
  approves a safe restart. A database migration requires additional maintenance
  coordination and the established verified-backup installer path.
- Never download, replace, or migrate a school's MDF/LDF as an app-only update.

## What has been implemented (phase 1)

- `Core/Updates/UpdateManifest.cs`: schema-validated, RSA-PSS/SHA-256 **signed**
  release metadata; safe Windows paths; semantic prerelease comparison; selection
  of no update, incremental, full, or *database installer required*.
- `Core/Updates/UpdateStager.cs`: offline isolated reconstruction from a
  hash-verified delta ZIP (changed files) or full application ZIP; verifies the
  complete reconstructed target; never modifies the installed application.
- `Tools/UpdatePackages`: release-package builder creates a full application ZIP,
  an optional changed-file ZIP, a JSON manifest, and its detached signature.
  The previous application tree is needed to generate a delta against a specific
  version. Removed files are omitted from the reconstructed target.
- The existing desktop-CI workflow now includes a synthetic staging/signature
  regression test, with no school database or GitHub credentials.

This **does not yet** add any background network request, user dialog, updater
service, activation process, GitHub Release or license issuance. Nothing runs on
a student's/school's PC as a result of this phase.

## Implemented service (phase 2 — backend source only)

- `Apps/UpdateBroker`: .NET 8 HTTPS-only broker with dedicated SQL schema,
  owner-only local school provisioning, school administrator enrollment of
  multiple computers, individual revocation, admin key rotation and school
  disabling. Random credentials are stored as keyed hashes, never plaintext.
- GitHub App JWT and short-lived contents-read installation-token exchange,
  restricted to the update repository. Only approved stable assets are relayed.
- RSA-PSS publisher verification before returning manifests; signed version
  and archive sizes must match GitHub's release inventory.
- Broker explicitly refuses to distribute updates while repository is public.
- Broker self-tests added to existing desktop CI; no production infrastructure
  or school computer has been enrolled.
- Deployment steps: [UPDATE_BROKER_DEPLOYMENT.md](UPDATE_BROKER_DEPLOYMENT.md).

## Required private distribution architecture (phase 2)

1. **Release signer (pending provisioning):** generate and securely store a publisher RSA signing key.
   The private key must be restricted to the approved release-publishing process;
   desktop clients embed only the pinned public key. Rotate by explicit versioned
   trust policy; never auto-trust an unknown signing key from a downloaded manifest.
2. **Private GitHub Releases (publishing still pending):** CI validates a reviewed release SHA, packages
   self-contained win-x64 binaries (full + delta), and publishes only to a
   private repository. Do not use short-retention workflow artifacts as a
   production update feed. The currently public repository must be made private
   by its owner **before** private packages or signing material are published.
3. **HTTPS update broker (backend source implemented; deployment pending):** a separately hosted service owns a narrowly scoped
   GitHub App installation credential with read-only access to private release
   assets. School PCs never receive GitHub PATs, GitHub App credentials or
   permanent GitHub asset links. The broker must authorize each installation
   before serving signed metadata or streaming release assets.
4. **Installation identity (enrollment API implemented; Windows DPAPI storage pending):** owner-approved enrollment issues a per-installation,
   revocable credential. Store it through Windows-protected credential storage.
   Enforce TLS, expiry/revocation, rate limits and release-channel access server
   side. Do not treat an easily copied machine fingerprint as authentication.
5. **Client update service (pending):** asynchronously request only the latest signed
   release manifest; rate-limit and retry on connectivity changes, keep offline
   failures silent, and show a non-modal update notice. Update Now downloads
   and stages in an isolated path while the desktop app continues running;
   Remind Me Later persists a local postponement time.
6. **Safe activation (pending):** once no school forms are open and the user exits, a
   separate, verified updater executable promotes the prepared application
   atomically using the established staging/rollback principles. If the delta
   fails verification or the installed version differs, download and verify the
   complete application ZIP instead. If the release requires SQL changes, use
   the existing installer and its verified pre-migration backup; never attempt a
   file-only activation against an incompatible database schema.

Suggested broker API (contract only; **not deployed**):

- `POST /v1/enroll`: one-time owner approval / revocable installation credential
- `GET /v1/updates/latest?channel=stable&platform=win-x64`: authorized manifest,
  detached signature, and immutable release identifiers
- `GET /v1/updates/assets/{releaseId}/{assetName}`: authorized, stream-limited
  private archive; never accept arbitrary GitHub URL or path from a client
- `POST /v1/updates/receipt`: optional, privacy-preserving success/failure report

## Safety boundaries

- Only signed manifest bytes are trusted; a checksum in an unsigned JSON file
  cannot by itself establish publisher authenticity.
- Always verify every entry's expected hash/size and path before activation;
  refuse absolute/parent traversal paths, duplicates, reparse points and
  database/secret files.
- Verify an exact previous application baseline before applying a delta.
  Mismatch selects full download rather than applying a speculative patch.
- A staged application is **not** an activated application; stage files outside
  the live directory, never overwrite running executable/DLL files.
- A failure during SQL migration is **not** a trigger to retry the full package:
  stop and use the verified database recovery procedure.
- Publisher approval and release-readiness checks are mandatory before an update
  is advertised; nothing is automatically published from feature branches.

## Packaging command (not for a production release until signing is provisioned)

```powershell
dotnet run --project Tools/UpdatePackages/UpdatePackages.csproj -- `
  --current-dir out/windows-x64-app `
  --previous-dir C:\previous-approved-win-x64-app `
  --previous-version 1.1.0-rc.1 `
  --version 1.1.0-rc.2 --schema 75 `
  --output-dir out/signed-update `
  --signing-private-key C:\secure-release-signing\publisher-private.pem
```

Use the actual approved previous and current build versions/schema; this is an
example only. No key or credential should be committed or copied into the ZIP.

For offline validation:

```powershell
dotnet run --project Tools/UpdatePackages/UpdatePackages.csproj -- --self-test
```

## Remaining release gates

- Owner's authorization/enrollment model and backend hosting location.
- Signed release-publishing workflow using **approved** immutable builds.
- Actual authenticated HTTPS broker, client, non-blocking WinForms notice and
  safe scheduled-restart activation.
- Installer integration, migration gates, fault-injection and Windows clone
  installation/rollback acceptance; no use on a live school database before
  sign-off.
