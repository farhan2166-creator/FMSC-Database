# Level 1: disposable SQL lifecycle testing

This layer runs **real SQL Server 2022 operations and persisted-state assertions** without starting WinForms or using the production LocalDB attachment path. It complements the unchanged desktop CI and Windows control/PDF checks; it is not interactive UI, printer, installer or school-database acceptance.

## Run locally or on GitHub

Relevant pull requests trigger **FMSC Level 1 lifecycle SQL**. Manual dispatch becomes available when the workflow definition reaches the default branch. Use the PR event first; never merge an unreviewed workflow merely to enable dispatch.

Local requirements: Linux/WSL, Docker's local Unix socket, .NET 8, Python 3.11+, enough memory for a bounded 3-GB SQL container, and NuGet/container registry access. SQL Server is pinned by image digest; SqlPackage is pinned in `Tests/LifecycleSqlChecks/.config/dotnet-tools.json`.

```bash
python -m unittest discover -s Tests/LifecycleSqlChecks -p 'test_*.py' -v
python Tests/LifecycleSqlChecks/run.py \
  --output /tmp/fmsc-level1-evidence \
  --state /tmp/fmsc-level1-owner.json
```

The harness restores tooling, builds projects, provisions fictional catalogs, executes checks, exports evidence and removes its container/anonymous volumes and credentials. Failed or blocked scenarios return nonzero. It accepts no school connection string or database files. `--sqlpackage /path/to/sqlpackage` and `--dotnet /path/to/dotnet` support already installed tools; a nonstandard SDK may need `DOTNET_ROOT` and a writable `DOTNET_CLI_HOME`.

If a process was forcibly killed, finish ownership-checked cleanup:

```bash
python Tests/LifecycleSqlChecks/run.py --cleanup \
  --output /tmp/fmsc-level1-evidence \
  --state /tmp/fmsc-level1-owner.json
```

`--inject-provision-failure` intentionally fails after SQL startup to verify real cleanup. The expected outcome is **FAIL with Cleanup PASS**. Optional `--diagnostics /private/path/diagnostic.log` retains credential-redacted local diagnostics; never upload that file. CI exports only `report.json` and `summary.md`.

## Data-free SQL fixture and isolation

The workflow uses only `Tests/LifecycleSqlChecks/Baseline/schema.dacpac`, a pinned data-free schema fixture, not an exported application database or owner-supplied DACPAC. The fixture ZIP is validated for allowed schema entries, no exported rows and no external payload relationships. There is no repository DACPAC provenance approval or automatic refresh. Review fixture and SQL tests together when production schema requirements change. A passing synthetic fixture test is not evidence that an owner-supplied MDF/LDF snapshot is compatible.

Schema-only publication has no operational reference rows. `Fixture.cs` explicitly seeds schema version 6, the admission counter, supported role names and permission catalog. It uses the production first-administrator/setup procedures for fictional credentials, school, campus, academic program, classes, sections, years, fee types and rates. Class assignments/system-fee flags and initial role grants are reference-data fixtures. Accountant has its established six grants; Administrator has full authority.

**DacFx limitation:** the reviewed model round-trips `FMSC_QuickRegistrationScope` as `varchar`. The fixture corrects that property **only in the disposable database** to a fresh typed `uniqueidentifier`, then runs the unchanged production compatibility validator. This does not prove the owner's MDF/LDF is compatible or authorize migration retirement/school-database repair.

Every `FMSC_AuditChecks_` catalog has a unique run identifier and owner property. C# rejects non-loopback endpoints, wrong ownership, attach-file configuration and recovery files outside the private run directory. Separate catalogs isolate lifecycle, registration, mutation and administration. Cleanup verifies the exact Docker name/ownership label before deletion.

## Coverage

- Admission: persisted fictional details/optional phone, unique identities, exact enrollment/placement, opening fees and sibling linking.
- Fees: exact monthly posting, reported ineligibility, duplicate/wrong-year posting, partial/final collection, allocations and settled replay refusal.
- Manual purchase: production posting/request ID, gross-discount-net, exact replay, conflicting payload refusal and collection.
- Promotion: destination-rate, invalid-section/same-year/repeated-promotion refusals, real bulk promotion/history and unchanged original-year finances.
- Next year: promotion intentionally disables billing until real `ConfirmPromotedAdmission`; then opening/monthly fees and collection.
- Negative/recovery: required fields, discounts/fractional rupees, unauthorized/stale finance, pending-collection guard, and the existing commit fault seam with refund reconciliation/idempotent retry.
- Final audit: identities/enrollments/promotions/billing uniqueness, trusted constraints, allocation ownership, no negative dues, financial totals and complete history.

Dates are deterministically constructed from the UTC run year: admission/monthly fees in the previous year; promotion/return on current-year 1 January; next monthly fee in the current month. January correctly reuses the opening charge. No runner clock change is needed. Refund amounts/allocations are negative under the actual financial contract. The sibling supplies the recovery fixture so primary history remains unchanged.

The collection journal blocks ambiguous resubmission and requires deliberate review. Tests do not claim server-side collection request IDs or universal automatic replay recovery.

Existing registration/administration suites remain unchanged. Mutation runs request recovery, adjustment/reversal, bank-certificate and additional-fee modes. Portable/performance checks are distinct from real SQL assertions. Legacy PowerShell validation scripts are inspected but not executed: they back up `FMSC_DB` or hardcode historical IDs. Applicable admission/family/financial assertions instead use generated test identities.

## Evidence and extensions

Artifacts contain commit/run identity, engine/schema, outcomes/counts, expected refusals, timings and sanitized failing assertions. They exclude student names/addresses, credentials, database files and raw logs. Cleanup cannot turn a failed business test into PASS.

Classify failures as provisioning, fixture dependency, expectation, application/SQL defect or runner limitation. Check expectations against production contracts. Fix fixture/tooling errors here; propose production fixes separately. Never relax assertions, alter financial rules or edit the pinned test schema to hide a defect.

Add bounded scenarios to `Lifecycle.cs` using existing writers/TVPs or real procedures, then assert persisted state. Seed only required reference records in `Fixture.cs`. Assert exact SQL refusal codes and preserve every disposable-target guard. Physical desktop, printer and deployment acceptance remain separate.

## Current release target

Runtime schema is 20. The retained record-free v6 fixture uses retired migrations
7–19 under `Tests/LifecycleSqlChecks/Migrations`, then current release migration 20.
The harness separately publishes the current root schema into another owned empty
catalog and executes school admission/payment/credit scenarios against it. Neither
fixture is a replacement for a school MDF/LDF. Optional communication is installed
from the reviewed reference SQL only in its owned fixture and remains disabled.
