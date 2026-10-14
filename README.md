# FMSC_SMS — School Management System

FMSC_SMS is a Windows desktop school-management application built with **C#**, **.NET 8 Windows Forms**, and **SQL Server Express LocalDB**. It supports student admissions and lifecycle records, staff management, tuition and fee workflows, reporting, certificates, school setup, controlled user access, and database backup/restore.

> **Deployment status:** Building successfully is not a production release sign-off. Validate the full installer, Windows operator account, printer/report workflows, and the intended school database on a disposable test copy before deploying it.

## Documentation

| Audience | Start here |
|---|---|
| School operator | [User guide](Docs/USER_GUIDE.md) |
| Installer or administrator | [Fresh installation and upgrade guide](Docs/INSTALLATION.md) |
| Administrator restoring data | [Backups and recovery](Docs/BACKUP_RECOVERY.md) |
| Developer / Codex | [Architecture](Docs/ARCHITECTURE.md) and [development / PR workflow](Docs/DEVELOPMENT_WORKFLOW.md) |
| Release owner | [Release acceptance checklist](Docs/RELEASE_CHECKLIST.md) |
| Everyone tracking changes | [Changelog](CHANGELOG.md) |
| Audit findings | [Current master audit](Audit/FMSC_MASTER_AUDIT.md) and [historical evidence](Audit/FMSC_HISTORICAL_AUDITS.md) |

For detailed screen naming, see [project structure](PROJECT_STRUCTURE.md). [Admission workflow decisions](ADMISSION_WORKFLOW.md) contain historical process context and should be read with the current code and schema-version notes.

## Technical overview

- **Platform:** Windows x64; WinForms on .NET 8. The complete installer publishes a self-contained Windows application.
- **Database:** Local SQL Server database `FMSC_DB`, normally managed through the current Windows user's `(localdb)\MSSQLLocalDB` instance. The application expects **schema version 6** and checks structural compatibility.
- **Database delivery:** Fresh installs require an owner-provided, verified, **detached matching MDF/LDF pair**. Real school database files, backup files and credentials must not be committed to GitHub or uploaded to Actions.
- **Installer:** `Installer/core.ps1`, launched by `Install FMSC_SMS.bat` in the release bundle. Existing installation and migration behavior must be tested against a disposable copy before production.
- **Reporting:** PDF/CSV and RDLC report components are included; physical printing and target-printer settings require Windows acceptance.

## Building the application

On Windows with the .NET 8 SDK:

```powershell
dotnet restore FMSC_SMS.sln
dotnet build FMSC_SMS.sln -c Release --no-restore
dotnet run --project Tests/AuditFixChecks/AuditFixChecks.csproj -c Release
dotnet run --project Tests/PerformanceChecks/PerformanceChecks.csproj -c Release
```

The Windows CI additionally builds and runs focused WinForms page/host tests and verifies the reviewed schema artifact. Tests which operate a real database require a disposable SQL fixture; never point them at school data.

## Private GitHub Actions

- [Desktop CI](.github/workflows/fmsc-desktop-ci.yml): push/PR checks for `main`.
- [Windows application-only ZIP](.github/workflows/fmsc-windows-download.yml): manual application bundle; **not** a complete installer.
- [Complete Windows installer ZIP](.github/workflows/fmsc-complete-installer.yml): manual private package with installer scripts, migrations, and prerequisites; the owner adds the detached MDF/LDF **after download** for a fresh install.

Run status, download availability and retention are shown in the [Actions tab](../../actions). No GitHub workflow installs onto a school PC or publishes a public release.

## Source of truth and change policy

The executable's project version is maintained in [FMSC_SMS.csproj](FMSC_SMS.csproj); the required **database schema version** is independently declared and validated by the code and installer. Do not equate those version numbers.

Propose functional changes in a focused pull request using [the PR template](.github/pull_request_template.md), explain before/after behavior, update affected documentation and [CHANGELOG.md](CHANGELOG.md), and include test evidence. Preserve financial rules, existing data, authorization, recoverability, and the owner's release packaging decision in [AGENTS.md](AGENTS.md).
