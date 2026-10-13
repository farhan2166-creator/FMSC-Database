# FMSC_SMS — Independent Security, Durability, and Release-Integrity Audit

**Audit date:** 13 October 2026  
**Repository:** `farhan2166-creator/FMSC-Database`  
**Branch:** `main`  
**HEAD reviewed for this audit:** `cf31e953eb0d25efb5a771221958f3343cd6e460`  
**Purpose:** An independent, adversarial review **from a different perspective** than the prior admissions, billing, dashboard, photo, and general UX audits: **authorization intent, administrative state changes, multi-step write atomicity, file persistence, failed-write honesty, operator data recovery, and release-tool correctness**.  
**Disposition:** Findings documented; **no fixes made**. No source code, tests, SQL migration, database, or existing audit file was changed by this review.  
**Related documents:** [Consolidated master audit](MASTER_AUDIT_FINDINGS_FIXES_STEPS_DONE_2026-10-13.md) and [prior follow-up code findings](FMSC_FOLLOWUP_CODE_REVIEW_FINDINGS_2026-10-13.md).

> **Evidence standard:** A *confirmed source finding* means both the problematic code path and its relevant caller/guard have been inspected, rather than inferring a problem from one suspicious line. Some failures require exceptional conditions or privileged maintenance access and have **not** been reproduced at runtime; those qualifications are explicit. There was no live school-database access or native Windows execution in this review. Nothing here proves every line of the roughly 379 tracked files is correct or defective.

## 1. Executive assessment and scope

The application has deliberately implemented protections that should be retained: SQL-enforced permissions and whole-rupee constraints, SQL migration preflight/backup discipline, financial payment recovery journaling, guarded active writes on critical money pages, read cancellation/generation checks, and atomic writes for several settings files. This new review **did not establish a newly introduced live financial posting or database-corruption failure**.

A separate class of concerns remains: **the intent expressed by an administrative UI can differ from the permissions actually submitted; multi-call administrative writes do not share one transaction; local report preferences suppress storage errors while UI claims success; report export overwrites are not staged; and an obsolete release verification command references a missing file**.

### Prioritized register

| ID | Priority | Evidence classification | Area | Problem | Reach / condition |
|---|---|---|---|---|---|
| IA-01 | **P2 High if maintenance UI is used** | Confirmed UI → parameter path | User administration | **Edit Account** unintentionally includes unsaved role checkbox changes in `SaveUser` | `UsersPage` is deliberately absent from ordinary navigation; applies if invoked for maintenance |
| IA-02 | **P2** | Confirmed separate-call transaction boundary; partial outcome requires injected failure | Role administration | Role definition and permission set are saved in **two independent SQL calls** | Controlled/hidden `RolesPage` only |
| IA-03 | **P2/P3** | Confirmed missing shell write contract; adverse navigation outcome needs runtime exercise | Administrative writes | User/role administration pages don't advertise active writes through `IBusyPage` | Matters if maintenance UI is opened and user leaves/closes during a write |
| DP-01 | **P2** | Confirmed failure-response mismatch | Report saved views | `SaveView`/`DeleteView` can **claim success after a failed settings-file write** | Windows access denied, disk full, locked file, etc. |
| DP-02 | **P2/P3** | Confirmed corrupt-file recovery and overwrite path | Report preferences | Malformed settings JSON is treated as empty; next save **overwrites recoverable settings** without quarantining original | File already unreadable/corrupt; later report preference write |
| DP-03 | **P2/P3** | Confirmed non-atomic write strategy; data loss conditional on interrupted overwrite | CSV/PDF exports | A failed replacement export can leave an existing user report truncated/changed | User chooses to overwrite an existing export; write fails |
| DP-04 | **P3** | Confirmed order of validation and allocation | School logo | 2 MB logo size check is applied **after reading the entire file into RAM** | Unexpectedly large selected file |
| RT-01 | **P3 / release-tool defect** | Confirmed repository inventory and script path mismatch | `VerifyBaseline.ps1` | Verification script depends on `Docs/FROZEN_BASELINE.json`, which is absent from the tracked repository | Invoking this optional script from checkout |
| V-01 | **Release verification** | Not a confirmed exploit or functional failure | Supply chain / authorization | Cached tools, Windows UI and real DB tests need assurance beyond code-level review | Documented in `6; **not** asserted as new defects |

**Priority scope:** IA-01 and IA-02 affect a hidden, intentionally restricted administrative module, not a feature presently exposed to normal school operators. The report does **not** imply that a normal user can reach or exploit those pages. DP-01/02/03 concern report/settings files, **not SQL school-data loss**. No P1 finding is claimed.

## 2. Detailed confirmed / conditional findings and proposed fixes

### IA-01 — Editing an account silently commits pending role changes

**Source evidence:** [Pages/Administration/UsersPage.cs L1414–1433](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/UsersPage.cs#L1414-L1433); [Pages/Administration/UsersPage.cs L1693–1723](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/UsersPage.cs#L1693-L1723); [Pages/Administration/UsersPage.cs L1761–1801](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/UsersPage.cs#L1761-L1801); [Pages/Administration/UsersPage.cs L1873–1955](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/UsersPage.cs#L1873-L1955).

**Problem explained**

1. The Users page retains two distinct role sets: the *saved* roles (`savedRoleNames` / `SavedRoleIds()`) and the checkboxes currently ticked (`SelectedRoleIds()`). It explicitly labels their difference as **unsaved role changes** and offers a separate **Save Roles** action.
2. The **Edit Account** action is enabled for a selected manageable user without checking `HasUnsavedRoleChanges()`. Its dialog saves via `SaveAccountAsync(..., roleIdsOverride: null)`.
3. When `roleIdsOverride` is null, `SaveAccountAsync` reads the **currently checked role boxes**. It then calls `CallSaveUserAsync`, serializing those role IDs into `@RoleIdsJson` for `SaveUser`.
4. Therefore, editing only the username, password or forced-password-change flag may also submit role grants/removals that the operator had **not** committed through **Save Roles**. The separate Save Roles removal-warning workflow can be bypassed unintentionally.

**Cross-checks:** The saved-vs-selected distinction is real (not a cosmetic label); grid-change/navigation/Deselect paths warn about unsaved roles. `RefreshEditAccountEnabled()` does not block account editing while roles differ. The target `SaveUser` call **does** receive the selected role JSON; comments in this method explicitly confirm an empty JSON array removes role memberships. The normal navigation has no `new UsersPage(...)` entry; the file itself says it is for controlled maintenance.

**Impact:** Accidental privilege escalation, unintended role removal, or loss of access during controlled user maintenance, conditional on this page being opened. Actual SQL authorization remains the separate authority.

**Proposed fix:** On **Edit Account**, pass the **saved** role set (`SavedRoleIds()`), not live unsaved checkboxes; leave permission changes exclusively to **Save Roles**. As additional defense, reject the edit with a clear unsaved-role decision if pending roles are present. Keep the established rule that a newly created normal account may have zero roles (do not silently grant a default).

**Required acceptance tests:** Start with saved roles A+B, untick B and tick C, do **not** press Save Roles; edit only username or password. Verify SQL membership remains A+B and the role draft remains visibly unsaved. Confirm Save Roles separately commits B removal/C addition after existing warning. Repeat with zero selected roles, same user, role removal, cancellation, and limited privileges.

### IA-02 — Role metadata and permissions can be committed separately

**Source evidence:** [Pages/Administration/RolesPage.cs L804–871](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/RolesPage.cs#L804-L871); [Pages/Administration/RolesPage.cs L854–935](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/RolesPage.cs#L854-L935).

**Problem explained**

`SaveRoleFromDialogAsync` awaits `SaveRoleDefinitionAsync` (which calls stored procedure `SaveRole`), **then** separately awaits `ExecuteUserAsync("SaveRolePermissions", ...)`. Each helper opens and executes its own database operation; the C# caller does not share a `SqlTransaction` or invoke a single procedure enclosing both changes.

If `SaveRole` commits and `SaveRolePermissions` then fails (SQL permission error, validation issue, disconnect, etc.), the role name/description or newly created role can persist while the permission update does not. The page catches the exception, reports failure, and reopens the dialog; it does not distinguish a completely rolled-back operation from a *partially committed* role.

**Double-check / bounds:** This conclusion concerns the **absence of a shared transaction across the two C# procedure invocations**. It does not assume anything about the SQL body other than each separate successful call being capable of committing. A failing second call is required for the partial outcome. The operation is on intentionally non-navigable maintenance UI, and creation with an empty permission set is **normally an approved zero-access default**, not itself a bug.

**Proposed fix:** Create an approved transactional SQL procedure that validates role identity and permission membership together, or add a single-connection C# transaction wrapper around both procedure calls and review the procedures' nested-transaction/rollback semantics. Return the authoritative role ID and final permission state; on an ambiguous outcome show “check role before retrying” rather than opening a create dialog that may duplicate the name. Use a numbered, reviewed migration **only** if a new SQL procedure is needed.

**Required acceptance tests:** Make the permission-update step fail after role creation/rename. Verify all-or-nothing final SQL state and role list; retry without duplicates; run concurrent editor and permission-denial scenarios. Do not relax protected Administrator role rules.

### IA-03 — Administrative async writes are not represented in shell-level busy guards

**Source evidence:** [Pages/Administration/UsersPage.cs L1–8](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/UsersPage.cs#L1-L8); [Pages/Administration/UsersPage.cs L1640–1729](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/UsersPage.cs#L1640-L1729); [Pages/Administration/RolesPage.cs L1–8](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/RolesPage.cs#L1-L8); [Pages/Administration/RolesPage.cs L87–110](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Administration/RolesPage.cs#L87-L110); [Core/V2Page.cs L719–783](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Core/V2Page.cs#L719-L783); [MainDashboardForm.cs L1597–1619](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/MainDashboardForm.cs#L1597-L1619).

**Problem explained**

- `UsersPage` implements `IUnsavedChanges` but **not** `IBusyPage`; `RolesPage` extends `V2Page` without either busy contract.
- Both pages perform asynchronous privileged writes while remaining capable of handling UI actions.
- `AddBusyAction` disables its own button and shows progress through `BusyScope`. Its base implementation expressly does **not** make the shell treat the page as busy. `MainDashboardForm.ConfirmLeaveCurrentPage` blocks active work only for pages implementing `IBusyPage`.
- Therefore, if either maintenance page is made available, a navigation/close gesture is not reliably blocked merely because an administrative write is in flight. A dirty-role prompt is not a substitute: clean forms may still be saving.

**Impact qualification:** Both pages are intentionally not exposed in current regular navigation; no ordinary-operator reachability is established. The potentially adverse outcome (UI disposal while an awaited call resumes, sign-out during write, confused failure reporting) needs a controlled Windows test.

**Proposed fix:** If maintenance UI remains usable, implement `IBusyPage` in both pages and track **all** save paths, including role definition + permission saves and account dialog saves. Capture the acting user and target values before `await`; block navigation and affected controls for the full write outcome. Never cancel a submitted permission write merely to satisfy the UI guard.

**Tests:** Initiate a deliberately delayed privileged save, then attempt workspace navigation, window close and sign-out. Verify the action is refused until the write completes; then test success, SQL refusal and ambiguous completion. Leave the hidden-by-policy menu state unchanged unless separately authorized.

### DP-01 — Report saved-view actions return success after persistence failure

**Source evidence:** [Core/ReportLayouts.cs L64–78](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Core/ReportLayouts.cs#L64-L78); [Core/ReportLayouts.cs L118–154](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Core/ReportLayouts.cs#L118-L154); [Pages/Reports/Students/StudentReportsPage.cs L1394–1432](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Reports/Students/StudentReportsPage.cs#L1394-L1432).

**Problem explained**

`ReportLayoutStore.Save(...)` catches **all** exceptions from directory creation, serialization and `AtomicTextFile.Write`, logs them, and returns `void`. `SaveView` likewise returns `void`. `DeleteView` returns whether an entry was removed from a temporary in-memory object, not whether persistence succeeded. The Student Reports page unconditionally refreshes its dropdown and announces that the view was “saved” or “deleted” after calling those methods.

When the settings folder is read-only or storage is full, no changed JSON reaches disk but the application still displays a successful save/delete message. The user may lose the chosen report setup on restart.

**Double-check:** Atomic file replacement itself is an improvement; the problem is **error propagation and final operator feedback**, not lack of atomicity in `AtomicTextFile`. The failure is conditional on an actual I/O or serialization error and was not reproduced here.

**Proposed fix:** Make `ReportLayoutStore.Save` return an explicit persisted result or throw a typed storage exception (with existing logging). `SaveView`/`DeleteView` should propagate that result, and their UI handlers should only claim success after verification. Keep the previous on-disk preferences intact on failure, and show the path to retry/fix permissions without showing an imaginary saved view.

**Tests:** Denied `%AppData%` folder, readonly JSON, full drive, invalid directory, successful normal save/delete and reopen. Confirm no success message is displayed when file persistence fails, and previous views survive.

### DP-02 — Corrupt report-settings JSON is silently replaced on the next write

**Source evidence:** [Core/ReportLayouts.cs L46–79](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Core/ReportLayouts.cs#L46-L79); [Core/ReportLayouts.cs L82–107](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Core/ReportLayouts.cs#L82-L107); [Pages/Reports/Students/StudentReportsPage.cs L1180–1190](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Reports/Students/StudentReportsPage.cs#L1180-L1190).

**Problem explained**

`ReportLayoutStore.Load()` attempts to deserialize the entire user JSON file. If read/deserialization fails, it logs the exception and returns a brand-new empty `ReportUserData`. The next `SetPreference`, `SetColumns` or `SaveView` modifies that blank data and publishes it with `AtomicTextFile.Write` at the **same path**. Even an automatic “last report scope” preference update can therefore replace the pre-existing damaged JSON without preserving it for repair or recovery.

**Impact:** Loss of the prior set of saved report views, layouts and preferences when the original file contains some recoverable content but cannot be deserialized in full. This is a deterministic recovery-policy weakness **given an already corrupt file**, not proof the application's atomic writes normally corrupt preferences.

**Proposed fix:** Quarantine the unreadable file under a timestamped, collision-safe name or retain an untouched copy, and distinguish a fresh missing file from a corrupt one. Inform the operator before reset/replacement or provide a recover/reset path. If recovery is possible, preserve valid saved views from the damaged file without weakening serializer safety.

**Tests:** Truncated JSON, invalid JSON token, unreadable bytes, unexpected schema, external file lock and missing file. After triggering an automatic preference save, verify a damaged original remains recoverable and never disappears without an explicit recovery decision.

### DP-03 — Replacing an exported report is not failure-atomic

**Source evidence:** [Core/V2Page.cs L1457–1535](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Core/V2Page.cs#L1457-L1535); [Core/ReportExportBuilder.cs L52–72](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Core/ReportExportBuilder.cs#L52-L72); [Core/PdfTableExporter.cs L79–95](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Core/PdfTableExporter.cs#L79-L95).

**Problem explained**

The export dialog allows the user to select an existing PDF/CSV filename with `OverwritePrompt = true`. CSV output is written directly using `File.WriteAllText(finalPath, ...)`, and PDF output is saved directly using `PdfDocument.Save(finalPath)`. Those operations do not first build a complete file at a unique temporary destination and then atomically replace the old file.

If the write fails after it opens/truncates the destination, the previously valid user export can be lost or damaged. The UI checks for a nonempty result file after successful execution, but a **nonempty file alone is not an integrity check** and cannot restore a previous export that was already overwritten.

**Important scope:** This concerns independently exported PDFs/CSVs, **not the application's SQL payment/admission records**. Overwrite was intentionally allowed; the missing protection is preservation of the old file on failure, not the lack of a prompt.

**Proposed fix:** Export to a unique temporary file **in the same destination directory**; validate final output completion (and parse format where practical); flush/close before atomically moving/replacing the destination. On failure delete only the temp file and keep the previous user export intact. Reuse the verified `AtomicTextFile` pattern where appropriate while preserving binary PDF handling.

**Tests:** Existing file, successful overwrite, simulated exception mid-write, access denied, disk exhaustion, temp cleanup, PDF generator exception, CSV failure, and file lock. Hash the old file before an injected failure and prove it is unchanged afterwards.

### DP-04 — School logo file is fully read before the stated 2 MB limit

**Source evidence:** [Pages/Setup/SchoolProfilePage.cs L230–269](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Pages/Setup/SchoolProfilePage.cs#L230-L269).

**Problem explained**

`ChooseLogoAsync` performs `await File.ReadAllBytesAsync(dialog.FileName)` first, **then** checks `bytes.Length > 2_000_000` and refuses the input. A very large selected file is therefore allocated in memory before validation. The JPG/PNG filter is a file-picker convenience, not a reliable byte-size guard; a renamed/large file can still be selected.

**Impact:** Possible responsiveness and memory pressure (especially on low-RAM school computers) despite a UI that promises a 2 MB cap. No out-of-memory exception was reproduced; the incorrect preflight order is directly visible in code.

**Proposed fix:** Inspect `FileInfo.Length` or stream metadata before allocating; reject oversized inputs first, then read through a bounded stream and revalidate the actual bytes to handle file growth/races. Continue validating image decoding and avoid retaining unnecessarily large intermediate buffers. Do not alter existing school-logo data or school-profile save semantics.

**Tests:** Exactly 2,000,000 bytes, one byte larger, 100 MB file, corrupt PNG/JPEG, file growing between size check/read, canceled picker, and GDI/memory measurement.

### RT-01 — Frozen-baseline verification script references a nonexistent manifest

**Source evidence:** [Installer/VerifyBaseline.ps1 L1–26](https://github.com/farhan2166-creator/FMSC-Database/blob/cf31e953eb0d25efb5a771221958f3343cd6e460/Installer/VerifyBaseline.ps1#L1-L26) and tracked repository inventory at `cf31e953` (no `Docs/` directory or `Docs/FROZEN_BASELINE.json`).

**Problem explained**

`VerifyBaseline.ps1` unconditionally executes `Get-Content -Raw ...\Docs\FROZEN_BASELINE.json`. The current tracked repository has no such file. Running this utility from a clean current checkout therefore cannot finish its intended fingerprint verification and fails while reading the manifest.

**Cross-check and scope:** The current standard installer has a separate migration/release verification path, and this review did **not** find `VerifyBaseline.ps1` invoked by the main installer. It is a **stale optional verification tool**, not proof the standard application installer is broken.

**Proposed fix:** Establish whether the frozen-baseline verifier is intentionally retired. If retired, remove or clearly mark it obsolete in a separately approved code cleanup. If supported, create/approve a current deterministic manifest and update exclusion rules to reflect the actual release layout; add a CI/PowerShell test that runs the tool from a clean checkout. Do not fabricate a manifest or silently treat failures as passing.

**Tests:** Execute verifier in clean Windows checkout; missing manifest fails clearly; valid manifest passes; changed source file fails; added unauthorized file fails; test with expected release assets/paths.

## 3. Confirmed protections and deliberately excluded false positives

This independent review checked relevant safeguards before deciding whether an observation was actionable.

| Topic checked | Evidence found | Disposition |
|---|---|---|
| Privileged admin reachability | Both `UsersPage` and `RolesPage` explicitly state they are not exposed in normal navigation; search found no active `new UsersPage(...)` or `new RolesPage(...)` call sites outside their definitions | **Not** reported as a normal-user privilege escalation |
| SQL authority | User/role saves invoke user-scoped `DatabaseHelper` procedures; SQL authorization remains authoritative | No claim of bypassing stored-procedure permission checks |
| Normal account zero-role policy | `CallSaveUserAsync` documents intentional empty role arrays; new-account flow explicitly provides an empty role list | No demand to assign implicit default privileges |
| Existing report preference atomicity | `AtomicTextFile` uses unique temporary files, flushed data, then `File.Move(... overwrite:true)` | No allegation that its **normal** save truncates previous settings |
| CSV formula safety | `ReportExportBuilder.Cell` calls `CsvSafety.Neutralise` and quotes/escapes cell text | No unverified spreadsheet injection exploit claimed |
| Financial collection | `CollectionDiscountWriter` uses one serializable transaction for charge discounts and collection, plus a durable recovery intent and no automatic retry on unknown commit | No finding alleging partial family collection here |
| Database upgrade discipline | Installer preserves existing MDF/LDF, verifies migration numbering and backup; `Migrations/2.sql` is transactional and enforces whole-rupee constraints | No DACPAC/replacement-MDF update recommendation |
| Pinned prerequisite download | `Installer/core.ps1` pins the downloaded go-sqlcmd archive SHA-256; downloaded checksum is validated | No blanket assertion that downloads lack integrity checks |
| Cached `sqlcmd` executable | A pre-existing `Tools\sqlcmd.exe` or PATH binary is accepted without the download-pin step | **Conditional release-hardening question**; requires trusted packaging and file-permission threat-model review before labeling a vulnerability |
| Prior dashboard/navigation findings | The current HEAD `cf31e953` adds dashboard/navigation fixes after the earlier follow-up audit; those files changed while this audit ran | **Not re-reported as unresolved** based on older revision |

## 4. Recommended remediation sequence

1. **Security maintenance readiness — IA-01:** Isolate **Edit Account** from pending role edits; test exact saved-vs-selected SQL membership. This is the highest-confidence and highest-impact behavior **if** maintenance UI is used.
2. **Administrative transaction integrity — IA-02:** Make role definition plus permission assignment atomic; add fail-between-steps tests and safe re-open/retry messages.
3. **Administrative write lifecycle — IA-03:** Track `IBusyPage` state for every privileged save. Keep these pages hidden from normal menu unless their business policy is explicitly changed.
4. **Save honesty — DP-01:** Propagate settings-file failure to UI, distinguishing attempted save from confirmed persisted view.
5. **Preference recovery — DP-02:** Preserve corrupted JSON before writing defaults; test automatic scope-saving side effects.
6. **Export preservation — DP-03:** Generate CSV/PDF through same-directory temp files and replace only on verified completion.
7. **Input preflight — DP-04:** Enforce logo input size *before* allocation; use bounded reading.
8. **Release tooling — RT-01:** Retire or repair stale frozen-baseline verifier; test from a clean checkout.

None of IA-01, IA-03, DP-01 through DP-04 or RT-01 inherently requires a database migration. IA-02 **may** require an approved stored procedure/migration for all-or-nothing semantics; verify the existing procedure internals before selecting that implementation.

## 5. Acceptance scenarios / negative tests before marking DONE

These tests are **not claimed to have run** in this review:

| Scenario | Setup / injected condition | Expected result |
|---|---|---|
| Unsaved role leak to account edit | Saved roles A/B, checkbox draft A/C, Edit Account username only | DB roles remain A/B; A/C remains unsaved until explicit Save Roles |
| Roles save atomicity | Fail permissions phase after successful role-definition phase | Neither definition nor permissions partially committed; clear reconciliation |
| Admin write navigation | Delayed `SaveUser` / `SaveRolePermissions` and operator closes page | Busy guard blocks page/sign-out/close until outcome established |
| Report view save failure | Access denied to `%AppData%\FMSC_SMS\ReportLayouts` | Explicit failure; no false “View saved” message; prior JSON preserved |
| Damaged settings | Corrupt JSON then run report that persists LastScope | Original JSON is quarantined/preserved, not destroyed |
| Export overwrite failure | Existing valid CSV/PDF; inject failed write | Existing bytes/hash identical after failure; temp removed |
| Oversized school logo | Choose a 100 MB bogus PNG | Rejected without loading whole file into managed buffer |
| Baseline script clean checkout | Run `VerifyBaseline.ps1` on published repository state | Supported path passes with reviewed manifest or script intentionally retired |
| Role/session consistency | SQL revoke during open maintenance session; stale frontend permission display | Backend denies unauthorized writes and app accurately refreshes/communicates rights |

## 6. Release gates and unproven areas

**Runtime/Windows:** The review used the current GitHub C# and PowerShell source; it did not run a Windows smoke harness, exercise a real school database, simulate an actual disk-full filesystem, or inspect SQL stored-procedure bodies within the DACPAC. Existing reported test results must not be repackaged as test execution from this audit.

**Data preservation:** First installation may place the original school MDF/LDF on the user computer. **Future updates must preserve these files and apply ordered SQL migration scripts**; DACPAC is a review/reference artifact only. Rollback/recovery must use approved SQL backup procedures, not replacement files.

**Privilege model:** User/role administration is currently intentionally excluded from normal application navigation. If it is never shipped or invoked, IA-01/02/03 do not constitute a presently reachable end-user defect; they remain maintenance-code correctness risks. Confirm deployment configuration before assigning a production release blocker to them.

**Supply-chain check:** Downloaded go-sqlcmd archives have an embedded pinned hash; a cached or PATH executable is accepted without the same SHA verification. Review installer package custody, admin elevation, OS access controls and signature verification *before* declaring a security weakness. No claim of malware was established.

**Scope limit:** Source inventory was examined across the repository and targeted flows were traced through caller, callee and protective guards. This is a deep **targeted cross-layer audit**, **not** an exhaustive statement of proof over every line of all 379 tracked files. Large SQL procedure bodies, runtime Windows resources, DB-specific multi-user races and real deployment conditions need additional direct testing.

## 7. Final assessment

**Eight reportable source-level findings** were recorded: IA-01/02/03, DP-01/02/03/04, and RT-01. They cover separate failure domains and do not repeat the prior admissions/photo/navigation findings. The strongest issue is an **administrative intent mismatch**: saving account details sends role changes which the UI expressly labels unsaved. This is important when the maintenance page is used, even though the current application intentionally hides it from normal navigation.

The application still has real existing protections and no newly proven P1 school-database corruption was identified in this review. **Nothing is marked fixed or production-verified by producing this report.** Proposed changes should be small, test-backed, and should preserve business logic and the existing database migration/update policy.

**Repository mutation for this task:** This Markdown report only.
