# Class and section setup consolidation — 2 November 2026

Campus Classes / Sections is the single setup entry point. Manage Classes and
Manage Sections open the complete existing editors in separate dialogs. Programs,
class/section editing, ordering, usage checks and guarded deletion are retained.
The old Classes & Sections workspace/navigation wrapper is removed.

Catalog records are school-wide. Renames affect every campus; creation does not
assign the new record to any campus. Apply Changes remains the only assignment
save action. Existing SQL procedures, permission requirements and history remain
unchanged; no migration is required.

Closing a catalog dialog refreshes the current campus matrix. Pending differences
are restored by ClassId/SectionId after loading the fresh saved baseline. Renamed
and reordered records retain their ticks, deleted keys are dropped, and new
combinations start from the database baseline (normally unassigned). Refresh
Catalogs retries a failed read while retaining the pending differences. Reset
Changes restores the fresh saved baseline. Campus switching retains the existing
owner-approved prompt-free discard policy. Small editor drafts remain prompt-free;
closing a dialog during a submitted write is blocked.

Verification: build the native ThemeSmokeChecks project and run
`--class-section-setup` on Windows. The assignment fixture
checks ID-based draft restoration across catalog changes and Reset behavior;
the workspace fixture checks the existing full editors and in-flight save guard.
Existing SQL delete/usage protections are reused, not replaced by UI-only checks.

The shared master busy contract now includes asynchronous deletion as well as
saving, so catalog dialogs cannot close during either write. Two existing
shortcut-permission test callbacks were updated to accept the dashboard reader's
cancellation token, resolving pre-existing native harness compilation errors.
