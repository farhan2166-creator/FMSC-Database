# Database reference snapshot

Extracted from the uploaded FMSC_DB database on 10 October 2026. These files contain object definitions and catalog metadata, not student, payment, account or school-profile records. They are a reference snapshot, not deployment migrations or a complete database creation script.

- 258 SQL module definitions and 57 tables.
- Latest application version: **3**, read by descending `DatabaseVersion.VersionId`, matching the application requirement. Historical version numbers survive the baseline reset; do not use their numeric maximum.
- Latest version note states migrations 4–17 were applied in sequence, keeping compatibility baseline 3. This is recorded history, not independent proof of every migration effect.
- Source engine database format: 904 (SQL Server 2019); compatibility level: 150. A separate working copy was attached and upgraded by SQL Server 2022. Original uploaded MDF/LDF files were not changed.
- Table columns, types, identity/computed definitions, parameters, table types, defaults, checks, foreign keys and indexes are in `SchemaMetadata.json`. Object IDs relate to this snapshot.
- Module files preserve the source ANSI NULL and quoted-identifier settings. Treat them as historical reference; future changes belong in `Migrations`, following the existing deployment/version policy.

## Verified fee cutoff behavior

`AdmitStudent` uses the admission date. With a cutoff of 1–28, admission on or after that day moves billing start to the first of the next month. Before it, billing starts in the admission month. A null cutoff does not move billing start. `SaveSchoolProfile` writes null when the field is blank, rather than preserving a previous cutoff.

`ReAdmitStudent` computes the same default, then overrides it with the mandatory reviewed `MonthlyStartMonth`. The current readmission workflow therefore uses the user's selected start month. This setting does not calculate a daily partial fee.
