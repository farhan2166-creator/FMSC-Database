# FMSC_SMS — Independent Security, Durability, and Release-Integrity Audit

**Current implementation update:** DP-04, RT-01, DP-01, DP-02 and DP-03 are
implemented in source at the owner's request. IA-01/02/03 were excluded from that
pass, then implemented in the subsequently authorized Users/Roles restructuring.
The two-role policy is implemented in section 11; owner-authorized Account menu
navigation follows in section 12, and colored lightweight themes in section 13.
See sections 8–13 below; the original review remains
pinned to its stated baseline and is not native deployment acceptance.

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

## 8. Owner-authorized implementation — 13 October 2026

The owner requested the five non-Users/Roles fixes, in two passes. The original
review above remains historical evidence for its pinned commit. Current status:

| ID | Current source disposition | Implementation |
|---|---|---|
| DP-04 | Implemented | School logo uses a bounded file reader. Empty/over-2,000,000-byte files are refused before buffer allocation, exact-limit input is accepted, short/growing reads are refused, and the previous preview/data stay intact until decoding succeeds. |
| RT-01 | Retired with explicit refusal | The obsolete freeze verifier has no current callers or maintained manifest. Its retained entry point now fails clearly with supported `VerifySchemaArtifact.ps1` and validation instructions; it never invents a source-freeze pass. Standard publishing continues to use the approved schema verifier. |
| DP-01 | Implemented | Report mutators return persistence success/change/error explicitly. View Save/Delete report success only after publication; failed operations keep the current selections/dropdown. Missing views are reported as already absent. Column-choice persistence failures also explain that the current selection is session-only. |
| DP-02 | Implemented | Report reads may use defaults without changing the source. Mutations refuse unreadable/locked originals. Corrupt JSON/invalid setting shapes require a unique adjacent `.corrupt-<UTC>-<guid>.bak`, containing the exact original bytes and flushed to disk, before replacement. Failed backup prevents replacement; failed publication retains original and any completed backup. Reads/no-op mutations do not repeatedly quarantine files. |
| DP-03 | Implemented | CSV, table PDFs and RDLC-preview PDFs generate a unique temporary file beside the chosen destination, close/flush and validate it, then replace by same-directory rename. Failed generation/validation/replacement preserves the previous export and cleans only the temporary file. PDFs are parsed and must contain pages; CSV uses strict UTF-8, keeps its BOM/escaping/formula safeguards, and streams rows instead of keeping a full second text copy. |
| IA-01/02/03 | Excluded by owner | UsersPage and RolesPage source were not changed or exposed in navigation by this pass. |

**Executed checks:** The portable production-helper suite passes **471
assertions**, including **82 new file-persistence checks**. The new cases cover
logo size boundaries, sparse 100 MB input, growing/truncated/cancelled reads,
actual file-sharing refusal, save/delete write failures, corrupt/null/incomplete
JSON, exact-byte recovery backups, backup and post-backup publication failures,
UTF-8 BOM compatibility, interrupted/empty/invalid exports, refused replacement,
temporary cleanup and actual CSV encoding failure while preserving prior bytes.
The application and native Windows smoke harness cross-build with zero warnings
and errors. PowerShell execution confirms that the retired verifier exits 1 with
its guidance and the supported approved-artifact verifier exits 0.

**Still pending:** Native PDF-generation/parse/file-lock fixtures are compiled,
not executed on this Linux host. Windows report-view dialogs/status, RDLC export,
real access-denied/disk-full behavior and deployed-filesystem acceptance remain
release checks. Injected I/O failures verify controlled recovery paths, not every
storage hardware failure. This pass changes no SQL, migration, DACPAC, school
records, stored images or Users/Roles functionality.

## 9. Completed Users/Roles maintenance flow — 13 October 2026

The owner subsequently authorized a complete, simpler and stronger Users/Roles
editing flow. The exclusion in section 8 applies to that earlier pass only.

| Finding | Current implementation |
|---|---|
| IA-01 | Account details and assigned roles form one explicit inline draft and one **Save User** action. Separate Edit Account/Save Roles dialogs/actions are retired. Password reset is a separate password-only dialog; it is disabled/refused while account/access edits are pending, uses the saved record rather than live checkboxes, and rechecks the exact snapshot before writing. |
| IA-02 | **Save Role** captures name, description and permissions in one request. Existing `SaveRole` and `SaveRolePermissions` execute on one connection inside an enclosing serializable C# transaction. Each nested procedure must preserve the enclosing transaction count; IDs are validated before the outer commit. A failed second step rolls back the definition, permissions, membership side effects and audit entries together. No SQL procedure/schema change or migration is required. |
| IA-03 | Both pages share `IBusyPage`/dirty-state guards across reads, account saves, combined role saves, password hashing/reset, post-save refresh and read-only recovery. Switching records, New, Reload, keyboard Save and affected controls respect those guards. |

**Simplified operator flow:** New User → username/initial password/password-change
requirement → explicit role selection → Save User. Existing User → edit displayed
account/role draft → Save User. Clean saved User → Reset Password, which changes
only credentials and its next-sign-in requirement. New/existing Role → name,
description and filtered task/area permission grid → Save Role. Each editor has a
compact searchable record list, selected-record/state heading, assignment filter
and effective/proposed access summary. Filters use cached DataViews and 150 ms
debounce; hidden checked rows remain selected. Large per-role/per-permission
decorative control trees and redundant dialogs are removed. Fewer source lines
are not claimed as measured deployed performance.

**Additional recovery safeguards:** A coherent catalog/record snapshot is checked
under the write transaction before either procedure is submitted. Competing
edits, catalog/grant meaning changes and changed affected memberships are refused
without overwriting the newer state. A successful write followed by failed UI
refresh retains confirmed identity/values and requires Reload before another
write. Lost commit replies/failed rollback remain unconfirmed and expose Check
Saved Changes; that action only reads and verifies submitted values, including
an opaque credential proof for password changes. It can establish a complete
save, unchanged original/absent new record, or conflicting stored values. It never
automatically repeats a write. Binding failure restores the entire previous
editor, including unsubmitted new-account password inputs. Raw derived hashes
and salts are not added to editor models, logs or audit display; temporary password
buffers are zeroed, plaintext inputs are cleared after confirmed success, and
PBKDF2 remains compatible with sign-in (100,000 SHA-512 iterations, 16-byte salt,
64-byte derived hash) off the UI thread. Cleanup cannot turn a confirmed commit
into an apparent failed save.

**Role-definition scope:** Role names, memberships and grants are database data;
the DACPAC supplies their schema/procedures, not the live school's catalog. No
defined role is merged, renamed, deleted or automatically granted by this
restructure. Permissions are grouped/displayed more clearly, and known viewing
dependencies are explained without silently adding permissions. Future role
consolidation needs the actual catalog, affected memberships, exact permission
sets and business responsibility review before an explicit data migration.
Administrator role/account protection includes both primary-role and membership
paths. Multiple roles, deliberate zero-role/zero-permission access and the legacy
primary-reference rule remain intact. Database permissions are checked on
operations; menus/session permissions refresh on sign-out/sign-in. The existing
hidden maintenance navigation policy is unchanged.

**Executed verification:** **522 portable assertions pass**, including 51 new
production administration model/coordinator checks. **39 native SQL checks pass**
using the production `AdministrationStore` against the approved DACPAC deployed
to a new, isolated SQL Server 2022 Developer container and synthetic
`FMSC_AuditChecks_Admin_20261013` database. They verify user/role round trips,
password compatibility/reset isolation, zero access, both Administrator
protection paths, stale account/role/catalog refusal, captured-actor audit,
read-only write denial, actual trigger-injected second-step rollback (including
audit/membership effects), and competing simultaneous editors. Ordinary login
counters do not invalidate an otherwise unchanged account draft. The application
and Windows smoke harness cross-build with zero warnings/errors; the latter now
uses the production internal UI helpers instead of linked duplicate copies.

**Native UI fixtures remain compiled/unrun:** They cover unified saves, dirty
selection guards, same-record draft retention, password/reset separation,
new-account validation/secret clearing, filtering hidden checks, stale/unconfirmed
recovery, atomic binding rollback including initial passwords, busy/disposed
completion, protected/read-only editors and compact/wide layouts in Professional
Simple and Ocean Blue. Actual Windows painting, DPI/keyboard/modal behavior,
target LocalDB/school-clone acceptance and session refresh remain release checks.
No school database or release MDF/LDF was opened or changed. SQL, migrations,
DACPAC and their approved fingerprints remain unchanged.

## 10. Data-bearing DACPAC review and role-change scope — 13 October 2026

**Reviewed input:** owner's commit `ad60bbb` (`dacpac`), package SHA-256
`4c2eb8cbb76ce892666708b5397c9b4ee1f0fdca529ea4cac72b8cdbca6afabe`.
This artifact actually includes BCP table data. It was published with native DacFx
into a newly created, loopback-only SQL Server 2022 Developer container database
`FMSC_AuditChecks_RoleData_20261013`. No installed school database or detached
release files were opened or updated. Original catalog observations below were
captured before the production-writer tests added or edited fixture records.

### Actual role definitions and assignments

| Role | Stored permission rows | Assigned accounts | Observed responsibility |
|---|---:|---:|---|
| Administrator | 0 | 1 | Protected; SQL and C# Administrator bypass supplies full access. Zero explicit grants is expected. |
| Accountant | 6 | 1 | Student viewing, fee viewing/collection, broad reports, fee reports and annual fee statements. No `Fees.Adjust` grant. |
| Students | 0 | 1 | Empty role; contributes no permissions. Its intended business purpose is unknown. |
| Front Desk | 6 | 0 | Admissions, student viewing/administration, fee viewing/collection and broad reports. |

There are **57 permission definitions, 12 role-permission mappings, three role
memberships and one account**. The same account has Administrator, Accountant
and Students memberships. Accountant/Students therefore add no effective access
to this account beyond Administrator. This snapshot cannot demonstrate separate
staff operation; fresh role-only probe accounts were used in the disposable copy.

Accountant grants: `Students.View`, `Fees.View`, `Fees.Collect`, `Reports.View`,
`Fees.Reports.View`, `Fees.Statement.View`.
Front Desk grants: `Students.View`, `Fees.View`, `Students.Administer`,
`Fees.Collect`, `Reports.View`, `Students.Admit`.

### Confirmed behavior and potential inconsistencies

- `Students.Administer` is broad maintenance access, including saved student
  editing/photos, re-admission and multiple status/lifecycle/legacy operations.
  Front Desk's stored description mentions only admissions and fee collection;
  the grant is broader than that description. Whether this is intended is a
  business decision. Removing it immediately would also remove existing useful
  detail/photo operations. The current source references this code in ten C#
  files and at least 24 procedure authorization checks.
- `Reports.View` opens student and finance categories in `ReportsHomePage` and
  guards their SQL reports. Accountant and Front Desk can therefore see broad
  reports; `Fees.Reports.View` does not isolate the report hub to finance. It
  currently also controls billing-status checks.
- Granular catalog codes such as `Students.Edit`, `Students.Photo.Manage` and
  `Students.ReAdmit` have no corresponding action gates or procedure permission
  checks in the reviewed source. Checking them alone does not enable those
  workflows. They must not be presented as functional substitutes for
  `Students.Administer` before C# and SQL are aligned.
- `System.Users`/`System.Roles` have legacy names that say "Manage", while the
  completed editors use them as read prerequisites and `.Manage` codes for
  writes. No staff role in this snapshot grants either management path.
- `System.Roles.Deactivate` exists in the catalog, but the schema has no role
  active flag and the application has no matching workflow. It cannot currently
  implement role deactivation. This is dormant metadata, not a finished feature.

These observations do not authorize automatic deletion, renaming, redistribution
or expansion of existing grants. The owner requested the scope before choosing
whether to redefine/reorganize roles. Existing definitions, memberships,
Administrator protection, zero-access behavior and hidden maintenance navigation
remain unchanged.

### Scope before choosing a role redesign

| Possible change | Scope | Required implementation |
|---|---|---|
| Use three operator roles: Administrator, Accountant, Front Desk; retire Students if it has no intended purpose | Small, data only | Inventory all assigned accounts and legacy `Users.RoleId` references; review responsibility matrix; transactionally remove/reassign only approved memberships and role. No permissions need merging. |
| Improve descriptions and read/manage labels | Small | Update selected catalog labels/descriptions after agreeing their meaning; preserve permission codes and IDs. |
| Limit Front Desk to admission, ordinary detail/photo edits and collection | Medium, C# and SQL | Define allowed actions first; replace broad maintenance guards selectively with functional granular permissions in both layers; migrate reviewed grants; verify allowed operations and denied lifecycle/correction actions. |
| Limit Accountant/Front Desk reports by responsibility | Medium, report navigation and SQL | Choose report categories; align hub visibility, report loading, exports and SQL checks; migrate reviewed grants and test both allowed and refused paths. |
| Remove/merge dormant permission definitions | Broader cleanup | Map all UI/SQL/legacy consumers and assignments first; preserve code/ID compatibility where needed. An absent C# literal alone does not prove a permission is safe to delete. |

Recommended next decision: agree an action matrix for the three operator roles,
then implement only the required guard/grant changes. Role count alone does not
resolve the broad-versus-granular permission mismatch. Any chosen production SQL
changes will be numbered scripts in **Migrations/**. **No SQL migration is
required by this artifact review or the already completed administrative flow.**

### Verification and reviewed artifact metadata

- Native DacFx successfully restored schema and embedded data. SQL counts and
  all twelve grants matched independently decoded role-catalog BCP records.
- Administrator returned all 57 effective permissions. All eight whole-rupee
  constraints were enabled and trusted; database version rows include version 2.
- **39 production administration SQL checks passed again** on this imported
  disposable copy: combined saves, credentials, protection, stale/catalog guards,
  concurrent edits, second-step rollback/audit rollback and read-only recovery.
  They modified only disposable fixtures; they are not real staff GUI acceptance.
- **12 additional SQL boundary assertions passed** using separately assigned
  Accountant, Students and Front Desk probe accounts. Effective grants matched
  exactly; none could manage users/roles; Accountant was denied fee adjustment;
  Front Desk passed broad student-administration and report checks. Probe account
  writes were rolled back.
- All **273 SQL module fingerprints remain identical**. Model differences are
  SQL Server's `FLOOR` keyword casing, serialization/order of those constraints,
  and identity last-value annotations. Full model comparison after these reviewed
  normalizations found no other changes. Runtime whole-rupee policy is intact.
- Updated approved package/model hashes and startup-contract package provenance
  after that review. Offline policy validation accepts casing of the `FLOOR`
  keyword only; it still rejects different columns, operators or extra SQL.
  Full package/model/module fingerprint checks remain strict.
- **12 artifact regression tests and 522 portable assertions pass.** Native
  Windows GUI/DPI and release acceptance remain pending.

The disposable SQL container and private test credentials were removed after
verification. No role definitions, production procedures, schema versions or
DACPAC bytes were changed by this review.

## 11. Owner-approved two-role policy and consistent permissions — 13 October 2026

The owner subsequently approved keeping **Administrator and Accountant only**, with
Administrator always holding full authority, and explicitly chose to preserve
Accountant's current access. This supersedes section 10's pending role decision.

**Implemented SQL update: `Migrations/3.sql`.** The script requires version 2,
uses one transaction for data, seven procedure definitions, audit and version 3,
and makes repeat execution at version 3 a no-op. Existing Accountant grants and
IDs remain intact. In the supplied snapshot these are exactly `Students.View`,
`Fees.View`, `Fees.Collect`, `Reports.View`, `Fees.Reports.View` and
`Fees.Statement.View`; student and finance reports remain available. The migration
removes every other role and its assignments. It preserves all user accounts,
credentials and password requirements. Accounts with only removed roles receive
no automatic replacement grant: their legacy primary reference becomes
Accountant while an empty membership set continues to mean zero access.

Administrator primary-role or membership accounts are normalized to Administrator
with only that membership. No extra role or permission grant is needed for full
authority. Role saves no longer automatically assign Accountant to Administrator,
and first-run setup now assigns Administrator only.

**Permission consistency:** a shared embedded `Core/PermissionCatalog.json`
contains **28 implemented permissions**. Migration 3 carries the same catalog and
removes 29 dormant/superseded options from the former 57 definitions. No permission
IDs are renumbered and no grant is remapped to a broader capability. Names and
descriptions distinguish viewing from management and make the broad scope of
`Reports.View` and `Students.Administer` explicit. `Fees.Reports.View` accurately
names billing-status checks; annual fee receipts retain `Fees.Statement.View`.

The updated procedures retain their existing parameter signatures/defaults:

| Procedure | Change |
|---|---|
| RequireAnyPermission | Same unconditional Administrator primary/membership bypass as RequirePermission. |
| SaveRole | Edit the existing Accountant only; no new roles/renaming; remove automatic assignment to Administrator. |
| SaveRolePermissions | Require management access, not System.Roles read access; validate the complete JSON array and positive/known supported IDs; require management viewing prerequisites. |
| SaveUser | Protect Administrator membership as well as primary role; validate whole role arrays and the supported two-role selections. |
| BootstrapFirstAdministrator | Assign only Administrator while retaining protected setup locking and credential checks. |
| AddStudentSibling | Require Students.Administer, matching the C# add/remove family controls and DeleteStudentSibling; retire Students.Siblings. |
| GetPermissions | Read via System.Roles/System.Administration, matching the maintained catalog flow; retire System.Permissions. |

C# and installer require **database version 3**. The role editor hides New Role,
keeps role names read-only and rejects direct new/rename calls. It edits only
Accountant's description and checked permissions; Administrator remains full and
protected. Its account display shows actual role membership rather than falsely
checking Accountant to represent full authority. User assignment offers
Accountant or no access for normal accounts.
C# refuses incomplete administrative management/view combinations before saving,
and SQL enforces the same rule. Catalog loading refuses unmigrated/unsupported
role or permission definitions. Existing dirty/stale/busy guards, combined saves,
password-only reset and read-only outcome recovery remain. Hidden maintenance
navigation remains the established policy.

**Verification:** 530 portable assertions (59 administrative), 67 production
administration SQL checks, 15 migration durability assertions, four cross-layer
permission-catalog checks and twelve artifact regression tests pass. The SQL
fixture was restored from the owner's version-2 DACPAC in a disposable loopback
SQL Server 2022 container. The migration tests inject a final version-write failure
and compare role/permission/account/audit state and procedure definitions: all
roll back. They verify older-version refusal, exact Accountant grant preservation,
legacy references, empty access, Administrator membership normalization, unchanged
school record counts and no-op repeat execution. The writer tests cover malformed
payloads, direct SQL read/manage boundaries, full Administrator authority,
concurrent/stale edits, combined-save rollback and exact Accountant capabilities.
Three first-run checks also pass on a separate, empty native schema-only copy:
Administrator-only assignment, full primary/membership authority and refusal of
repeat setup. `Tests/AdministrationSqlChecks/FirstRunChecks.sql` documents that fixture.
The application and Windows smoke harness compile with zero warnings/errors;
interactive Windows/DPI tests remain unrun on this Linux executor.

**Deployment handoff:** apply migration 3 with the established recovery backup and
all application sessions closed, then provide a newly extracted version-3 DACPAC
for review. The supplied version-2 DACPAC and its approved fingerprints were left
unchanged; the artifact verifier passing for it does not certify version-3 release
packaging. Updated MDF/LDF preparation remains the owner's existing release step.
No installed school database was changed. The completed flow is committed source
plus a tested migration, not a claim that the owner's database is already upgraded.

## 12. Owner-authorized dashboard Account navigation — 13 October 2026

The owner explicitly requested Users/Roles access in the dashboard Account menu.
This supersedes the hidden-navigation policy recorded in earlier sections.

The Account dropdown now groups **Users** and **Roles & Permissions** after the
personal username/password commands. Visibility uses the same `AccessRules` read
prerequisites as the pages. Administrator's full authority makes both entries
available; Accountant's preserved six grants expose neither. Separately granted
read access can open a read-only page, while writes retain the existing management
checks in both C# and SQL.

Opening an administrative entry is deferred until the dropdown click completes,
then rechecks the original signed-in user and current read permissions. Navigation
uses the normal dashboard route, breadcrumb/title and dirty/busy/recovery guards.
Role names remain fixed, Administrator remains protected/full, and permission
editing remains the existing combined Roles & Permissions page. No additional
SQL change, migration, DACPAC or data update is required by this navigation change.

The application and Windows smoke harness compile with zero warnings/errors;
the existing 530 portable assertions pass. Interactive Windows menu execution
remains part of native acceptance, not a claim made by the Linux build checks.

## 13. Colored lightweight themes and rendering reliability — 13 October 2026

The owner requested lightweight versions of the existing selectable color themes,
stronger colors and improvements to the shared light-rendering system. The
notification-system proposal remains on hold; no communication feature is added.

**Available appearances:** the four original themes retain their standard
rendering. Account → Change - Appearance/Theme presents each color family as an
explicit **Standard / Lightweight pair**: Ocean Blue, Emerald Green, Professional
Orange and Royal Violet. Professional Simple remains an additional lightweight
choice. The owner's clarification is implemented in menu labels/grouping; no
standard theme was removed or converted to lightweight. All five
share the established flat/no-shadow/immediate-feedback path; forms and report
flows are not duplicated. The existing enum IDs 0–7 and saved names keep their
meaning; four new IDs 8–11 are appended. Retired Rose/Teal/Amber preferences keep
their established mappings. Unknown preferences safely fall back to Ocean Blue.
The selectable collection is read-only and theme preference writes retain the
existing atomic file replacement and logged fallback behavior.

**Stronger presentation:** each lightweight family has a distinct saturated
accent, darker hover, more visible selection/header tints and clearer input
boundaries. Professional Simple's blue is refreshed too. Shared success/warning
colors and muted guidance use readable contrast. Menus no longer use pale text
against their white surfaces. Selected student rows use a contrast-selected
foreground; modern and legacy solid actions share actual sRGB luminance-based
text selection, including hover/press fills. Lightweight hover uses its darker
semantic role color. Standard theme accent palettes remain intact.

`AppTheme` remains the public source of colors/rendering behavior;
`AppTheme.Appearance.cs` holds its portable palette/policy implementation.
Palette lookup uses fixed switches/cached palette values rather than a theme-list
search during painting. Fonts, spacing, widths, heights, data, control order,
action meaning and keyboard navigation are retained. The 55 portable theme
assertions verify saved identities/fallbacks, all five distinct palettes, at
least 4.5:1 primary/hover/text contrast and 3:1 input-boundary contrast on white,
plus readable solid-action foregrounds and independent refresh delivery.

**Resource/lifecycle improvements:** loading indicators stay static in lightweight
mode without allocating animation timers. Standard-mode loading timers are lazy,
released while hidden, on handle destruction and on disposal; Windows motion/
remote-session preferences are respected. Loading labels reuse the shared font
cache and refresh their colors. Status-attention flashing is limited to one
session per label, replaces prior sessions, cancels on theme changes and releases
its timer/subscriptions on disposal. Lightweight warnings retain their text and
tooltip with no flash timer. Popup/calendar shadows are disabled in lightweight
mode. Existing flat rendering avoids rounded clipping/path allocation and button
shadows; hover-transition timers are released on entry to any lightweight option.

**Safer switching:** recursive theme requests during refresh are ignored. Failure
to capture the old control bindings logs and aborts before changing the theme.
A failed theme subscriber is logged without blocking later subscribers; individual
control/form refresh failures during a theme change are contained so the remaining
UI can refresh. Forms/children are snapshotted during theme-change iteration.
The regular idle initialization does not add a snapshot allocation on every idle
callback. Account-menu theme changes are deferred until its click completes,
then recheck the session before the sidebar rebuild retires its owner button.
These safeguards contain refresh failures; they do not claim every third-party
control can always redraw successfully or that deployed CPU/RAM was measured.

**Verification:** **585 portable assertions** pass, including 55 new theme checks.
The application and expanded Windows smoke harness build with **zero warnings
and errors**. The harness covers forward/reverse switching of all nine choices,
saved selection, draft/validation/focus/geometry preservation, clipping regions,
flat paint paths, loading/attention lifecycle, contrast, failed listener/control
refresh and recursive callbacks. Compact/wide Users/Roles layouts include all
appearances. Those native GUI tests are **compiled but unrun** on Linux; Windows
100/125/150% DPI, physical color/readability and low-end resource measurements
remain acceptance work. The sixteen artifact/catalog tests still pass.

No SQL procedures, migrations, permissions, database version or DACPAC were changed
by this work. Database version 3 and its earlier migration handoff remain as
previously documented.
