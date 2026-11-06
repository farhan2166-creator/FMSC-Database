# Student promotion and placement workspace

The **Placement Management** sidebar group opens four independent pages:
**Annual Promotion**, **Transfers & Sections**, **Placement Corrections**, and
**Placement History**. Each page has its own title, subtitle and full content area,
without an internal tab strip. Drafts last for the current page visit; navigating
away does not save an unprocessed plan. An unconfirmed submitted request blocks
navigation until its result is resolved using Check / Retry Plan.

Source filters are grouped on the left and destination choices on the right.
The centered action row follows the workflow: load/select students, suggest/assign
destinations, related student tools, then Review. Spacing separates these groups.

## Annual promotion

Choose the source year. Leave campus, class or section blank to include all active
placements in that scope. Click **Load Students**, then confirm the destination
year and optionally choose another campus. The app suggests the immediate next class within the same academic program
and the matching section. Missing assignments and final classes remain explicit
exceptions. They must be mapped, repeated or skipped before processing.

To promote one student, click **Clear Selection** and tick **Process** beside
that student. Leave their decision on **Promote** and check the destination
placement. Add a reason if needed, click **Review**, and then click **Process Reviewed
Plan** if the details are correct. **Confirm Return Admission** is a separate
step after promotion, before the new placement can be billed.

Each student can Promote, Repeat Class or Skip for Now. Repeat keeps the original
campus/class/section in the destination year. Skipping leaves the record unchanged.
New annual placements retain the existing return-admission requirement before billing.
The monthly concession option retains the existing permission and fee-cap rules.

## Transfers and corrections

Transfers & Sections and Placement Corrections operate within the recorded year. They use
the selected source year without a second academic-year choice. Concession carry
appears only on Annual Promotion; transfer fee choices appear on Transfers &
Sections and Placement Corrections. Decision lists also follow the selected task.
Suggest Destinations uses the selected campus/class and matching section. Highlight
rows and choose Assign to move groups; individual destination cells allow
section merges, splits, balancing and different destinations within one class.
The source filters can include several classes and campuses.

Section-only changes preserve fees. Class/campus changes can preserve current fees
or apply destination fees from next month or using the school's cutoff. Existing
transfer rules still govern historical/nonbilling placements, permissions, bills,
credits and fee schedules. More specialized custom-month/refund/schedule decisions
remain available in the individual transfer dialogs in Admission / Review.

Return / Completion on Annual Promotion opens the related departure and return
admission dialogs. Student Actions on Placement Corrections also provides readmission and
hold/resume dialogs. Final-class completion is an explicit departure review;
it does not invent a next class or silently mark students as having left.

## Review, recovery and correction

Review validates the selected plan within a transaction and
rolls it back, then displays source/destination placements, fee agreements and
bill differences. This preview can consume identity values, but saves no records.
A dependency snapshot prevents applying a plan if relevant placements, fees,
payments, bills or setup changed after review. Processing is all-or-nothing.

Every submitted plan has a unique request ID and saved result. An uncertain result
locks editing; Check / Retry Plan resubmits the identical request and returns the
saved outcome without applying it twice. After a restart, refresh history and
reload active source placements before preparing further work. Saved history is
retained even when a promotion is corrected.

Placement History loads the latest 2,000 student movements. Its undo date and
explanation belong to that page. Undo Promotion on Placement Corrections navigates
to Placement History and loads the saved movements.
Earlier Promotion History opens the existing historical report, with its report
permission requirement. Completed annual promotions disappear from their source
year's active list; skipped students remain for later processing.

Undo Selected Promotion is available for unchanged annual placements created by
this workspace, before return admission/billing, payments, certificates or later
movement. It requires student administration permission. The correction reason is optional.
It closes the mistaken destination and restores the original placement, retaining
both enrollments, original promotion records and correction history. Changed fee
agreements also prevent undo. Certificate checks are deliberately conservative:
any existing student certificate requires manual review.

For a wrong campus/class/section after billing or later movement, use Correct
Placement with a reviewed fee policy. Student History / Dates opens existing date
correction tools. Confirmed academic-year mistakes with downstream activity require
individual reconciliation; the workspace refuses automatic reversal rather than
discarding bills, payments, certificates or student history.

## Deployment and validation

Migration 62 installs the workspace tables/procedure and permits explicit
cross-campus annual promotion. Existing promotion callers retain their original
same-campus restriction. Apply the numbered migration with the normal installer;
no live database is changed by source edits or tests.

Run `Tools/TestAutomaticBilling.ps1 -PlacementOnly` for isolated synthetic SQL
checks and `dotnet run --project Tests/ThemeSmokeChecks -- --placement-workspace`
for UI/mapping checks. No supplied release database snapshot is required.
