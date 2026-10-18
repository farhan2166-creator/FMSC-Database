# Admission workflow (v1 baseline)

> Historical design record. Current release routes and schema-20 deployment are described in [School operations](../SCHOOL_OPERATIONS.md) and [Release readiness](../RELEASE_READINESS.md).

User decisions, updated 2026-10-13:

- Admission is the single workspace for student details and legacy placement corrections.
- Direct Admission activates the placement and posts opening fees atomically on Save. A new admission does not require a separate Confirm Placement step.
- Optional Quick Registration creates a pending student and placement, assigns a QR reference, and issues reviewed opening charges. No official admission or registration number is allocated yet. Its own pending picker and review workspace handle unfinished records; pending registrations are excluded from the Admission workspace.
- Pending registrations can receive payments, including family collection. They are excluded from normal monthly posting, promotion and official certificates.
- Quick Registration uses one Registration Details section ordered student, placement, parent/contact, then address. Link Existing Family opens its searchable picker. Choosing a family member copies parent/contact/address and locks the parent name. Saving checks the linked parent against the current stored name; pending saves preserve the stored parent. Contact/address edits affect this child only. Add Another Child clears child details and fees while retaining the family; New Family clears the link and shared details.
- Confirm Admission in Quick Registration review allocates both the official admission number and registration number, then activates the same student/placement. Family links, bills and payments are retained without reposting charges. Confirmation and Cancel Registration are offered only inside Quick Registration, never on the Admission form.
- Mark as Left closes an admitted placement without requiring payment or a certificate. Valid outstanding dues remain collectible. Later-month charges must be reviewed before departure.
- Cancel Registration is available only for pending quick registrations. It preserves history and reverses eligible unpaid bills; paid allocations require fee corrections/refunds first. Once an official admission number is allocated, cancellation is refused. Use Mark as Left for departure and the correction workspaces for details or fees.
- Existing records remain legacy records until the one-time Go Live operation. They may be corrected in Admission and marked verified. Student, placement or fee edits clear verification.
- Go Live requires verified active legacy records with valid dates and mandatory fee setup. It previews the go-live date, billing month and optional replacement placement date, then atomically activates the reviewed set. Recorded placement dates are preserved by default. Existing financial history is retained; conflicting dates block activation. Stale previews and repeated execution are refused.
- The academic year on a confirmed operational enrollment is protected. Movement uses Promotion, Transfer or Re-admission; genuine placement errors use the audited correction workflow. Calendar.Manage users can still edit Academic Years within its unique-name, valid-date and non-overlap rules; this does not move student placements.
- Student Data Review combines legacy progress, record issues/history and possible duplicates. It opens Admission for editing. Duplicate review does not automatically merge or delete students.
- After Go Live completes, its action and legacy setup tab disappear permanently, using the saved database completion state on every reopen. Quick Registration is available only from Dashboard Quick Actions, not the navigation sidebar.
- Quick Registration has a Registration Form tab and a separate Pending Registrations tab. The pending list searches temporary references, child/parent names and contact numbers. Open Registration or a double-click loads the Registration Form for editing basic details and discounts. Selecting a row enables Review / Confirm on the pending list; the Registration Form has no confirmation action. Returning to the list refreshes it, so confirmed and cancelled records disappear from the pending list.
- The opening-bill grid shows configured fee, editable discount (with Fees.Adjust permission), and calculated net amount. Save Changes persists pending edits through the audited details/discount transaction and updates unpaid charges. Printing and collection wait until pending edits are saved; existing paid charges remain protected by the financial procedures.

## Database delivery

The user supplies the latest detached FMSC_DB.mdf and matching FMSC_DB_log.ldf
in ../Database when preparing a release. That snapshot already contains the
current schema, procedures and guards. The applied development upgrade scripts
have been removed; deployment and validation do not reference them.

Legacy verification and Go Live are operator actions on the current database;
supplying the snapshot does not run Go Live or change school records.

The live development schema, runtime compatibility contract and FMSC.dacpac
include this workflow. The current runtime database schema requirement is version 6. The application version is separate from the database schema version.
Local recovery backups, when retained, belong in the ignored RecoveryBackups directory.

Run Validation/AdmissionWorkflowChecks.ps1 and
Validation/StudentCorrectionChecks.ps1 on Windows with LocalDB. Each restores
a disposable copy of the current database, checks the already-installed workflow,
performs all record writes there, then drops it. They do not apply upgrade scripts.
No test students or payments are inserted into the live school database.

Release packaging continues to use the latest user-supplied detached MDF/LDF
pair in ../Database. Development snapshot absence is not a release blocker.
