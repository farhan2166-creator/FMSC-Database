# FMSC_SMS Level 2 results — 16 October 2026

The real WinForms application was exercised through FlaUI/UIA3 on this PC's
unlocked interactive Windows desktop, using newly created fictional LocalDB
instances. **Latest verified catalog outcomes: 48 total; 47 Pass; 0 Fail;
1 Blocked; 0 Not Run.** This is a finite scenario catalog, not certification of
every application feature or every input combination. Level 3 readiness is **not
established**.

The [complete scenario table](Level2Evidence/2026-10-16/summary.md) contains the
requested expected/actual results, status, evidence, defect and database checks.
The [structured report](Level2Evidence/2026-10-16/report.json) maps each scenario
to its actual source run, commit and application hash. No scoped, unexecuted
workflow was counted as passed.

| Module | Catalog scenarios | Pass | Fail | Blocked | Not Run |
|---|---:|---:|---:|---:|---:|
| Security | 6 | 6 | 0 | 0 | 0 |
| Admissions | 13 | 13 | 0 | 0 | 0 |
| Finance | 11 | 11 | 0 | 0 | 0 |
| Promotion | 6 | 6 | 0 | 0 | 0 |
| Profiles | 4 | 4 | 0 | 0 | 0 |
| Usability and recovery | 8 | 7 | 0 | 1 | 0 |

## Execution and traceability

This report combines two separate disposable runs; it does **not** describe one
fresh 47-pass execution:

| Run | Source commit | Observed outcome | Use in final catalog |
|---|---|---|---|
| Full operator run `e6af686b-0c0d-44a0-8fa6-2cb8bc1d11a9` | `a5717b6` | 46 Pass, 2 Blocked | Finance, promotion, family, status, recovery and other retained outcomes |
| Admission/display regression `06010fcd-430b-4379-a48a-a78e46665ad7` | `fe5ac05` | 23 Pass, 25 explicitly out of scope/Blocked, after visual review | Latest executed admission, security, navigation and current-display outcomes |

The full run completed actual admission creation/edit/discard, financial
collection/configuration/posting, promotion/return/history, profiles, sibling
linking, student status and connection recovery. Its financial and promotion
results precede the final admission-toolbar sizing correction. That correction
changes admission presentation only; save, reopen, edit, discard, navigation and
security were repeated afterward. The SQL rules and financial/promotion business
logic were not changed. Per-scenario source mapping prevents the two databases
from being mistaken for one persisted dataset.

Both instances and their owned private directories were removed successfully.
The [environment evidence](Level2Evidence/2026-10-16/environment.json) preserves
both observed application hashes, clean working-tree flags and cleanup results.
The final artifact validator checks counts, source references, missing evidence
files, path boundaries and prohibited credential payloads.

## Environment and Level 1 prerequisites

Windows build `10.0.26200`, .NET SDK `9.0.318`, .NET 8 Desktop Runtime `8.0.31`,
SQL Server LocalDB `15.0.4382.1`, SQLCMD and authenticated GitHub CLI were
available. The actual monitor was 1920×1080; observed window DPI was 96.
The application also ran in a verified 1280×720 window. Resizing a window is not
represented as changing the monitor resolution or Windows scaling.

The branch incorporates main `76227d2` and its owner-approved PR #43 cleanup of
exported-DACPAC provenance tooling. The existing **data-free** Level 1 fixture
bytes remain unchanged, its current validator passes, and runtime compatibility
still validates schema version 6 before the application opens. Observed model
SHA-256 is `f151be03cd23e9cc4689d5261edcdfb03976f6500b7a9dc6028b94ba9946e7f6`.
This records the tested model; it does not introduce or rewrite an approval
fingerprint. Level 1 files and runtime compatibility requirements have no Level 2
changes relative to current main.

Normal/test builds passed. Ten current Level 1 safety regressions, six Level 2
safety/report regressions and nineteen focused Windows layout assertions passed.
GitHub's desktop regression, SQL lifecycle, UI infrastructure and sensitive-file
checks passed for the implementation. The hosted interactive job was explicitly
skipped; hosted build results are not desktop operator evidence.

## Financial persistence

The original-year UI admission created 5,000 tuition plus 2,000 admission fees.
A 2,500 payment left 4,500; another 4,500 settled that account. A UI purchase
created 430 gross minus 30 discount = 400, subsequently collected. Promotion and
return confirmation created the current-year Class 7 opening amount of 8,000;
February monthly posting added exactly 6,000 once, followed by a 14,000 payment.

The final guarded [SQL snapshot](Level2Evidence/2026-10-16/L2-FIN-final-reconciliation.txt)
was taken after the fictional sibling's leaving action. It is from the full
operator run, not from the later fresh admission regression:

| Fictional student | Status | Charged | Paid | Allocated | Outstanding |
|---|---|---:|---:|---:|---:|
| Example Student | Active | 21,400 | 21,400 | 21,400 | 0 |
| Example Sibling | Left | 7,000 | 0 | 0 | 7,000 |

Allocation ownership errors were zero. The sibling's uncollected opening fees
were not treated as settled. Separate-connection queries verified committed
records, enrollment ownership, cancellations and balances throughout the UI
workflows. Student/admission/payment/promotion results were never seeded.

Useful evidence includes the [real 4,500 receipt preview](Level2Evidence/2026-10-16/L2-FIN-08-receipt.png),
[account statement](Level2Evidence/2026-10-16/L2-FIN-07.png),
[connection failure dialog](Level2Evidence/2026-10-16/L2-UX-07-interrupted-connection.png)
and [successful post-recovery preview](Level2Evidence/2026-10-16/L2-UX-07.png).
The interruption removed only the proven owned LocalDB instance, restored it
from its original private files, reconnected through the UI and retried a read.
Charge/payment counts and amounts remained unchanged.

## Defects and corrections

No open critical functional/data-integrity defect was observed in the executed
catalog. Three reproduced usability defects were corrected:

| Defect | Cause and smallest correction | Verification |
|---|---|---|
| L2-D01 — validation message over phone caption | Percentage-based badge width covered the caption; reserve measured caption space | Nine focused assertions plus actual invalid-phone UI refusal |
| L2-D02 — “New Admission” clipped | Fixed badge/column width; measure the status text | Seven focused assertions and current-display screenshots |
| L2-D03 — compact toolbar captions/message clipped | Fixed button widths after adaptive scaling and narrow status reservation; use native preferred button sizing and wider status space | Three focused assertions, actual compact/maximized captures and admission CRUD/navigation regression |

Compare the [compact toolbar before correction](Level2Evidence/2026-10-16/L2-UX-05-before-compact-toolbar.png)
with the [verified 1280×720 correction](Level2Evidence/2026-10-16/L2-UX-05-1280x720.png)
and [maximized view](Level2Evidence/2026-10-16/L2-UX-05-maximized.png).
The [earlier status clipping](Level2Evidence/2026-10-16/L2-UX-status-before.png)
is preserved separately as defect evidence.

Automation issues were classified separately: caption mnemonic removal caused
a title locator timeout; optional native-grid inventories could fail after a
successful screenshot; and the recovery dialog requires an explicit Close after
reconnecting. The harness now handles these behaviors without changing production
business rules or permissions. Existing screenshot references were enriched from
files actually captured; no assertion status was changed to repair a missing link.

## Remaining acceptance and regression risks

`L2-UX-06` remains **Blocked**: actual 1280×720/1920×1080 monitor configurations
and 125%/150% Windows scaling were not exercised. Only the current real display
and two application-window sizes were inspected. Follow the explicit matrix
instructions in [LEVEL2_TESTING.md](LEVEL2_TESTING.md).

Additional acceptance depth outside this 48-case catalog is **Not Run**, including
exhaustive promotion-history/statement filter combinations, every restricted
action and keyboard tab sequence, supported deletion workflows, financial input
boundaries/concurrent operators, interruption during a write, receipt export or
physical printing, large grids and long-session memory/performance behavior.
The current results do not certify those behaviors. Backup/restore and installer
operations are also excluded. Complete the relevant remaining acceptance checks
before declaring broad Level 2 acceptance or Level 3 readiness.

All screenshots contain fictional records. No operational database was connected,
no school files/settings were overwritten, no genuine messages were sent, and
no private credentials or data-bearing database artifacts are included.

## Rerun

From the repository in an unlocked Windows operator session:

```powershell
.\Tools\Run-Level2.ps1
# Focused reruns, with other scenarios explicitly blocked:
.\Tools\Run-Level2.ps1 -AdmissionsOnly
.\Tools\Run-Level2.ps1 -StartupOnly
```

Review captured images explicitly with `review_layout.py`, validate the public
evidence with `validate_report.py`, and retain each run's actual source metadata.
The suite returns nonzero while a required scenario remains blocked or unreviewed.
Detailed prerequisites, safety boundaries, cleanup and dedicated-runner setup are
in [LEVEL2_TESTING.md](LEVEL2_TESTING.md). Implementation, fixes, results and
fictional evidence were published to `main` through
[PR #42](https://github.com/farhan2166-creator/FMSC-Database/pull/42)
(merge commit `2c22a53`) with the owner's authorization. Historical source commits
in the evidence identify the actual tested revisions; publication does not add
test coverage or change the recorded outcomes.
