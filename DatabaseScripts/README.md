# Admission correction and confirmation (v1 baseline)

User decision: Admission owns student edits and placement review. Saving a new
placement leaves it unconfirmed. The operator explicitly confirms it after
reviewing placement and fees. Confirmed placements do not expose academic-year,
class or status correction fields; actual movement uses Promotion, Transfer or
Re-admission. Genuine date corrections remain available with the existing
financial and certificate review.

`PlacementConfirmation.sql` adds confirmation metadata and updates admission,
confirmation and lookup procedures. Existing recorded placements remain
confirmed. `StudentCorrectionGuards.sql` prevents premature/out-of-period
billing, checks backdated promotions, and permits audited reversal of unpaid
charges on closed enrollments while protecting payments. Apply the scripts in
that order, in one transaction, to a compatible v1 development database. They
are idempotent. The current live development schema and `FMSC.dacpac` include
these changes; the application/schema versions remain v1.

The Admission placement-correction batch is embedded in the application.
It uses a dependency snapshot, database locks and one transaction. It preserves
existing fee amounts, cancels only reviewed unpaid out-of-period charges, and
records reasons and before/after values. Paid charges require Fee Corrections.
Draft placement review never posts new bills. Confirmation posts opening fees
for an active enrollment once; historical closed records are confirmed without
posting fees. Confirm Return remains the return/billing workflow after promotion.

Student Record Checks is a diagnostic/history view. Its Review/Open Admission
actions open the student in Admission instead of exposing a second editor.

Run `Validation/StudentCorrectionChecks.ps1` on Windows with LocalDB to verify
the SQL flows. It backs up the live database with COPY_ONLY, restores a separate
test database into `Tests`, performs all record writes there, and drops that test
database afterward. It does not repair or change live student or fee records.

Release packaging continues to use the latest user-supplied detached snapshot
in `../Database`; these source scripts do not replace that established step.
