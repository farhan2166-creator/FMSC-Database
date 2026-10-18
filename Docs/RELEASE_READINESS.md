# Operator-control revision readiness

Candidate: 1.1.0-rc.2, database schema 21.

Status: the final combined security, SQL, Windows, real-installation and package
checks all PASS. PR [#54](https://github.com/farhan2166-creator/FMSC-Database/pull/54)
is merged; `main` is the source of truth. Ready for the owner's local acceptance.
See [operator decisions](OPERATOR_CONTROL.md) for the implemented choices.

## Changes

- Concessions have chosen start/end months and a reason. A temporary concession
  restores the previous agreement afterward and retains scheduled rate changes.
- The operator explicitly chooses whether agreements or issued bills change. Preview
  shows current/proposed bills and paid amounts released as credit or refund.
- The same review covers monthly rates, annual fees and individual purchases.
  Closed-placement bill corrections retain the original placement and agreement.
- Available credit stays on account unless its use is selected; the review shows
  credit before, use, and remaining credit.
- Promotion explicitly resets or carries the final prior monthly concession;
  return confirmation still precedes new-year billing.
- Concession confirmation uses stale-review protection and the existing durable
  mutation recovery mechanism; uncertain saves can be checked without re-entry.
- Direct admission and Quick Registration expose admission-month versus following-
  month concessions instead of forcing a hidden next-month rule.
- Leaving offers the normal cutoff, retained issued bills, or waiving from a chosen
  month. Its financial preview includes the full bill correction chain.
- Revised procedures have no retiring audit writes or audit-only JSON preparation.
  Financial, student history and request/recovery records remain operational.

## Verification

Tested runtime source: `163957aa652b7a5cddec675c687acf944b832a92`.
[Final combined run](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/39133282766):

- Security: PASS, including source, available history and historical schema packages.
- SQL Server 2022: 84 stages PASS; zero failed/blocked stages; 47 expected refusals.
  Includes actual production bill readers, temporary terms, rates, paid/partial
  annual and purchase corrections, closed-placement corrections, credit use,
  promotion, stale reviews, exact request recovery and same-session rollback.
- Windows: 1,880 focused native control checks PASS; 254 component assertions,
  240 dialog cycles and a 19-page PDF PASS. Release builds have zero warnings/errors.
- Real Windows installation: schema 19 → 21, installed compatibility, update and
  reinstall without replacing the database, fictional record/identity/theme
  preservation, prerequisite refusal and activation rollback PASS.
- Additional LocalDB billing: 95 cases and production startup/restore compatibility PASS.
- Complete installer assembly, signed Microsoft prerequisite verification and
  package inventory/integrity checks PASS.

The merged tree was compared with the tested source and is identical. Subsequent
changes in this checkpoint are documentation only, so the full tests were not
repeated after the merge. No school database was used or changed. School Windows
profiles, physical printing and the owner's actual data-copy acceptance remain
local checks.

## Installer artifact

[Download the complete Windows x64 installer](https://github.com/farhan2166-creator/FMSC-Database/actions/runs/39133282766/artifacts/12139321592).

Artifact: `FMSC_SMS-Complete-Windows-x64-Add-Database-Files` (161,026,727 bytes).
GitHub ZIP SHA256:
`5ef3fe0e3231662f0417d8bf35d81a4486383d2fcab31bf1297724df092b14b5`.
Retained until 2026-11-01. The package records the tested source commit and contains
its `SHA256SUMS.txt`; school database files are excluded.

The [previous RC1 evidence](ReleaseHistory/1.1.0-rc.1.md) applies to that older
artifact only. It does not include this operator-control revision.

## Deployment and scope

All database changes remain in [Migrations](../Migrations/README.md) for later
application. Migration 20 upgrades schema 19; migration 21 upgrades schema 20.
A schema-20 database needs only 21.sql; schema 21 needs neither file again.
Apply the required files before using the new application. The installer
backs up and verifies before applying pending files. Existing school, payment and
permission rows are preserved. Schema 21 changes five business procedures, the closed-charge guard, the
communication version guard and the recovery-kind constraint; it adds no tables.
The unused C# amount editor is retired; its SQL compatibility API is retained
and remains exercised by the compatibility billing checks. Current schema-model
SHA256: `208fe37e490ebee676f1c8f48c4baaceeccd8ea1d0d3c8a58a573b91f289d2f5`.

Local operator/login, printing and school-data clone upgrade/recovery acceptance
remain necessary. Follow [INSTALLATION.md](INSTALLATION.md) and the established
owner-supplied detached MDF/LDF preparation. No live school database has been
accessed or changed.
