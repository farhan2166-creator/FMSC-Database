# Publication security review

**Current repository visibility (checked 2026-11-01): PUBLIC.**
**Privacy/publication acceptance: NOT YET ESTABLISHED.** This is not an assertion
that a leak exists, and this review does not change visibility or permissions.
The original findings below describe an earlier local review and must not be
mistaken for a fresh all-assets clearance.

## Current evidence and remaining privacy gate

- [FMSC Security Checks at main 12b37c3](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/41325439569) passed. It scans available history, current source, and selected schema-package contents; it is not a visual or artifact-wide privacy certification.
- Public GitHub repository metadata was checked on 2026-11-01. No GitHub Releases were returned in that check. Existing Actions artifacts, logs, issue attachments, screenshots and remote-only refs were **not** individually cleared.
- Before sharing release assets or production-adjacent screenshots, review all images and documents, issue/PR attachments, retained workflow artifacts and any external mirrors. Verify fictional fixtures and redact sensitive student/staff data. If an actual credential or private record is discovered, restrict access and follow incident response; deleting the current file does not clean history.

## Historical publication assessment (retained for traceability)

## Earlier local findings

- Reviewed all 14 commits available through local refs (not a shallow clone).
  The configured fetch covers main only; other remote branches and tags remain unreviewed.
- Inspected 1,831 historical blobs with supplemental private-key, GitHub token,
  AWS access-key and literal-password patterns. The sole candidate path was a
  historical AdministrationSqlChecks fixture; values matched the existing exact
  synthetic fixture allowlist. This limited check is not a substitute for Gitleaks.
- No forbidden database, backup, credential-container or environment-file paths
  were found across 1,478 historical paths.
- All five distinct historical DACPACs declare ContainsExportedData=false and
  contain schema/metadata entries only. SQL schema can still include sensitive
  constants; Gitleaks scans extracted XML as well as ordinary source.
- One admission screenshot was visually sampled and labeled fictional Level 2.
  The remaining images have not received a complete visual privacy review.
- Existing workflow security scripts were missing. Restored Tools/check_security.py
  and Tools/test_security.py. Safety tests and real Gitleaks integration pass. The checksum-verified
  Gitleaks 8.30.1 CLI found no leaks in available history, working files or
  extracted historical DACPAC XML after network access was restored.
- Several desktop/SQL workflows reference test projects and other Tools scripts
  missing from the current commit. Security repairs do not restore these test suites.

## Earlier changes prepared locally

- Security workflow runs on main pushes and pull requests, plus manual dispatch.
- Checkouts explicitly disable credential persistence.
- Interactive self-hosted desktop execution requires a private repository and main.
  Keep that runner disconnected from a public repository; this condition alone is
  not an isolation boundary against someone permitted to edit workflows.
- Installer instructions now describe artifact visibility accurately.
- Added security reporting and data-handling guidance in SECURITY.md.

## Remaining publication requirements

1. Confirm FMSC Security Checks succeeds on GitHub after pushing these changes.
   The local Gitleaks scan passed. Do not treat
   a blocked tool download or a failed scan as success. Rotate any real credential
   found, including credentials removed from current files but present in history.
2. Review all remote branches/tags and every screenshot/document for school records,
   contact details, IDs and other private data. Removing a current file does not
   remove historical copies; any required history cleanup needs a separate plan.
3. Review existing releases, release attachments, Actions artifacts, logs, issues,
   pull requests and wiki pages before visibility changes. Local Git checks cannot
   audit these. Revoke any credentials already exposed through these surfaces.
4. In GitHub settings enable secret scanning/push protection where available,
   Dependabot alerts/security updates and private vulnerability reporting. Configure
   rules for main to require reviewed pull requests and successful security checks.
   Require approval for outside-contributor Actions runs; retain read-only token
   defaults and do not expose production secrets to public tests.
5. Disconnect self-hosted runners before making this repository public. Hosted CI
   must use fictional data. Public standard hosted runners have free Actions usage;
   larger runners remain billed.

At the time of the earlier local review, remote settings and assets were not changed or verified. As of 2026-11-01 this repository is public; the publication/privacy acceptance gate above remains unresolved. This document edit does not alter GitHub visibility or settings.
