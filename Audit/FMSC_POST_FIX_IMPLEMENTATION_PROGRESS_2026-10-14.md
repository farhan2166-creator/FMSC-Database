# Post-fix audit implementation progress

Source: [independent regression audit](FMSC_POST_FIX_INDEPENDENT_REGRESSION_AUDIT_2026-10-14.md).
Each finding is verified, implemented, checked and committed before the next.
Native Windows GUI tests cannot run in this Linux workspace; compiled fixtures
are not reported as executed acceptance tests.

## RFA-01 — Quick Registration internal tab context

Confirmed the host had no Selecting guard. It now refuses tab selection while
its registration form saves or loads fee/family context, any loaded child exposes
an active busy contract, or an exact registration save remains unresolved.
Returning to Registration Form for outcome checking remains allowed when no
operation is running. The active page shows inline guidance and draft/family state is retained; refused
selection never triggers the Pending tab's automatic refresh. Unresolved recovery
is kept as a leave/discard contract rather than an indefinite busy operation.

Added actual-host selection fixtures for new/saved write states, settled release,
fee/family loading, retained unknown outcomes and a busy sibling child. App and
Windows smoke project compile with zero warnings/errors. Native delayed-save and
mouse/keyboard tab scenarios remain Windows acceptance work. No SQL changes.


## RFA-02 — Regular Student Certificate new-date drafts

Confirmed HasUnsavedChanges excluded every new issue. Added a student-bound loaded
baseline for the editable new-issue date. Reverted/default dates and an unselected
student stay clean. Saved certificates retain their original class/date snapshot.
Student changes, Next Student, Refresh, saved-row selection and shell leave now
consult the same dirty predicate. A declined saved-row switch clears the unaccepted
highlight when the editor remains a new draft. Stay preserves context without a reload;
accepted new-draft Refresh reloads student defaults and establishes a clean
baseline. Confirmed save clearing retires the earlier draft intent.

Added actual-page injected-read/confirmation fixtures covering clean defaults,
new-date edits, Stay without reads, revert, accepted Refresh/Next, saved-record
edits and post-save field clearing. App and Windows smoke project compile with
zero warnings/errors; native Windows interaction remains unexecuted here.
No SQL changes. The two New-button bypasses are the separate RFA-03 change.
