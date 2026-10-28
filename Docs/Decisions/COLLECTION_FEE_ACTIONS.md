# Focused collection fee actions — 27 October 2026

28 October follow-up: cancelling an unpaid charge requires both collection and adjustment permission through every UI route and at the SQL write. Migration 60 retains a full cancellation reason, actor and timestamp separately from the bill's existing notes; it does not delete the bill or its history. See [the admission/fee flow fixes](ADMISSION_FEE_FLOW_FIXES.md).

The collection grid shows unpaid fees and newly waived posted fees awaiting a
receipt or fee-change acknowledgement. A zero balance alone cannot distinguish
payment from waiver: the existing visit/snapshot baseline supplies that evidence.
Once a saved document records a waiver, it leaves the collection grid. Historical
payments, credit applications and already documented waivers do not reappear.

Waived rows are read-only, clearly labelled, excluded from allocation, and offer
only View Fee Details. On payable rows, Give/Change Discount and Remove Discount
open separate, fixed tasks for the selected bill. These dialogs cannot switch to
waiver, rate correction, fee-plan changes or a different period. Waive Remaining
Dues keeps its direct-save path and preserves earlier payments. Future fee-plan
and fee-rate management remain in the fee-management workflow.

View Fee Details is read-only. Uncertain saves retain a separate Check Fee Change
Save recovery action, shown when recovery is pending, without reopening general
editing choices. Payment drafts must be finished or cleared before a saved fee
change or its acknowledgement is submitted.

When only pending waivers remain, the main print action becomes Print Fee Changes
and uses the established zero-payment acknowledgement. After printing, the grid
reloads from saved documents. With dues remaining, Print Outstanding retains the
existing bill flow and pending waivers accompany the next payment receipt.
This does not change the financial Outstanding/Aging report's debt-only meaning.

No schema or stored-procedure changes are required: selection uses the existing
schema-56 FeeDocumentAccounts and FeeVisitLines functions. The acknowledgement
print layout is documented separately in [Fee-change note](FEE_CHANGE_NOTE.md).
