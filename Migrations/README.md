# Apply the database changes later

The application requires **schema 29**. SQL changes are checked in here;
merging the code does not execute them against your database.

| Current database version | Files to apply, in order |
|---|---|
| 19 | `20.sql`, `21.sql`, `22.sql`, `23.sql`, `24.sql`, `25.sql`, `26.sql`, `27.sql`, `28.sql`, then `29.sql` |
| 20 | `21.sql`, `22.sql`, `23.sql`, `24.sql`, `25.sql`, `26.sql`, `27.sql`, `28.sql`, then `29.sql` |
| 21 | `22.sql`, `23.sql`, `24.sql`, `25.sql`, `26.sql`, `27.sql`, `28.sql`, then `29.sql` |
| 22 | `23.sql`, `24.sql`, `25.sql`, `26.sql`, `27.sql`, `28.sql`, then `29.sql` |
| 23 | `24.sql`, `25.sql`, `26.sql`, `27.sql`, `28.sql`, then `29.sql` |
| 24 | `25.sql`, `26.sql`, `27.sql`, `28.sql`, then `29.sql` |
| 25 | `26.sql`, `27.sql`, `28.sql`, then `29.sql` |
| 26 | `27.sql`, `28.sql`, then `29.sql` |
| 27 | `28.sql`, then `29.sql` |
| 28 | `29.sql` |
| 29 | None |

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
eight include paths to your checkout, and run it with SQLCMD. For schema 20, omit
the `20.sql` include and its following `GO`. For schema 21, start at `22.sql`;
for schema 22, start at `23.sql`; for schema 23, start at `24.sql`; for schema 24, start at `25.sql`; for schema 25, start at `26.sql`; for schema 26, start at `27.sql`; for schema 27, start at `28.sql`; for schema 28, include only `29.sql`.

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
IF XACT_STATE() <> 1
    THROW 51000, 'The upgrade transaction cannot be committed.', 1;
IF ISNULL((SELECT TOP (1) TRY_CONVERT(int, VersionNumber)
           FROM dbo.DatabaseVersion ORDER BY VersionId DESC), -1) <> 29
    THROW 51000, 'The upgrade did not reach schema 29.', 1;
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
