# Professional Simple theme — 2026-10-13

Choose **Account menu → Change - Appearance/Theme → Professional Simple
(Lightweight)**. The choice uses the existing saved appearance preference, and
the existing themes remain selectable. The new enum value is appended, preserving
the numeric values of all older preferences.

The palette uses restrained blue primary actions, white surfaces, neutral page
backgrounds and visible borders. Fonts, spacing, control dimensions, navigation,
keyboard handling, validation and business workflows are not changed.

## Rendering changes

- Buttons, text fields, dropdowns, date fields and student pickers use flat
  rectangles instead of allocating rounded paint paths and clipping regions.
- Cards use flat borders, release their retained rounded paths and clipping
  regions, and restore rounded rendering when switching back.
- Button shadows are omitted; keyboard focus, pressed, disabled and validation
  indicators remain visible.
- Shared hover/border transitions apply immediately. No animation timer is
  created for the simple theme, and switching into it disposes existing timers.
- Status badges and selected popup menu items use flat fills instead of curved
  paths. Status meanings and action labels remain intact.
- Logos, functional icons, dashboard charts and clear text rendering remain in
  place. The theme does not replace controls or rebuild page workflows.

## Validation and limits

The application and Windows smoke-check executable cross-build successfully.
Existing portable audit checks (75 money/quotation and 102 role/search/geometry
checks) and the loading/cache regression suite pass.
Palette calculations give body text on white a 14.68:1 contrast ratio and white
text on the primary blue 10.54:1.

`Tests/ThemeSmokeChecks` exercises real shared controls on Windows: theme selection,
repeated switching, control bounds/insets/fonts/tab order, entered values,
validation state, focus, rounded-region release/restoration, immediate transition
feedback without a timer, shape allocation and bitmap rendering. It uses no SQL.
Run on a disposable Windows test profile:

```powershell
dotnet run --project Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj
```

The test restores the initial theme on exit. It has been compiled here but cannot
be executed in this Linux environment. Interactive Windows/DPI visual checks and
CPU/RAM comparisons on a low-end Windows PC remain unmeasured; no numerical speed
or overall memory improvement is claimed for this theme.
