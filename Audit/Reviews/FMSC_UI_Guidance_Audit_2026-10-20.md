# FMSC — UI Wording, Guidance & Consistency Audit

**Audit date:** 20 October 2026  
**Repository:** `farhan2166-creator/FMSC-Database`  
**Revision assessed:** `main` at `d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac`  
**Status:** Source-code review completed for a representative cross-module sample; runtime visual confirmation pending. **No source changes made.**

## 1. Owner-approved scope and safeguards

Assess whether explanatory text is worth the space it occupies, whether users can recognize actions at a glance, and whether comparable forms look and read like one application. **The visual design is nearly final.** Preserve existing layout, workflow, control hierarchy, colors, themes, validation, permissions, and navigation. Every full-page screen retains a consistent title treatment; short title context is optional **only when informative**. Do not insert meaningless subtitles for symmetry.

**Absolutely protect:** consequential financial effects, difference between posting and collecting, refunds/reversals, saved-versus-unposted state, data-loss and recovery warnings, consent/privacy, required permissions, error/empty/retry states, accessibility text, and confirmation semantics. Avoid changing shared `V2Page`, `HumanUi.Card`, `MasterPage`, metrics, or theme components as part of a copy edit.

### Design baseline confirmed in source

- `Core/V2Page.cs` supplies the common title, optional adjacent context, dynamic header guidance, shared action bar and measured guidance height. An empty context is already supported. The title should **not** be redesigned.
- `Core/UiText.cs` shares common actions, field names, finance terms, prompts and empty states.
- `Docs/Design/UI_STYLE_GUIDE.md` requires succinct task-oriented text, explicit action semantics and adjacent critical consequences. It expressly accepts **Fee Collection**'s current layout, absent demonstrated problems or another owner decision.
- `Docs/FINANCE_WORDING_REVIEW_2026-10-20.md` deliberately keeps critical finance effects visible while using hover examples for supplementary help.
- `V2Page.PrepareForEmbedding()` hides child-page headers when a page is embedded. Do not mistake these deliberate exceptions for missing headers.

## 2. Coverage and evidence limits

The repository's `Pages/` directory contains **160 C# files across 10 module directories**, including partial classes, models, services and helper files—not 160 distinct screens. **More than 50 representative files** were fetched and inspected across Administration, Communication, Dashboard, Finance, Help, Reports, Security, Setup, Staff and Students. Examples below reference the exact revision reviewed.

This is a **code-level wording and hierarchy audit**, not proof of pixel-space waste. There are no checked-in presentation screenshots in the tree matching the audited UI, and the Windows application has not been launched here. Exact wrapping, clipping, display scaling and reading order must be verified with screenshots or Windows smoke tests before implementation.

## 3. Findings and recommendation register

| ID | Priority | Screen / evidence | Observation | Recommendation pending screenshot/owner approval |
|---|---|---|---|---|
| G01 | **P1** | [Audit Logs, line 49](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Administration/AuditLogsPage.cs#L49) | **Audit Filters** card contains a long paragraph describing all searchable fields, examples and six-month value retention. Field labels already show user/table/date/keyword. | Split essential retention/search limitation from routine filter explanation. Candidate: short context beside search + concise retention note available where relevant. Do not suppress retention information without a replacement. |
| G02 | **P1** | [Communication tab guidance](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Communication/CommunicationCenterPage.Guidance.cs#L26-L56), [shared header sizing](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Core/V2Page.cs#L307-L345) | Several long, tab-dependent header messages. Shared header measures/expands to accommodate guidance, so length has a real vertical-space cost. | Shorten *routine process narration* first; keep permission/consent, live-vs-simulation and delivery-status caveats. Inspect each tab and viewport scale. |
| G03 | **P1 / visual check** | [Annual Fee Receipts, lines 37–74](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Reports/Finance/AnnualFeeReceiptsPage.cs#L37-L74) | Page context and selector card explain annual receipts; a separate **Tip** card repeats receipt ordering, occupying the second row of a layout that assigns remaining height to that row. | Verify actual rendered footprint. If visibly empty/oversized, propose a *separately approved* micro-layout improvement; do not delete or resize cards in a wording-only patch. |
| G04 | **P1** | [Quick Registration, lines 63–89 and 127–141](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Students/Admissions/QuickRegistrationFormPage.cs#L63-L141) | Page-level instructions, **Registration Details** help, a two-sentence **Fee Plan** explanation and an initial status message all tell the operator what to do next. | Identify which guidance is redundant after the actions/fields are visible. Keep distinction between saving a registration, printing a bill and collecting payment, and preserve concessions' effective-period rules. |
| G05 | **P2** | [Family Admission, lines 57–111](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Students/Admissions/FamilyAdmissionWorkspace.cs#L57-L111) | Title context embeds a three-step workflow while Children/Family/Editor sections also provide instructions. Family and editor card headers are explicitly hidden. | Test whether the numbered workflow still adds value. Hidden headings may be intentional compact embedding; review layout before treating as inconsistent. |
| G06 | **P2** | [Student Profile, lines 90–112, 156–224](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Students/Profile/StudentProfilePage.cs#L90-L112) | Five descriptive card subtitles, plus a detailed how-to-find-student explanation. Several simply restate obvious card labels. | Shorten low-value captions first (not empty/loading state messages). Keep differentiators such as lifecycle history and current versus previous placement. |
| G07 | **P2** | [Student Fee Adjustments, lines 58 and 110–165](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Finance/StudentAccounts/StudentFeeAdjustmentsPage.cs#L110-L165) | Page context plus Student, Payment Adjustment and Recorded Payments instructions repeat select/review phrasing. | Candidate to shorten static instructions, **not** the dynamic eligibility note, recovery message or explanations of reduction/refund/reversal consequences. |
| G08 | **P2** | [Student Account Statement, lines 169–272](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Reports/Finance/StudentAccountStatementPage.cs#L169-L272) | Section headings use mixed casing: **Student account**, **Choose students / families**, **Account details**, while style guide prefers title case. Date scope and selection behavior are complex. | Safe candidate: casing consistency only; retain scope and family-selection guidance where consequential. Avoid incidental changes to grid/report logic. |
| G09 | **P2 / visual check** | [Student Certificates, lines 14–39](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Students/Certificates/StudentCertificatesPage.cs#L14-L39) | The page title and the intro card both use the **Student Certificates** title and both explain choosing a certificate. | Confirm whether intro panel aids navigation or merely duplicates page header. Removal changes layout, so do not automatically remove it. |
| G10 | **P2** | [Student Family, lines 47 and 82](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Students/Profile/StudentFamilyPage.cs#L47-L82) | **Family / Siblings** repeats as page and card title; both include navigation instructions. | Assess whether card title needs a more specific purpose or whether concise card help suffices. No automatic title removal. |
| G11 | **P2** | [Class Sheets, lines 25–112](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Reports/Students/ClassSheetsPage.cs#L25-L112) | Page, section, and empty-grid instructions all mention selection and loading. | Keep empty-grid prompts state-specific; review static repeated guidance, especially at narrower widths. |
| G12 | **P3** | [Transfer & Readmission, lines 140 and 293–313](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Students/Enrollment/StudentTransferReadmissionPage.cs#L293-L313) | Header and section text are wordy but help explain conditional action eligibility. | Minor shortening possible after visual review; keep action eligibility and effective-date clarity. |
| G13 | **P3** | [School Profile, lines 36 and 53–98](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Setup/SchoolProfilePage.cs#L36-L98) | **School Identity** description elaborates what the school-name/address fields are for. Logo and fee-policy captions carry practical constraints. | Consider reducing School Identity exposition; retain file type/size and fee policy details. |
| G14 | **Protected** | [Backup & Restore, lines 47–199](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Administration/BackupRestorePage.cs#L47-L199) | Contains a prominent data-location / backup warning and a potentially destructive restore explanation. | **Preserve prominently.** Condensing for space must not weaken data-loss, replacement or recovery implications. |
| G15 | **Protected** | [Fee Collection](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Finance/Collection/FeeCollectionPage.cs#L460-L580) and [accepted design decision](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Docs/Design/UI_STYLE_GUIDE.md#L1-L4) | Rich card instructions, but the owner has expressly accepted the current collection layout. | No structural modification. Only propose specific, demonstrated copy duplication; retain allocation and payment-safety explanations. |
| G16 | **Design exception** | [Confirm Return Admission, lines 126–199](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Students/Enrollment/ConfirmReturnAdmissionPage.cs#L126-L199) | Returning Student and Opening Charges card headers are hidden by the page; individual fields and totals remain present. | Inspect why those compact sections differ from other cards. **Not** a confirmed defect; do not make headers visible blindly. |
| G17 | **No defect found** | [Student Reports dynamic header](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Pages/Reports/Students/StudentReportsPage.cs#L725-L734) and [shared setter](https://github.com/farhan2166-creator/FMSC-Database/blob/d0ae013aeac1aba77f25d453bfc6b57c6e5b08ac/Core/V2Page.cs#L1149-L1168) | `SetPageHeaderText(string.Empty, context)` does **not** clear the title; it intentionally updates context alone. | Preserve behavior. A surface-level code scan could otherwise report a false missing-title problem. |

## 4. Proposed wording directions (illustrative, **not approved replacements**)

| Screen | Current text / pattern | Possible concise direction | Safety condition |
|---|---|---|---|
| Audit Logs — Filters | Lengthy explanation of filter mechanics, examples and retention | “Filter by user, date or keyword.” + separate concise six-month limitation | Retention limitation remains discoverable and intelligible |
| Student Fee Adjustments — Student card | “Choose a student to review their recorded payments.” | “Select a student.” or no card subtitle if the finder is self-explanatory | Empty-state and eligibility messages remain |
| Student Profile — Student selector | “Type the student's Admission Number or name, then click the student in the list.” | “Search by name or admission number.” | Finder behavior remains clear to first-time users |
| Family Admission — Header | Three numbered steps in the title-context area | A short purpose line only if workflow steps are shown elsewhere | Workflow still clear to a first-time clerk |
| Student Account Statement — section titles | “Student account”; “Account details” | “Student Account”; “Account Details” | Text-only casing change; no field/column changes |

## 5. Consistency rules for the review

1. Preserve the shared `V2Page` title look on full-page workspaces. Do not require a subtitle when it adds nothing. Remember that the existing context is displayed beside the title, not as a separate paragraph.
2. Prefer one short purpose statement per major section, and use *state-specific* messages for empty/loading/error conditions rather than permanently visible instructions.
3. Keep concise, explicit field and action labels so scanning is sufficient for routine users; ensure keyboard and screen-reader information is retained.
4. Apply title case and shared terminology consistently, but never rename controls in a way that changes their operational meaning (New vs Clear vs Reset; Post vs Collect vs Refund).
5. Do not standardize away genuinely different sections, embedded forms, status panels or role-specific guidance.
6. Tooltips can carry extra illustrations, but must **not** be the only place critical consequences or instructions appear.
7. High-risk financial, data-loss, security and consent messaging is exempt from superficial shortening.

## 6. Visual verification and change gates

**Required visual review:** Take screenshots at the app's standard and constrained Windows widths, standard and enlarged DPI/scaling, and light/dark themes for G01–G11. Check title/context overflow, card-header wrapping, vertical space used, primary-action visibility, data-table height, long translations/strings, keyboard tab order and screen-reader semantics.

**Change approval gate:** Each recommendation should show: current screenshot, proposed text, before/after screenshot, risk category, and explicit approval. Any card removal/reflow is a separate layout proposal and is **outside** a pure copy pass. Regression validation includes finance-sensitive pathways and theme smoke checks. Do not bundle this work with stashed, unfinished student-fee adjustments.

## 7. Initial decision

**Do not redesign the app or immediately trim all subtitles.** First candidates for *copy-only* review are **Audit Logs**, **Communication Center**, **Quick Registration**, **Student Fee Adjustments**, and **Student Profile**. **Annual Fee Receipts** merits early visual inspection for a possible space problem but no layout change until approved. **Backup & Restore** and **Fee Collection** remain protected baselines.

**Source audit outcome:** Enough concrete evidence exists to move to targeted visual validation and proposed wording comparisons. There is *not yet* evidence to authorize blanket removal of text or card headers.

## 8. Implementation and verification follow-up — 21 October 2026

The owner authorized applying confirmed copy findings and verifying the remaining candidates. The original review above is retained as the historical baseline.

| Findings | Current outcome |
|---|---|
| G02 | Shortened routine Communication Center narration. Retained compose/configure permission guidance, consent and ownership requirements, immutable template approval, provider mapping, future-event scope, approval/dispatcher controls, and simulation/live warnings. |
| G04 | Shortened Quick Registration's initial status. Retained its registration/confirmation context, parent-link guidance beside the fields, concession timing, and bill/payment instructions. |
| G06 | Shortened Student Profile search, identity and sibling captions. The search caption fits the inspected 1000-pixel-wide capture; lifecycle and current-placement descriptions remain. |
| G07 | Shortened routine student/payment selection captions. Financial consequences, eligibility and save-recovery messages are unchanged. |
| G08 | Applied title case to Student Account, Choose Students / Families and Account Details. Date scope and cross-search family selection guidance are unchanged. |
| G03 | Rendered at 1000×620 and 1280×720, actual Windows DPI 96, in OceanBlue and ProfessionalSimple. Confirmed that the empty Tip content occupies 152 and 252 pixels of height respectively. Layout remains a separate change candidate. |
| G09 | Captures confirm the repeated Student Certificates title and introduction. At constrained height the certificate-card content also merits a dedicated live layout review. No card removed or resized. |
| G05, G16, G17 | Source confirms deliberate compact/embedded headers and title-preserving context updates. No defect established; preserved. |
| G10, G11 | Repeated Family / Siblings titles remain. Class Sheets instructions also explain meaningful all-class/all-section and hidden-checked-student behavior. Retained pending targeted visual review. |
| G12, G13 | Eligibility, effective-date, school-document purpose, logo limits and fee-policy guidance remain useful. No demonstrated defect; preserved. |
| G01 | Deferred with the audit system's gradual retirement. The current page also requests a 10,000-row retention cap; any later wording change must account for that as well as six-month detail retention. No pruning behavior changed. |
| G14, G15 | Protected Backup & Restore and Fee Collection layouts and critical consequences are unchanged. |

Reproducible offline captures: `dotnet run --project Tests/ThemeSmokeChecks -- --ui-guidance-review`. Output: `artifacts/ui-guidance-review/` (generated evidence, not committed). The fixture uses administrator permissions and disables database initialization; no school records, certificates, or reports are created. These captures are not a substitute for populated-data, keyboard/screen-reader, or actual enlarged-DPI review. Current selectable theme families were exercised; no distinct dark-mode verification is claimed.

Validation: 299 communication layout/focus/workflow checks; 89 finance wording and recovery checks; 12 offline guidance render checks. Builds succeeded; NuGet vulnerability metadata lookup produced NU1900 because its service index was unavailable.

During this work the owner separately requested clearer printed fee documents. All four bill/receipt templates now hide the redundant family-count detail, including historical snapshots. Following the owner's wording correction, debts are labelled **Student outstanding** and **Family outstanding** rather than balance. Receipts distinguish listed fee totals, payment received, existing credit used and outstanding fees. Existing monetary values, allocations and snapshots are unchanged. The 301 fee-document checks cover individual/family bills and receipts, partial payments, credit cases, legacy/A4 output, and a longer owner-edited caption, including a rendered example with 4,800 listed fees, 3,000 received, 4,300 student debt and 6,500 family debt. The PDF and PNG examples were visually inspected.

Fixed captions were also separated from data expressions across bill/receipt, admission, class-sheet, outstanding, summary and aging templates. Receipt sizing now measures all text runs and respects the legacy total row's hide-when-true condition. Seven additional report templates were rendered with typed fixture data. Conditional grammar and genuinely data-dependent certificate wording remain dynamic. See `Docs/Design/EDITING_REPORT_LABELS.md` for owner editing instructions; literal captions are not tied to exact-English assertions in the receipt checks.
