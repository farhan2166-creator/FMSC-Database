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

## FQA-15 — Shared Admission operations are invisible to leave guards

**Confirmed and fixed.** V2Page now exposes its existing RunBusyAsync state and
operation message to derived pages. StudentAdmissionPage includes that state in
IBusyPage along with its existing admission-save and photo flags. Placement/
admission confirmation, legacy verification and both print preparations now
remain visible to shell/modal guards until their shared operation settles.
The shared double-submission gate and exception propagation are retained.

**Verification:** App and Windows smoke project build without warnings/errors.
Added pending-operation tests for the five operation messages, duplicate refusal,
success and server refusal releasing the guard. Tests use delayed delegates,
not SQL writes; native Windows execution remains required. No SQL changes.

## FQA-01 — Registration Review / Confirm modal has no leave guard

**Confirmed and fixed.** Added ModalPageCloseGuard using existing IBusyPage and
IUnsavedChanges contracts. The registration review owner attaches this guard:
submitted operations block every close reason, edited drafts require discard,
and an explicitly completed/cancelled registration closes without a false draft
prompt. FQA-15's shared-operation state is included through the hosted editor.

**Verification:** App and Windows smoke project build without warnings/errors.
Added modal event fixtures for clean close, Stay/discard, active user/app close,
completion while still busy and confirmed completion. Native modal interaction
and SQL-delayed close acceptance remain outstanding on Windows. No SQL changes.

## FQA-07 — Daily closing operation can be left by confirming discard

**Confirmed and fixed.** DailyCollectionClosingPage now implements IBusyPage
using its existing operation flag, including submit, approval and their reloads.
IUnsavedChanges tracks only actual unsubmitted edits. Dashboard leave/close now
blocks the operation before it can offer a discard decision; draft-only notes
and amounts retain their normal discard prompt.

**Verification:** App and Windows smoke project build without warnings/errors.
Added actual-page close-contract fixtures for busy clean/dirty state, zero discard
prompts during the operation and normal settled draft behavior. Native delayed-
SQL, sign-out and navigation acceptance remains required. No SQL changes.
