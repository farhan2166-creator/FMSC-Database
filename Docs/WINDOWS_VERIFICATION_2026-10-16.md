# Windows verification — 16 October 2026

Initial main: ff48ec0568fa83760aaa0a9d447b8fd5327c1178; clean checkout, fetched origin. All open PRs inspected: #48 88037290153cb6c0f2ab38b919565440523cd907; #49 fc4b692c8430c3ea86a8cea3894f68736a019d6e; #50 b487e9a49a256ec1f7bc1023c94ff3ebfb4ff3a2; #51 6d71ae1dd90221fe250b505173be053dcdd918ed. They branch independently from the same main. #49–51 are drafts. Hosted failures do not prove application failures: #51 desktop job has runner_id=0 and zero steps. No reruns requested.

## Environment and isolation

Windows active console session; one 1920x1080 display, 1920x1032 working area, 96 DPI. SDK 9.0.318 only; .NET/WindowsDesktop runtime 8.0.31 installed. An exact SDK-8 build is NOT RUN. GitHub CLI authentication/fetch succeeded outside sandbox; initial sandbox failures were environment restrictions. LocalDB tools and MSSQLLocalDB/ProjectModels instance names detected; neither existing instance was connected. WSL and Docker are unavailable. Python 3.12.14, PowerShell 7 and Windows PowerShell available. Desktop automation available.

Control runs use the existing FmscLevel2=true compile-time isolation, a generated FMSC_LEVEL2_OWNER and matching owned FMSC_LEVEL2_ROOT with owner.txt. No database was provisioned for these controls. This redirects theme/preferences and guards any accidental SQL target. Operational files/settings were never overwritten. Normal Release build was also tested separately.

## PR #51

At original 6d71ae1: `dotnet build FMSC_SMS.sln -c Release` PASS (zero warnings/errors); `dotnet run --project Tests/HelpChecks/HelpChecks.csproj -c Release` PASS 1919; AuditFixChecks PASS 832; PerformanceChecks PASS 32183. Help search timing: 2000 searches in 9 ms; synthetic timing is not a SQL/UI benchmark.

`dotnet run --project Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -c Release -- --help-center` with isolated build/profile reproduced an application exception twice: FitSplit changed orientation while table layout gave an axis insufficient space. The focused fix waits for both axes to fit and bounds the old distance before changing orientation. Extended the existing compact/wide transition regression. Fixed working tree: 40 Help control assertions PASS. Full `--post-fix` outside sandbox: 1787 assertions PASS. Sandbox-only PDF replacement denial was not reproduced outside sandbox and is an environment limitation.

Added `--help-center-interactive` to the same existing test executable. It requires the owned Level2 profile, hosts the actual production page without startup/SQL, and provides theme/window selectors. Actual desktop observations: search, no-results guidance, Tab search→topic→rule→results, result arrows, Enter into answer, Ctrl+F selecting query, combined payment/Collection/Recovery filters (two results), and related double-click clearing filters/selecting destination worked. Mouse testing stopped at the user's request. Interactive related Enter, answer scrolling, theme switching and compact visual acceptance are NOT RUN; their control contracts passed. Physical alternate DPI/mixed monitors and Narrator/operator comprehension remain LOCAL REQUIRED. Resized-window contracts do not prove those scenarios. The automation helper's focus metadata lagged visible focus; visible screenshots were inspected rather than trusting that field alone.

## Other existing checks

Official Gitleaks 8.30.1 Windows archive verified against published SHA256 d29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e. Eight security fixture tests PASS; fetched-history/staged-content scan PASS before fixes were staged (repeat required for final staged changes). Initial sandbox security-fixture Git/cleanup denials are environment failures; unsandboxed execution passed.

CommunicationChecks PASS 42 (fake transports, no external request). TestInstallerLocalDb.ps1 PASS (mocked process exits, no MSI run); TestInstallerMigrations.ps1 PASS 15 scenarios plus wrapper/cleanup/order checks in PowerShell 7. Windows PowerShell first attempt refused execution policy; not a migration assertion failure. TestPublishPowerShell.ps1 PASS: full disposable publish, CLI unsafe-target refusal, installer/migration inventory. This is a disposable publish regression with an inert prerequisite, not installation or release packaging.

Local detailed logs are ignored under artifacts/verification/windows-20261016. No logs containing credentials, real records or data-bearing database assets are included in this report. Installer/MSI/file-corruption tests require a disposable VM and remain blocked on this everyday PC. No prerequisite installation, SQL/business-rule/permission/version/DACPAC changes or real messaging occurred.
