# Readmission eligibility — owner decision, 1 November 2026

Re-Admit is for an officially admitted student whose current status is Left or
Inactive (or legacy Withdrawn). Leaving through a School/College leaving
certificate and recording departure in Student Admissions both lead to the
canonical Left status. Original departure dates, certificates and history remain.

Reaching the highest class **does not automatically change the status**.
The existing Record Departure / Final-Class Completion action, or the leaving
certificate workflow, records the departure. That student can then be
readmitted to a valid placement if the normal academic-year, date and fee
checks pass. There is no artificial next-class rule on readmission.
A still-Active final-class student cannot be readmitted.

A cancelled pending registration without an official admission number uses
Reopen Registration, not Re-Admit. Active or Pending status, an active
enrollment, and an unresolved posting hold block readmission; held students
use Lift Hold / Resume. Readmission writes still require Students.Administer.

Migration 68 adds readmission status, official-number and hold guards to the
existing dbo.ReAdmitStudent writer, and aligns the C# screens. The lifecycle
enrollment reader permits Enrollment.Manage or Students.Administer to view
history; each actual writer retains its own required permission. The
individual UI keeps calling ReAdmitStudentWithNumberChoice, whose readmission
writer is governed by dbo.ReAdmitStudent's eligibility checks.

No existing student, enrollment, certificate, fee, bill, payment or history
rows are changed. The SQL change is a forward-only migration after schema 67.
Financial review, admission-number decisions, date checks and past cases
are preserved. Run targeted checks against an owned disposable database
before any production deployment.
