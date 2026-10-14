# Changelog

Notable user-facing, database, installer, and operational changes will be tracked here. This file begins as a **forward-looking** changelog; it is not a fabricated complete history of previous builds.

## [Unreleased]

### Fixed
- Dashboard Student Reports and profile shortcuts follow destination permissions. Read-only record review remains available; unavailable photo/family actions are clearly disabled. Administrator and Accountant grants are unchanged (UFA-09).
- Daily closing rejects fractional new actual amounts without rounding; printed summaries match the on-screen precision and preserve historical stored fractions (UFA-08).
- Daily closing summaries print with intentionally blank Refund/Correction actuals, retaining recorded net totals and leaving unchecked methods blank (UFA-07).
- Ctrl+K searches permitted destinations independently of collapsed or compact sidebar visibility and invokes navigation directly (UFA-06).
- Admission and fee collection hold submitted inputs through awaited writes and refreshes, preventing post-submit typing from being lost. Validation remains editable before submission (UFA-05).
- New sports and provisional certificate content participates in existing leave/discard guards. Date-only untouched dialogs remain freely abandonable (UFA-04).
- Classes & Sections forwards active-save state without adding setup-draft prompts. Bulk fee-rate drafts retain their existing discard protection when embedded (UFA-03).
- Admission closure and legacy go-live hold review inputs, reject obsolete previews, and prevent closing during pending operations (UFA-02).
- Certificate creation keeps inputs locked and refuses repeat submission until the pending write finishes (UFA-01).

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
