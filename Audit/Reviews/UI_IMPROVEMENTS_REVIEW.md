# UI improvement review

The six stages are separate commits for review and rollback.

1. Shared grid presentation preserves page-specific date formats and amount alignment. The existing content-aware column sizing remains in use.
2. Outstanding & Aging, Collections & Receipts, and Billing & Discounts remember the location from the last successful current report using the existing per-user, per-PC preference store. A reminder appears after restoration. Clear Filters removes the preference and clears the report. Invalid saved lookup IDs fall back to All with a review notice. Restoring filters does not execute a query.
3. A failed report refresh clears the displayed result and pager cache, and shows an inline failure message. Finance totals and print/export readiness now reflect loading, failure and reset states.
4. Shared report filter bars wrap when a field would be narrower than the scaled 150-pixel minimum. Controls are repositioned rather than recreated. Filter-card sizing is queued after layout to avoid retaining startup dimensions.
5. Monthly Fee Posting includes the location in its existing confirmation and retains a last-posting outcome above the grid when the eligible list refreshes. Missing server counts are labelled unavailable. Existing previews, selections, procedures and confirmations remain the write path.
6. Generic printed grid reports calculate widths from formatted printed values, align numeric types and dates consistently, and retain their existing A4 sizing, growing textboxes, repeating headings and page footer.

## Cloud validation

The application cross-builds for Windows with zero warnings and errors. All eleven portable check suites passed: StudentReportViewChecks, ShortcutLogicChecks, ReportPreviewChecks, ReceiptLayoutChecks, ClassSummaryChecks, ClassSheetChecks, ClassOutstandingChecks, FeeReminderChecks, FeeRateApplyChecks, ReportFilterChecks and GridReportChecks. Rendering checks use the actual RDLC engine. SQL definitions were not changed.

## Windows review

- Check grid amounts, dates, admission numbers, names, selection, sorting and keyboard navigation in reports, admissions, fee collection and setup.
- Run each finance report with a location, reopen it, confirm the reminder, clear filters, then reopen again. Check that unsuccessful reports and filter changes during loading do not save a new location. Review saved filters under a restricted user and after a lookup is removed. Restore an existing workspace tab and confirm its own selections take priority over the last-completed-report preference.
- On a test database, verify report success, empty results, failure and retry. A failed refresh should not leave a previous result available to print or page.
- Review report and posting screens at laptop sizes and increased display scaling using the [UI tuning guide](../../Docs/Design/UI_TUNING_GUIDE.md).
- On a test database, review posting confirmation, selected students and net total. Verify full and partial outcomes, filter changes during refresh, and a refresh failure after a confirmed write. The last confirmed outcome should remain visible, with refresh failures still reported.
- Review generic reports and the existing receipt, reminder, class-sheet and class-summary previews, including multi-page results and a sample print/export.

Native WinForms layout, printing devices and live database behavior require this Windows review; cloud build and portable checks do not exercise them.

## Certificate edits and student search

Sports, No Objection, Provisional and Regular Student certificate pages expose existing-record edits to the application's leave/close guard. Switching students or certificates asks before discarding edits; selecting the same certificate preserves those edits. Declining a record switch restores the selected row. Certificate creation exposes its busy state to the navigation guard. Regular Student's New action now includes class-name edits in its discard check.

Student search ranks exact student/father names before name prefixes and substring matches, retaining official admission/registration priority, admission normalization, scope filters, natural admission ordering and ambiguous picker highlights. StudentSearchChecks covers the production matching and picker-result code.

On Windows, edit an existing certificate on each of these four pages and try another student, another certificate, the same certificate, New, Refresh, another tab/page and app close. Check Cancel preserves both fields and selected identity; accepting discards edits; saving clears the warning. Confirm page navigation is blocked during creation. Search for an exact name also contained in longer names, a father name, mixed student/father words, an admission number with leading zeros and duplicate admission results. Review with a restricted account. Database rules and permissions were not changed.
