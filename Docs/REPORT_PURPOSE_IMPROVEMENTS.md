# Report meaning and scope improvements — 2 November 2026

## Debt reports

Migration 70 updates only GetOutstandingAndAgingReport. Charges, Students and
Families display Outstanding plus Overdue Amount, Due Today and Not Yet Due.
Missing due dates are a fourth explicit amount category so legacy rows remain
visible and all categories reconcile to the full balance. Has overdue charges selects a
whole student/family row with overdue debt; it does not relabel future fees as
currently overdue. For charge rows, it selects the actual overdue charges.
Day-range filters on grouped rows use the oldest unpaid charge. Filtered totals
show the three money categories separately. Families retains students without a
family as separate Student: reference groups. No family links are invented.

The shared reader's permissions, charge eligibility and financial balance
function remain unchanged. No existing charges, payments, allocations, credit,
student or lifecycle history are rewritten. App/installer require schema 70;
apply 70 after 69 through the existing verified-backup transaction workflow.
The reference DACPAC is not updated or published over school data.

## Concession Summary

Summary as of is an explicit required date (today initially). From/To apply only
to the other Billing & Discounts views and are disabled/omitted from the summary
scope description. The summary selects eligible assignment windows and placements
on that day, then one latest effective term per enrolled fee, then checks its
concession label. A newer ordinary agreement therefore does not revive an old
concession. Monthly/yearly rates remain separate, and equal names under different
campus/class/fee IDs do not merge. Concession Assignments remains the history view;
Discount Detail remains actual billed reductions. This is an effective-agreement
reader, not reconstruction of all database content as known at a past timestamp.

## Attendance and checks

Monthly register screen/print captions explicitly describe the current roster.
A past print month does not imply a reconstructed historic class roster.

The report regression date expectations now match numeric month formatting.
Desktop CI invokes both report data and grouped RDLC engine suites. Focused
portable checks cover split totals, empty results, stable grouping IDs and SQL
syntax/contracts. Optional --sql checks run only in a uniquely owned disposable
local database: effective concession versions, exact migration application,
family/standalone reconciliation, due-today/future boundaries and permissions.
Native rendering and real SQL execution are separate from compilation.
