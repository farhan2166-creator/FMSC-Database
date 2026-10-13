# Existing UI flow and consistency — 2026-10-13

Reviewed shared navigation, master editors, entry focus, report filter bars,
feedback methods, workspace tabs and representative admission/collection/report
call sites. This pass changes no SQL, financial rules or layout metrics.

## Changes

- The top bar's previously blank location label now shows Module › Page, plus the
  selected workspace section. Long paths retain their full tooltip. Workspace
  section changes update it immediately, and rebuilding the top bar retains the
  current location. A failed page factory no longer changes navigation metadata
  to a page that never opened; failure feedback names the requested destination.
- Shared master editors preserve unsaved input when the same record is reopened
  with a row click, Enter or the existing programmatic edit route. They do not
  ask to discard that same draft. Different-record and leave confirmations remain.
  Clean editors still reload refreshed values when the same record is opened.
- Opening an editor focuses its first editable field. Read-only output fields
  are skipped by entry focus/Enter traversal; normal Tab navigation remains.
- Enter at the final master-editor field now focuses Save rather than submitting
  immediately. A separate Enter/click or the existing Ctrl+S shortcut saves.
  This matches the existing general form-entry convention. New/edit status lines
  explain the current mode and the next action.
- The optional Payment Method dropdown participates in active-filter counts and
  Clear Filters. Explicit filter registration keeps report view/grouping choices
  intact; changing Bank/Cash can no longer leave “No optional filters” displayed.
- Shared report guidance no longer says every report can run without a scope;
  it explains selecting scope, All and unchecked optional dates.
- Lightweight-theme warnings retain steady status colour and their tooltip,
  without starting the decorative pulse timer.

## Message review

Routine success feedback already uses the inline Info/status path. It remains
inline. The redundant same-record discard prompt was removed. Payment, deletion,
refund/reversal, genuine record replacement, connection recovery and failed-write
messages retain their existing safeguards; no blanket conversion of errors into
silent status text was made.

## Validation and Windows checks

Application and Windows smoke-harness cross-build with zero warnings/errors.
Portable audit and expanded performance/ordering checks pass, including location
formatting with workspace sections and duplicate/empty segments.

The Windows smoke harness includes shared entry-focus/Save behaviour, same-record
draft preservation in an unshown master fixture, workspace-section notification,
and registered choice-filter counts/reset while preserving view choices.
The master fixture checks that its handle is not created, so its deferred database
loader is not started. These native checks are compiled but unrun in this Linux
environment.

On Windows, review location visibility and tooltips at laptop widths/DPI, New vs
Edit context, row click/Enter with unsaved fields, inline grid boolean/delete
actions, keyboard-only entry through Save/Ctrl+S, finance Payment Method counts
and Clear Filters, and warning presentation in both simple and existing themes.
No school database was used for this pass.
