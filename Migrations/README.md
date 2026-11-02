# Apply migrations without the installer

Use **`Apply-Migrations.bat`** (double-click on Windows) or run
**`Apply-Migrations.ps1`** from this folder. This updates an **already attached**
FMSC SQL database; it does **not** install LocalDB, attach an MDF/LDF,
replace database files, install the desktop app, or activate an app release.

1. Run under the **same Windows user account** that owns the intended
   `(localdb)\MSSQLLocalDB` database. Close FMSC and other database users.
   Confirm you have an approved maintenance window and a separate tested backup.
2. Double-click `Apply-Migrations.bat`. The script displays the actual SQL
   instance, database, Windows identity, detected schema, required schema,
   pending versions and backup location. Type **APPLY** to proceed, or close
   the window/cancel without modifying SQL data.
3. Before the first pending script, the applier makes a
   `BACKUP DATABASE ... WITH CHECKSUM`, validates it with
   `RESTORE VERIFYONLY WITH CHECKSUM`, and checks that its `.bak` file exists
   and is non-empty. The default backup folder is
   `C:\ProgramData\FMSC_Database\Backups`. It records the recovery point
   in `LastPreMigrationBackup.txt` and writes `ApplyMigrations.log`.
4. Each pending numbered `*.sql` is executed in ascending order inside the
   same-connection, exclusive-lock, outer-transaction and schema-version
   wrapper used by the installer. Already-applied files are skipped. The
   applier verifies the complete consecutive sequence from the database's
   current version to the required schema **before taking a backup**. If an
   intermediate file is missing, it refuses to run instead of skipping ahead.
   The process stops on any SQL/version/backup error; do not blindly retry.
5. Verify the final version and test the updated FMSC app against the database.
   **Only the full installer performs its staged-application compatibility
   checks and activation**; this standalone tool does not certify app
   compatibility merely by reaching the numbered schema.

For a **read-only migration plan** (database connection and version query, no
backup or SQL changes):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Apply-Migrations.ps1 -PlanOnly
```

For an approved **disposable database clone** with a different name, or when
`sqlcmd.exe` is already installed elsewhere:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Apply-Migrations.ps1 -DatabaseName FMSC_DB_Test -BackupRoot 'C:\FMSC-Clone-Backups'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Apply-Migrations.ps1 -SqlCmdPath 'C:\Tools\sqlcmd.exe' -PlanOnly
```

`-Apply` skips the interactive **APPLY** prompt (use only for intentionally
approved unattended runs). By default the applier reads the required version
from the app/installer source next to `Migrations` and refuses if they
disagree. If only the migration folder is copied, the highest numbered SQL
file becomes the target and the script warns that application compatibility
must be checked separately. If `sqlcmd.exe` is missing, the same pinned and
SHA-256-checked Microsoft go-sqlcmd build as the installer is obtained.

**This is a database mutation tool. Test on a disposable copy first.**
See `Docs/BACKUP_RECOVERY.md` for recovery guidance.

---

## Current target: schema 70

Migration 70 changes only GetOutstandingAndAgingReport output. Charges, Students
and Families include Overdue Amount, Due Today, Not Yet Due and Due Date Missing. Families includes
unlinked students as standalone groups. Existing financial balances, charge
eligibility and payment writers are unchanged. Apply after 69 using the verified
backup and transaction workflow above. The DACPAC remains a reference snapshot;
it does not replace these migrations or school data.

## Previous target: schema 69

Migration `69.sql` enforces the approved transfer classification at the
SQL writer boundary: `TransferEnrollmentClass` requires a different ClassId.
If only SectionId changes within the same campus and class, use
`TransferEnrollmentClassSection`. The C# individual-transfer screen and
bulk placement-plan dispatcher already route these operations correctly.
The new SQL guard runs before history insertion and optional fee synchronization,
so a mistaken direct class-transfer call cannot write TRANSFER_CLASS history
or alter class-transfer fees. The guard does not restrict upward/downward
same-year class moves, campus moves, historical transfer dates, or the
intentionally C#-owned IsActive selection rule. Migration is forward-only
after schema 68 and changes no student, history, billing or fee rows.

## Previous target: schema 68

Migration `68.sql` aligns readmission eligibility across C# and SQL. Officially
admitted former students in Left, Inactive or legacy Withdrawn status may
readmit, provided there is no active enrollment or unresolved posting hold.
Cancelled pending registrations use Reopen Registration. Final-class
completion and school/college leaving certificates retain the usual Left status.
The lifecycle reader accepts Enrollment.Manage or Students.Administer for
reading, but readmission writes still require Students.Administer. No student,
enrollment, financial or certificate rows are rewritten. Apply 68 after 67.
See `Docs/Decisions/READMISSION_ELIGIBILITY_2026-11-01.md`.

## Previous target: schema 67

Migration `67.sql` supersedes only the class-section IsActive hard stops
introduced for `dbo.ReopenPendingRegistration` in 65 and
`dbo.ApplyStudentPlacementPlan` in 62/66. C# selects and rechecks active
destinations for new intake, reopening, promotions, readmissions and current
transfers. SQL retains permission, review, financial, referential and history
rules; historical corrections and retained older placements may reference an
inactive section. This decision does not change inactive-user security or
setup-readiness reporting. Apply 67 once after 66; never rerun old migrations.
Previous entries below document behavior at those version numbers.


Migration `66.sql` updates **only error-message wording** for the current
`dbo.ApplyStudentPlacementPlan`, `dbo.BulkPromoteStudents`, and
`dbo.CloseStudentAdmission` procedures. The old **Fee Corrections** screen name
is replaced with **Student Fee Account > Payment History**. The embedded
`Pages/Students/Admissions/PlacementCorrection.sql` runtime script receives
the same wording correction. SQL validation, permissions, financial calculation,
request recovery, error numbers and historical records are unchanged.
Apply 66 once after 65 under the normal verified-backup migration workflow;
do not rerun earlier versions.

Migration `65.sql` requires schema 64 and updates **only**
`dbo.ReopenPendingRegistration`. An operator may reopen a cancelled pending
registration into a class-section only while that placement has
`CampusClassSections.IsActive = 1`. Both preview and apply refuse inactive
destinations; existing cancelled and historical episodes, fee/payment records
and successfully saved reopening-request replays are not rewritten.
Apply migration 65 once, under the existing migration transaction rules;
**do not rerun migration 64** on a database already at 64. Use a verified
backup and test against a disposable clone before applying it to school data.

Migration `64.sql` adds guarded, recoverable cancelled-registration reopening for
the simplified Admission flow. It preserves existing student/financial rows and
cancelled placements, creates a new pending placement only on an explicit reviewed
reopening, and permits repeated registration cancellation when earlier episodes
were cancelled too. Actual attendance/certificate safeguards remain. Apply after
63 under an outer transaction. See
`Docs/Decisions/ADMISSION_REGISTRATION_SIMPLIFICATION.md` and
`Tools/TestRegistrationWorkflow.ps1`. Never rerun an applied migration.

Migration `63.sql` implements the owner decision of 31 October 2026: academic
years referenced by any enrolment (including historical/left students) or either
side of a promotion batch keep their Start and End dates. Name, Remarks and
Current remain editable. It adds a reader flag and a transactional save guard,
and enforces consecutive four-digit year names. Existing rows are not rewritten.
Apply after `61.sql` and `62.sql`, under an outer transaction. Verify with
`Tools/TestAcademicYearDateLock.ps1`, `Tests/AcademicYearChecks` and native
`ThemeSmokeChecks --academic-year-date-lock`.

Migration `60.sql` fixes admission/fee flow authorization and recovery. It adds nullable cancellation reason/actor/time fields without rewriting old history, requires collection and adjustment permissions for cancelling a debt, passes the current operator into credit settlement, and marks verified direct-admission rollbacks with SQL error state 60. C# also keeps pending transport editing in registration and aligns the aging shortcut with account access. Apply 59 first if needed; run 60 under an outer transaction. Validation: `Tools/TestAdmissionFeeFlowFixes.ps1` and native `--admission-fee-flow-fixes`.

Migration `59.sql` retires the remaining 19 generic audit writers after schema 58. Apply it once under an outer transaction using the installer or `sqlcmd -b`. It preserves existing audit rows/readers, permissions, preview snapshots, operational history and recovery markers. This remaining batch is explicitly owner-authorized on 28 October 2026. Validate with `Tools/TestRemainingAuditRetirement.ps1` (owned, record-free fixture).

For an approved upgrade starting at a supported schema between 19 and 69, apply every consecutive numbered file strictly above the detected version through 70 in ascending order. Do not skip a version or rerun an applied migration. The installer and standalone applier refuse missing intermediate files. Historical tables below stop at an earlier migration-51 stage and are not the current complete upgrade list.

# Apply the database changes later

The current application requires **schema 70**. SQL changes are checked in here;
merging the code does not execute them against your database.

For the historical chains below ending at 51, continue with `52.sql`, `53.sql`,
`54.sql`, `55.sql`, `56.sql`, `57.sql`, `58.sql`, `59.sql`, then `60.sql`. A database already at 59
needs only `60.sql`; one at 58 needs `59.sql` and `60.sql`.
Migration 55 preserves existing financial records and introduces complete,
immutable receipt snapshots. See `Docs/Decisions/FEE_RECEIPT_PAPER_TRAIL.md`.
Migration 56 changes the printable breakdown to each visit: carry outstanding,
add subsequent fees/adjustments, deduct subsequent concessions, and retain only
current payments and closing balances. Original snapshots remain unchanged.
Migration 57 corrects pending-return classification for historical admissions.
Migration 58 adds the independent Transport Cutoff Day and the shared transport
start/stop rules. It preserves paid months and existing financial history.

| Current database version | Files to apply, in order |
|---|---|
| 19 | `20.sql`, `21.sql`, `22.sql`, `23.sql`, `24.sql`, `25.sql`, `26.sql`, `27.sql`, `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 20 | `21.sql`, `22.sql`, `23.sql`, `24.sql`, `25.sql`, `26.sql`, `27.sql`, `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 21 | `22.sql`, `23.sql`, `24.sql`, `25.sql`, `26.sql`, `27.sql`, `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 22 | `23.sql`, `24.sql`, `25.sql`, `26.sql`, `27.sql`, `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 23 | `24.sql`, `25.sql`, `26.sql`, `27.sql`, `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 24 | `25.sql`, `26.sql`, `27.sql`, `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 25 | `26.sql`, `27.sql`, `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 26 | `27.sql`, `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 27 | `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 28 | `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 29 | `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 30 | `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 31 | `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 32 | `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 33 | `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 34 | `35.sql`, `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 35 | `36.sql`, `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 36 | `37.sql`, `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 37 | `38.sql`, `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 38 | `39.sql`, `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 39 | `40.sql`, `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 40 | `41.sql`, `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 41 | `42.sql`, `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 42 | `43.sql`, `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 43 | `44.sql`, `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 44 | `45.sql`, `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 45 | `46.sql`, `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 46 | `47.sql`, `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 47 | `48.sql`, `49.sql`, `50.sql`, `51.sql` |
| 48 | `49.sql`, `50.sql`, `51.sql` |
| 49 | `50.sql`, `51.sql` |
| 50 | `51.sql` |
| 51 | `52.sql`, `53.sql`, `54.sql`, `55.sql`, `56.sql`, `57.sql`, `58.sql` |
| 52 | `53.sql`, `54.sql`, `55.sql`, `56.sql`, `57.sql`, `58.sql` |
| 53 | `54.sql`, `55.sql`, `56.sql`, `57.sql`, `58.sql` |
| 54 | `55.sql`, `56.sql`, `57.sql`, `58.sql` |
| 55 | `56.sql`, `57.sql`, `58.sql` |
| 56 | `57.sql`, `58.sql` |
| 57 | `58.sql` |
| 58 | None |

Check your version first:

```sql
SELECT TOP (1) VersionNumber
FROM dbo.DatabaseVersion
ORDER BY VersionId DESC;
```

Migration 20 aligns the optional communication callback and retires unused read
procedures. Migration 21 implements reviewed fee terms, admission concessions,
departure choices and promotion concessions, updates the financial guard and
adds the fee-change recovery kind. Financial corrections, original receipts,
allocations and student lifecycle history remain operational records.

Migration 22 lets Fee Collection apply available student credit to posted bills,
including future months, in the same transaction as cash and discount.

Migration 23 saves one printable receipt for each cash/credit collection, linking
its recorded payments and retaining the issue-time balances for later reprints.

Migration 24 adds independent B/FB/R/FR calendar-year counters and saved bill
documents. It preserves all previously issued receipt numbers. New documents
use five-digit minimum sequences; larger sequences are never truncated.

Migration 25 clarifies Outstanding & Aging: admission/QR references, class–section,
open-charge settlement labels and a separate Due Today bucket. It changes report
output only; financial records and balances remain unchanged.

Migration 26 separates reused credit from money received in Collections & Receipts,
uses saved receipt links and original participants for receipt identity, and adds
campus and natural school/transaction ordering to both finance reports. Legacy
credit applications without an issued receipt appear with a readable reference.
Financial records and saved documents are not rewritten.

Migration 27 adds read-only quick-counter bill evidence for fully waived fees, including future monthly plans. It also fixes decimal XML total validation when saving an all-zero bill. It creates no charges or payments and does not rewrite saved documents.

## Manual application with SQLCMD

Close the application and make a verified backup first, as described in
[backup and recovery](../Docs/BACKUP_RECOVERY.md). The scripts require an outer
transaction, which the installer normally provides.

For a schema-19 database, save this wrapper **outside this directory**, change the
include paths to your checkout, and run it with SQLCMD. For a newer supported
database, omit every include up to and including its current version. For example,
schema 56 needs migrations 57 and 58. Schema 57 needs only migration 58.

```sql
:ON ERROR EXIT
SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO
:r "C:\FMSC-Database\Migrations\20.sql"
GO
:r "C:\FMSC-Database\Migrations\21.sql"
GO
:r "C:\FMSC-Database\Migrations\22.sql"
GO
:r "C:\FMSC-Database\Migrations\23.sql"
GO
:r "C:\FMSC-Database\Migrations\24.sql"
GO
:r "C:\FMSC-Database\Migrations\25.sql"
GO
:r "C:\FMSC-Database\Migrations\26.sql"
GO
:r "C:\FMSC-Database\Migrations\27.sql"
GO
:r "C:\FMSC-Database\Migrations\28.sql"
GO
:r "C:\FMSC-Database\Migrations\29.sql"
GO
:r "C:\FMSC-Database\Migrations\30.sql"
GO
:r "C:\FMSC-Database\Migrations\31.sql"
GO
:r "C:\FMSC-Database\Migrations\32.sql"
GO
:r "C:\FMSC-Database\Migrations\33.sql"
GO
:r "C:\FMSC-Database\Migrations\34.sql"
GO
:r "C:\FMSC-Database\Migrations\35.sql"
GO
:r "C:\FMSC-Database\Migrations\36.sql"
GO
:r "C:\FMSC-Database\Migrations\37.sql"
GO
:r "C:\FMSC-Database\Migrations\38.sql"
GO
:r "C:\FMSC-Database\Migrations\39.sql"
GO
:r "C:\FMSC-Database\Migrations\40.sql"
GO
:r "C:\FMSC-Database\Migrations\41.sql"
GO
:r "C:\FMSC-Database\Migrations\42.sql"
GO
:r "C:\FMSC-Database\Migrations\43.sql"
GO
:r "C:\FMSC-Database\Migrations\44.sql"
GO
:r "C:\FMSC-Database\Migrations\45.sql"
GO
:r "C:\FMSC-Database\Migrations\46.sql"
GO
:r "C:\FMSC-Database\Migrations\47.sql"
GO
:r "C:\FMSC-Database\Migrations\48.sql"
GO
:r "C:\FMSC-Database\Migrations\49.sql"
:r "C:\FMSC-Database\Migrations\50.sql"
GO
:r "C:\FMSC-Database\Migrations\51.sql"
GO
:r "C:\FMSC-Database\Migrations\52.sql"
GO
:r "C:\FMSC-Database\Migrations\53.sql"
GO
:r "C:\FMSC-Database\Migrations\54.sql"
GO
:r "C:\FMSC-Database\Migrations\55.sql"
GO
:r "C:\FMSC-Database\Migrations\56.sql"
GO
:r "C:\FMSC-Database\Migrations\57.sql"
GO
:r "C:\FMSC-Database\Migrations\58.sql"
GO
IF XACT_STATE() <> 1
    THROW 51000, 'The upgrade transaction cannot be committed.', 1;
IF ISNULL((SELECT TOP (1) TRY_CONVERT(int, VersionNumber)
           FROM dbo.DatabaseVersion ORDER BY VersionId DESC), -1) <> 58
    THROW 51000, 'The upgrade did not reach schema 58.', 1;
COMMIT TRANSACTION;
GO
```

Example command, using your actual SQL instance and wrapper path:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d FMSC_DB -E -b -i "C:\Temp\apply-fmsc-upgrade.sql"
```

The error-exit setting and transaction prevent a failed upgrade from committing
partway through the files. Recheck the version before launching the new app.
An already upgraded database must not rerun these files. A database older than 19
needs its earlier supported upgrade path; the scripts under
`Tests/LifecycleSqlChecks/Migrations` are disposable test fixtures.

Keep this directory's executable SQL filenames numeric. The installer discovers
them in order and supplies its own verified backup and transaction when used
instead of manual application. Do not replace an existing school database with
`fmsc.dacpac`; that file is the schema model used by compatibility/testing tools.

Migration 28 applies the School Profile cutoff to student placement activation. Monthly fees use the permitted month; admission and selected one-time fees remain due at placement. It retires activation audit writes and leaves existing student and financial records unchanged.

Migration 29 corrects migration 28: class placement is historical and does not select the fee cutoff. The selected billing start determines monthly fees under School Profile cutoff rules; annual and selected one-time fees use that selected billing date. Existing records are not rewritten.

Migration 30 adds reviewed student fee-posting holds and resume, with separate history, selective restoration, optional annual fees/advance, SQL posting safeguards and saved-request recovery. Existing Left records are not converted automatically. See [the operator and recovery policy](../Docs/STUDENT_POSTING_HOLDS.md).

Migration 31 keeps held students out of Confirm Return and rejects stale return confirmations before changing fees or placement. Held students use the reviewed Lift Hold / Resume workflow. No existing records are rewritten.

Migration 32 removes audit-only writes and snapshots from ChangeOwnPassword, RenameOwnUsername, CreateNoObjectionCertificate and UpdateNoObjectionCertificate. Permissions, validation, account changes, certificate snapshots and returned identifiers are preserved. Existing data and historical audit entries are unchanged.

Migration 33 removes audit-only writes and snapshots from CreateRegularStudentCertificate, UpdateRegularStudentCertificate, CreateSportsCertificate and UpdateSportsCertificate. It preserves permissions, validation, certificate records, student snapshots and returned identifiers. Existing data and historical audit entries are unchanged.

Migration 34 removes audit-only writes and snapshots from CreateRegularLecturerCertificate, UpdateRegularLecturerCertificate, CreateBankAccountOpeningCertificate and UpdateBankAccountOpeningCertificate. Permissions, validation, staff snapshots, certificate fields and returned IDs remain unchanged. Existing data and historical audit entries are preserved.

Migration 35 removes audit-only writes and snapshots from CreateCharacterCertificate and CreateDateOfBirthCertificate. It preserves permissions, validation, original student details, certificate issuance history, duplicate protection and returned certificate/issue IDs. Existing data and historical audit entries remain unchanged.

Migration 36 removes audit-only writes and snapshots from CreateProvisionalCertificate and UpdateProvisionalCertificate. It preserves permissions, validation, student snapshots, exam details, subject snapshots and saved subject ordering. Existing data and historical audit entries remain unchanged.

Migration 37 removes audit-only work from VerifyLegacyStudent, CompleteQuickAdmission, DeleteStudentFeeAssignment, SetStudentAdmissionNumber, SetEnrollmentBillingStartDate, SaveRole, SaveSchoolProfile and SaveFeeConfiguration. Permissions, validation, role restrictions, number serialization, admission history, billing records and returned results remain intact. Existing data and historical audit entries remain unchanged.

Migration 38 removes audit-only work from SaveDailyCollectionClosing, ApproveDailyCollectionClosing, ApplyFeeConfigurationToExistingAssignments and DiscontinueStudentFeeAssignment. Closing snapshots, revisions, approvals, concurrency protection, fee versions, effective-month checks and per-student results remain unchanged. Existing data and historical audit entries are preserved.

Migration 39 restricts PostAdditionalFee to As Needed fee types and excludes admission, tuition and transport. Existing financial records and manual request markers are preserved. The form uses the same fee-type restriction.

Migration 40 validates every printed bill line against its own charge, current outstanding amount and student. Duplicate, omitted and offsetting stale lines are rejected. Zero-value waiver evidence and existing saved bill reprints remain supported; financial records and old bill snapshots are not rewritten.

Migration 41 removes the callback's dependency on the main database version. Communication runtimes keep their own API/structural checks. Permissions, webhook inbox hashes, delivery updates and recovery records remain intact. Future migrations must not reintroduce a main-version comparison into comm.IngestMeta.

Migration 42 lets Fee Matrix clear saved class-rate templates in the same transaction as other rate edits. Blank means unconfigured; zero remains an explicit zero rate. Clear requests verify the loaded rate and its campus/class/fee identity. Student assignments and posted financial records are preserved.

Migration 43 keeps new admission and placement dates equal, while monthly billing starts from the current date under the school cutoff. Admissions into completed academic years create no fee assignments or opening charges and remain outside automatic billing. Existing students and financial history are preserved.

Migration 44 limits same-year transfer fee synchronization to monthly tuition and transport already in use. Admission, books, other monthly fees, annual fees and one-time charges remain unchanged. Existing discounts, paid-bill correction handling and recovery records are preserved.

Migration 45 adds IsTransportLinked to GetFeeTypes so class and campus transfer fee reviews can identify transport without failing. Existing columns, fee permissions, sorting and all saved business records are preserved.

Migration 46 preserves regular-student and date-of-birth certificate personal details per issuance. Existing copies are backfilled from their saved certificate details. Issue Again reviews the current school DOB and creates a new saved copy without changing earlier copies. Reprints identify the exact issue; pending reissues use the existing durable request/outcome recovery system.

Regular and DOB reissue workflow: correct personal details in Admission first. Select the certificate and choose Issue Again, review the current Admission names and DOB, then confirm. Migration 47 makes the new copy use those reviewed personal details and today's certificate date. Saved certificate class stays unchanged. Earlier history rows reprint their own snapshots. DOB certificates keep the existing collection requirement. If a response is interrupted, use Check Reissue before submitting again.

Migration 47 saves SSC/HSSC on character certificates, allows one certificate identity per student and exam type, and saves the printable details separately for every issuance. Correct Details changes only the entered exam type, roll, remarks and certificate date, creating a linked replacement with a required reason. Personal details are corrected in Admission. Issue Again reviews and saves current Admission details, retaining saved certificate-specific history. Earlier copies retain their snapshots, and collection requirements remain. Character creation, correction, type review and reissue use durable request/outcome recovery through Check Save. School/college leaving issues also receive snapshots: reissues refresh Admission names, DOB and admission date, preserve the historical leaving episode and duplicate limit, and reject an invalid selected print issue. Existing character records with no saved exam type remain unclassified until explicitly reviewed; the migration never guesses their type or deletes them. User-authorized cleanup of local test records is a separate action, not a release migration.

Migration 48 makes normal Admission save the operator's exact fee selection. Tuition and admission still load by default but can be removed before saving, including removing every fee. Omitted fees create no assignment or charge. The admission screen submits all retained fees and opts into this behavior with @UseSelectedFeeTypes. Existing callers, including Quick Registration's separate reviewed exclusion flow, retain their existing behavior until they opt in. No existing assignments, bills or payments are changed.

Migration 49 adds the character certificate academic-year choice (passing year = end year minus one), fixed conduct options, and direct Save Changes for the latest copy. Issue Again creates a new copy with current Admission personal details. Existing records are not rewritten by this migration.

Migration 50 adds collection to every certificate type. Collected copies are immutable, including saved subjects and personal wording. Printing is allowed before collection. Uncollected copies can be corrected on the form or voided; Issue Again preserves earlier copies. Regular Student identities use the enrollment. Leaving copy corrections do not repeat withdrawal or fee operations.

Migration 51 retires the certificate cancellation action and drops `VoidCertificateCopy`. It retains existing cancellation markers and historical locks without deleting records or reversing student/financial actions. See `Docs/Decisions/CERTIFICATE_CANCELLATION_REMOVED.md`.
