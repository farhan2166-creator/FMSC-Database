# Student Certificates Workspace — Stage 1 design and reuse review
Date: 2026-11-03
Base commit: `110de257b93ee897637849b7fbc09f970d2bffb1`
Status: **PROPOSAL — requires owner approval before implementation**

## Intent and boundaries

Provide a **single-student context** for the eight student certificate types. A user selects a student once, views that student's actual saved certificate copies, opens an exact copy for its existing editing/printing/collection/reissue workflow, and starts another certificate type without repeating a student lookup.

**Keep all current certificate pages and the original Student Certificates navigation functional until explicit owner acceptance and a separate retirement step.** This Stage 1 branch does not authorize live DB changes, certificate data writes, migrations, SQL procedure changes, template changes, or deletion of legacy UI. The conceptual image is an interface direction, not evidence of new fields, printed status, or eligibility rules.

## Verified existing reuse map

| Responsibility | Existing source | Keep/reuse |
| --- | --- | --- |
| Hub and list of eight types | `Pages/Students/Certificates/StudentCertificatesPage.cs` | Reuse type catalog/permission intent; keep current hub while piloting |
| Students and selected identity | `Core/StudentPicker.cs`, `StudentSearchCache`, `GetStudentDetails` | One workspace student selector; no separate student table |
| Ordinary vs leaving access | `Core/CertificateWorkspaceAccess.cs` | `Students.View + Certificates.Create` or `Students.View + Students.Leave` |
| Shared form UI, print, navigation | `Core/CertificateUi.cs`, `Core/CertificateButtonRow.cs`, `Core/CertificateRecordOrder.cs` | Keep current form/print/focus mechanics |
| Physical collection/linked copy | `Core/CertificateCollectionFlow.cs`, `Core/CertificateCollectionDialog.cs` | Printing never collects; collected copies remain locked |
| Reissue and durable recovery | `Core/CertificateReissueFlow.cs`, existing mutation/recovery utilities | Preserve version/hash checks, intent identity, distinct copies |
| Cross-type report precedent | `Pages/Reports/Certificates/CertificateIssueRegisterData.cs` and `CertificateIssueRegisterPage.cs` | Reuse type naming and status semantics as reference, **not** report permissions or its display rows as actionable record IDs |
| Existing document output | `Pages/Reports/Templates/*Certificate.rdlc`, `Get*CertificatePrintData`, current page print methods | No new PDF/preview renderer |

Current type-specific editors and their saved-record reads:

| Type | Existing page | Existing reader | Saved-record identity |
| --- | --- | --- | --- |
| Regular Student | `RegularStudentCertificatePage` | `GetRegularStudentCertificateRecords` | `RegularStudentCertificateId` + `RegularStudentCertificateIssueId` |
| Character | `CharacterCertificatePage` | `GetCharacterCertificateRecords` | `CertificateRecordId` + `CharacterCertificateIssueId` |
| Leave | `LeaveCertificatePage` | `GetLeaveCertificateRecords` | `LeaveRecordId` + `LeaveCertificateIssueId`; School type only |
| College Leaving | `CollegeLeaveCertificatePage` | `GetLeaveCertificateRecords` | `LeaveRecordId` + `LeaveCertificateIssueId`; College type only |
| Date of Birth | `DateOfBirthCertificatePage` | `GetDateOfBirthCertificateRecords` | `CertificateRecordId` + `DateOfBirthCertificateIssueId` |
| Sports | `SportsCertificatePage` | `GetSportsCertificateRecords` | `SportsCertificateId` |
| No Objection | `NoObjectionCertificatePage` | `GetNoObjectionCertificateRecords` | `NoObjectionCertificateId` |
| Provisional | `ProvisionalCertificatePage` | `GetProvisionalCertificateRecords` | `ProvisionalCertificateId` |

The existing print pipelines and selected-record editors are authoritative. In particular, **record IDs and issue/copy IDs are not interchangeable**. Some types have a separate issue table and some have copy identity on the certificate row.

## Important technical findings

1. `StudentPicker.SetStudent(int,string)` and `SetStudent(DataRow)` intentionally **do not raise** `UserSelectionChanged`. Most certificate pages currently depend on this event to run `StudentChanged` and load history. Setting the picker text/ID alone would leave their state stale. A deliberate, testable context load method is required in each affected editor. The Leave page already has an example of preselecting a student and then explicitly calling its existing change pathway.
2. The general-purpose Certificate Issue Register combines student **and staff** certificates and its `BuildQuery` requires `Reports.View`. The new *student workspace* must not elevate permissions or require `Reports.View` from users who can already work with certificates. The displayed report fields also omit the underlying record/issue IDs needed to open an exact saved copy. Do not use textual certificate number/search as identity.
3. Print is an action, not reliable persisted proof of printing. Do **not** add a generic `Printed` badge or imply that printing creates a separate issue. Use only saved/issued/collected states genuinely recorded by the source for that type.
4. Avoid synthetic `Fees Clear`, `Eligible`, or activity timeline labels without an authoritative existing reader/rule.
5. `Certificates.Print` is separate from `Certificates.Create`. Leaving-only users must see only leaving types and copies. Individual existing SQL procedures continue to authorize reads/writes.

## Proposed operator layout

1. **Header:** one `StudentPicker` plus factual identity card (admission no., name, father, campus/class/section, enrollment status). No synthesized financial/eligibility badge.
2. **Default center:** **Issued Certificates** grid, sorted latest first, showing type, actual certificate date, saved/issue identity or copy reference, actual collection information, and one **Open** action. Filters are local to the selected student's returned records.
3. **Left/compact selector:** **Prepare Certificate** with exactly the eight existing student types, filtered by current permissions. Choosing a type keeps the selected student and opens the existing editor bound to that student.
4. **Selected details / actions:** when a saved row is chosen, show available recorded details, then **Open** the type-specific existing editor with the **exact saved row/copy selected**. The editor remains responsible for Print, Save Changes, Issue Again, Mark Collected, and its own protections. Do not implement these commands a second time in the shell.
5. **History:** the default register is already the cross-type saved-copy history; the existing per-type editor retains detailed history. Avoid a separate invented timeline or duplicate History tab until it has a distinct purpose.
6. **Preview:** initial implementation uses the existing Print Preview in the selected editor. An always-visible miniature preview is **deferred** unless existing report infrastructure can render the exact saved copy without a duplicate print/report path. Do not show a decorative fake certificate.
7. **Small windows:** keep the register primary and display details/editor in a full-width view or contextual window. Do not compress all columns until unreadable. Follow existing themes, scaling and close/navigation guards.

Recommended first shippable interaction:

`Select Student -> View Saved Copies -> Open Exact Copy in Existing Editor -> Print / Reprint / Mark Collected / Issue Again`

Or:

`Select Student -> Choose Certificate Type -> Open Existing Editor Preselected -> Create / Save / Print / Collect`

Return from a changed editor: refresh only the selected student's register, preserve the selected type/row if the exact ID still exists, and otherwise fall back to the newest applicable row.

## Smallest viable integration design

- **Workspace screen:** a new student-centric orchestration page (proposed `Pages/Students/Certificates/StudentCertificatesWorkspacePage.cs`), separate from the old hub initially. Reuse `V2Page`, HumanUi, StudentPicker, existing page navigation/close guards.
- **Fixed type catalog:** a small descriptor with title, type key, permission and editor factory. Keep it tied to the existing eight types; **never** add staff certificates to a student record.
- **Identity-bearing register reader:** a read-only aggregator that calls the **existing student-specific `Get*CertificateRecords` readers** only for permitted types, maps their row keys to a common discriminated record descriptor, and retains raw identity (type, record ID, optional issue ID). Preserve chronological saved histories and explicit no-issue rows. Verify each reader's shape/permissions and leave type discriminator with disposable fixture SQL before shipping. No writes. Do not silently convert read failures into an empty "no certificates" result.
- **Editor bridge:** implement an explicit API on each existing page, for example `LoadWorkspaceSelectionAsync(studentRow, optionalSavedRecordKey)` (exact signature to be chosen during coding). It must reuse the same `StudentChanged`, `LoadCertificates`, and `SelectSavedCertificate`/`SelectCertificate` paths as normal operator selection; it must not fake keyboard/mouse events, reflect into private controls, or just call `SetStudent`. Handle async lookups, session versioning, exact copy selection, and cases where a saved copy no longer exists.
- **Guarding:** the workspace cannot switch students/types/copies while an embedded editor is busy or has unsaved changes without consulting its existing `IUnsavedChanges`/`IBusyPage` behavior. Never discard an uncertain-save recovery marker. Validate current user/permission on each dispatch and during async return.
- **Navigation:** keep `MainDashboardForm.cs` opening the current hub. Pilot workspace can be opened through a temporary permission-gated test entry or dedicated test host without introducing a second permanent Certificates item. **No existing navigation switch until acceptance.**

Prefer a contextual existing editor in the first slice if embedding it would duplicate selection controls or break page lifetime/focus/guard behavior. Convert to embedded detail view only after focused native UI tests support it.

## Implementation slices after design approval

1. Add a **non-destructive** workspace shell with one student picker, context card, and permission-filtered type list; cover student switching, loading, and empty state.
2. Add the cross-type **identity-bearing read-only** register using existing readers; prove row-to-subject and row-to-copy mapping with synthetic fixtures and real disposable SQL where available.
3. Add preselection bridge to each of the eight existing forms, preserving their normal manual selection pathways and lock/print/reissue logic.
4. Add contextual opening and return refresh. Adapt narrow-resolution layout and focus/keyboard behavior.
5. Add/extend `Tests/ThemeSmokeChecks` coverage; run the existing certificate workflow and reissue tests. Verify leaving-only and create-only/print-only restrictions, stale reads, copied snapshots, and busy/unsaved recovery.
6. Owner/operator inspection and acceptance. **Only then**, in a separate approved cleanup, reroute the normal sidebar destination and remove obsolete *navigation-only* UI. Never delete editors, stored procedures, RDLCs, or history merely because the old hub is retired.

## Proposed change list (implementation only, not changed during Stage 1)

- Add: `Pages/Students/Certificates/StudentCertificatesWorkspacePage.cs`
- Add: `Pages/Students/Certificates/StudentCertificateRegisterReader.cs` (or equivalent small read-only service)
- Add: `Pages/Students/Certificates/CertificateWorkspaceEditorContext.cs` (or equivalent interface)
- Adapt: each of the eight student certificate pages with minimal explicit student/copy context entry point
- Adapt: certificate navigation/tests only for **temporary** pilot, preserving existing default
- Add/adapt: `Tests/ThemeSmokeChecks` focused workspace/identity/permission/guard fixtures
- Potentially adapt: existing report data helper only if **shared projections** can be extracted without extending report permissions; no alteration to its semantics
- Leave alone: `Migrations/*`, `fmsc.dacpac`, installed DB, `Pages/Reports/Templates/*`, staff certificate forms, existing certificate save/print/collection/reissue writers

## Acceptance gates

- Every saved row is scoped by exact `AdmissionId`; switching student clears stale data synchronously and invalidates outstanding fetches.
- Every row opens the *same saved copy* (correct type+record+issue identity), including old copies and school-versus-college leaving.
- All eight types create/print/collect/reissue with their original business rules. There is no generic replacement save or print handler.
- Collected copies remain immutable; Print does not collect; Issue Again creates a separate permitted copy with original retained.
- Report-only permission is **not** added to certificate users; returning to the workspace cannot expose forbidden types.
- Unsaved drafts and unresolved mutations remain protected during navigation and close.
- Existing navigation and old pages function until the owner authorizes removal.
- Native tests and disposable SQL checks pass; real Windows manual acceptance is explicitly recorded before rollout.

## Request for design approval

Proposed first delivery is a **single-student register plus existing type editors opened in exact-copy context**. This deliberately favors correctness and reuse over implementing the concept image's multiple tabs, omnipresent PDF preview, and duplicate action buttons literally.

After owner approval, implementation can proceed on this feature branch. No existing certificate UI is retired by this plan.
