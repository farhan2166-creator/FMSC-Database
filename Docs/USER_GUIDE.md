# FMSC_SMS — Operator User Guide

**Applies to:** The current .NET 8 Windows application. Menu visibility depends on the permissions assigned to the signed-in user; a missing action may be access-controlled rather than broken.

## Start the application

1. Open the installed FMSC_SMS shortcut or executable.
2. If the database connection screen appears, wait for it to confirm the correct school data. If it reports an error, follow [connection recovery](BACKUP_RECOVERY.md#when-the-app-cannot-connect); do not create a replacement database.
3. Sign in using your authorized account. A first-run setup or required password-change prompt must be completed by the appropriate administrator.
4. Open the relevant workspace from the dashboard or navigation. Use the navigation groups below rather than editing database files manually.

## Common workspaces

| Work | Where to start | Operational caution |
|---|---|---|
| New student | **Daily Work → Student Admissions** | Check student, placement, guardian, date, and opening charges before saving |
| Provisional registration | **Dashboard → Quick Registration** | Pending records have a temporary reference; an official admission/registration number is assigned only upon confirmation |
| Student history and family | **Student Profile / Student Management** | Verify the selected student before editing or printing |
| Student leave/transfer/re-admission | **Student Management → Transfers / Re-Admission** | Preserve history; do not treat leaving as deleting the student |
| Fee configuration | **Fees & Billing → Fee Rate Setup** | Rates affect future billing; use approved whole-rupee rules |
| Post fees or corrections | **Fees & Billing → Fee Posting / Student Fee Corrections** | Verify class/academic year/student and review existing postings before repeating |
| Receive a payment | **Daily Work → Fee Collection** | Reconcile an uncertain save outcome before attempting another payment |
| School or financial reports | **Reports** | Confirm student, time range, report filters and selected printer/export path |
| Staff and certificates | **Staff / Student Certificates** | Finish or intentionally discard edits before switching records |
| Back up and restore | **System Maintenance → Backup / Restore** | Restore is restricted and can replace newer records; see [recovery guide](BACKUP_RECOVERY.md) |

Screen naming and source mapping: [PROJECT_STRUCTURE.md](../PROJECT_STRUCTURE.md).

## Recommended daily routine

1. Sign in and confirm the right academic year, campus, student and current context before a write.
2. Search before creating a student or payment to avoid duplicate work.
3. In admissions, save and check the returned status; for a delayed or disconnected response, use the workflow's **Check Save Outcome** or recovery guidance where offered rather than blind resubmission.
4. In fee collection, posting, adjustments or certificate creation, **do not click Save repeatedly** if the outcome is unknown. Let the application reconcile first. Seek an administrator if the outcome cannot be confirmed.
5. After finishing a transaction, verify the resulting record, receipt or posted balance.
6. Before finishing for the day, confirm a current, verified backup is stored at an approved location. Follow school policy for off-computer backup copies.

## Admissions and Quick Registration

**Direct admission:** Student details and placement are handled in Admissions. Valid opening fees are posted as part of the save operation. A legacy-record verification / Go Live sequence applies to historical data and has separate safety checks.

**Quick Registration:** Creates a pending student with temporary reference and reviewed opening charges. The **Registration Form** supports editing pending details; **Pending Registrations** supports searching and reviewing. Confirmation allocates official admission and registration numbers. Pending students are excluded from normal monthly fee posting, promotion and official certificates. Existing paid charges must not be casually reversed or reposted.

For business-rule context see [ADMISSION_WORKFLOW.md](../ADMISSION_WORKFLOW.md). The current runtime requires schema 6 even if historical text in that file refers to older database versions.

## Unsaved edits and unresolved transactions

- If **Stay / Discard** is offered, select **Stay** to keep the current unsaved draft.
- If an operation is busy, wait; the app intentionally blocks navigation during several write flows.
- If a save times out, disconnects or gives uncertain feedback, **do not create a new transaction to test whether it worked**. Use the page's reconciliation action, then ask the administrator if necessary.
- A printed report or exported CSV/PDF is a copy of selected data; verify filters and avoid sharing student or financial documents with unauthorized recipients.

## Roles, personal data and help

Administrators maintain user accounts and permissions. General tasks require role-specific access; do not use another employee's login to bypass permissions.

Never email or commit the live `FMSC_DB.mdf`, `FMSC_DB_log.ldf`, `.bak` backups, or student reports to a public repository or support ticket. When reporting a bug, record the screen, steps, expected/actual result, approximate time and **sanitized** screenshot/log only.

For installation and recovery issues, use [installation](INSTALLATION.md) and [backup/recovery](BACKUP_RECOVERY.md).
