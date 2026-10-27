# Historical admission and return eligibility — 27 October 2026

An admission entered into a completed academic year is an active, confirmed
historical placement. Its BillingReady=0 deliberately prevents historical fees;
it does not mean that the child has been promoted and is awaiting return.

Family Admission identifies this placement as Previous Academic Year and uses
the existing-child editor. Historical posted dues remain collectible. Individual
Admission and Confirm Return omit expired placements from their pending-return
list. Future-year placements are likewise not offered for early return.

Promotion eligibility is preserved: these students can be selected from their
previous-year source class. The normal promotion/hold batch creates the new
placement. A confirmed active placement in the current academic year with billing
not yet enabled may then use Confirm Return. Holds retain their separate reviewed
Lift Hold / Resume workflow.

Migration 57 corrects GetPendingReturnAdmissions and independently guards
ConfirmPromotedAdmission against expired/future years, legacy setup and draft
placements. It preserves all existing student, enrollment, fee, payment, credit,
history and recovery records. Reapply the versioned migration after restoring an
earlier snapshot; do not repair this classification by setting historical
BillingReady=1 or by changing the student's original admission/placement dates.
