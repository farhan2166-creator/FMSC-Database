# Recent changes regression review

Reviewed the report viewer enhancements and commits from the approved audit corrections through the outstanding-sheet sorting change (`cb7f073` through `0932681`). Checked the shared code paths, not only each new page.

## Problems found and fixed

1. **Class-sheet preview could appear enabled after invalidation.** `AddBusyAction` temporarily disables its button and normally restores it. A class-filter change during the pending preview left the button already disabled, so the wrapper could miss that the page wanted it to stay disabled. The preview method still refused stale data, but the button state was misleading. `ClassSheetsPage` now rechecks its loaded scope, selection and busy state whenever the button is enabled.
2. **Long names were clipped on compact marks/attendance sheets.** The fixed-position row textboxes did not grow. A rendered 54-character student name visibly lost its last words. Both templates now use table rows that grow together, preserving the whole name and keeping all columns aligned. Entered school/exam/teacher details can wrap too. The two copies retain the same data and geometry. Typical pages still hold 30 students; unusually long text may require physical continuation pages rather than dropping text.

## Verification

- Application build: zero warnings/errors.
- 50 shortcut logic checks, 4,960 report-page arrangement checks, 10 fee-rate outcome checks, 576 class-sheet checks and 141 class-outstanding checks. Both new report types process through the real RDLC engine without warnings. Long-name output was visually reviewed through rendered HTML.
- 36 disposable-database scenarios: nine approved-audit checks, 14 lifecycle/correction checks, five enrollment-date checks and eight outstanding-balance checks. Fixtures roll back. Covered selected-year preview/posting and mixed-year rejection, occupied-placement protection, established reversal eligibility, long-history reporting, audit JSON, financial/lifecycle protections and exact outstanding balances under seven filter combinations.
- Ran the application's complete compatibility query against the updated database copy: **zero incompatible/missing schema features**, including the new academic-year procedure parameters. Searched both C# monthly preview/posting callers; both supply the year.
- Reviewed optional reasons: routine amount changes and transport stopping supply factual action summaries when blank. Existing SQL reason requirements, permissions and audit logging remain in force. Correction, reversal, refund and additional-charge explanation requirements remain.
- Reviewed shared finance report integration: only Outstanding & Aging enables the new A4 action. Its original Students/Charges/Aging/Families views, exports and normal grid-print paths were not replaced. Other canonical finance pages use the default constructor behavior.
- Reviewed receipt printing and the shared viewer: overview arranges the original rendered pages only; current-page printing uses a page range. Existing receipt report definitions and fee/billing calculations were not changed by this review.

## Practical limits and deployment

These checks found no additional financial or lifecycle regression in the tested scenarios. They do not establish that every Windows interaction or physical printer behaves correctly. Native WinForms focus/checkbox handling, DPI scaling, PDF/EMF output, printer page setup/ranges and simultaneous administrator contention still require Windows/multi-session checks. The new preview-button readiness fix is source-reviewed and compiled; its native EnabledChanged behavior remains part of that manual review.

No new migration is needed for these review fixes. **Migration 18 remains required for the earlier audit corrections before deploying the current application.** There are no new business-rule decisions or changes to established audit behavior in this review.
