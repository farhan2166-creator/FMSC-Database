# UI wording and action style

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
