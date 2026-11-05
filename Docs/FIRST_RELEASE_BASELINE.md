# First production baseline

The owner approved a one-time pre-deployment reset on 5 November 2026.
Application version is **1.0.0**, and required database version is **1**.
The supplied, fully updated database already contains the approved schema.
Resetting its version history does not change schema or school records.

`Tools/ReleaseBaseline/Reset-Version.sql` is the reviewed one-time metadata
operation, not a runtime migration. Run only inside an outer transaction on a
verified, backed-up copy of the fully updated pre-release database. Validate
the application compatibility contract and compare preservation fingerprints
before using the detached pair for release preparation. Keep the original backup.

The production `Migrations` directory starts empty. The next schema change is
`2.sql`, followed by `3.sql`, with the normal verified-backup installer safeguards.
Application-only releases increase the application version and retain schema 1.
Use increasing stable versions: `1.0.0`, `1.0.1`, `1.1.0`, and so on.

Old development migrations remain under `Tests/LifecycleSqlChecks/Migrations`
only to construct disposable regression fixtures from the historical schema-only
model. They are excluded from application/release publishing. Never replay them
on the baseline-1 school database or copy them back into production migrations.

The private updates repository is reset separately after validating a full Git
backup. Its first new package must be 1.0.0 with schema 1. Source Git history,
repository visibility, and enrolled-computer deploy keys remain in place.
Old development installations with higher version labels need a deliberate
fresh baseline installation; normal downgrade protections remain enabled.
