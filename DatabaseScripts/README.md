# Admission workflow (v1 baseline)

User decisions, 2026-10-12:

- Admission is the single workspace for student details and legacy placement corrections.
- Direct Admission activates the placement and posts opening fees atomically on Save. A new admission does not require a separate Confirm Placement step.
- Optional Quick Registration creates a pending student and placement, assigns a QR reference, and issues reviewed opening charges. No official admission or registration number is allocated yet. Its own pending picker and review workspace handle unfinished records; pending registrations are excluded from the Admission workspace.
- Pending registrations can receive payments, including family collection. They are excluded from normal monthly posting, promotion and official certificates.
- Choosing an existing family member copies parent/contact/address into the new child form. Those values remain editable for that child; edits retain the selected sibling link and never update older family records. New Family explicitly clears the link and shared entry details.
- Confirm Admission in Quick Registration review allocates both the official admission number and registration number, then activates the same student/placement. Family links, bills and payments are retained without reposting charges. Confirmation and Cancel Registration are offered only inside Quick Registration, never on the Admission form.
- Mark as Left closes an admitted placement without requiring payment or a certificate. Valid outstanding dues remain collectible. Later-month charges must be reviewed before departure.
- Cancel Registration is available only for pending quick registrations. It preserves history and reverses eligible unpaid bills; paid allocations require fee corrections/refunds first. Once an official admission number is allocated, cancellation is refused. Use Mark as Left for departure and the correction workspaces for details or fees.
- Existing records remain legacy records until the one-time Go Live operation. They may be corrected in Admission and marked verified. Student, placement or fee edits clear verification.
- Go Live requires verified active legacy records with valid dates and mandatory fee setup. It previews the go-live date, billing month and optional replacement placement date, then atomically activates the reviewed set. Recorded placement dates are preserved by default. Existing financial history is retained; conflicting dates block activation. Stale previews and repeated execution are refused.
- Confirmed operational academic years cannot be edited. Movement uses Promotion, Transfer or Re-admission. Audited genuine date corrections remain available.
- Student Data Review combines legacy progress, record issues/history and possible duplicates. It opens Admission for editing. Duplicate review does not automatically merge or delete students.
- After Go Live completes, its action and legacy setup tab disappear permanently, using the saved database completion state on every reopen. Quick Registration is available only from Dashboard Quick Actions, not the navigation sidebar.
- Quick Registration has a Registration Form tab and a separate Pending Registrations tab. The pending list searches temporary references, child/parent names and contact numbers; Open Registration or a double-click opens its editable review form. Returning to the list refreshes it, so confirmed and cancelled records disappear from the pending list.

Apply PlacementConfirmation.sql, StudentCorrectionGuards.sql, then
AdmissionWorkflow.sql, in that order in one transaction on the compatible
development baseline. The first two scripts represent earlier contracts;
AdmissionWorkflow.sql applies the final decisions above. Do not apply an earlier
script on its own over the final schema. Existing records are initially marked
for legacy review; schema installation does not run Go Live or change their
placement dates, admission numbers, fees or payments.

The live development schema, runtime compatibility contract and FMSC.dacpac
include this workflow. Application and database schema versions remain v1.
Recovery backups are private local files under the ignored Tests directory.

Run Validation/AdmissionWorkflowChecks.ps1 and
Validation/StudentCorrectionChecks.ps1 on Windows with LocalDB. Each restores
a disposable database copy, performs all record writes there, then drops it.
No test students or payments are inserted into the live school database.

Release packaging continues to use the latest user-supplied detached MDF/LDF
pair in ../Database. Development snapshot absence is not a release blocker.
