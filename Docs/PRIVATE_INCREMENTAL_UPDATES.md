# FMSC private GitHub Releases updater — implementation plan

**Status: packaging/staging foundation completed; GitHub authorization choice pending.
No update feature is active on school computers.**

## Owner decisions — confirmed

- No VPS, web application, hosted broker, third-party licensing API or separate SQL service.
- GitHub Releases deliver both **private** full and changed-file incremental packages.
- School administrator can manually select **Check for Updates** from the existing
  FMSC Account/Help menu; unattended checks are optional and not required now.
- Download and staging must not interrupt the running FMSC application.
- Update installation, file replacement and any SQL migrations run only after a
  safe application exit / explicit restart consent.
- Keep the full ZIP as fallback whenever the incremental baseline has changed,
  verification fails, or the delta for the installed version is unavailable.
- Preserve installed MDF/LDF, school identity and records. The existing
  `Installer/core.ps1` applies numbered SQL migrations with its verified
  backup/safe activation checks where necessary.
- **Do not reset application or database schema version to 1 yet.**

## Security constraint: GitHub private Releases need authorization

**An email address alone does not authorize a private release download.**
The person must authenticate to a GitHub account with access to the private
distribution repository. A permanent code embedded in every installed binary
is extractable and cannot authenticate a school to GitHub.

Preferred authorization under review:

1. Create/keep the *source repository* private and accessible only to developers.
2. Create a **separate private distribution repository** containing just approved
   releases/manifests; source does not belong in this repository.
3. Put the distribution repository in a GitHub **organization** to make the
   school's GitHub account a genuinely **read-only** collaborator. GitHub's
   documented personal-account repository collaborator model does not reliably
   offer read-only access; do not give a school write permissions to releases.
4. Create a GitHub App limited to that distribution repository with Contents:read,
   and enable device authorization. FMSC uses the official GitHub device flow
   to show a temporary verification code; school admin signs in via GitHub in a
   browser and authorizes each computer once.
5. Use GitHub App *user access* and refresh tokens for private release API calls,
   **never an installation private key, client secret or master PAT embedded in
   the desktop application**. Tokens for device-flow grants can refresh without
   a client secret. Protect saved credentials per Windows user using DPAPI.
   Handle revocation/expiration by requesting sign-in again.
6. Use the authenticated releases API, not unchecked private download URLs.
   Verify every publisher-signed manifest and SHA-256 digest independently of
   GitHub authorization before any application installation.

Alternative: authorized school enters a dedicated fine-grained GitHub
`Contents:read` personal access token, secured through DPAPI. More setup and
secret-management burden, but simpler to implement. Each computer should get a
separate revocable token. **Do not ask users to save email alone.**

Any GitHub account with release download permission can obtain and retain the
distributed **application binaries**. No GitHub-only method can revoke binaries
already downloaded; this is separate from software licensing.

## Existing branch implementation

- `Core/Updates/UpdateManifest.cs`: strict validation, signed RSA-PSS/SHA-256
  manifest parsing, semantic versions, exact-baseline incremental selection,
  complete ZIP fallback, and SQL-schema mismatch deferral to installer.
- `Core/Updates/UpdateStager.cs`: reconstruct complete target application
  offline in a separate verified staging location, without touching the
  installed application.
- `Tools/UpdatePackages`: signed manifest + full ZIP + changed-file ZIP
  builder, with self-tests exercising fallback, tampering, old-file deletion and
  non-disruptive staging.
- `.github/workflows/fmsc-desktop-ci.yml`: existing desktop CI runs the update
  package/staging self-tests. No separate hosted-broker job.

## Next implementation stages (pending owner's auth choice)

1. **GitHub Release packaging workflow** — after existing release-readiness
   checks and owner approval, package signed full + incremental assets and
   numbered migration files. Publish **only** in the private distribution
   repository with immutable version labels and stable release channels.
2. **Direct GitHub authentication** — browser device flow or school-supplied
   fine-grained token; secure Windows credential storage and explicit disconnect.
3. **Manual UI check** — Account/Help -> Check for Updates. Query the GitHub
   Releases API asynchronously, compare installed app + supported schema, show
   new version, notes and Update Now / Remind Me Later. Offline is not an error
   that disrupts normal school activity.
4. **Resilient package download** — authenticated GitHub release asset retrieval,
   retry, size/hash/signature verification, exact delta-baseline check and full
   fallback. Keep running application untouched.
5. **Installer integration** — reconstruct a complete staged app; on approved
   safe exit, run a separately launched update helper that can activate the
   verified application through existing folder-staging/rollback semantics.
   Package necessary migration scripts; run the existing SQL installer logic
   and pre-migration backup instead of patching SQL files directly. Record
   migration-safe rollback/recovery instructions.
6. **Acceptance** — WinForms/manual GitHub authentication, network interruption,
   revoked credential, tampered release, crash/reboot recovery, full fallback,
   Windows file locking, and disposable LocalDB copy upgrade/rollback tests.
   No live school database until reviewed and explicitly approved.

## Local offline regression test

```powershell
dotnet run --project Tools/UpdatePackages/UpdatePackages.csproj -- --self-test
```

The current packager has no credentials and does not publish to GitHub.
All updates remain *draft-only* until the above stages are complete.
