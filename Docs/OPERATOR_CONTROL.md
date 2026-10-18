# Operator decisions and financial safeguards

School decisions belong in the application. SQL validates permissions and keeps
identities, bills, allocations and money consistent when the decision commits.
A cutoff is a school default; it must not silently replace an authorized decision
about an individual student.

## Concessions

From Fee Discounts & Transport, select a monthly fee and open Fee Concession.
Choose the first and last month, amount or full-concession label, reason and
whether already issued bills in that period should change. Review the issued-bill
and agreed-period tabs before confirming. Future scheduled rates and concessions
outside the chosen dates are retained; a temporary concession restores the
previous agreement after its last month.

Leaving issued bills unchanged changes only the agreement used for posting.
Applying the concession to issued bills reconciles their full correction chains.
Reductions consume unpaid dues first; a paid difference becomes the selected
credit or refund. Removing a concession can add an explicit amount owed. Receipts
are retained. No monthly bill is manufactured just to represent a concession.

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

## Remaining boundaries

Closed placements keep their assignment history. Broader retrospective contract
changes, rate-change timing, annual/one-time fee exceptions and carrying agreed
concessions through promotion need the same operator-decision review before the
whole operating model can be called complete. This revision does not certify
those paths merely because the existing automated tests pass.
