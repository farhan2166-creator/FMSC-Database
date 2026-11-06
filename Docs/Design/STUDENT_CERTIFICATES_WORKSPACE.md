# Student certificates workspace

On 6 November 2026 the owner made the workspace the default for **Student
Certificates** on the dashboard and kept the original certificate cards and
screens as a fallback. To restore the original dashboard view, set
`UseStudentCertificatesWorkspace` to `false` in `StudentCertificatesPage.cs`
and rebuild. Both dashboard entry points use the same switch. Saved certificate
records are shared by both views.

## Implementation and verification

The workspace selects one student, offers a button for each permitted
certificate type, and presents a filterable register
of saved copies. The selected copy has contextual detail, print/reprint,
collection and reissue actions. Search and selected-copy details/actions share
a left column of about 20% of the available width (with a readable minimum).
The right pane switches between the register and the existing certificate fields.
The form locks the student identity and shows operation feedback. A confirmed
save returns to the register. Back and Escape discard pending edits without a
prompt, and an idle editor allows the app to close.

Record and issue IDs remain distinct. Refresh preserves the selected copy;
creation/reissue focuses the newest applicable type. Changing students resets
copy preference. Printing does not collect or create another copy. Collection
locks the copy; cancellation, deletion and uncollection are absent.

No SQL objects or report templates change. Existing procedure authorization,
optimistic versions, leaving review and durable recovery remain authoritative.
The workspace is not a second certificate persistence or printing engine.

Local checks: Release build; SQL-free identity/permission/projection checks;
native WinForms renders of the workspace and all eight detail dialogs; existing
certificate regressions. Synthetic renders do not verify actual database writes,
physical printer output or real operator acceptance.

Verification result: Release build passed with zero warnings/errors; 26 workspace
checks, 39 native render and menu checks, 113 collection checks across ten forms, 58
submission checks, 10 Character reprint checks, 7 Regular Student search checks,
23 reissue checks and 1,252 existing layout checks passed. The older
`--certificate-navigation` suite still fails the Character New/Next focus
assertion; that existing ClearForm path was not modified by this workspace.
Its keyboard-focus behavior remains an open check before retirement.

Selection loads the exact saved copy even when the original editor's hidden
history viewport is too small to scroll. The UI checks exercise all eight inline
forms, tab order, returning after a confirmed save and closing an idle editor.

Before retiring the original screens, verify all eight types against an
authorized disposable database: create/save, exact older-copy reprint,
collection locks, reissue eligibility, permission changes and lost-reply recovery.
Then extract the remaining operation ownership from the original pages into
shared services before deleting their code. Preserve business and recovery
records and the existing RDLC output.
