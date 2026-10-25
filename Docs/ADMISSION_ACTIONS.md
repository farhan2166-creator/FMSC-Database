# Admission action simplification

Based on main `7ce4ae5`.

## Scope and database contracts

This is a C# navigation/dialog change. No SQL, numbered migration, DACPAC,
compatibility requirement, fee calculation or financial writer changes.

The older DACPAC is not assumed to be the newest procedure definition. Reviewed
the relevant migration contracts through the current migration set (48):

- Migration 29 ActivateStudentBilling requires LegacySetupPending, existing
  Students.Administer and Fees.Adjust permissions, and the existing preview and
  snapshot. Only the activation entry opens this dialog.
- Historical placement uses the existing embedded PlacementCorrection.sql and
  MissingPlacementWindows.sql policy. It preserves current status and creates no
  fees. Placement History opens directly even for a legacy student.
- Existing draft completion uses that same restricted correction editor and
  StudentPlacementConfirmation's existing ConfirmStudentPlacement review/apply
  flow. Confirm is in the editor instead of an unreachable menu entry; saved corrections reload before
  confirmation becomes available. Legacy, failed-load, already-confirmed and
  dirty-editor states cannot use it. Historical creation keeps its prior close
  behavior; it does not invoke activation or confirmation of a current student.
  Existing drafts open with their recorded start date, so reloading does not
  silently propose moving a saved mid-year placement to the academic-year start.
- Migrations 30/31 retain independent hold episodes, resume review, existing dues
  and the refusal of readmission while a hold exists. Departure retains the
  CloseStudentAdmission review, billing policy and paid-reduction choice.
- Transfers retain their original operation strings and dialogs, including the
  reviewed fee treatment in migration 44. They are grouped in one submenu.

## Interface

Actions offers Activate Current Student for legacy setup, Complete Placement for
an ordinary draft, and an independent Placement History browser. Ordinary active
students see Hold, Departure and permitted transfers; a held student sees Resume
and no competing readmission. Former students retain history and eligible
readmission. The exceptional Left-to-Hold adoption is under Corrections / Advanced.
Confirm Return remains the existing contextual toolbar action for a promoted
student whose actual return awaits confirmation.

The action-state model controls navigation only. Existing SQL and dialog rules
remain authoritative. Permissions are reevaluated when the menu opens. Pending
registrations and unsaved students have guidance instead of saved-student actions.
Busy/unknown-save states and unresolved placement metadata disable invocation.
Unsaved-detail checks still run in handlers. No student is activated, held,
departed, moved or billed merely by selecting/opening a menu.

## Targeted verification

```sh
dotnet run --project Tests/AdmissionActionsChecks/AdmissionActionsChecks.csproj -c Release
```

25 portable action-state checks pass. They exercise current/former/held/adopted,
legacy/draft/loading/busy/pending, permission combinations and status casing.

Windows-native controls (no database connection):

```powershell
dotnet run --project Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -c Release -- --admission-actions
```

The native runner checks actual menu groups and contextual availability, the
direct draft editor and loaded/legacy/confirmed confirmation guards. It can be
compiled on Linux; actual Windows execution and visual/printing acceptance remain
pending. No broad regression suite or school database is used for this change.
