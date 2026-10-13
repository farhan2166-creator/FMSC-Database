# Functional workflow audit — verification and fix progress

Source report: [Deep functional audit](FMSC_FUNCTIONAL_WORKFLOW_DEEP_AUDIT_2026-10-13.md).
Each finding is checked against current source, fixed independently, tested and
committed before the next finding starts. Source-confirmed fixes are distinguished
from native Windows acceptance; this Linux environment cannot execute WinForms.

## FQA-16 — Additional Fee Posting stale candidate scope

**Confirmed and fixed.** A delayed Campus A response could bind after the operator
selected Campus B. Candidate loads now use LatestReadScope and a full year/campus/
class/section signature. Filter changes invalidate pending results and selections;
disposal rejects late replies. Posting checks the loaded signature and membership
of every selected enrollment, snapshots those IDs, and names the scope and a
student summary in its confirmation. Post/Select All require a current loaded list.
Existing SQL permissions, request identity and transaction behavior are retained.

**Verification:** Windows application and smoke project compile without warnings
or errors; added delayed A→B, current-result selection, scope clearing and disposed-
response GUI fixtures. Existing portable latest-read/cancellation regressions run
in PerformanceChecks. The Windows GUI fixtures require native Windows execution.
No SQL migration is needed.

## FQA-18 — Embedded fee matrix save is invisible to its host

**Confirmed and fixed.** FeeRateMatrixForm's existing savingGrid state now
implements IBusyPage for the entire save and post-save reload. WorkspacePage
already scans all loaded children and blocks switching tabs when one is busy;
the dashboard can now see this save through that established contract.

**Verification:** App and Windows smoke project build without warnings/errors.
Added an embedded-host fixture covering active-save tab refusal, navigation after
completion and a busy child in a non-selected tab. Native Windows execution of
these GUI fixtures remains required. No SQL or permission changes.

## FQA-17 — Campus picker silently discards fee-grid edits

**Confirmed and fixed.** Internal campus changes now finish validation of the
edited cell and check the actual grid snapshot. Changed grids require an explicit
discard; declining restores the previous campus without loading or touching the
edited cells. Clean switches remain immediate. Programmatic changes during a
submitted save also restore the original campus. This decision does not reuse
or consume the independent form-close discard confirmation.

**Verification:** App and Windows smoke project build without warnings/errors.
Added fixtures for Stay retaining campus/amount, deliberate discard, clean switch
and a campus change during save. Native GUI execution remains outstanding.
No SQL migration is needed.
