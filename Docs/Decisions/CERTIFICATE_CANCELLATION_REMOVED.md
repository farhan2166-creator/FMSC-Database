# Decision: remove certificate cancellation

Date: 25 October 2026. Owner decision recorded from the certificate workflow discussion.

## Decision and reason

Remove the certificate cancel/void feature entirely from the normal application, rather than merely hiding its button. The user first requested hiding it, then preferred removal if the scope was manageable.

The action introduced unnecessary complexity. The user cancelled a row and could then issue another copy from that same row, making cancellation appear ineffective. More importantly, “cancel” or “void” can imply deleting the certificate record or undoing the student's leaving, enrollment or fee decisions, although the operation only invalidated a paper copy. Hiding cancelled rows alone would conceal that ambiguity and make replacement handling harder to discover.

The supported counter flow remains: **Create → Save Changes while uncollected → Print → Mark Collected → locked copy**. **Issue Again** prepares a separate copy while preserving the earlier one. Reprinting uses the selected saved copy. These existing actions remain otherwise unchanged.

## Implementation and historical data

- Remove the shared cancellation button, event handler and command call from all ten certificate forms.
- Migration 51 drops `dbo.VoidCertificateCopy`; startup compatibility no longer requires that procedure.
- Keep existing historical cancellation markers, reasons and guards read-only. They preserve what already happened and do not offer a new cancellation operation. Removing the feature must not resurrect, delete or silently collect old cancelled copies.
- Do not undo student leaving, enrollment, bills, payments, allocations or fee decisions as part of this change.
- RDLC files are unchanged.

Do not reintroduce certificate cancellation or a “show cancelled / prepare replacement” workflow without a new explicit owner decision.
