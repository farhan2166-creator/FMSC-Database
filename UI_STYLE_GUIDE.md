# UI wording and action style

Owner decision, 12 October 2026: the current Fee Collection layout is accepted.
Keep its existing layout and payment flow; revisit only for a demonstrated issue
or an explicit owner request. Clear/New deliberately discards edits immediately
without an unsaved-change confirmation; record switching and navigation retain
their existing confirmations.

Shared date fields support direct year/month navigation through the calendar
arrow, F4 or Alt+Down. Navigation applies only when a day is picked, Use Date is
clicked or Enter is pressed; Escape/outside-click cancels. Unchecked optional
dates display “Not entered”. Month-only and time spinners keep their existing
entry behavior, and every picker retains its date limits.

All single-line fields use `AppMetrics.InputControlHeight` through
`HumanUi.ApplyFieldInputLayout` (normally via `HumanUi.Field`). Field containers
own the spacing; do not add input margins that shrink the available height or
override single-line inputs to fill taller cells. Multiline fields keep their
own height or fill the area reserved for longer text.

Use `Core/UiText.cs` for repeated captions and instructions, and `Core/PageTitles.cs`
for titles shared with navigation. `Core/ActionButtonStyle.cs` sets action colours
for both directly created buttons and page action helpers.

- Use short title-case headings and field labels: “Student Profile”, “Fee Type”.
- Write subtitles and guidance as short sentences that explain the task or next step.
- Name actions with a verb, adding the object when needed: “Save”, “Remove Logo”.
- Keep meanings distinct: New starts a record; Clear empties fields or selections;
  Reset restores defaults or saved values; Cancel abandons an operation; Close exits.
- Save and Update use success styling. Apply, Create and other workflow actions use
  primary styling. Review, Print, Search and other supporting actions use secondary
  styling. Delete, Reverse and Restore use danger styling.
- Keep navigation, tab, outline and deliberate custom styles separate.
- Put eligibility rules and important effects beside the relevant controls. Do not
  remove warnings to shorten a sentence.
- Describe what users can do or see. Keep table names, stored procedures and data
  storage details in support details or logs.
- Use “…” on actions that open a file picker. Avoid unnecessary punctuation in titles.

Examples: “Choose a JPEG or PNG image up to 5 MB”; “Review unpaid charges and balances”;
“Creating this certificate marks the student Left”. Do not rewrite button captions
at runtime or replace New with Clear solely for visual consistency.

Check presentation on Windows in light and dark themes, at the display scales used
by staff. Confirm captions fit, guidance is readable, shortcuts still work, and
state-dependent actions retain their intended meaning.

## Fee-grid wording

Use `UiText.Fees` captions for fee columns on admission, registration, student
accounts, fee setup, billing, collection and finance-report grids:

- **Fee Type** names the fee; **Frequency** says when it recurs.
- **Amount (Rs.)** is the fee before discount.
- **Discount (Rs.)** is the rupee reduction; **Net Amount (Rs.)** is Amount minus Discount.
- Compare **Current Amount (Rs.)** and **New Amount (Rs.)** when changing rates.
- Keep distinct payment meanings: **Due (Rs.)**, **Pay Now (Rs.)**, and posted,
  collected or uncollected amounts. Net paid is not a fee's Net Amount.

The shared grid presenter also uses these captions for bound fee columns. Keep
data-property names, order, calculations and saved-view column identities intact.

## Lightweight appearances

Account → Change - Appearance/Theme lists each original color theme next to its
lightweight version: Ocean Blue, Emerald Green, Professional Orange and Royal
Violet, each explicitly labeled Standard or Lightweight. Professional Simple
remains an additional lightweight option. All lightweight options use the same rendering path:
flat controls, immediate hover feedback and no decorative loading/attention
animation or popup shadows. Strong accents, dark guidance and visible borders
retain a professional management-system appearance. Labels, input sizes,
spacing, focus order and work flows remain the same.

`AppTheme` remains the public source of theme colors and rendering mode.
`AppTheme.Appearance.cs` contains its internal portable palette/policy; do not
create independent per-page theme settings or checks for ProfessionalSimple
alone. Use `AppTheme.UseSimpleRendering` for rendering choices and
`AppTheme.IsLightweight(theme)` for appearance-menu labels. Treat progress text
and warnings as functional information; animation is optional decoration.
