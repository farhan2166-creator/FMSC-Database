# FMSC private update broker — operator deployment guide

**Status: source implemented on a draft feature branch; not deployed, not yet
production-validated. No Windows school client is connected.** Private GitHub
Releases and user-facing update prompts are separate later stages.

## Architecture

The updater is a dedicated ASP.NET Core (.NET 8) HTTPS service, NOT an
extension of the school's SQL/WinForms application's local database.

- Owner provisions a **school** on the broker server using a local CLI.
- School receives one administrator credential, kept with its administrator.
- Administrator enrolls multiple named Windows computers using HTTPS.
- Every computer receives a **different** revocable device credential; it
  must eventually be stored in Windows DPAPI-protected credential storage.
- The computer uses only its own bearer credential to ask the broker for
  updates. It never receives a GitHub token, publisher signing key or school
  administrator credential.
- Broker obtains one-repository, `contents:read` short-lived installation
  credentials with a GitHub App. It reads private Releases and checks the
  publisher's RSA-PSS signature before exposing their signed manifests.
- Broker relays only the full and changed-file ZIPs named by the validated
  manifest. It refuses to operate while the GitHub repo is public.
- Application updates may be prepared in a separate staging directory without
  interrupting FMSC. **No automatic activation is implemented yet.**
- SQL schema changes must continue through the EXISTING installer, verified
  backup and migration process; neither the broker nor the app-only ZIP
  downloads, replaces, backs up or rewrites school MDF/LDF files.

## Dependencies and secrets

1. Make the source/distribution GitHub repository private; today the repository
   may be public. The broker checks the GitHub API's `private` field and
   refuses to serve if false.
2. Install a GitHub App on the private repository with repository
   **Contents: Read-only** only. Record its App ID, Installation ID and
   PEM private key in the server's secret store. Do not use a school PC PAT.
   The server narrows installation tokens further to one repository and
   `contents: read` via GitHub's token API.
3. Create a separate dedicated SQL Server database for the update broker.
   Run `Apps/UpdateBroker/Schema.sql` with a deployment account. The runtime
   account needs only SELECT/INSERT/UPDATE on its tables. Do not point to
   `FMSC_DB`. Enable encryption and validate the SQL server certificate.
4. Generate a **publisher signing RSA key of at least 3072 bits** under your
   offline/CI release-signing process. The private publisher key signs the
   packaged manifest; the broker keeps only its public PEM. The eventual
   client must pin that same public key. Keep it distinct from GitHub App keys.
5. Generate one permanent random 32-byte HMAC pepper per broker installation,
   protect it in the server secrets manager, and back it up securely. Losing
   or replacing it invalidates all stored administrator/device credentials.

Required environment variables, with **placeholders only**:

```text
FMSC_UPDATE_SQL_CONNECTION=Server=...;Database=FMSC_UpdateBroker;Encrypt=True;TrustServerCertificate=False;...
FMSC_UPDATE_KEY_PEPPER_BASE64=<base64 of at least 32 cryptographically random bytes>
FMSC_UPDATE_GITHUB_OWNER=<private repository owner>
FMSC_UPDATE_GITHUB_REPO=<private repository name>
FMSC_UPDATE_GITHUB_APP_ID=<app id>
FMSC_UPDATE_GITHUB_INSTALLATION_ID=<installation id>
FMSC_UPDATE_GITHUB_APP_PRIVATE_KEY_PATH=<secure server-only PEM file path>
FMSC_UPDATE_PUBLISHER_PUBLIC_KEY_PATH=<pinned RSA publisher PUBLIC PEM path>
FMSC_UPDATE_MAX_DEVICES_PER_SCHOOL=100
FMSC_UPDATE_TRUSTED_PROXY_IP=<optional single trusted reverse-proxy IP>
```

The enrollment ceiling is an operational anti-abuse configuration, **not** a
final licensing policy. The owner may choose a different limit later.

Only an HTTPS-capable Kestrel endpoint or a reverse proxy explicitly listed
by its IP through `FMSC_UPDATE_TRUSTED_PROXY_IP` is supported. The broker
accepts only one forwarding hop and only from that address; omit the setting
when Kestrel itself terminates TLS. The
program rejects `HttpContext.Request.IsHttps == false` before accepting
credentials. Configure the reverse proxy's TLS, forwarding, firewall and
logs carefully; never log authorization headers or enrollment secrets.
Do not grant any public SQL access.

## Owner-only commands (execute on the broker server)

```powershell
dotnet run --project Apps/UpdateBroker/UpdateBroker.csproj -- provision-school "Example School"
dotnet run --project Apps/UpdateBroker/UpdateBroker.csproj -- rotate-school-admin <school-guid>
dotnet run --project Apps/UpdateBroker/UpdateBroker.csproj -- disable-school <school-guid>
dotnet run --project Apps/UpdateBroker/UpdateBroker.csproj -- --self-test
```

Provision/rotate prints the administrator credential **once**. Give it only
to the authorized administrator via a protected channel. Rotating revokes
the old administrator credential; existing enrolled device credentials
remain valid until separately revoked or the school is disabled.
Disabling a school stops **all** enrolled computers from authenticating.

## School administrator operations (HTTPS)

The school administrator uses the school admin credential only during
computer enrollment and administration; never leave it in the FMSC
application configuration. Example PowerShell, where variables are
obtained securely (not hardcoded into scripts):

```powershell
$base = "https://updates.example.org"
$adminKey = Read-Host "School admin credential"
$device = Invoke-RestMethod -Method Post -Uri "$base/v1/enroll" `
  -Headers @{ "X-FMSC-School-Admin-Key" = $adminKey } `
  -ContentType "application/json" `
  -Body (@{ deviceName = "Office-PC-01" } | ConvertTo-Json)
# The deviceCredential is shown only once. The future Windows enrollment
# UI must protect it with DPAPI; do not put it in school SQL or a text file.
$device.deviceId
```

Devices may subsequently be listed and individually revoked:

```powershell
Invoke-RestMethod -Uri "$base/v1/school/devices" `
  -Headers @{ "X-FMSC-School-Admin-Key" = $adminKey }
Invoke-RestMethod -Method Post -Uri "$base/v1/school/devices/<device-guid>/revoke" `
  -Headers @{ "X-FMSC-School-Admin-Key" = $adminKey }
```

## Enrolled computer update endpoints

```text
GET /v1/updates/latest?channel=stable&platform=win-x64
Authorization: Bearer <unique device credential>
=> {releaseId, version, manifestBase64, signatureBase64, full, incremental}

GET /v1/updates/assets/{releaseId}/{approvedArchiveName}
Authorization: Bearer <unique device credential>
=> Streamed private signed-release ZIP; no arbitrary URLs
```

The client MUST verify the **raw manifest bytes** with its pinned publisher
public key and verify the downloaded archive's signed hash and length. The
metadata's JSON envelope and transport TLS alone are not sufficient proof
of publisher authenticity. Never use client-supplied GitHub URLs, release IDs
not approved by the broker, unsigned manifests or old release ZIPs without
an approved signed manifest.

Stable releases use the exact tag `v<signed-manifest-version>`, and must
contain `update-manifest.json`, `update-manifest.sig`,
`FMSC_SMS-app-full.zip`, plus the optional
`FMSC_SMS-app-incremental.zip`. The broker refuses draft/prerelease assets
on the stable feed. GitHub can return binary data or a redirect when
downloading release assets; the broker follows only narrowly allowed HTTPS
GitHub asset-CDN hosts and **never forwards its App token to redirects**.

## Validation / release restrictions

- Run `dotnet run --project Apps/UpdateBroker/UpdateBroker.csproj -- --self-test`
  and the package builder's existing `--self-test`.
- In an isolated server with a synthetic school, test: enrollment of two
  computers; wrong school key; revoked device update refusal; rotated
  administrator refusal; school suspension; compromised/stale credentials;
  invalid metadata signature; private repo permission errors; missing asset;
  interrupted download; reverse-proxy TLS and rate limiting.
- Release infrastructure, real SQL integration and end-to-end HTTPS tests
  require operator-provisioned infrastructure and are **not** performed by
  the source-only regression suite.
- No live FMSC school database upgrade, automatic installer launch or
  production distribution is authorized by this work.
- App and database schema version resets to 1 remain explicitly deferred.
