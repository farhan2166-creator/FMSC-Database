# Transport policy — 27 October 2026

The owner chose one transport policy for admission and fee collection.

- School Profile has its own Transport Cutoff Day, independent of the general admission fee cutoff. It accepts days 1–28 or blank. Migration 58 initially copies the existing fee cutoff to preserve the previous transport-start boundary until the owner changes it.
- Before the transport cutoff, the current month is permitted. On or after it, changes begin next month. Blank permits current-month changes throughout the month.
- Any month with a recorded payment, including a partial payment, remains charged. Stopping begins after protected months. Earlier unpaid balances remain owed.
- Stopping from a permitted month cancels wholly unpaid posted transport charges from that month onward and ends the fee plan. Recorded payments and allocations are never cancelled, refunded or moved to credit by this action.
- Starting in the current permitted month posts its charge immediately without adding a duplicate. A future start waits for normal monthly billing. A new transport period may bill a month that has an older cancelled copy, while that cancelled copy remains historical.
- Start and Stop use the same dialog, with a selectable month, an optional reason, and the effect on posted fees explained before confirmation.
- Fee Collection has one context-sensitive Start/Stop Transport button immediately after Student Fees. Admission's Fee Discounts & Transport uses the same service.
- Writes recheck cutoff, payments, eligibility and the loaded snapshot under the monthly billing lock. A committed request marker prevents an uncertain retry from applying the action twice.
