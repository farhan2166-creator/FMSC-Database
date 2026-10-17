# Family counter verification — 17 October 2026

Verified implementation: `2c1bb92214fab190c88952e5b7e2b70c5bc94021`, based independently on main `cbbd7add6cade6cb7d4ddce4c849e9e462527718`. Later evidence-only commits do not change this application/test tree. The separate preview-layout PR #52 is not included.

## Operator flow

Find a child in Fee Collection. Student Fees → Family / Returns opens the existing Family / Siblings page. Cards show the actual class/year, reference and readiness. Confirm Selected Return opens the existing guarded per-child dialog. It refreshes and selects the next waiting child without automatically confirming them. Closing the family window reloads the counter's student and placement.

Tick the required siblings and use Student Fees → Print Outstanding Bill. Only selected children are included, with all their posted unpaid charges, including older periods. This reuses the Quick Registration family report and does not save a payment. Return management refuses to clear an entered payment/reference/remarks/discount draft. Bill printing refuses unsaved discount changes.

Quick Registration retains its existing family-linking flow. Its whole-family bill now warns about awaiting returns or placement review; the operator must explicitly choose to print existing posted charges only. The warning defaults to No. A pending new child remains pending and retains their QR reference. Missing/zero bill lines never imply return completion.

## Tests actually run

- Windows Release build: `dotnet build FMSC_SMS.sln -c Release --no-restore -m:1`, PASS, zero warnings/errors. SDK 9.0.318 targets .NET 8; no exact SDK-8 build claimed.
- Existing portable audit suite: `dotnet run --project Tests/AuditFixChecks/AuditFixChecks.csproj -c Release --no-restore`, **848 PASS**.
- Existing Help suite: `dotnet run --project Tests/HelpChecks/HelpChecks.csproj -c Release`, **1923 PASS**, 126 questions/15 categories. Initial no-restore attempt lacked generated assets removed during cleanup; restoring fixed the environment prerequisite.
- Owned-profile native `ThemeSmokeChecks --family-counter`: **20 PASS**, covering one-child confirmation, re-entry, cancellation, refreshed next selection, current permissions, obsolete reads, actual checked sibling identities and draft retention.
- Owned-profile `ThemeSmokeChecks --post-fix`: **1818 PASS**. This includes the new family contracts and existing account, payment, shortcut, dirty/busy, modal-close and layout regressions. These are native control contracts, not operator acceptance.
- `pwsh -NoProfile -File Tools/Run-Level2.ps1 -FamilyCounterOnly`: **15 owned SQL assertions PASS**, engine **15.0.4382.1**. Source commit and clean working tree recorded by the existing runner. Catalog `FMSC_Level2_e8096a636e254fc7be4a6e9cd57640ec` was uniquely created; marker/file ownership was verified and cleanup reported **Pass: owned instance removed**.
- Existing Level 2 Python safety/report regressions: **6 PASS**. Existing operational-target rejection guards also passed in the provisioned run.
- Pinned official Gitleaks 8.30.1 history/staged scan: PASS. Windows archive SHA256 `d29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e` verified before execution.

The SQL scenario creates fictional admissions using production procedures, promotes two linked siblings, adds a pending QR child, and confirms one sibling explicitly. Reads preserve the two older 7000 dues and the new child's 7000 bill; confirmation adds only that child's 8000 opening charges. Selecting that child and the new child excludes the unchecked sibling. Unrelated, removed and newly added members invalidate stale selections as appropriate. Unknown-user SQL reads are refused; payments remain zero. These are fixture amounts, not changes to financial rules.

An initial fixture expectation assumed unknown-user refusal code 51000; the actual existing RequirePermission error is 50003. Only that expectation was corrected, and the scenario reran successfully. Production SQL/permissions were not altered to obtain a pass.

## Boundaries

New production SQL is read-only, parameterized and permission checked. Canonical family membership is rechecked before reading charges, and whole-family reads also reject new members absent from the reviewed snapshot. Existing schema, financial calculations/procedures, role grants, migrations, compatibility versions, DACPAC fingerprints, installer and recovery safeguards have no diff.

No operational database, school MDF/LDF, real messaging or physical printer was used. The provisioner reuses the established schema-only fixture/reference seeding and ownership guards; no pristine prerequisite or installer experiment was performed. Live operator comprehension, physical DPI/mixed-monitor behavior and physical print acceptance remain NOT RUN/LOCAL REQUIRED. Hosted Actions allowance is exhausted; no passing CI or workflow rerun is claimed. Detailed logs and local cleanup evidence remain ignored under artifacts/verification/family-counter-20261017.
