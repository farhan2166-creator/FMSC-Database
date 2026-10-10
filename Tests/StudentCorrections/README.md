# Coordinated leaving-date checks

Run `LeavingDateChecks.sql` through `sqlcmd -b` against a **disposable database copy** with the existing lifecycle repair procedures. No migration or trigger change is required. Tests need catalogue rows and an administrator account. All synthetic student/enrolment/fee/certificate fixtures are created inside transactions and rolled back. Do not run against the live school database.

Coverage: related departure dates update together; paid bills and certificate creation timestamps remain unchanged; earlier departures preserve a later withdrawal; stale certificate data rejects the complete correction; financial-history acknowledgement remains mandatory; established closed-bill cancellation and amount protections remain intact; chronology and authentication are enforced.

The console calls the existing `RepairLifecycleDates` contract. Original database definitions in `Database/Reference` and all migrations remain unchanged. The new review automatically proposes linked certificate dates and, for the latest departure of a Left student, the withdrawal date. It passes every enrolment and the original snapshot to the database for its established validation and atomic save. Existing financial history is preserved only after explicit acknowledgement. Legacy exceptions use the existing explicit acknowledgement, not a new bypass.

## Windows review

Select a Left enrolment and open Correct Leaving Date. Check the student/enrolment context, linked certificate review and applicable withdrawal date. For a student with bills, Review must require the financial-history acknowledgement. Edit the date, reason or acknowledgement after review: Apply must disable until reviewed again. Database refusals remain inline. Save should refresh the console and retain the result. Closing during processing must be blocked; cancelling before Apply makes no changes. Check normal and increased display scaling.

This first workflow corrects an already recorded departure, not a new withdrawal or readmission. It does not create, cancel or rewrite bills or change student/enrolment status. Other difficult correction workflows remain future work.
