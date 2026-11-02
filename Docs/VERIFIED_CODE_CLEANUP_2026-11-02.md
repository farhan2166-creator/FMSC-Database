# Verified source cleanup — 2 November 2026

Reviewed the desktop compilation inputs, dashboard factories, contextual page
construction, report template consumers, test reflection, help sources and the
separate communication host. Compiler symbol references were checked across
partial classes using the project's resolved reference assemblies. Follow-up
passes identified helpers used only by the removed implementations.

## Removed

| Source | Verification and surviving path |
| --- | --- |
| `BulkPromotionPage.cs` | No application construction/reference. Dashboard opens `StudentPlacementPage` for annual promotion, transfers, corrections and history. Retargeted operator tests to that page; retained earlier `PromotionHistoryPage` read/filter/disposal tests and navigation permissions. |
| `MatchingSectionPromotion.cs`, `PromotionSectionMappingDialog.cs` | Only the retired bulk-promotion screen called these. Current placement plans use `PlacementPlan`; SQL history and old procedures remain. |
| `MigrationBillingCleanupPage.cs` | No construction, navigation or reflective caller. Active legacy admission setup still uses `LegacyGoLiveDialog` and `FinishLegacyAdmissionSetup`. Removed the unused page title only; retained cleanup procedures and markers. |
| `ClearanceUi.cs` | No caller. Leaving certificates use the existing `LeaveFeeReviewBar`; departure and fee review behavior is preserved. No checklist data or procedures were deleted. |
| `LoadingState.cs` | No production caller. Removed only its component-specific fixture assertions; retained rendering-mode, warning attention and theme resource checks. |
| `FeeAgingReport.rdlc` | Only a generic template smoke fixture referenced it. Active aging reports use generated grid PDFs and `ClassOutstandingCharges.rdlc`. Updated the fixture inventory and stale build/publish-output cleanup. |

Also removed 38 unreferenced private methods and an unused admission bill row
type, field/caption constants and surface aliases. These include older Enter-key
navigation and legacy button/grid styling paths superseded by the active shared
helpers. Application entry points, interface implementations and reflection-used
test hooks were excluded from automatic unused-symbol candidates.

## Retained deliberately

- `AsyncTableLoader`: called by `DatabaseHelper`, including multi-result reads.
- `MetaWebhookStore`: used by the separate `Apps/Host` project.
- `SmsSegments`: communication protocol/SQL parity checks still use it.
- Classes and Sections editors: opened by `ClassSectionCatalogDialog` inside
  campus assignment management; their absent sidebar routes do not make them dead.
- All numbered migrations, schema requirements, business history and recovery data.
- Historical audit documents describe their original reviewed versions and were
  not rewritten as if these files had never existed.

## Verification

- Application and native smoke harness build: zero warnings/errors.
- Report data/grouping/layout checks: 679 passed, without SQL integration.
- Offline help and source-link checks: 1,922 passed.
- Compiler reference analysis and repository searches: no remaining production
  or test callers of the removed types; changed help links target the active page.

Native promotion/operator/theme fixtures were updated and compiled. Their
interactive Windows runs have not been performed in this Linux workspace.
No database or migration changes are included.
