# Changelog

Notable user-facing, database, installer, and operational changes will be tracked here. This file begins as a **forward-looking** changelog; it is not a fabricated complete history of previous builds.

## [Unreleased]

### Changed

- [PR #19](https://github.com/farhan2166-creator/FMSC-Database/pull/19) makes Student Account Statement start with one student and full-width account details; group/family selection remains a secondary mode. Annual Receipts opens for the selected student. Family / Siblings opens from Student Profile in an owned window and refreshes the profile on return. Financial calculations, SQL, permissions and installer behavior are unchanged.

- [PR #15](https://github.com/farhan2166-creator/FMSC-Database/pull/15) renames Bulk Promotion to **Student Promotion** and moves Promotion History into a window opened from the promotion-details header. History starts with the selected source filters, remains independently filterable and leaves the promotion draft intact. Existing permissions, promotion processing and SQL contracts are unchanged.

### Added

- [PR #18](https://github.com/farhan2166-creator/FMSC-Database/pull/18) adds conservative DACPAC refresh comparison and trusted default-branch synchronization PR automation. Exact full-model/module/data matching, sanitized refusal reports and synthetic safety regressions preserve existing approval/security/SQL-isolation rules; no automatic merge or material-change override.
- Generated [PR #17](https://github.com/farhan2166-creator/FMSC-Database/pull/17) aligns the verified metadata-only `91ec136` refresh across three approval/provenance records, preserving the refreshed DACPAC and reusing the schema-only fixture.

### Testing

- Added an isolated Level 1 SQL lifecycle harness in [PR #14](https://github.com/farhan2166-creator/FMSC-Database/pull/14), with schema-only approved-model provenance, fictional school fixtures, persisted admission/fees/promotion/recovery assertions, existing-suite integration and sanitized CI evidence. Production application/database/installer behavior is unchanged; real Windows and deployment acceptance remain separate.

### Fixed

- Improve LocalDB installation on clean Windows computers: MSI verbose logging, explicit wizard fallback after silent failure, restart-required exit handling, executable verification and PATH discovery. No school data or database schema changes.

- [PR #16](https://github.com/farhan2166-creator/FMSC-Database/pull/16) restores date-field height after shared width limits are applied, preventing date controls from disappearing while their captions remain visible in admissions and other field layouts. Date values, optional-date handling and validation are unchanged.

- Shared DPI and responsive-layout corrections are proposed in [PR #13](https://github.com/farhan2166-creator/FMSC-Database/pull/13): explicit per-monitor awareness, logical client-area density, monitor-relative fonts and grid/tab dimensions, preserved window minima, scrollable small-screen dialogs and bounded fixed-purpose field widths. Appearance and business workflows are preserved. Physical DPI/monitor and ReportViewer acceptance remains manual.

The following end-user-flow corrections are proposed in [PR #8](https://github.com/farhan2166-creator/FMSC-Database/pull/8).

- Dashboard Student Reports and profile shortcuts follow destination permissions. Read-only record review remains available; unavailable photo/family actions are clearly disabled. Administrator and Accountant grants are unchanged (UFA-09).
- Daily closing rejects fractional new actual amounts without rounding; printed summaries match the on-screen precision and preserve historical stored fractions (UFA-08).
- Daily closing summaries print with intentionally blank Refund/Correction actuals, retaining recorded net totals and leaving unchecked methods blank (UFA-07).
- Ctrl+K searches permitted destinations independently of collapsed or compact sidebar visibility and invokes navigation directly (UFA-06).
- Admission and fee collection hold submitted inputs through awaited writes and refreshes, preventing post-submit typing from being lost. Validation remains editable before submission (UFA-05).
- New sports and provisional certificate content participates in existing leave/discard guards. Date-only untouched dialogs remain freely abandonable (UFA-04).
- Classes & Sections forwards active-save state without adding setup-draft prompts. Bulk fee-rate drafts retain their existing discard protection when embedded, with a clean baseline after a successful apply (UFA-03).
- Admission closure and legacy go-live hold review inputs, reject obsolete previews, and prevent closing during pending operations (UFA-02).
- Certificate creation keeps inputs locked and refuses repeat submission until the pending write finishes (UFA-01).

### Security
- Added weekly Dependabot proposals for application/test NuGet packages and GitHub Actions, with grouped minor/patch updates, bounded open PRs and human review.
- Added read-only Gitleaks CLI and sensitive-file checks on pull requests and pushes to `main`, with checksum-pinned downloads, temporary redacted reports and exact synthetic-fixture exceptions. No paid service or automatic merging is enabled.
- Added disposable detection/redaction regression tests and guidance for security failures and dependency review. Application, SQL, database compatibility and installer behavior are unchanged.

### Documentation
- Added a project overview and operator, installation, recovery, architecture, release-readiness, and development-workflow guides.
- Added a pull-request template prompting contributors and Codex to explain behavior changes, safety implications, tests, and manual acceptance requirements.
- Established a single current [master audit](Audit/FMSC_MASTER_AUDIT.md) with a [historical index](Audit/FMSC_HISTORICAL_AUDITS.md) for earlier investigations.

### Build and delivery
- Existing Windows desktop CI and manually triggered private application-only and complete-installer artifact workflows are documented. **Workflow existence does not mean a complete installer has passed deployment acceptance.**
- Fresh-install database MDF/LDF files remain owner-supplied after downloading the private full-installer ZIP; school data is not included in source control or Actions artifacts.

## Versioning policy

The application assembly version in `FMSC_SMS.csproj` and the database's required schema version are **different concepts**. The current runtime contract requires **database schema 6**. A version should not be tagged or described as a production-approved school release until the tests and checks in [RELEASE_CHECKLIST.md](Docs/RELEASE_CHECKLIST.md) are recorded.

When a pull request changes behavior, add an Unreleased entry describing the operator-visible result and a PR link. Move entries to a dated version section only after an actual release approval.

For the original fixes, implementation passes, schema verification and historical decisions, see the [historical audit index](Audit/FMSC_HISTORICAL_AUDITS.md) and commit history.
