# Cross-application reliability hardening — 2026-10-13

This follow-up inventories the tracked C# source and reviews targeted startup,
authentication, navigation, student-photo, promotion, fee-posting, collection,
report-preference and backup/restore paths alongside the previous audits. It is
a source and portable-regression review, not an assertion that every application
line or native Windows interaction has been exercised.

## Confirmed gaps addressed

| Area | Previous gap | Result |
| --- | --- | --- |
| Connection explanations | Engine-busy, wrong-engine, attachment and ordinary SQL explanations were replaced by generic text; recovery details were omitted from exception logging. | Typed reasons retain their explanation and engine output. Restart/repair choices match the reason, including missing files and incompatible databases. Repair errors update the diagnostics panel. |
| Database readiness | An existing attachment could be fully validated twice per connection check; a fresh attach could set the ready flag before validating. | Both attachment paths validate once before readiness is cached. Explicit connection checks still revalidate; failed validation clears readiness. |
| Restore | A restore attempt could change the database while leaving the old readiness and lookup caches active. | Once RESTORE is attempted, all connection/readiness and lookup caches are refreshed in finally, including failed execution or safety-recovery paths. Later access must validate again. |
| Sign-in | SQL and password derivation blocked the UI; authentication exceptions lost diagnostics. | One worker attempt uses captured credentials. Fields and closure are held until completion, errors are logged, and connection failures use recovery without automatically repeating authentication. Password policy, lockouts and permissions are unchanged. |
| Posting/promotion navigation | Monthly posting, additional-fee posting and bulk promotion held their own actions but did not expose active writes to the shell/workspace navigation guard. | These pages implement IBusyPage for writes. Fee-posting inputs are held during posting; leaving, tab closure and sign-out must wait for the outcome. Read-only loading remains outside their write flag. |
| Student photos | An old same-student read could replace a newer result/draft; oversized files were read fully before checking 5 MB. The shell did not know about photo drafts or active writes. | Generation/cancellation checks protect reads and replacement-file loading. The file length is checked before allocating, and reads reject changes/cancellation. Drafts hold their student and participate in navigation checks; saves/removals hold controls and navigation. Cache warm-up waits for page activation. |
| Preferences | Theme/backup settings could truncate the previous file; report layouts reused one temporary filename. Backup-folder save failure was logged while the UI still claimed success. Unknown numeric theme values could become the active theme. | Unique temporary files are flushed before replacing theme, backup and report-layout settings. Failed backup-setting writes preserve the old preference and prevent relinking/reporting success. Folder updating participates in the busy guard. Unknown themes fall back to Ocean Blue. |
| Collection recovery journal | Structurally incomplete JSON could lack the information needed for payment review. Cleanup did not verify ownership and was not idempotent. | Missing IDs, summaries and dates require administrator review. Completed cleanup is idempotent; a stale journal owner cannot delete a replacement payment intent. Commit/rollback ambiguity continues to retain the journal and never triggers an automatic payment retry. |

No schema, DACPAC, migration, money policy, role entitlement or automatic-write
retry changes are introduced. The existing restore safety backup and compatibility
checks remain mandatory. Existing collection receipts remain usable after a
confirmed commit even if local cleanup fails.

## Verification

- 255 portable audit checks pass: 75 money/quotation, 102 role/search/geometry,
  27 compatibility, 21 recovery-action and 30 persistence/photo/journal checks.
- 32,176 portable loader, cache, report/picker cancellation, latest-read and
  ordering checks pass.
- Artifact verification and nine regression tests pass: ZIP/model checksums,
  provenance, contract inventory, whole-rupee constraints and 273 SQL-module
  fingerprints. The DACPAC has not changed.
- Application and Windows smoke harness cross-build without warnings/errors.
  The expanded harness covers recovery messages/actions/diagnostics, About,
  sign-in credential capture/reentry/closing, and photo write/draft guards using
  SQL-free fixtures. Native Windows tests are compiled but unrun here.
- No school database was opened. SQL integration and real restore/attachment
  failure tests were not rerun for this pass.

## Required runtime verification before deployment

Run on a disposable Windows profile and separate test databases, then repeat
release-specific checks against the intended snapshot at packaging time:

1. Execute the Windows smoke harness and review both Professional Simple and an
   existing theme at laptop widths and 100/125/150% DPI. Check expanded connection
   details, About, keyboard-only entry and cancellation/closing.
2. Exercise sign-in with slow/unavailable SQL, valid/invalid credentials, durable
   lockout, missing version history and older/newer/structurally changed schemas.
   Reconnect must not automatically authenticate or repeat a write.
3. Interrupt collection before/around commit; reopen the page and application.
   Inspect receipt/history before acknowledging recovery. Test two application
   instances and an unreadable/incomplete journal; never collect twice to resolve
   an unknown result.
4. During monthly/additional posting and bulk promotion, attempt to leave, switch
   workspace tabs, sign out and close. Review committed counts and partial-batch
   reporting, including connection loss after a server commit. Run the existing
   two-clerk and permission SQL checks on clones.
5. Exercise restore success, incompatible restore with successful safety recovery,
   and failed safety recovery. Later access must validate the actual database;
   existing lookup data must not be reused after a restore attempt.
6. Choose/change/save/remove photos with slow reads, A → B → A selection, unsaved
   navigation and oversized files. Check real image decoding, GDI resources and
   memory under repeated use; the file-byte limit alone is not a measurement of
   decoded image memory.
7. Test settings/export/backup storage on the intended Windows account, including
   denied access and disk exhaustion. Run realistic school-size reports, printing
   and a sustained entry/collection session on the minimum supported PC.

These runtime checks remain necessary. Passing portable tests and cross-builds
does not by itself establish deployment readiness or measured low-end-PC memory,
CPU or end-to-end response time.
