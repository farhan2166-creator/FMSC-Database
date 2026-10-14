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

## FQA-04 — Posting reversal does not block page departure

**Confirmed and fixed.** Reversal now runs through the established shared
RunBusyAsync gate while the whole page is held. IBusyPage remains true through
validation, confirmation, the SQL call and the post-reversal month/impact refresh.
Conflicting preview/filter/selection actions cannot run during the operation.
Collected-charge exclusions, scope validation and server result counts remain.

**Verification:** App and Windows smoke project build without warnings/errors.
Added actual-page shared-operation fixtures for busy visibility, conflicting
control lock and settled restoration. Native delayed SQL/navigation acceptance
remains outstanding. No SQL migration is needed.

## FQA-08 — Family maintenance does not advertise its active write

**Confirmed and fixed.** StudentFamilyPage now implements IBusyPage through its
existing RunFamilyActionAsync flag. Both add/remove actions and their family
reload remain protected by shell/modal leave guards. The existing action,
selection and Students.Administer/SQL guards are preserved.

**Verification:** App and Windows smoke project build without warnings/errors.
Added pending family-action, overlap refusal and full-completion fixtures using
delayed delegates, with lookup initialization disabled only in the test seam.
Native delayed SQL and shell leave acceptance remains required. No SQL changes.

## FQA-09 — Duplicate Confirm/Dismiss decisions can overlap

**Confirmed and fixed.** Scan and resolution batches now share RunBusyAsync and
hold all conflicting page controls. Selection events cannot re-enable either
decision during a batch. Captured ReviewIds, individual SQL calls, partial-result
accounting and permission checks are retained; no batch auto-retry is added.

**Verification:** App and Windows smoke project build without warnings/errors.
Added selected-pair tests for both decision buttons remaining disabled during
selection changes, both handler paths refusing overlap, scan refusal and settled
restoration. Native SQL-delayed multi-row acceptance remains required. The host
leave-contract gap is tracked separately under FQA-10. No SQL changes.

## FQA-10 — Duplicate batch/scan can outlive its workspace

**Confirmed and fixed.** DuplicateStudentAuditPage advertises its existing shared
operation gate through IBusyPage. StudentDataReviewPage forwards active state
and its message from every loaded tab and refuses tab changes while any child
is busy. The dashboard therefore blocks departure through full scan/resolution
and refresh, including a cached child that is not currently selected.

**Verification:** App and Windows smoke project build without warnings/errors.
Added real-host fixtures using inert cached children for hidden-child visibility,
tab refusal and settled navigation; extended the actual duplicate-page fixture
with its busy contract. Native delayed SQL/navigation execution remains required.
No SQL changes.

## FQA-11 — Student Record Checks correction writes lack a composite guard

**Confirmed and fixed.** Status save, placement save, missing-record creation and
date review now enter one shared correction gate and hold the whole page through
preflight, SQL and post-save reads. Beginning a correction invalidates older mode
reads. IBusyPage forwards this state through the FQA-10 review host, preventing
conflicting corrections and workspace departure. Existing correction rules and
permission checks remain; synchronous SQL performance is a separate concern.

**Verification:** App and Windows smoke project build without warnings/errors.
Added delayed actual-page correction tests for composite-host busy visibility,
whole-page control hold, overlapping correction refusal and settled restoration.
Native SQL and GUI execution remains required. No SQL changes.

## FQA-14 — Unsubmitted correction drafts are invisible to workspace leave

**Confirmed and fixed.** StudentRecordChecksPage implements IUnsavedChanges using
separate loaded snapshots for status and placement/date/reason fields, including
missing-record intent. Programmatic successful loads establish clean baselines;
default fields without a selected student are not drafts. User student changes
and explicit Clear check discard before replacing the draft. StudentDataReviewPage
aggregates dirty state from all cached tabs for shell navigation/close.

**Verification:** App and Windows smoke project build without warnings/errors.
Added loaded-clean, changed reason, billing-date, missing-record and hidden-tab
aggregation fixtures. Native GUI/discard-dialog acceptance remains required.
No SQL changes.

## FQA-02 — Adjustment draft carries from one payment to another

**Confirmed and fixed.** Adjustment amount, reason and transaction date are now
owned by the (student, payment) identity. Changing identity clears those fields;
reselecting the same identity retains its in-progress values. UpdateActionMode
still derives locked full-reversal amounts and the existing final confirmation
and SQL eligibility checks remain in place.

**Verification:** App and Windows smoke project build without warnings/errors.
Added actual payment-grid fixtures for same-payment preservation, A→B→A resets,
date reset and a changed student context. No financial write is submitted in
these fixtures; native GUI execution remains required. No SQL changes.

## FQA-05 — Delayed Payment Details can open over a newer selection

**Conditional source race verified and fixed.** Detail reads now use LatestReadScope
and compare captured student/payment/actor before showing data. Payment identity
changes, history reload and disposal invalidate those requests; A→B→A cannot
revive A's earlier response. A superseded error cannot overwrite the new context.

**Verification:** App and Windows smoke project build without warnings/errors.
Added delayed read fixtures for A→B→A rejection, valid current preview and disposal
using injected reads/preview callbacks. Existing portable latest-read tests passed
with FQA-16. Native Windows execution remains required. No SQL changes.

## FQA-12 — Annual receipts preview can arrive for an obsolete selection

**Conditional source race verified and fixed.** Annual receipt preparation now
uses the shared operation gate, holds the page's student/year controls and
advertises IBusyPage through the full preview lifecycle. After receipt, identity,
receipt-line and balance awaits it checks captured student/year and disposal
before continuing or opening a preview. Receipt contents still use captured IDs.

**Verification:** App and Windows smoke project build without warnings/errors.
Added actual-page preparation fixtures for held student/year controls, owner busy
visibility and settled restoration. Native delayed SQL/RDLC preview acceptance
remains required. No SQL changes.

## FQA-13 — Confirmed adjustment success is hidden by failed history reload

**Confirmed and fixed.** The payment-history loader now distinguishes a normal
read failure from a read after a confirmed adjustment response. The latter keeps
the exact completion/receipt message, explains that the adjustment was recorded
and only history failed to refresh, and directs reload instead of repeating the
financial command. Existing selection clearing and SQL behavior are retained.

**Verification:** Traced the confirmed-write → completion message → load failure
branch and current-student/load-version guards; app and Windows smoke project
build without warnings/errors. Native successful-write/failed-read injection is
still required. This does not resolve FQA-06's separate lost-write-response risk.
No SQL changes.

## FQA-19 — Failed statement regeneration leaves old Export current

**Confirmed and fixed.** Starting Generate Statement now clears the applied
signature and disables Export before validation or SQL. Only complete successful
binding/totals restore export eligibility. A failure explicitly labels the visible
snapshot as not refreshed. The previous table can remain for reference, with
student selection and dates retained for another generation attempt.

**Verification:** App and Windows smoke project build without warnings/errors.
Added an actual-page rejected-generation fixture asserting old export/signature
invalidation and previous-table retention without reaching SQL. Native SQL failure
and GUI status acceptance remains required. No SQL changes.

## FQA-20 — Statement SQL/assembly blocks the UI thread

**Synchronous source design confirmed and improved.** Statement generation now
awaits each captured student's read and assembles/sorts tables on a worker. Reads
remain sequential, avoiding a burst of SQL connections and simultaneous tables.
Selectors are held by the existing busy gate; filter changes/disposal invalidate
results, and only a complete current batch can bind and enable Export. Names are
captured from visible rows or resolved asynchronously. Existing transaction
filtering, distinct charge totals, payments and opening balance are retained.

**Verification:** 7 portable batch tests pass for immediate caller return, ordered
assembly, cancellation, failed-read stopping and one-at-a-time reads. Synthetic
profile: 32 delayed 8-ms queries finished in 266 ms with 62 caller heartbeats and
maximum 1 active read. This excludes real SQL and WinForms and is not a deployment
speed/RAM claim. Existing performance suites also pass (20,028 + 2,040 + 10,108).
App and Windows smoke project build without warnings/errors; added delayed
multi-student assembly/identical-total/export fixtures. Real SQL latency, Windows
responsiveness and resource measurements remain outstanding. No SQL changes.

## FQA-21 — New certificate drafts are lost on selection/departure

**Confirmed and fixed.** Character, School Leave and College Leave editors now
expose loaded-baseline dirty tracking for new drafts. User student changes,
Next Student, Refresh and switching to an issuance check discard first; declining
an issuance switch clears its misleading highlight without changing the draft.
The certificate hub's modal route attaches the common page-close guard. Existing
read-only issuances/reprint wording are not treated as new persisted drafts;
exam-type storage and optional leaving-clearance business rules are unchanged.

**Verification:** App and Windows smoke project build without warnings/errors.
Added all-three-editor fixtures for clean loaded defaults, edited wording, modal
Stay, existing-issuance exclusion and unbound defaults. Native student-picker,
refresh and real create/print acceptance remains required. No SQL changes.

## FQA-22 — Staff bank-certificate creation lacks a full busy contract

**Confirmed and fixed.** Create now uses the shared RunBusyAsync gate and holds
the whole page, so New/Clear/Refresh, editor fields, staff and history cannot
conflict with the submitted create. IBusyPage advertises the complete operation
through SQL and history reload; the existing staff/row restoration is preserved.

**Verification:** App and Windows smoke project build without warnings/errors.
Added an actual-page delayed operation fixture for owner busy visibility,
conflicting control hold and settled restoration. Native delayed Create,
close/sign-out and return-ID acceptance remains required. No SQL changes.

## FQA-23 — Bank-certificate save success hides a failed history reload

**Confirmed and fixed.** The bank history loader now returns an explicit result.
Create/Update preserve confirmed save feedback separately from history reads;
a failed or empty post-save history produces a qualified warning directing reload
and no repeat save. Only verified nonempty history is used for saved-row selection.
Post-save read errors are logged without a second generic failure popup masking
the confirmed outcome. Ordinary manual-read failures retain their error handling.

**Verification:** Traced both successful-write/failed-read branches and empty
history handling; app and Windows smoke project build without warnings/errors.
Native injected SQL history failure remains required. FQA-25's lost Create reply
is a separate verification item. No SQL changes.
