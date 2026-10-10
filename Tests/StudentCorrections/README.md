# Coordinated leaving-date checks

Run `LeavingDateChecks.sql` through `sqlcmd -b` against a **disposable database copy** with the existing lifecycle repair procedures. No migration or trigger change is required. Tests need catalogue rows and an administrator account. All synthetic student/enrolment/fee/certificate fixtures are created inside transactions and rolled back. Do not run against the live school database.

Coverage: related departure dates update together; paid bills and certificate creation timestamps remain unchanged; earlier departures preserve a later withdrawal; stale certificate data rejects the complete correction; financial-history acknowledgement remains mandatory; established closed-bill cancellation and amount protections remain intact; chronology and authentication are enforced.

The console calls the existing `RepairLifecycleDates` contract. Original database definitions in `Database/Reference` and all migrations remain unchanged. The new review automatically proposes linked certificate dates and, for the latest departure of a Left student, the withdrawal date. It passes every enrolment and the original snapshot to the database for its established validation and atomic save. Existing financial history is preserved only after explicit acknowledgement. Legacy exceptions use the existing explicit acknowledgement, not a new bypass.

## Windows review

Select a Left enrolment and open Correct Leaving Date. Check the student/enrolment context, linked certificate review and applicable withdrawal date. For a student with bills, Review must require the financial-history acknowledgement. Edit the date, reason or acknowledgement after review: Apply must disable until reviewed again. Database refusals remain inline. Save should refresh the console and retain the result. Closing during processing must be blocked; cancelling before Apply makes no changes. Check normal and increased display scaling.

This first workflow corrects an already recorded departure, not a new withdrawal or readmission. It does not create, cancel or rewrite bills or change student/enrolment status. Other difficult correction workflows remain future work.

## Active enrollment dates

Run `EnrollmentDateChecks.sql` against the same disposable database copy. Five scenarios verify the existing `ReconcileEnrollmentDates` procedure: preview is read-only and the save matches its charge counts; paid bills block both preview eligibility and saving; financial changes invalidate the reviewed snapshot; confirmed enrollments cannot lose their billing start; closed enrollments cannot use active reconciliation. Fixtures roll back, including expected database failures. No SQL definition is changed.

On Windows, select an Active enrolment and choose Correct Enrollment Dates (or Save Enrollment for a date-only correction). Student administration and fee-adjustment permissions are required. Check the loaded/proposed dates and the database-returned charge counts, amounts, billing-through month and resulting confirmed-return date. Editing any input invalidates the review. Apply must remain disabled for a blocked plan. A save must retain the actual added/cancelled counts inline after refreshing the console. Mixed date/status/placement edits still follow the database's required sequence; the console now names the date-correction action explicitly.

## Status review and checks after saving

The leaving-date check script also tests confirmed status outcomes, closing an active enrollment without altering bills, no-op reporting and the established reactivation restriction. On Windows, verify that the existing status confirmation shows old/proposed status, withdrawal handling, related enrollment effect and reason. A Left student without an active enrollment must receive inline readmission guidance. After any successful console correction, verify that the selected student's consistency checks rerun, the saved result remains visible with the remaining issue count, and Issues refreshes when displayed. Changing student while checks run must discard the old result. A recheck failure must say the correction was saved but the check failed, without reporting the save itself as failed.
