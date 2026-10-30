# Keyboard shortcuts

Page commands use one modifier and one key. The Hot Keys page, button tooltips
and the hints shown while holding Ctrl or Alt use the shared command definitions.

| Action | Shortcut |
| --- | --- |
| Save changes, including committing Add Fee / Add Charge | Ctrl+S |
| Start a new record | Ctrl+N |
| Find / Search | Ctrl+F |
| Edit selected record | Ctrl+E |
| Print / Preview | Ctrl+P |
| Refresh / Reload | Ctrl+R |
| Export (including report previews) | Alt+E |
| Add a draft row or open an Add dialog | Alt+A |
| Load | Ctrl+L |
| View / Open | Ctrl+O |
| Clear editor | Alt+C |
| Run | Alt+R |
| Generate | Ctrl+G |
| Scan | Alt+S |
| Apply / Confirm / Collect / Process | Ctrl+Enter |
| Delete / Remove | Alt+Delete |

Ctrl+K opens quick navigation and F1 opens shortcut help from the dashboard.
Normal text editing keys, field navigation and tab navigation retain their usual
behavior. F2 remains available for native grid cell editing.

Commands follow the active editor. Hidden, disabled or ambiguous buttons do not
execute. Validation, permission checks, confirmations and busy-state checks still
run through the existing button handlers.

An Add button that opens an entry dialog uses Alt+A. The button that actually
saves the entry uses Ctrl+S. Commit buttons with captions other than Save declare
`ModernButton.SavesChanges`; they are excluded from caption-based Add/New actions.
Use the existing `SetSaveShortcut` override when one primary Save must take
precedence, such as admission Save versus Save Photo.

The former Insert/Add, F2/Edit, F5/Refresh, F8/Scan, Ctrl+E/Export and Ctrl+R/Run
page bindings have been replaced by the conventions above.

SQL-free regression checks:

```powershell
dotnet run --project Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -- --action-shortcuts
```
