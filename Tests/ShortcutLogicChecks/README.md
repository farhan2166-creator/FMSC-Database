# Shortcut logic checks

Run from the repository root with .NET 8:

```sh
dotnet run --project Tests/ShortcutLogicChecks/ShortcutTests.csproj
```

These dependency-free checks compile the production shortcut catalog and controller
against minimal simulated controls. They exercise focused-editor resolution,
ambiguity, disabled targets, search focus, native editing keys, cache invalidation,
local registration, tab navigation, and consistent action styling. They do not test native WinForms key routing,
report viewer integration, DPI scaling, or visual layout.

On Windows, also check:

1. Open Hot Keys with F1 from the dashboard. Search by action, key and description;
   search punctuation such as `'` and `[` must be treated literally. Resize the
   window and confirm the three columns remain readable with wrapped descriptions.
2. Use Ctrl+N/S/F on an entry page. On split editors, focus each editor in turn and
   verify its own action runs. Ambiguous and disabled actions must not execute.
3. Type in an input: Ctrl+A/C/X/V/Z and Ctrl+Backspace/Delete retain editing behavior.
4. Hold Ctrl or Alt to see available badges. Use Alt+1–8 for dashboard navigation,
   Ctrl+Tab/PageDown and Ctrl+PageUp for tabs, and Ctrl+1–9 for direct selection.
5. Open a report preview: Ctrl+P prints, Ctrl+E exports, Alt+P opens page setup,
   Ctrl+Left/Right changes pages, Ctrl+0/1 changes zoom and Esc closes. Disabled
   report actions must remain blocked while the report is loading.

Changed bindings: View → Ctrl+O; Add → Insert; Edit → F2; Clear → Alt+C;
Scan → F8; Delete/Remove → Alt+Delete; previous tab → Ctrl+PageUp;
dashboard modules → Alt+1–8; report page setup → Alt+P.

Also compare Student Admissions, Staff, School Information, Fee Rate Setup and
certificate pages in both light and dark themes. Common actions should use the
same styling, while New, Clear and Reset keep their own meaning. Check long labels
and guidance at your usual Windows display scale.
