# First production baseline

Application **1.0.0** and database **1** are the first supported production baseline. The owner's supplied database snapshot contains the complete approved schema and school data. No runtime reset or development-version upgrade path is required.

The next database change belongs in `Migrations/2.sql`, followed by consecutive numbered scripts. Application-only releases increase the application version while retaining the required database version. Normal compatibility checks, verified pre-migration backups, sequential migrations and recovery safeguards remain required.

Disposable tests publish `Tests/LifecycleSqlChecks/Baseline/schema.dacpac` directly. It contains schema only, with no school records. Its checked provenance records its SHA-256 digest. `Baseline/Initialize.sql` initializes only explicitly owned, empty test databases at version 1; it refuses populated databases. Future migrations then run normally.

Source Git history remains available for historical development changes. Old development migrations and one-time reset tools are retired from the current tree. Do not replay development versions against baseline 1 or reset production migration history.
