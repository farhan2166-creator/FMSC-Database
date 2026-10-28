# System redundancy review — 28 October 2026

## Follow-up: migration 59 prepared

The owner subsequently requested work on the remaining audit writers. Migration 59 now revises all 19 inventoried procedures and preserves the historical readers and data. Parser-based business-SQL checks passed for all 19 procedures, isolated SQL tests passed, and the application builds. Application and installer require schema 59. Live application was rejected by automatic approval review pending explicit production-change approval; live schema remains 58. See [the migration decision and validation details](Decisions/REMAINING_AUDIT_RETIREMENT.md). The earlier no-bulk-rewrite status below describes the source-cleanup stage and is superseded by this prepared migration.

## Cleanup performed after owner authorization

The findings below record the original review. The following status supersedes their proposed/removal language.

- Connected to the existing live LocalDB instance after starting it normally. Verified database FMSC_DB, schema 58, and the expected MDF/LDF under `C:\ProgramData\FMSC_Database`. No attach, restore, record changes or procedure changes were performed.
- Verified the live database contains the same 19 generic audit-writing procedures listed below, plus WriteAuditLog. Metadata also confirms GetAuditLogs, GetAuditTableNames, PruneAuditTrail and AuditValueLeaves remain. Audit data was not deleted.
- Removed eight confirmed unused files: four student-report compatibility wrappers, ChangePasswordHostPage, CurrentEmploymentPanel, AuditLogsPage and FeeReminder.rdlc. Kept the old billing-cleanup page pending workflow assessment.
- Removed the retired group report's constructors, placement filters, hidden results grid, selection actions, messages and page title. Retained Include Siblings, optional dates, statement export, original receipt views, payment review and recovery. Replaced the hidden grid's name storage with an ordinary dictionary; verified GetStudentFamily's live result includes StudentName. Contextual student preload still verifies the selected ID and rejects stale responses.
- Removed two uncalled legacy statement helpers. Kept annual receipts and outstanding-bill shortcuts, Collection History and the aging report account link: these are active features, not confirmed dead code.
- Updated account/statement tests, help, the FAQ inventory and the account decision document. FAQ-047 retains its stable ID and now describes Include Siblings. FAQ-075 points to relevant business histories. Corrected an existing help-test assumption about sidebar navigation to match the account menu.
- Removed the obsolete project exclusion for the deleted FeeReminder source; retained stale-output template deletion for older builds.

Validation: the application and native test project build successfully. The native test project reports NU1900 because NuGet vulnerability data could not be fetched. Focused native runs passed 55 statement checks and 65 account/permissions/layout/recovery checks. Offline help passed 1,922 checks across 126 questions and 15 categories. These tests use synthetic data and did not mutate the school database.

Remaining SQL work: no bulk audit rewrite was performed. AGENTS.md explicitly says to retire generic auditing when revising a procedure and not sweep unrelated procedures. The live fee-history reader still requires old audit tables/functions. A later procedure revision must include a numbered SQL migration; no migration was needed for this source-only cleanup. The old billing-cleanup workflow and optional dialog consolidation remain review candidates.

## Original review scope and verification limits

Review only: no application code, database objects, or existing files were removed or changed during this audit. This report is the proposed cleanup inventory.

Surveyed 410 C# files across Pages, Core and Services plus startup/database code, dashboard and Reports navigation, 89 explicit ShowDialog call sites under Pages, report templates and project packaging, numbered migrations 20–58, and the 279 stored procedures in fmsc.dacpac. Reference searches covered production callers, test references, help text and reflective construction patterns. Counts describe static searches, not executed workflows. Generated output, historical audit documents and tests are not presumed redundant merely because they are outside normal application execution.

Application verification: `dotnet build FMSC_SMS.csproj --no-restore -v minimal` succeeded with zero warnings and zero errors. No interactive workflow or destructive/database test was run. The read-only connection attempt to `(localdb)\MSSQLLocalDB`, database FMSC_DB, timed out. Consequently SQL findings below describe the checked-in DACPAC/source, not a verified inventory of the currently installed database. No database was attached or restored. Supplied release MDF/LDF files were not requested or assessed.

## Confirmed source cleanup candidates

These types have no production construction/reference outside their own implementation (the audit viewer additionally has a help-source reference). No production reflection-based page loader was found. Remove their stale references and validate the surviving flows together with any eventual deletion.

| Candidate | Evidence / proposed treatment |
|---|---|
| `Pages/Reports/Students/AdmissionsReportPage.cs` | Compatibility wrapper around StudentReportsPage admissions preset; no remaining caller. Remove wrapper, retain preset. |
| `Pages/Reports/Students/CurrentStudentsReportPage.cs` | Same, current-student list preset. |
| `Pages/Reports/Students/StudentMovementReportPage.cs` | Same, movement preset. |
| `Pages/Reports/Students/StudentStrengthReportPage.cs` | Same, strength preset. |
| `Pages/Security/ChangePasswordHostPage.cs` | Unused intermediate page whose only action opens ChangePasswordForm. Dashboard already opens ChangePasswordForm directly. Remove host only. |
| `Pages/Staff/CurrentEmploymentPanel.cs` | Unreferenced editable employment panel. StaffEmploymentPage remains a separate caller of SaveStaffEmployment. Remove unused UI only; keep employment records/procedures. |
| `Pages/Administration/AuditLogsPage.cs` | No navigation/construction caller. Page still reads GetAuditLogs and invokes PruneAuditTrail. Remove orphan viewer and revise FAQ-075, which currently directs users to a nonexistent System Maintenance → Audit Logs destination. Do not infer that its underlying tables are unused. |
| `Pages/Administration/MigrationBillingCleanupPage.cs` | Unlinked old setup page using GetMigrationBillingCleanupStatus, PreviewMigrationBillingCleanup and RunMigrationBillingCleanup. Candidate UI deletion only after checking the intended imported-student setup workflow. Student Data Review still opens LegacyGoLiveDialog; this does not prove the two operations are interchangeable. Do not drop cleanup procedures/markers based solely on the missing page route. |
| `Pages/Reports/Templates/FeeReminder.rdlc` | Tracked legacy template with no C# reference found. Explicitly excluded from template copying and deleted from output by RemoveRetiredFeeReportTemplates in FMSC_SMS.csproj. Candidate source deletion; retain stale-output cleanup target. |

## Incomplete retirement of Family / Group Statements

The menu entry and factory/opening method are gone, but StudentAccountStatementPage still has groupMode constructors/branches, placement lookups, result grid, selection actions and group-specific messages. Core/PageTitles.cs retains FamilyGroupStatements. Production construction found in CreateAccount and FeeHistoryDialog does not enable groupMode.

Tests/ThemeSmokeChecks/Program.FeeAccount.cs and Program.StudentAccountFlow.cs still assert that the retired report is available. Docs/Decisions/STUDENT_FEE_ACCOUNT_WORKSPACE.md still describes group-report shortcuts and group reporting as live dependencies. These are confirmed stale expectations, independent of the build passing.

Proposed cleanup: remove the unreachable group-only UI and constructor option; update affected tests, titles and decision documentation. Preserve the statement reader, export, dates and the familySelections/GetSelectedAdmissionIds functionality needed by the still-supported Include Siblings flow. Do not delete shared selection methods simply because their names mention family or groups.

## Remaining generic audit writers in the packaged database

The DACPAC contains audit-write code in these 19 procedures, plus dbo.WriteAuditLog itself. Searches of the numbered migrations found no replacement procedure definitions for these names. A live sys.sql_modules inventory is still required before calling this an installed-database inventory.

- comm.SaveRolePermissions
- dbo.AddStudentFeeAssignment
- dbo.AdmitHistoricalStudent
- dbo.ConfirmStudentPlacement
- dbo.CorrectEnrollmentStatus
- dbo.CorrectStudentStatus
- dbo.CreateLeaveCertificate
- dbo.CreateReviewedLeaveCertificate
- dbo.FinishLegacyAdmissionSetup
- dbo.ReconcileEnrollmentDates
- dbo.RepairLeaveCertificateEnrollmentLinks
- dbo.RepairLifecycleDates
- dbo.ReverseBillingCharge
- dbo.ReverseMonthlyFees
- dbo.SaveRolePermissions
- dbo.SaveStudentDetails
- dbo.SaveStudentDetailsWithFirstEnrollment
- dbo.SaveUser
- dbo.TransferEnrollmentClassSection

Services/Communication/Schema.sql also retains the generic audit write and audit-only old/new permission snapshots in comm.SaveRolePermissions (around lines 492–499). The file is still embedded and read by CommunicationSchemaContract to identify required APIs, so deleting Schema.sql would break runtime verification. It should not be mistaken for an unneeded historical script. Not all JSON in that script is auditing: recipient parameters, for example, are operational.

Existing repository policy explicitly requires gradual audit retirement when a procedure is revised and forbids a separate sweep through unrelated procedures. Therefore this inventory is not a recommendation to rewrite all 19 immediately. Any approved SQL change must be delivered as a versioned Migrations script, preserving permission checks, transactions, results and operational records.

### Dependency preventing wholesale audit-object deletion

Pages/Reports/Finance/StudentFeeChangeHistory.cs actively reads dbo.AuditLogs, dbo.AuditOperations and dbo.AuditValueLeaves. Its report is opened from Student Profile, transfer/readmission and Student Fee Account. Removing audit tables/functions now would break this live feature. Before removing them, decide how to retain meaningful historical fee-change evidence and incomplete-history disclosures. Retiring future generic writes does not authorize deleting historical records.

Keep lifecycle/enrollment history, certificate issue/collection records, bills, payments, allocations, credits, migration versions, mutation/request markers and recovery journals. Communication delivery/consent/approval history and comm.Audit require separate assessment; their name alone is insufficient evidence that they belong to the retired generic audit system.

## Report openings and dialogs

| Opening | Assessment |
|---|---|
| Fee Collection → History → FeeHistoryDialog → StudentAccountStatementPage | Strong simplification candidate: opens another statement interface while Student Fee Account already exists. Prefer one contextual route to the account workspace if approved. Preserve access for collection operators, preselected student, busy state and return-to-collection context. Current wrapper is an active dependency, not a dead file. |
| OutstandingAgingReportPage → selected student → full Student Fee Account dialog | Active duplicate opening of the account workspace. Consider navigation to the existing workspace with student preselected. Preserve aging filters and selected result on return; do not just remove the useful student-account link. |
| Student Fee Account → Reports → Annual Receipts | Optional shortcut removal if the owner wants a smaller menu. The report is single-student/year-specific and has a main Reports entry; it is not functionally identical to a date-filtered statement. Keep the report itself. |
| Student Fee Account → Reports → Outstanding Bills | Calls OutstandingBillsPreview directly; not another student-selection form. A useful contextual print action. No evidence of redundant intermediate screen here. |
| Fee Change History in profile, lifecycle and account | Same shared implementation entered with useful student context. Multiple buttons do not imply duplicate implementations. It also has the audit-data dependency noted above. |
| Account payment review, save recovery, original receipt details | Separate correction, uncertain-save recovery and original-document workflows. Retain their close/busy guards and original payment/document context. |
| Transfer Section/Class/Campus and Re-Admit dialogs | Factory-created in StudentEnrollmentActionDialog.cs. Initial type scan flagged them because references occur in the same file; manual follow-up confirmed callers. Retain. |
| Certificate, clearance, backup/restore and placement decision dialogs | Do not remove merely to reduce dialog count: these contain distinct choices, previews or protected operations. No confirmed redundant intermediate opening established by this static review. |

## Proposed order

1. Complete group-report retirement and repair stale tests/help/decision references.
2. Remove confirmed unused wrappers/host/panel/viewer/template, then build and run focused navigation, help, staff and reporting checks.
3. Review old billing-cleanup UI against the supported migration/setup workflow before deletion.
4. If approved, consolidate Collection History and the aging report's account opening while preserving access and return context.
5. Verify installed SQL metadata; retire generic audit writes as affected procedures are revised, using numbered migrations. Plan a replacement for the fee-history audit reader before any table/function retirement.

No recommendation here authorizes deleting business history, migration scripts, the DACPAC, schema-contract resources, recovery facilities or historical audit documentation. This review identifies source redundancy and UI simplification candidates; it does not certify every database procedure or interactive path as unused or correct.
