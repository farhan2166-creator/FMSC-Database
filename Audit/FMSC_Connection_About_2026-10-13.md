# Connection compatibility guidance and About — 2026-10-13

The attached-database check previously converted the compatibility exception to
text, wrapped it in a generic InvalidOperationException, and then discarded its
operator-facing explanation. The connection page also treated every detected
compatibility problem as a database upgrade, including a newer database that
instead requires a matching application.

Compatibility failures now retain their issue, required/found schema versions,
and diagnostic detail through attachment, startup checks, and connection recovery.
The connection page displays the versions and the appropriate next step directly:

- Older database: back up the school data and apply the release migrations using
  FMSC Setup, then reconnect.
- Newer database: install a supporting FMSC application release; do not downgrade
  or replace the school data.
- Missing/unrecognized version: verify the database and version history before
  updating. Missing version-table columns retain their diagnostic explanation.
- Matching version with incompatible components: review the structural diagnostics
  and complete the approved update/recovery.
- SQL compatibility level or database identity mismatch: show the corresponding
  setting or identity guidance rather than a schema-upgrade instruction.

Restart and dependency repair are disabled for compatibility failures; Reconnect
remains available after administrative correction. Explanations wrap and scroll,
and expanding diagnostics respects the working area. Identity/path checks and the
full structural manifest remain required. No schema migration or automatic data
replacement is introduced.

Account → About FMSC opens a themed dialog with actual application/version build
metadata, required database schema, appearance, and .NET runtime. It can copy these
details for support, with inline clipboard feedback. The dialog does not query the
database or display an unverified current database version.

Validation: 27 portable compatibility guidance checks pass, together with the 177
existing audit checks. Application, Windows smoke harness, and SQL integration
harness compile with zero warnings/errors. The expanded Windows harness covers
main-message versions, older/newer guidance, restart/repair/reconnect state, and
About contents/closing. It is compiled but unrun in this Linux environment. SQL
integration was not executed for this pass, and no school database was accessed.

Windows review: open About from Account in both Professional Simple and an
existing theme; check keyboard closing, Copy details, wrapping at laptop DPI;
review startup/reconnect with older, newer, unknown and structurally incompatible
disposable databases, including expanded diagnostics at a small working area.
