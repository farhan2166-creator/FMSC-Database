# FMSC_SMS — Operator User Guide

**Applies to:** The current .NET 8 Windows application. Menu visibility depends on the permissions assigned to the signed-in user; a missing action may be access-controlled rather than broken.

## Start the application

1. Open the installed FMSC_SMS shortcut or executable.
2. If the database connection screen appears, wait for it to confirm the correct school data. If it reports an error, follow [connection recovery](BACKUP_RECOVERY.md#when-the-app-cannot-connect); do not create a replacement database.
3. Sign in using your authorized account. A first-run setup or required password-change prompt must be completed by the appropriate administrator.
4. Open the relevant workspace from the dashboard or navigation. Ctrl+K searches all destinations your account can open, including collapsed and compact sidebar entries. Use the navigation groups below rather than editing database files manually.

## Main navigation

- **Daily Work** contains six frequent shortcuts: Student Admissions, Pending
  Registrations, Student Profile, Fee Collection, Print Outstanding Bills and
  Daily Collection Closing. Billing/posting is under Fees.
- **Fees** contains Fee Rate Setup, Billing Center, Fee Collection, Print
  Outstanding Bills, Student Purchases, Student Fee Account and Daily Collection Closing.
- **Students** contains admissions, pending registrations, profiles, Student Data
  Review, student certificates, Family / Siblings and placement management.

Daily shortcuts open the same destinations as the full menus. Permissions apply
to both, and page search lists each destination once. Use **Alt+3** for Fees and
**Alt+4** for Students; **Alt+2** still opens Daily Work.

## Communication Center

Open Communication Center if your account has access. Start with a reviewed template and saved guardian/staff contacts whose ownership and consent have been verified. A student record with a phone number is not automatically a communication contact. Empty lists explain whether to widen filters, add contacts through Contacts / Consent, review a template or ask an administrator.

Compose / Bulk follows **choose template → find/select contacts → preview → create draft**. Then use Outbox / Schedule / History to review and submit; approval may be required. Draft creation and provider acceptance do not mean delivery. Each tab shows its purpose and next step. Changing filters requires a new search; New Message clears the previous recipients, text and schedule. If a save succeeds but its list refresh fails, use Refresh rather than repeating the saved action. Provider accounts, live-mode approval and a running dispatcher are still required for external delivery; see [Communication Center setup](COMMUNICATION_CENTER.md).

## Display scaling and smaller screens

Use Windows display scaling for readable text. The application follows the monitor's DPI and usable desktop area. On smaller screens, dialog content may scroll; use the scrollbars or Tab to reach fields and bottom actions. Enlarging a window gives grids and flexible content more space while standard entry controls retain sensible dimensions. Printing and report zoom controls retain their existing behavior.

Result messages stay within two font-scaled lines so feedback cannot displace the workspace. Hover over the status text for full details. A warning or unknown save outcome still requires its stated recovery checks before retrying.

Report previews keep the window title above the print toolbar. Student Admissions uses a compact finder and caption-sized actions so Print stays with the other actions when there is room; narrower windows still wrap whole buttons.

Report and bill previews use the established Windows print dialog. **Print** starts with **All**; its **Pages** fields are prefilled with the page shown in the preview (for example, **from 3 to 3**). Select **Pages** to print that page, or edit the range. **Print page N** opens the same dialog with that single-page range already selected. Copies and printer preferences remain available, while each document retains its report paper size, including each thermal bill's own length. **Cancel** sends no print job.

Admission's **Save & New** button is temporarily hidden to give Print more space. Save the student with **Save**, then use **Clear / New** to start the next admission; **Keep placement** retains the selected campus, year, class and section.

Confirmation dialogs show the question first, then important consequences and the details to review. Yes/No buttons are centered. Family payments and matching student records use read-only tables; scroll through long details before confirming. The payment counter keeps its approved Yes default, while restore and duplicate-student confirmations default to No. Enter uses the selected default; Escape declines a Yes/No question.

## Family counter: returning students and new siblings

In **Fee Collection**, the Paying For panel shows each child's status, such as **Awaiting Return**, **Return Confirmed**, **Ready for Billing** or **On Hold**. For the loaded child awaiting return after promotion, **Confirm Return** appears beneath their details. Review their billing start date, transport and opening fees in the existing dialog. Closing the dialog refreshes the account. Checked siblings are included only in the payment; each return is confirmed separately. Double-click a sibling's name to load that child's account. Pending new registrations use their own registration review.

**Student Fees → Family / Returns** also opens the existing family window, with each child's year, placement and readiness. Select an **Awaiting Return** child and choose **Confirm Selected Return**. After confirmation, the next waiting child is selected for a separate review.

Close the family window to reload the selected student's placement and fees. Tick the siblings to include, then use **Student Fees → Print Outstanding Bill**. The bill includes all posted unpaid charges for those children, including older periods. It is separate from **Print Receipt**, which follows a saved payment. Finish or clear an entered payment draft before opening Family / Returns; unsaved discount changes must be reviewed before printing a saved-charge bill.

For a new sibling, open **Daily Work → Student Admissions**, load an existing student in that family and choose **Admit Sibling**. The child editor inherits the selected student's father name, contact number, address and birthplace as read-only family details; enter the new child's own name, gender, placement and fees. The sibling is first saved as **Pending**. Open that child in Admissions (or from **Daily Work → Pending Registrations**) to finish the details and **Confirm & Save**; creating the pending sibling does not confirm any other child's return. Printing warns if selected family members still await return or placement review. You can finish those reviews first or explicitly print only charges already posted. A zero balance or missing bill line does not prove that return admission is complete.

## Entry and search actions

On shared record-entry pages, **New Record** clears entry fields and starts a new record. It deliberately discards unsaved entry edits. **Clear Search** resets the list search without clearing the entry. Ctrl+N keeps its existing New Record behavior.

In Setup Readiness, select an issue and use **Open Selected Setup**. The action is disabled while checking or when your account cannot open that destination; the status line explains when administrator help is needed. Double-click follows the same checks.

## Common workspaces

In Student Admissions, **Actions** shows choices for the selected student's state:

- **Activate Current Student…** reviews a legacy student's current placement,
  fees and billing start through the established activation dialog.
- **Complete Placement…** opens an existing unconfirmed placement directly.
  Review/apply any corrections, then Confirm Placement after checking the saved
  details and fee plan. Confirmation is unavailable after a failed reload, while
  edits remain, or for legacy records that require activation instead.
- **Placement History…** always opens the history browser, including for legacy
  students. Use Add Missing Historical Placement for verified gaps; it preserves
  current student status and creates no fees or bills.
- **Put on Hold… / Lift Hold / Resume…** handles temporary absence. **Record
  Departure…** remains the separate formal leaving operation. **Re-Admit…** is
  offered for eligible former/inactive students without a hold. **Confirm Return**
  remains the existing contextual button for promoted students awaiting return.
- **Transfer** contains section, class and campus choices. **Corrections /
  Advanced** contains Review Left Record as Temporary Hold for correcting a
  wrongly classified temporary absence.

Save personal-detail edits before opening these workflows. Each existing dialog
retains its financial preview, confirmation and server-side permissions.

| Work | Where to start | Operational caution |
|---|---|---|
| New student | **Daily Work → Student Admissions** | Check student, placement, guardian, date, and opening charges before saving |
| Pending/sibling registration | **Daily Work → Student Admissions → Admit Sibling** or **Daily Work → Pending Registrations** | New siblings first receive a temporary reference; complete details and Confirm & Save in Admissions for official numbers |
| Student history and family | **Student Profile / Students** | Verify the selected student before editing or printing |
| Student leave/transfer/re-admission | **Students → Transfers / Re-Admission** | Preserve history; do not treat leaving as deleting the student |
| Fee configuration | **Fees → Fee Rate Setup** | Rates affect future billing; use approved whole-rupee rules |
| Post fees or review payment corrections | **Fees → Billing Center / Fees → Student Fee Account → Payment History** | Billing remains in Billing Center; select the original payment to review an eligible correction, and check uncertain save outcomes before retrying |
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

## Admissions and pending registrations

**Daily Work → Student Admissions** is the main admission and registration workspace. **Daily Work → Pending Registrations** opens the pending-record list; **Open** or **Review** loads the selected child into the same Admissions form. The old standalone **Quick Registration** page and separate **Family Admission** navigation entry are retired. **Existing Student Setup** remains under **Students → Student Data Review**; Billing Center's exception lists can open the selected student directly. See [school operations](SCHOOL_OPERATIONS.md) for the daily sequence.

**Direct admission:** Enter the student's details, placement and opening fees in Admissions. Review the details and charges before saving. Historical/legacy student activation uses its own guarded process.

**Admit a sibling:** Load an existing student in Admissions and choose **Admit Sibling**. The sibling editor intentionally inherits and locks the father's name, contact number, address and birthplace. Enter the child's own name, gender, current-year placement and fees. Saving creates a **Pending** child and keeps the original student loaded. Open the new sibling from **Siblings** or **Pending Registrations** to finish any missing details.

**Confirm a pending registration:** With the pending child loaded, **Confirm & Save** opens the existing official-number review and confirmation; you do not have to save a separate draft first. If you only want to save edits without confirming, use **Actions → Save Registration Draft**. Pending rows in **Siblings** offer **Confirm & Save** and **Cancel**; cancelled pending rows offer **Reopen**, subject to permission. Official admission and registration numbers are assigned on confirmation. Pending students are not normal monthly-billing or official-certificate candidates.

**Reopen a cancelled pending child:** Review the new placement, academic year, joining date, reason and financial preservation notice. Reopening retains the same child identity and temporary reference and preserves earlier cancelled placements, bills, payments and family history; it does **not** automatically restore cancelled fees or bills. Review the current fee plan before subsequently confirming admission.

If a registration save has an uncertain result, use **Check Registration Save** when offered rather than creating or confirming a second child. Other admission saves also retain their existing **Check Saved Admission** or recovery controls. Do not close an active save or blindly repeat a transaction. See [current admission/registration decisions](BUSINESS_DECISIONS.md#bd-01) and [admission workflow](Workflows/ADMISSION_WORKFLOW.md) for details.

## Find Student

Typing an admission number clears Campus, Academic Year, Class and Status. Changing any of those filters clears the admission number while keeping the other filters. Either change clears old results and disables **Open Student** until you click **Search** again. Student/father name searches can still be combined with filters. **Reset** clears both modes.

## Placement Management

Open **Placement Management** in the sidebar. It contains four separate pages under the existing enrollment-management permission, rather than the old single **Student Promotion** screen:

- **Annual Promotion:** Choose a source academic year; blank campus, class or section includes all matching active placements. Choose the destination year, then **Load Students**. Use **Suggest** or assign destinations to selected rows. Each student can **Promote**, **Repeat Class** (same placement in the new year) or **Skip for Now**. Review exceptions such as missing next classes before processing. Newly promoted students still require return-admission confirmation before billing for the new year.
- **Transfers & Sections:** Move students between sections, classes or campuses **within the recorded academic year**. Use **Load Students**, **Suggest**, **Assign** and row-level destinations. Section-only moves preserve fees; class/campus moves offer current fees, destination fees from next month, or the school cutoff rule.
- **Placement Corrections:** Correct a recorded placement within the same year using a correction explanation and date. Review the fee policy and affected records before processing. **History / Dates** opens the existing date/history tools; **Undo Promotion** takes you to **Placement History**.
- **Placement History:** Use **Refresh History** to review recent saved movements. For an eligible, unchanged annual promotion, select its record, enter a correction date and reason, and choose **Undo Selected Promotion**. The operation retains the original and corrected history; it is refused if later return/billing, payments, certificates, fee changes or subsequent movement make automatic undo unsafe.

**Review** previews a proposed plan and its fee/bill implications before applying it. Processing is all-or-nothing. If a submission's outcome is uncertain, the page locks editing and offers **Check / Retry Plan** for that same request; do not create a duplicate plan. Leaving a page without processing does not save its draft.

**Earlier Promotion History** is available from the placement-history workflow for older promotion records and retains its report permission check. Placement-management access requires the existing enrollment-management permission; undo additionally requires student-administration access. Individual admission/return/transfer dialogs remain available for specialized cases. See [placement workspace rules](STUDENT_PLACEMENT_WORKSPACE.md).

## Unsaved edits and unresolved transactions

- If **Stay / Discard** is offered, select **Stay** to keep the current unsaved draft.
- Small class/section catalog drafts can be left without a discard prompt; active saves must finish. Bulk fee-rate adjustments are protected when leaving the finance workspace, and a successful apply starts a clean baseline.
- New sports/provisional certificate wording and result details use the existing discard guards. Opening an untouched dialog or changing only its new default date remains freely abandonable.
- Certificate creation holds inputs through saving and refresh, refuses a second submission, and preserves the returned result message.
- Admission and fee collection hold submitted inputs until the pending write and refresh finish. Correct invalid entries before submission; failed saves restore editing.
- After a successful admission save and refresh, **Mark as Left** and **Correct Placement** can be opened immediately. Save any subsequent detail edits or pending photo first; an incomplete refresh still requires reopening the student as instructed on screen.
- If a save times out, disconnects or gives uncertain feedback, **do not create a new transaction to test whether it worked**. Use the page's reconciliation action, then ask the administrator if necessary.
- A printed report or exported CSV/PDF is a copy of selected data; verify filters and avoid sharing student or financial documents with unauthorized recipients.

## Student Fee Account and annual receipts

Open **Fees → Student Fee Account** and select a student by admission number or name. From Fee Collection, choose **Student Fees → Student Fee Account** to open the same account with the student preselected. Finish or clear any collection draft first. Closing the account returns to Fee Collection and refreshes that collection context.

The account has four direct tabs:

- **Fees & Dues:** Shows fee charges, payments and per-fee balances for the selected period. Leave optional dates unchecked for the unfiltered statement, or choose dates and click **View Statement**. **Charge & Payment Activity…** opens the chronological charge/payment rows for those dates; it is **not** a complete lifetime event ledger or a running account balance. **Fee Change History…** and **Related Payment…** open the relevant separate details when available. Changing a student or date range invalidates an earlier statement export until it is refreshed.
- **Payment History:** Shows payments, refunds and corrections across all dates. Use **Fee Breakdown** for a selected payment, **Review Correction…** for eligible changes and **Check Save Outcome…** to resolve an uncertain correction without submitting a new one.
- **Receipts:** Lists saved receipts across all dates. **Open Receipt** shows the saved document for review or reprinting; a family receipt appears once rather than once per child.
- **Advance Credit:** Shows retained advance payments and how credit was used. Current dues and available credit are displayed separately, not netted against one another.

Choose **Include Siblings** to work with the family scope; the button then says **Student Only** to return to an individual account. Double-clicking a sibling's eligible account row loads that child's individual account and restores **Include Siblings**. This scope affects account views and outstanding bills, **not** the Annual Receipts student.

Under **Reports… → Annual Receipts…**, choose the academic year for the **main selected student only**, even if siblings are included. Under **Reports… → Outstanding Bills**, choose **Separate Student Bills** or **Combined Family Bills** as needed; these reports use posted unpaid fees, not unsaved payment drafts. Annual receipts also remain available from their other authorized report/profile routes. Statement, receipt, correction and annual-receipt actions retain separate permissions; collection-only operators do not gain statement/export access.

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

## Collections & Receipts grouping

In **Collections & Receipts**, choose the **Receipts** view and select **Group By**: **Whole School** for a combined list, **Campus**, **Class** (within campus), or **Section** (within campus and class). Click **Run Report** after changing the selection. Existing date, campus, class, section and payment-method filters still restrict the records included. Whole School combines those selected records; it does not clear the filters.

The screen, printout and export include the same group headings, monetary subtotals and final total for the selected receipts. Credit applied remains separate from money received. A family receipt spanning several campuses or class/sections stays intact in a **Multiple** group. The **Summary** view retains its separate date, payment-method, class, campus and fee-type grouping choices.

## Lecturer/teacher certificate defaults

When selecting a staff member or starting a new Regular Lecturer/Teacher Certificate, Designation starts with their saved employment designation. Subject Taught uses the most recent nonblank subject in that staff member's saved certificates; it stays blank when none exists. Both fields remain editable. Selecting an existing certificate shows that certificate's saved wording.

The Bank Account Opening Certificate also fills Designation from the selected staff member’s employment record when starting a new certificate. It remains editable; opening an existing certificate retains its saved designation.

## Staff job actions

Use **Edit Job** on the Job Details card to open the active job, **Edit** on a history row for that saved job, or **Add Job** to start a blank job. Each opens a direct job form without history tabs. Employment History stays on the staff page; the redundant top Employment button has been removed. Save or clear staff-profile edits before opening a job form.

## Collection receipt amounts

Saved collection receipts show each included fee's saved amount due before that payment, followed by **Total**, **Received** and **Balance**. Received is new money; credit applied, discounts and retained credit remain separate when applicable. Balance is the saved outstanding balance for the student or family, which can include dues outside the listed charges. Later adjustments remain identified; historical receipt/payment records are preserved.

## Report review and advance credit

The existing **Export** dialog now also offers **Excel workbook (.xlsx)**.
It exports the chosen columns in their current order and the complete generated
report across all pages. Amounts stay numeric, dates stay usable as Excel dates,
and phone/CNIC/admission/receipt references remain text so their digits and
leading zeros are preserved. Headings are frozen and Excel filters are enabled.
Collections & Receipts uses its data rows, excluding inserted group headings and
subtotals, as CSV already does. Microsoft Excel is not required to create the file.

Finance reports and Certificate Issue Register offer optional **Order by** choices
suited to the current view. **Default** is selected automatically; there is no need
to choose an order before running a report. To override it, choose an order and
press **Run Report**. Names support A–Z
and Z–A, amounts support highest/lowest first, and dates support newest/oldest first.
Admission numbers compare numerically with their suffixes. **Default** retains
the report's original order: payment dates for collections, campus/class/section
and admission number for student outstanding lists, billing months for monthly
reconciliation, campus/class and student name for credit lists, and newest recorded
entries first for certificates. Summary and family views retain their own group
ordering. Changing view refreshes the available choices.

Print, PDF and CSV follow the chosen order across the whole result, rather than
only the visible page. Grouped receipt reports sort details within each group;
their headings, subtotals and grand total stay together. Saved Views remember the
chosen order; earlier saved views use Default. Student Reports retains its
existing Sort by control. Bills/receipts retain their finalized wording.

Named **Saved Views** are available in Collections & Receipts, Outstanding &
Aging, Billing & Discounts, Student Advance Balance Register, Monthly Fee
Reconciliation, Certificate Issue Register and Class Marks & Attendance Sheets.
Set the filters and grouping, run the report and choose **Columns** where offered,
then **Save View**. Select a saved name and click **Apply View**, review the restored
settings, then run/load the report. Saving under an existing name asks before
replacing it; Rename View and Delete View manage the selected saved name.

For dated reports, choose **Fixed dates**, **All dates**, **Today**, **This month**,
**Previous month** or **This calendar year**. Relative periods resolve when applied
and each time the report runs. Editing a date switches to Fixed dates. Monthly
Reconciliation continues to include whole billing months. Student Reports keeps
its existing Views selector and loading behavior; it also gains Rename View and
the Date period choices. Earlier saved student views retain their fixed/all-date
meaning.

Views are saved for the signed-in user on this computer, using the existing local
report-settings file. They store settings, not results or selected students.
Class-sheet views require a fresh Load Students and selection review. If a saved
location or certificate option is unavailable, the new shared toolbar refuses
the view and restores the previous filters. Receipt references and total-label
columns remain visible where needed for grouped reports. See
[saved-view details and verification](SAVED_REPORT_VIEWS.md).

Collections & Receipts now offers **Account Credit** as a payment-method filter.
Choose CSV in Export for data rows only; formatted PDF/Print retain subtotals.
Receipt page counts exclude headings and totals.

Outstanding & Aging offers **Overdue Only**, **Minimum Outstanding** and optional
**Days Overdue From/To** for student, charge and family views. Blank limits mean
any value. Click **Run Report** after edits, then **Print** for those filtered
rows. **Class / Section A4** prints the full location scope and is disabled while
these extra filters apply. **Open Statement** opens a selected student's account
when your permissions allow it.

In Billing & Discounts, **Concession Summary** shows assigned discount rates per
billing cycle. Monthly and yearly rows remain separate; these are not amounts
already deducted on posted bills.

**Student Advance Balance Register**, under Reports > Finance, lists current
positive advance credit by student or family, with latest credit activity. It
requires report-view and fee-view access. Location filters use current/latest
placement; the minimum credit filter applies to each student before family totals.
Credit held is separate from fees owed to the school and money received today.

**Monthly Fee Reconciliation**, under Reports > Finance, groups posted fees by
billing month, optionally by campus, class, section or fee type. From/To select
whole months. A missing billing month uses the charge month. Academic year and
location follow each charge's enrollment. Money Allocated includes signed refunds
and payment corrections; Credit Applied is separate. Settlements are current,
including later payments on older fees. This is not cash received during the
selected months or a historical closing balance. Cancelled charges and reversed
payments are excluded. Check the equation:
**Net Fees + Excess Settlement = Money Allocated + Credit Applied + Outstanding**.
Excess Settlement identifies amounts allocated above a charge's net fee; review
those accounts separately. Unallocated advance deposits appear in the Credit
Balance Register rather than being counted as settled fees here.

**Certificate Issue Register**, under Reports > Certificates, combines student
and staff certificate records. Report access is required, plus existing general
certificate access or student-leaving access for the relevant sources. Filter by
type, recipient, recorded date, entry kind, collection status or name/reference.
Character, Date of Birth and Leaving certificates have recorded issue entries;
repeated issues remain separate rows. Other types show **Saved record only** and
**Not tracked** for collection. Saving a record does not confirm that a certificate
was printed or handed over. Recorded On is the issue timestamp where available,
otherwise the record creation timestamp. Certificate Date is shown separately
(Leaving certificates use Leave Date). Recorded names come from the saved
certificate; student admission/QR references use the current student record.
Printing/exporting this register does not issue, reissue or collect certificates.

### Manage campuses, classes and sections together

Open School Setup → Campus - Classes / Sections. Use Manage Campuses to define
locations in the aligned form above the campus list. Manage Classes and Manage
Sections add/edit the school-wide catalogs, manage class programs, reorder
records, or delete records when existing usage safeguards allow it. Renames apply
to all campuses. Adding a class or section does not assign it automatically.
Close the dialog, tick the desired combinations for the campus, then Apply Changes.
Pending ticks survive catalog refresh. Reset Changes restores saved assignments.
Use Refresh Catalogs to retry a failed refresh.
Campus names and the campus selector refresh when its dialog closes. Pending
ticks stay with the same campus; deleting it never transfers those ticks to another.

### Understand debt and concession reports

Outstanding shows the full unpaid balance split into Overdue Amount, Due Today
and Not Yet Due (plus Due Date Missing for legacy records). Has overdue charges selects debtors with an overdue charge;
future fees stay in their separate column. Families also includes standalone
students without family links. Day limits on student/family rows use the oldest
unpaid charge. Concession Summary uses its Summary as of date for effective
agreements; use Concession Assignments for history or Discount Detail for actual
billed reductions. Monthly attendance sheets use the current active roster.

### Saving management reports as PDF

Export PDF uses the same readable layout as Print Preview: school and report
headings, selected scope, summary amounts, repeated table headings and page
numbers. Very wide reports continue in labelled sections with repeated references.
CSV remains a data export. Configure School Profile before saving report PDFs.
Receipts retain the finalized Net payable, Payment received and Total wording. Discounts and applied credit share the print-only deduction; saved values remain separate. Family summaries retain the Net total, Net payable, Net received and Net due captions.
