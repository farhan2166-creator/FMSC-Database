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


## RFA-03 — New commands bypass dirty checks

Confirmed Regular's explicit confirmDiscard:false and Bank's unguarded field
clearing. Regular New now follows its normal ClearForm discard contract; Bank New
checks its existing dirty predicate before clearing. Stay retains the selected
record/editor, accepted discard starts a clean editor, and clean/repeated New
remains prompt-free. Active saves and unresolved bank recovery remain unconditional
refusals before discard; New performs no database write.

Added actual-page confirmation fixtures for both saved editors: Stay, discard,
clean/repeated New, regular write-busy refusal and bank unresolved-recovery refusal.
App and Windows smoke project compile with zero warnings/errors. Native Windows
execution remains outstanding. No SQL changes or additional migration required.


## RVA-01 — Additional-fee SQL replay verification

11 native SQL checks pass on synthetic, confirmed admissions in a disposable
copy: discarded post-commit reply, exact original charge IDs on reconnect and
reordered selections, changed amount/fee/discount/reason/date/students refused
with error 53026 and no new charges, a single retained marker, concurrent copies
and recovery of the original request after intervening postings. The existing
stored procedure enforces this contract; no SQL change was required.

This verifies retry with the **original request ID**. It does not certify a C#
application restart that loses its in-memory ID, or an actual transport failure.
A different request ID is a different posting. The restart/unknown-reply operator
scenario remains part of native application acceptance rather than a claim of
unconditional duplicate prevention. Fixture creation is atomic and supplies
valid admission numbers, unique codes, completed legacy setup and confirmed
placement so the existing database safeguards remain exercised.

## RVA-02 — Earlier snapshot upgrade and restore verification

Published the earlier owner-supplied schema-4 data snapshot into a disposable
loopback SQL container; preserved its recovery GUID while correcting DacFx's
text serialization to the required SQL variant type in this copy only. All eight
migration 5/6 checks pass: ordered upgrades, injected final-write rollback,
version-safe rerun and original student/charge/payment/certificate/permission
preservation. Current production startup compatibility passes after upgrade.
Checksum backup, VERIFYONLY and actual restore also pass; the shared production
compatibility check accepts the restored database. No school MDF/LDF or current
DACPAC was modified. The disposable container and credentials were removed.

## Final regression checks and native Windows acceptance

650 portable audit checks and 32,183 performance/loading checks pass. Reviewed
package provenance, complete contract, whole-rupee constraints and all 273 SQL
module fingerprints pass. App and Windows smoke project compile with zero
warnings/errors. Native SQL checks ran successfully; their NU1900 warning records
unreachable NuGet vulnerability metadata, not a disabled audit or dependency edit.

The three RFA findings are implemented in independent commits. A focused native
Windows entry point compiles and can run the actual page/host contract fixtures:

```powershell
dotnet run --project Tests/ThemeSmokeChecks -- --post-fix
```

These use injected reads/discard decisions and disabled startup/recovery-file
initialization; they do not issue school database writes. This Linux workspace
has no Windows executor, so no Windows-run pass is claimed.

On Windows, also verify real delayed registration save/update followed by mouse
and keyboard tab attempts; failed/unknown save followed by Check Save Outcome;
new-date edits followed by student change, Next, Refresh and page leave; edited
saved certificates followed by New/Stay/New/Discard; busy and unresolved bank
creation; clean default actions without prompts. Use a disposable database copy
for SQL-dependent acceptance. Real process restart/network interruption, printer
preview/physical printing and intended screen DPI/resolution remain unexecuted
here. No additional SQL migration is required by these three UI fixes.
