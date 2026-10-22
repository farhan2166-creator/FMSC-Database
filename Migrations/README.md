# Apply the database changes later

The application requires **schema 39**. SQL changes are checked in here;
merging the code does not execute them against your database.

| Current database version | Files to apply, in order |
|---|---|
| 19 | `20.sql`, `21.sql`, `22.sql`, `23.sql`, `24.sql`, `25.sql`, `26.sql`, `27.sql`, `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 20 | `21.sql`, `22.sql`, `23.sql`, `24.sql`, `25.sql`, `26.sql`, `27.sql`, `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 21 | `22.sql`, `23.sql`, `24.sql`, `25.sql`, `26.sql`, `27.sql`, `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 22 | `23.sql`, `24.sql`, `25.sql`, `26.sql`, `27.sql`, `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 23 | `24.sql`, `25.sql`, `26.sql`, `27.sql`, `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 24 | `25.sql`, `26.sql`, `27.sql`, `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 25 | `26.sql`, `27.sql`, `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 26 | `27.sql`, `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 27 | `28.sql`, `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 28 | `29.sql`, `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 29 | `30.sql`, `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 30 | `31.sql`, `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 31 | `32.sql`, `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 32 | `33.sql`, `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 33 | `34.sql`, `35.sql`, `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 34 | `35.sql`, `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 35 | `36.sql`, `37.sql`, `38.sql`, then `39.sql` |
| 36 | `37.sql`, `38.sql`, then `39.sql` |
| 37 | `38.sql`, then `39.sql` |
| 38 | `39.sql` |
| 39 | None |

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
schema 37 needs `38.sql` and `39.sql`; schema 38 needs only `39.sql`.

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
IF XACT_STATE() <> 1
    THROW 51000, 'The upgrade transaction cannot be committed.', 1;
IF ISNULL((SELECT TOP (1) TRY_CONVERT(int, VersionNumber)
           FROM dbo.DatabaseVersion ORDER BY VersionId DESC), -1) <> 39
    THROW 51000, 'The upgrade did not reach schema 39.', 1;
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
