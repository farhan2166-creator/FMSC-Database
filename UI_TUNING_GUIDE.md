# UI Tuning Guide — Start Here

This is the map for changing the appearance of FMSC SMS. You do not need to understand the whole project.

## The 30-second method

1. Find what you want in the first table below.
2. Click the file name.
3. Press **Ctrl+F** and search for the phrase shown.
4. Change only that setting.
5. Build and look at the affected screens.

### Understand the impact symbol first

| Symbol | Meaning | Be careful because… |
|---|---|---|
| 🌍 **GLOBAL** | Changes most or all screens | One edit can affect the entire application |
| 🧩 **SHARED** | Changes one family of components | For example, every card or every standard page |
| 📄 **ONE PAGE** | Changes only the named screen | Safest choice for a screen-specific adjustment |

> **Simple rule:** If only one page looks wrong, do not edit a GLOBAL setting.

## I want to change…

This is the main table you will use most of the time.

| I want to change | Impact | Click this file | Press Ctrl+F and search for |
|---|---:|---|---|
| Main title size on every page | 🌍 | [AppTheme.cs](Core/AppTheme.cs) | `PageTitleFont` |
| Top empty space on every page | 🌍 | [AppMetrics.cs](Core/AppMetrics.cs) | `PageTopPadding` |
| Left, right or bottom page space | 🌍 | [AppMetrics.cs](Core/AppMetrics.cs) | `SpacingXl` |
| Extra space around page headings | 🌍 | [AppMetrics.cs](Core/AppMetrics.cs) | `PageHeaderContentHeightAllowance` |
| Normal writing size | 🌍 | [AppTheme.cs](Core/AppTheme.cs) | `NormalFont` |
| Section heading size | 🌍 | [AppTheme.cs](Core/AppTheme.cs) | `HeadingFont` |
| Field-label size | 🌍 | [AppTheme.cs](Core/AppTheme.cs) | `FieldCaptionFont` |
| Text-box and dropdown height | 🌍 | [AppTheme.cs](Core/AppTheme.cs) | `InputHeight` |
| Form-row height | 🌍 | [AppMetrics.cs](Core/AppMetrics.cs) | `FieldRowHeight` |
| Button height | 🌍 | [AppTheme.cs](Core/AppTheme.cs) | `ButtonHeight` |
| Minimum width of standard action buttons | 🌍 | [AppMetrics.cs](Core/AppMetrics.cs) | `ActionButtonMinimumWidth` |
| Grid row height | 🌍 | [AppMetrics.cs](Core/AppMetrics.cs) | `GridRowHeight` |
| Grid heading height | 🌍 | [AppMetrics.cs](Core/AppMetrics.cs) | `GridHeaderHeight` |
| Space between normal sections | 🌍 | [AppMetrics.cs](Core/AppMetrics.cs) | `SectionGap` |
| Card heading height | 🧩 | [AppMetrics.cs](Core/AppMetrics.cs) | `CardHeaderHeight` |
| Space inside every card | 🧩 | [AppMetrics.cs](Core/AppMetrics.cs) | `CardContentVerticalPadding` |
| Card corner roundness | 🧩 | [AppTheme.cs](Core/AppTheme.cs) | `CardRadius` |
| Page toolbar height | 🧩 | [AppMetrics.cs](Core/AppMetrics.cs) | `PageActionBarHeight` |
| Admission-form section gaps | 📄 | [StudentAdmissionPage.cs](Pages/Students/Admissions/StudentAdmissionPage.cs) | `groupGap` |
| Gap before Admission / Placement | 📄 | [StudentAdmissionPage.cs](Pages/Students/Admissions/StudentAdmissionPage.cs) | `studentGuardianCard.Margin` |
| Admission / Placement height | 📄 | [StudentAdmissionPage.cs](Pages/Students/Admissions/StudentAdmissionPage.cs) | `admissionCardHeight` |
| Fee Collection layout | 📄 | [FeeCollectionPage.cs](Pages/Finance/Collection/FeeCollectionPage.cs) | `root.RowStyles` |
| Dashboard sections and sizing | 📄 | [WelcomePage.cs](Pages/Dashboard/WelcomePage.cs) | `Build` / `UpdateDashboardLayout` |
| Dashboard Quick Actions | 📄 | [WelcomePage.cs](Pages/Dashboard/WelcomePage.cs) | `BuildQuickActionsCard` |
| Dashboard top bar | 📄 | [MainDashboardForm.cs](MainDashboardForm.cs) | `DashboardTopBarHeight` |
| Dashboard KPI size | 📄 | [WelcomePage.cs](Pages/Dashboard/WelcomePage.cs) | `KpiGridRowHeight` / `KpiValueFont` |
| Dashboard Quick Action icon size | 📄 | [WelcomePage.cs](Pages/Dashboard/WelcomePage.cs) | `DashboardActionButton.PaintLeadingContent` |
| Certificate card icon size | 📄 | [StudentCertificatesPage.cs](Pages/Students/Certificates/StudentCertificatesPage.cs) | `CertificateCardIconFont` |
| Reports-home cards | 📄 | [ReportsHomePage.cs](Pages/Reports/ReportsHomePage.cs) | `BuildReportGrid` |

## Before changing anything: what do these words mean?

| Word | Plain meaning | Example |
|---|---|---|
| **Margin** | Empty space outside a box | Gap between two cards |
| **Padding** | Empty space inside a box | Space between a card border and its fields |
| **Height** | How tall something is | A button, field row or heading |
| **Width** | How wide something is | Sidebar or search box |
| **RowStyle** | Height rule for one horizontal area | Top section, middle grid, bottom summary |
| **ColumnStyle** | Width rule for one vertical area | Left panel and right panel |
| **Percent** | Shares available space | Two columns at 50% each |
| **Absolute** | Fixed size | A toolbar that stays the same height |
| **AutoSize** | Fits its contents | A label grows to fit its text |

### Margin and padding picture

```text
              MARGIN (outside gap)
       ┌─────────────────────────┐
       │  PADDING (inside gap)   │
       │   ┌─────────────────┐   │
       │   │     CONTENT     │   │
       │   └─────────────────┘   │
       └─────────────────────────┘
```

If two sections are too far apart, look for **Margin** or a section **Gap**. If fields are too far from the edge of their own card, look for **Padding**.

## Three common examples

### Example 1: “The title is too large everywhere”

1. Click [AppTheme.cs](Core/AppTheme.cs).
2. Search for `PageTitleFont`.
3. Adjust that font size slightly.
4. Check the dashboard, Admission, Fee Collection and one setup page.

This is a **GLOBAL** change.

### Example 2: “Only the Admission sections are too far apart”

1. Click [StudentAdmissionPage.cs](Pages/Students/Admissions/StudentAdmissionPage.cs).
2. Search for `groupGap` for the Student Details → Parent/Contact gap.
3. Search for `studentGuardianCard.Margin` for the gap before Admission/Placement.
4. Do not change `SectionGap`, because that would affect other screens.

This is a **ONE PAGE** change.

### Example 3: “Every grid row is too tall”

1. Click [AppMetrics.cs](Core/AppMetrics.cs).
2. Search for `GridRowHeight`.
3. Change it carefully and inspect a student grid, finance grid and report grid.

This is a **GLOBAL** change.

## Which file owns which part?

Use this when the first table does not contain your exact request.

### Global design files

| File | What it owns | Use it when… |
|---|---|---|
| [Core/AppTheme.cs](Core/AppTheme.cs) | Fonts, colours, control heights and corner styles | You want the same visual change across the application |
| [Core/AppMetrics.cs](Core/AppMetrics.cs) | Spacing, dimensions, field rows, grids, cards and toolbar sizes | You want to tune a shared measurement |
| [Core/UiDesignSystem.cs](Core/UiDesignSystem.cs) | Friendly aliases for shared spacing, typography and actions | You need to understand what a page is reusing |
| [Core/AppScale.cs](Core/AppScale.cs) | DPI and screen-size scaling | The whole application scales incorrectly—not when one page is wrong |

### Shared component files

| File | What it owns | Useful Ctrl+F searches |
|---|---|---|
| [Core/V2Page.cs](Core/V2Page.cs) | Standard page title, guidance, toolbar and content frame | `BuildHeader`, `FitHeaderGuidance`, `EnsureActionBar` |
| [Core/WorkspacePage.cs](Core/WorkspacePage.cs) | Pages containing tabs with other pages inside | `BuildHeader`, `PrepareForEmbedding` |
| [Core/HumanCard.cs](Core/HumanCard.cs) | Standard card title, subtitle and content area | `public HumanCard` |
| [Core/HumanUi.cs](Core/HumanUi.cs) | Generated fields, group headings and common grid styling | `Field`, `GroupHeading`, `ApplyGridStyle` |
| [Core/MasterPage.cs](Core/MasterPage.cs) | Simple setup pages with an editor and records grid | `editorHeight`, `BuildSplitPanels` |

Do not edit a shared component to fix one unusual page unless the shared component itself is wrong everywhere.

## Page map

| Screen shown to the user | Source file | Good starting search |
|---|---|---|
| Student Admission | [StudentAdmissionPage.cs](Pages/Students/Admissions/StudentAdmissionPage.cs) | `BuildAdmissionForm` |
| Student Profile | [StudentProfilePage.cs](Pages/Students/Profile/StudentProfilePage.cs) | `HumanUi.Card` |
| Transfers / Re-Admission | [StudentTransferReadmissionPage.cs](Pages/Students/Enrollment/StudentTransferReadmissionPage.cs) | `BuildStudentContext` |
| Fee Collection | [FeeCollectionPage.cs](Pages/Finance/Collection/FeeCollectionPage.cs) | `Payment Summary` |
| Fee Rate Setup | [FeeConfigurationWorkspace.cs](Pages/Finance/Configuration/FeeConfigurationWorkspace.cs) | `RowStyles` |
| Dashboard and sidebar | [MainDashboardForm.cs](MainDashboardForm.cs) / [WelcomePage.cs](Pages/Dashboard/WelcomePage.cs) | `BuildSidebar` / `Build` |
| Reports home | [ReportsHomePage.cs](Pages/Reports/ReportsHomePage.cs) | `BuildCategory` |
| School Profile | [SchoolProfilePage.cs](Pages/Setup/SchoolProfilePage.cs) | `School Identity` |
| Classes and Sections | [ClassesAndSectionsWorkspacePage.cs](Pages/Setup/Academics/ClassesAndSectionsWorkspacePage.cs) | `split` |
| Simple setup pages | [MasterPage.cs](Core/MasterPage.cs) | `editorHeight` |

## The spacing sizes already available

When a page needs a gap, prefer one of the existing names below instead of typing an unexplained number:

```text
SpacingXs  → very small gap
SpacingSm  → small gap
SpacingMd  → normal gap
SpacingLg  → large gap
SpacingXl  → page-level gap
SpacingXxl → extra-large gap
```

These names are defined in [AppMetrics.cs](Core/AppMetrics.cs) and exposed as `UiDesignSystem.Spacing`. The actual values remain in the source file so this guide cannot become a conflicting second configuration.

## Safe editing checklist

- [ ] I decided whether this is GLOBAL, SHARED or ONE PAGE.
- [ ] I followed a file link instead of searching the whole project blindly.
- [ ] I searched for a setting name, not a raw number.
- [ ] I changed only one related setting first.
- [ ] I reused an existing spacing name when possible.
- [ ] I built the solution successfully.
- [ ] I checked 1366×768 and a larger window.
- [ ] For a global change, I checked Dashboard, Admission, Fee Collection, a setup page and a report.

## Avoid these common mistakes

- Do not create another file containing global UI values. The existing sources of truth are `AppMetrics`, `AppTheme` and `UiDesignSystem`.
- Do not copy a global value into several pages.
- Do not change `AppScale` to fix one page.
- Do not adjust a fixed row height without checking whether its contents still fit.
- Do not reduce a field row below the field and caption it contains.
- Do not use a raw number when an existing spacing or height name already expresses the intent.
- If a unique number is unavoidable, keep it on the page that owns it and add a short `UI TUNING` comment.

## After editing

Build the solution. In Visual Studio, use **Build → Build Solution**. From a terminal in the project folder, use:

```powershell
dotnet build FMSC_SMS.sln --no-restore
```

A successful build proves the code is valid. It does not prove the layout looks right, so open the affected screen and inspect it before finishing.

