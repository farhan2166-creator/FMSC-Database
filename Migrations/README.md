# Apply the database changes later

The application requires **schema 24**. SQL changes are checked in here;
merging the code does not execute them against your database.

| Current database version | Files to apply, in order |
|---|---|
| 19 | `20.sql`, `21.sql`, `22.sql`, `23.sql`, then `24.sql` |
| 20 | `21.sql`, `22.sql`, `23.sql`, then `24.sql` |
| 21 | `22.sql`, `23.sql`, then `24.sql` |
| 22 | `23.sql`, then `24.sql` |
| 23 | `24.sql` |
| 24 | None |

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

## Manual application with SQLCMD

Close the application and make a verified backup first, as described in
[backup and recovery](../Docs/BACKUP_RECOVERY.md). The scripts require an outer
transaction, which the installer normally provides.

For a schema-19 database, save this wrapper **outside this directory**, change the
five include paths to your checkout, and run it with SQLCMD. For schema 20, omit
the `20.sql` include and its following `GO`. For schema 21, start at `22.sql`;
for schema 22, start at `23.sql`; for schema 23, include only `24.sql`.

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
IF XACT_STATE() <> 1
    THROW 51000, 'The upgrade transaction cannot be committed.', 1;
IF ISNULL((SELECT TOP (1) TRY_CONVERT(int, VersionNumber)
           FROM dbo.DatabaseVersion ORDER BY VersionId DESC), -1) <> 24
    THROW 51000, 'The upgrade did not reach schema 24.', 1;
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
