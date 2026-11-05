# Restore the project on a new PC

The public `farhan2166-creator/FMSC-Source` repository contains application source,
assets, report templates, project/publish configuration, installer and updater
scripts, database compatibility definitions, schema-only test fixtures, tests,
documentation, and GitHub Actions workflows. Generated builds and local caches
are recreated; they do not need to be committed.

## Restore and build

On Windows, install Git and the .NET 8 SDK (or Visual Studio with the .NET desktop
development workload and .NET 8). Then:

```powershell
git clone https://github.com/farhan2166-creator/FMSC-Source.git
cd FMSC-Source
dotnet restore FMSC_SMS.sln
dotnet build FMSC_SMS.sln -c Release --no-restore
```

Open `FMSC_SMS.sln` in Visual Studio for development. The build needs no publisher
token, school SSH private key, database snapshot, or old `bin`/`obj` folder.
Running the application requires the intended compatible database; a successful
build alone does not create or restore school records.

## Files kept privately

Keep a separate backup outside this PC of the current school database: a verified
SQL `.bak`, or a matching detached MDF/LDF pair. This includes the approved
baseline-1 snapshot used to prepare fresh installations. GitHub source and the
schema-only DACPAC fixtures do **not** contain those school records.

For release preparation, place the approved detached pair in `../Database` as
`FMSC_DB.mdf` and `FMSC_DB_log.ldf`. Follow [installation](INSTALLATION.md) and
[backup/recovery](BACKUP_RECOVERY.md). Do not commit these files or SQL backups.
Private Git recovery bundles and local recovery evidence also stay private.

Publishing credentials remain in GitHub Actions secrets. Local Git credentials,
Windows DPAPI files, school SSH private keys and Visual Studio user settings do
not belong in source control. A lost school key requires a new computer enrollment
and revocation of the old deploy key; copying source cannot restore a DPAPI key.

## Build and publish on GitHub

The existing Actions workflows remain available after losing the local folder:

- **09** builds the desktop application and runs regression checks.
- **02** tests installation and recovery using disposable synthetic databases.
- **03** builds the database-free complete installer; add the approved detached
  database pair privately after downloading it for a fresh installation.
- **04** builds the application bundle and can publish an approved private update
  when the owner chooses publication. Keep `FMSC_UPDATES_PUBLISH_TOKEN` in repository
  secrets with access to the private distribution repository.

The private `FMSC-Updates` repository retains the latest two approved update
versions. Source history remains in `FMSC-Source`. Neither repository replaces
the separate backup of the school database.
