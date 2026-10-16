# Developer Architecture — FMSC_SMS

## System boundaries

FMSC_SMS is a Windows Forms desktop application targeting `net8.0-windows`, backed by SQL Server Express LocalDB. The `FMSC_SMS.csproj` produces a WinExe and references `Microsoft.Data.SqlClient`, PDFsharp/MigraDoc, and ReportViewerCore.WinForms. It includes WinForms resources, icons and RDLC report templates.

```text
Program.cs → connection / first-run / login
          → MainDashboardForm.cs (navigation and workspaces)
          → Pages/* (WinForms screens, workflow coordination)
          → Core/* (shared controls, validation, connection, recovery contracts)
          → DatabaseHelper.cs (FMSC_DB and SQL connection setup)
          → SQL Server LocalDB, stored procedures and transactions
Services/Backup/* → verified SQL backup/restore and recovery
Installer/core.ps1 + Migrations/*.sql → pending numbered upgrades, then database verification
Tests/* + Tools/* → isolated regression and disposable SQL safety checks
```

This is a logical dependency map, not a claim that all pages directly call every layer.

## Startup and database identity

`Program.cs` initializes WinForms and exception handling, displays a connection/startup dialog, performs first-run setup where needed, then login and the dashboard. `DatabaseHelper.cs` points at `C:\ProgramData\FMSC_Database\FMSC_DB.mdf` using the per-user LocalDB `(localdb)\MSSQLLocalDB` endpoint. It verifies the identity and calls the production `Core/DatabaseCompatibility.cs` contract; **required database schema version is 6**.

The compatibility contract combines version requirements with schema structural requirements stored in embedded JSON (`Core/DatabaseCompatibilityRequirements.json`, `Core/DatabaseMigrationRequirements.json`). The embedded compatibility manifests are enforced against the connected database at runtime; they do not authorize overwriting school database files.

## Functional modules

| Source area | Responsibility |
|---|---|
| `Pages/Students/Admissions`, `Enrollment`, `Maintenance`, `Profile`, `Certificates` | Admission, provisional registration, student lifecycle, data checks, profile and certificates |
| `Pages/Finance/Billing`, `Collection`, `StudentAccounts`, `Configuration` | Charges, collection, posting/reversal, corrections, rates and fees |
| `Pages/Reports` | Students, financial reports, exports and printable documents |
| `Pages/Staff`, `Pages/Setup`, `Pages/Administration`, `Pages/Security` | Staff, academic configuration, backup/audit and authentication |
| `Core/` | Shared UI, input validation, page state, read coordination, reports, atomic file writing, recovery and access-policy helpers |
| `Services/Backup/` | Verified backups, pre-restore safety backup, SQL restore and compatibility checks |

The page organization and navigation group names are documented in [PROJECT_STRUCTURE.md](../PROJECT_STRUCTURE.md). Avoid adding a second helper for behavior that already lives in shared `Core` classes.

## Shared DPI and layout ownership

The application requests `PerMonitorV2` through WinForms application configuration. The existing programmatic UI retains `AutoScaleMode.None`: shared metrics own geometry, while Windows handles monitor-relative native fonts. `AppScale` calculates density from the available client area in 96-DPI units and converts geometry to device pixels once; font density remains separate. `ApplicationWindowForm` scales existing child geometry on monitor changes, excluding the caption that already handles its own DPI notifications. `UiDpi` preserves monitor-relative shared fonts and converts reapplied grid/tab metrics when a dialog is on a different monitor from the dashboard.

Windows are bounded to the current working area without permanently losing their requested minimum size. Eligible top-level dialog content uses `ResponsiveViewport` to retain readable dimensions and provide scrolling on smaller work areas, then expand on larger ones. Existing scroll hosts, standalone grids and ReportViewer roots retain their own viewport behavior. Fixed-purpose combo, date and numeric fields use a font-based width cap; long-text fields remain flexible. Navigation destinations, theme colors and report zoom are unchanged.

Portable sizing checks and Windows native-control fixtures cover synthetic client areas/DPI contexts. They do not change physical display settings or prove mixed-monitor behavior. Interactive acceptance must check 100%, 125%, 150% and 200% scaling at the supported resolutions, including dialog scrolling, grids, tabs, ReportViewer and repeated moves between monitors.

## Data correctness and safety contracts

- **Use SQL-side transaction and permission enforcement** for financial/state-changing work. A button disabled in WinForms is not a substitute for server authorization.
- **Unknown commit acknowledgment is not a failed transaction.** Several admission, fee and certificate flows use request IDs/reconciliation; preserve original IDs and payload identity when retrying, and explicitly expose unresolved outcomes.
- Preserve whole-rupee validation, paid-charge protections, historical student placement, backup recovery identity and owner-approved business rules. See [master audit](../Audit/FMSC_MASTER_AUDIT.md).
- Preserve page-level busy/dirty contracts (`IBusyPage`, unsaved-edit guards, modal close protection); support cancel/stay without silent loss. Apply draft prompts selectively to substantial work. The owner explicitly permits small Classes & Sections drafts to be abandoned; their workspace forwards busy writes without implementing `IUnsavedChanges`. Embedded bulk fee-rate changes expose their existing dirty contract and reset the baseline after applying.
- Hold submission-defining controls across awaited writes and refreshes with the existing `BusyScope`. Admission lifecycle modal close guards distinguish a pending operation from an idle, freely abandonable dialog.
- Command palette destinations use permission-filtered `NavigationCommand` records; sidebar visibility and expansion are presentation state, not navigation eligibility. Destination callbacks retain the shell leave guard and recheck their permissions.
- Use shared report/CSV/PDF utilities and sanitize exported data appropriately.
- Avoid unnecessary synchronous DB reads on the UI thread. Keep reloads tied to active selection/context.

## Installer, schema and release

- Communication Center is optional and separately verified. Its already-applied setup script is retired and is not offered by the current installer. Fresh installations needing messaging use an owner-approved snapshot that includes the module; existing communication data is preserved. Opening the page does not install its schema or enable live dispatch. See [current deployment guidance](COMMUNICATION_CENTER.md).
- Normal full packaging uses `Properties/PublishProfiles/PortableRelease.pubxml`, `Installer/ValidateReleaseAssets.ps1` and `Installer/core.ps1`.
- This release requires the current schema-6 baseline. Applied migrations 2–7 are retired. An empty or missing migrations folder skips migration execution; numbered SQL files newer than the installed base version run in numeric order within the schema version supported by that release.
- The private complete-installer workflow obtains signed LocalDB MSI and pinned sqlcmd, bundles the application and scripts **without** school database files. The owner supplies verified detached MDF/LDF locally after download for a fresh install.
- `CIAppOnlyBundle` is intentionally scoped to the separate app-only artifact, not the full installer validation.

## Tests and CI

| Test family | Scope |
|---|---|
| `Tests/AuditFixChecks` | Portable policy, durability, recovery and workflow checks |
| `Tests/PerformanceChecks` | Synthetic loading, state and performance contracts |
| `Tests/ThemeSmokeChecks` | Focused WinForms actual-page/host fixtures; not a complete interactive school-PC test |
| `Tests/*SqlChecks` | SQL integration/migration invariants using disposable test databases only |
| `Tools/TestPublishPowerShell.ps1` | Disposable publish validation, not a production installer acceptance |

GitHub Actions builds on `windows-2022`. Passing automated tests does **not** prove SQL under a second operator Windows account, report printers/DPI, actual network loss, target database migration or installer rollback.

## Change protocol

Before editing, read [AGENTS.md](../AGENTS.md), [development workflow](DEVELOPMENT_WORKFLOW.md), the active [audit](../Audit/FMSC_MASTER_AUDIT.md), and the relevant feature sources. Keep changes incremental; require a focused PR explaining behavior, safety impact, tests, and migration requirements. Do not edit or introduce real school data into test fixtures.
