# FMSC_SMS — Operator User Guide

**Applies to:** The current .NET 8 Windows application. Menu visibility depends on the permissions assigned to the signed-in user; a missing action may be access-controlled rather than broken.

## Start the application

1. Open the installed FMSC_SMS shortcut or executable.
2. If the database connection screen appears, wait for it to confirm the correct school data. If it reports an error, follow [connection recovery](BACKUP_RECOVERY.md#when-the-app-cannot-connect); do not create a replacement database.
3. Sign in using your authorized account. A first-run setup or required password-change prompt must be completed by the appropriate administrator.
4. Open the relevant workspace from the dashboard or navigation. Ctrl+K searches all destinations your account can open, including collapsed and compact sidebar entries. Use the navigation groups below rather than editing database files manually.

## Communication Center

Open Communication Center if your account has access. Start with a reviewed template and saved guardian/staff contacts whose ownership and consent have been verified. A student record with a phone number is not automatically a communication contact. Empty lists explain whether to widen filters, add contacts through Contacts / Consent, review a template or ask an administrator.

Compose / Bulk follows **choose template → find/select contacts → preview → create draft**. Then use Outbox / Schedule / History to review and submit; approval may be required. Draft creation and provider acceptance do not mean delivery. Each tab shows its purpose and next step. Changing filters requires a new search; New Message clears the previous recipients, text and schedule. If a save succeeds but its list refresh fails, use Refresh rather than repeating the saved action. Provider accounts, live-mode approval and a running dispatcher are still required for external delivery; see [Communication Center setup](COMMUNICATION_CENTER.md).

## Display scaling and smaller screens

Use Windows display scaling for readable text. The application follows the monitor's DPI and usable desktop area. On smaller screens, dialog content may scroll; use the scrollbars or Tab to reach fields and bottom actions. Enlarging a window gives grids and flexible content more space while standard entry controls retain sensible dimensions. Printing and report zoom controls retain their existing behavior.

Result messages stay within two font-scaled lines so feedback cannot displace the workspace. Hover over the status text for full details. A warning or unknown save outcome still requires its stated recovery checks before retrying.

Report previews keep the window title above the print toolbar. Student Admissions uses a compact finder and caption-sized actions so Print stays with the other actions when there is room; narrower windows still wrap whole buttons.

Admission's **Save & New** button is temporarily hidden to give Print more space. Save the student with **Save**, then use **Clear / New** to start the next admission; **Keep placement** retains the selected campus, year, class and section.

Confirmation dialogs show the question first, then important consequences and the details to review. Yes/No buttons are centered. Family payments and matching student records use read-only tables; scroll through long details before confirming. The payment counter keeps its approved Yes default, while restore and duplicate-student confirmations default to No. Enter uses the selected default; Escape declines a Yes/No question.

## Family counter: returning students and new siblings

In **Fee Collection**, find any child and open **Student Fees → Family / Returns**. The existing Family / Siblings window shows each child's year, placement and readiness. Select an **Awaiting Return** child and choose **Confirm Selected Return**. Review that child's date, transport and opening fees in the existing dialog. After confirmation, readiness refreshes and the next waiting child is selected; they are not confirmed automatically. Pending new registrations use their own registration review, not return confirmation.

Close the family window to reload the selected student's placement and fees. Tick the siblings to include, then use **Student Fees → Print Outstanding Bill**. The bill includes all posted unpaid charges for those children, including older periods. It is separate from **Print Receipt**, which follows a saved payment. Finish or clear an entered payment draft before opening Family / Returns; unsaved discount changes must be reviewed before printing a saved-charge bill.

For a new sibling, continue using **Quick Registration → Link Existing Parent**. Saving that child does not confirm the other children's return. Printing warns if selected family members still await return or placement review. You can decline and finish the return reviews first, or explicitly print only charges already posted. A zero balance or missing bill line does not prove a child has completed return admission.

## Entry and search actions

On shared record-entry pages, **New Record** clears entry fields and starts a new record. It deliberately discards unsaved entry edits. **Clear Search** resets the list search without clearing the entry. Ctrl+N keeps its existing New Record behavior.

In Setup Readiness, select an issue and use **Open Selected Setup**. The action is disabled while checking or when your account cannot open that destination; the status line explains when administrator help is needed. Double-click follows the same checks.

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

Screen naming and source mapping: [PROJECT_STRUCTURE.md](Development/PROJECT_STRUCTURE.md).

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

Admission saves hold the submitted details until saving and refresh finish. For **Mark as Left / Cancel Registration** or **Legacy Go Live**, Review holds the inputs; changing them after review requires another review. Wait for a pending operation to finish before closing.

For business-rule context see [ADMISSION_WORKFLOW.md](Workflows/ADMISSION_WORKFLOW.md). The current runtime requires schema 6 even if historical text in that file refers to older database versions.

## Find Student

Typing an admission number clears Campus, Academic Year, Class and Status. Changing any of those filters clears the admission number while keeping the other filters. Either change clears old results and disables **Open Student** until you click **Search** again. Student/father name searches can still be combined with filters. **Reset** clears both modes.

## Student Promotion

Open **Student Promotion** (previously Bulk Promotion) to choose the source and destination placement, review candidates and process ticked students. **View Promotion History**, at the top right of Promotion Details, opens completed batches in a separate window using the selected source year, class and section. Blank source filters show all history. You can change the history filters and use **Load**, or clear them to review other batches. Closing the window keeps your promotion selections and remarks intact.

Promotion History is reached from this button rather than a separate navigation item. Student Promotion retains the existing Enrollment Management permission; viewing history also requires the existing report-viewing permission enforced by SQL. Administrator retains full access. The history button is unavailable while candidates are loading or promotions are processing. After promoting students, continue to Confirm Return Admission as before.

## Unsaved edits and unresolved transactions

- If **Stay / Discard** is offered, select **Stay** to keep the current unsaved draft.
- Small Classes & Sections drafts can be left without a discard prompt; active saves must finish. Bulk fee-rate adjustments are protected when leaving the finance workspace, and a successful apply starts a clean baseline.
- New sports/provisional certificate wording and result details use the existing discard guards. Opening an untouched dialog or changing only its new default date remains freely abandonable.
- Certificate creation holds inputs through saving and refresh, refuses a second submission, and preserves the returned result message.
- Admission and fee collection hold submitted inputs until the pending write and refresh finish. Correct invalid entries before submission; failed saves restore editing.
- After a successful admission save and refresh, **Mark as Left** and **Correct Placement** can be opened immediately. Save any subsequent detail edits or pending photo first; an incomplete refresh still requires reopening the student as instructed on screen.
- If a save times out, disconnects or gives uncertain feedback, **do not create a new transaction to test whether it worked**. Use the page's reconciliation action, then ask the administrator if necessary.
- A printed report or exported CSV/PDF is a copy of selected data; verify filters and avoid sharing student or financial documents with unauthorized recipients.

## Student Account Statement and annual receipts

Open **Reports > Student Account Statement**, then choose a student by admission number or name. Their account loads without an Include checkbox. The full-width details show fee charges, allocated payments and remaining amounts; the summary shows opening balance, charges, payments and closing balance. Leave the optional dates unchecked for the complete account, or choose a period and click **View Statement**. Changing a student or period makes the old export unavailable until the statement is refreshed.

Choose **Annual Receipts** to open the existing yearly receipt sheet for that student. Select the academic year inside that window; this is a receipt printout, while the statement explains charges and balances. The separate Reports tile has been consolidated into the statement action; the Student Profile receipt shortcut remains available. Existing statement permission is required. When exactly one student is selected in group mode, its explicit academic-year filter is carried into the receipts window; an unavailable year must be chosen again.

For several students, switch on **Group / family statements**, use the placement filters and **Find Students**, then tick **Include** or **Include Family** and click **View Statement**. Existing group selection, due printing and exports remain available. Switching modes clears every selected student and family, including selections outside the visible results, while retaining the chosen date period.

## Family / Siblings from Student Profile

Select a student in **Student Profile**, then choose **Open Family / Siblings**. The existing family workspace opens in an owned window for that student. Close it to return to the same profile; the profile refreshes to show changed family links. The window cannot close during a pending link/unlink operation. Family viewing and student-administration permissions still govern viewing and changes separately. Normal sidebar navigation uses Student Profile; the direct family route is retained for an account with family access but no profile-viewing permission.

## Daily Collection Closing

Enter whole-rupee actual amounts. Leave Refund/Correction actuals blank and explain their impact in Notes; blank ordinary methods remain unchecked. Printed totals include recorded adjustments without inventing counted amounts. Historical stored fractions remain visible at their original precision and are not silently rounded when printing.

## Roles, personal data and help

Administrator retains full authority, and Accountant grants are unchanged. Dashboard Student Reports requires student and report access. Profile photo/family/receipt shortcuts follow their destination permissions; read-only student data review remains available with student viewing access. Disabled shortcuts explain missing access or student selection.

Administrators maintain user accounts and permissions. General tasks require role-specific access; do not use another employee's login to bypass permissions.

Never email or commit the live `FMSC_DB.mdf`, `FMSC_DB_log.ldf`, `.bak` backups, or student reports to a public repository or support ticket. When reporting a bug, record the screen, steps, expected/actual result, approximate time and **sanitized** screenshot/log only.

For installation and recovery issues, use [installation](INSTALLATION.md) and [backup/recovery](BACKUP_RECOVERY.md).

## Help for rules and unexpected results

Open **Help & FAQ** on the dashboard. Search in ordinary words, combine Topic and Rule type filters, and follow Where to go. System rules are protected; School settings have a supported authorized editor; User choices remain within permitted options. Help is offline and does not grant action access. Related questions clear the current filters to show their destination. See [Help Center instructions and acceptance limits](HELP_CENTER.md).
