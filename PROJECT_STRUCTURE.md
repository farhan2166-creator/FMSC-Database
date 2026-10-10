# Page names and project structure

The sidebar follows the operator's work. Source folders follow the same workflows; their C# namespaces match their locations.

## Navigation

| Group | Work |
|---|---|
| Daily Work | Student Admissions, Student Profile, Fee Collection, Student Certificates |
| Student Management | Family/siblings, photos, transfers/re-admission, promotion, record checks and duplicate review |
| Fees & Billing | Student Fee Corrections, Fee Posting, Fee Rate Setup |
| Staff | Staff Management and staff certificates |
| Reports | Student reports and financial reports/documents |
| School Setup | School profile, campuses, academic years, classes/sections, campus assignments and subjects |
| System Maintenance | Audit Logs and Backup / Restore |

Permissions remain attached to each existing action. User/role administration remains outside the shell navigation. Posting-status reports remain available within Fee Posting because they support that workflow.

## Source folders

| Folder | Responsibility |
|---|---|
| `Pages/Dashboard` | Dashboard overview and quick actions; `MainDashboardForm.cs` handles shell navigation |
| `Pages/Students/Admissions` | Admission entry and editing |
| `Pages/Students/Profile` | Student profile, family and photo screens |
| `Pages/Students/Enrollment` | Transfers, re-admission, promotion and return admission |
| `Pages/Students/Maintenance` | Record checks and duplicate review |
| `Pages/Students/Certificates` | Student certificate workflows and their shared clearance helper |
| `Pages/Students/Dialogs` | Student search and sibling search dialogs |
| `Pages/Staff/Certificates` | Staff certificate workflows; staff profile/jobs remain in `Pages/Staff` |
| `Pages/Setup/Academics` | Academic calendar, programs, classes, sections, subjects and campus assignments |
| `Pages/Finance/Configuration` | Fee rates/types, rate matrix, copying/bulk rate changes, configuration notifications |
| `Pages/Finance/Billing` | Posting charges, additional charges and reversing uncollected charges |
| `Pages/Finance/Collection` | Recording payments and printing collection receipts |
| `Pages/Finance/StudentAccounts` | Student fee assignments, projections, history and payment corrections |
| `Pages/Reports/Students` | Student report builder and its preset entry pages |
| `Pages/Reports/Finance` | Collections, balances, billing coverage, account statements and annual receipts |
| `Pages/Reports/Templates` | RDLC templates; the build still copies these to the runtime `Reports` directory |
| `Pages/Administration` | Audit, backup UI and restricted administrative pages |
| `Pages/Security` | Login and account/security screens |
| `Services/Backup` | Backup execution and settings; these are services, not pages |
| `Core` | Shared controls, navigation, validation, printing and design helpers |

## Clearer names

| Previous name | Current name / source |
|---|---|
| New / Search Admission | Student Admissions — `StudentAdmissionPage` |
| Certificates Center | Student Certificates — `StudentCertificatesPage` |
| Student Lifecycle / Transfer / Re-Admit | Transfers / Re-Admission — `StudentTransferReadmissionPage` |
| Student Status Console / Status Check | Student Record Checks — `StudentRecordChecksPage` |
| Assign Classes to Campus | Campus Classes / Sections — `CampusClassSectionAssignmentsPage` |
| Adjust Fees / Refunds | Student Fee Corrections — `StudentFeeAdjustmentsPage` |
| Post Fees to Students | Fee Posting — `FeePostingWorkspace` |
| Fee Setup | Fee Rate Setup — `FeeConfigurationWorkspace` |
| Bulk Adjust Fees | Bulk Fee Rate Changes — `BulkFeeRateChangesForm` |
| Fee Statement | Student Account Statement — `StudentAccountStatementPage` |
| Student Annual Fee Ledger | Annual Fee Receipts — `AnnualFeeReceiptsPage` |
| Missing Charge Candidates / Not Posted Yet | Unposted Fees — `UnpostedFeesReportPage` |
| DefineClasses / DefineSections / DefineSubjects / DefineCampuses | ClassesPage / SectionsPage / SubjectsPage / CampusesPage |
| DefineFeeTypes / DefineUsers / StaffPage | FeeTypesPage / UsersPage / StaffProfilePage |

Shared screen titles live in `Core/PageTitles.cs`. Use them for page headings, menu destinations and contextual links. Keep action labels specific to what they do. New screen files should normally contain the corresponding page, form or workspace; shared helpers have their own files. Keep template filenames stable unless all runtime references and packaging are updated together.
