# Coordinated leaving-date checks

Run `LeavingDateChecks.sql` through `sqlcmd -b` against a **disposable database copy** with the existing lifecycle repair procedures. Apply `Migrations/18.sql` first. No trigger change is required. Tests need catalogue rows and an administrator account. All synthetic student/enrolment/fee/certificate fixtures are created inside transactions and rolled back. Do not run against the live school database.

Coverage: related departure dates update together; paid bills and certificate creation timestamps remain unchanged; earlier departures preserve a later withdrawal; stale certificate data rejects the complete correction; financial-history acknowledgement remains mandatory; established closed-bill cancellation and amount protections remain intact; chronology and authentication are enforced.

The console calls `RepairLifecycleDates` with `@WriteAudit=0`. Original database definitions in `Database/Reference` remain a historical snapshot; migration 18 adds the optional flag without changing lifecycle rules. The new review automatically proposes linked certificate dates and, for the latest departure of a Left student, the withdrawal date. It passes every enrolment and the original snapshot to the database for its established validation and atomic save. Existing financial history is preserved only after explicit acknowledgement. Legacy exceptions use the existing explicit acknowledgement, not a new bypass.

## Windows review

Select a Left enrolment and open Correct Leaving Date. Check the student/enrolment context, linked certificate review and applicable withdrawal date. For a student with bills, Review must require the financial-history acknowledgement. Edit the date, reason or acknowledgement after review: Apply must disable until reviewed again. Database refusals remain inline. Save should refresh the console and retain the result. Closing during processing must be blocked; cancelling before Apply makes no changes. Check normal and increased display scaling.

This first workflow corrects an already recorded departure, not a new withdrawal or readmission. It does not create, cancel or rewrite bills or change student/enrolment status. Other difficult correction workflows remain future work.

## Active enrollment dates

Run `EnrollmentDateChecks.sql` against the same disposable database copy. Five scenarios verify the existing `ReconcileEnrollmentDates` procedure: preview is read-only and the save matches its charge counts; paid bills block both preview eligibility and saving; financial changes invalidate the reviewed snapshot; confirmed enrollments cannot lose their billing start; closed enrollments cannot use active reconciliation. Fixtures roll back, including expected database failures. Migration 18 adds the optional audit flag; reconciliation rules are unchanged.

On Windows, select an Active enrolment and choose Correct Enrollment Dates (or Save Enrollment for a date-only correction). Student administration and fee-adjustment permissions are required. Check the loaded/proposed dates and the database-returned charge counts, amounts, billing-through month and resulting confirmed-return date. Editing any input invalidates the review. Apply must remain disabled for a blocked plan. A save must retain the actual added/cancelled counts inline after refreshing the console. Mixed date/status/placement edits still follow the database's required sequence; the console now names the date-correction action explicitly.

## Status review and checks after saving

The leaving-date check script also tests confirmed status outcomes, closing an active enrollment without altering bills, no-op reporting and the established reactivation restriction. On Windows, verify that the existing status confirmation shows old/proposed status, withdrawal handling, related enrollment effect and reason. A Left student without an active enrollment must receive inline readmission guidance. After any successful console correction, verify that the selected student's consistency checks rerun, the saved result remains visible with the remaining issue count, and Issues refreshes when displayed. Changing student while checks run must discard the old result. A recheck failure must say the correction was saved but the check failed, without reporting the save itself as failed.

## Missing enrollment review

The leaving-date check script verifies successful console creation with `@WriteAudit=0`: confirmed ID/status, synchronized student status, retained correction/status histories, no automatic fees/bills/billing start and no audit rows/operations. It also verifies rejection of a second active enrollment and the leaving-date requirement for Left. The optional flag defaults to 1 so callers outside the console retain existing auditing. A separate check confirms that default behavior.

On Windows, Add Missing Record should show Active explicitly and disable the billing-start/legacy-date controls that this procedure does not accept. Review the existing creation confirmation: year, placement, status, dates, reason, existing count and overlapping record warnings must match submitted values. Overlap warnings are advisory; the database retains final authority. Cancel makes no changes. Creating a record should select the returned record, explain that fees/bills were not generated and recheck remaining issues. No new success/error message boxes are added.

## Existing enrollment correction review

The existing-enrollment closure check verifies the procedure's returned status/Changed flag, coordinated student withdrawal, unchanged assignment/bill/certificate and no-op response. The console uses that returned result instead of a generic success message.

On Windows, select an Active record and change its placement or status. The existing confirmation must show student/year, old/proposed placement, status and leaving date, reason, student-status effect and unchanged fee/billing/certificate scope. Dates must still use Correct Enrollment Dates first. A refresh detected before confirmation must require another review. A missing Left date and a Promoted record without an active destination must be explained inline. Cancel must make no changes. After saving, the corrected record must remain selected and the refreshed checks must retain the result. The procedure remains the final authority, including changes made by another user after the review.

## Console audit policy and deployment

Apply `Migrations/18.sql` to the target database **before deploying the updated application**. It uses `CREATE OR ALTER` and may be applied again. The application compatibility baseline remains 3. All four correction procedures gain a trailing optional `@WriteAudit bit=1` parameter. The console and its correction dialogs pass 0, including reconciliation preview/save. Audit operations and audit log entries are skipped; permission checks, snapshots, atomic saves, chronology, financial protections and lifecycle/correction histories remain in place. Existing audit entries are retained.

Both scripts assert unchanged audit row/operation counts for successful console corrections. The historical snapshot's missing-enrollment creation branch passes plain text into JSON audit values; skipping that audit branch removes the console blocker without altering audit behavior for other callers.
