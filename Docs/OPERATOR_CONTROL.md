# Operator decisions and financial safeguards

School decisions belong in the application. SQL validates permissions and keeps
identities, bills, allocations and money consistent when the decision commits.
A cutoff is a school default; it must not silently replace an authorized decision
about an individual student.

## Agreed fees and concessions

From Fee Discounts & Transport, select a fee and open Review Fee Terms.
Choose a revised rate if needed, a fixed or full concession, the first and last
month for a monthly fee, and a reason. Decide whether to update the agreement,
issued bills, or both. Review the issued-bill
and agreed-period tabs before confirming. Future scheduled rates and concessions
outside the chosen dates are retained; a temporary concession restores the
previous agreement after its last month.

Leaving issued bills unchanged changes only the agreement used for posting.
Applying the concession to issued bills reconciles their full correction chains.
Reductions consume unpaid dues first; a paid difference becomes the selected
credit or refund. Removing a concession can add an explicit amount owed. Receipts
are retained. No monthly bill is manufactured just to represent a concession.

Annual fees and individual purchases use the same comparison. Fee Collection's
bill menu opens a correction scoped to that one bill, including its earlier
correction chain. The fee-assignment list also includes completed purchases, so
fully paid bills remain accessible. A closed placement allows issued-bill
corrections while retaining its historical agreement and closed status.

Available credit is retained unless Use available credit is selected. The review
shows available credit before, credit to use, and credit remaining. If selected,
application follows the displayed order: oldest due tuition, then other due fees;
future-dated obligations do not consume credit early. Credit use may settle an
older bill outside the selected correction period. It creates no cash received.

A signed-in operator still needs Fees.Adjust at save time. A changed bill/payment
invalidates the review. An uncertain save requires Check Save Outcome; the saved
request is checked under the same database identity before another submission.
These request markers and financial corrections are operational records, not
writes to the retiring audit system.

Direct admission and Quick Registration explicitly choose whether monthly
concessions start in the admission month or the following month. Unticking the
admission-month choice keeps the opening monthly fee at its full rate when due.
The school cutoff still chooses when monthly billing starts; a concession never
creates a monthly liability by itself.

## Leaving

Mark as Left separates the departure date from the decision about issued monthly
fees. Choose the normal cutoff, retain all issued monthly bills, or waive from a
selected month. The preview shows outstanding dues, unpaid fees waived, paid
money returned as credit/refund, and the remaining balance on this placement.

Annual/one-time fees and earlier-placement dues remain as recorded. Leaving stops
future automatic posting; keeping an issued bill does not authorize new posting
after departure. Reductions need Fees.Adjust. Recording departure while retaining
issued bills needs student administration permission and no financial override.

## Other existing operator choices

- Billing commencement and historical admission-fee acknowledgement are reviewed
  when activating each existing student.
- Missed billing months require a deliberate inclusion/recovery decision.
- Collection supports partial/family money and reviewed allocation; credit can be
  retained, refunded or transferred to a linked sibling.
- Transfer offers retaining fees or applying reviewed destination fees by month.
- Return confirmation allows reviewing the new placement's fee amounts/discounts.

## Promotion and return

Student Promotion explicitly offers carrying the prior placement's final monthly
concession or resetting it for return review. Expired temporary concessions do
not carry. A carried fixed amount is capped at the new rate; a full concession
covers that new rate. Carrying requires Fees.Adjust. Yearly and purchase
concessions do not carry automatically. No new-year charges are issued until
return confirmation, where the operator can review the student's actual fee plan.

## Safeguards and acceptance

The database still refuses duplicate identities, negative or fractional money,
unreconciled paid reversals, stale reviews and unauthorized changes. These protect
records and money; school-policy choices belong in the reviewed application flow.
Closed placement dates and original agreements remain history; correcting a bill
does not reopen a student or silently change their enrollment.

This covers the operator choices identified in this revision. School-PC acceptance
still needs realistic examples using the school's policies and a disposable copy.
Passing automated scenarios alone does not establish that every policy exception
or physical operator workflow has been accepted.
