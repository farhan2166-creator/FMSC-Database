# Report preview checks

Run the portable page-arrangement checks:

```sh
dotnet run --project Tests/ReportPreviewChecks/ReportPreviewChecks.csproj
```

They check that original pages remain distinct, fit views show pages side by side, zoom retains aspect ratio, pages do not overlap, and scrolling reaches the final page. They do not exercise WinForms, EMF rendering or a printer.

## Windows review

- Open a family receipt for two or more students. Choose Multiple pages: each student's original receipt must appear as a separate page on the canvas. Resize, zoom out to see more pages, and zoom in to check legibility. Page count, receipt numbers, fees and balances must match Single page.
- Close and reopen the preview and restart the app: the chosen view must persist for this application user on this PC. Switching back to Single page must persist too. Other users retain their own preference.
- Ctrl++ (main keyboard or numeric keypad) and Ctrl+- must zoom. Ctrl+wheel must zoom without also scrolling. Ordinary wheel must scroll. Ctrl+0 fits pages; Ctrl+1 fits width. Right-click the canvas must offer the same zoom/fit choices. Zoom controls must stay disabled during rendering.
- Drag the report canvas with the left mouse button at a scrollable zoom. Check both axes, releasing outside the canvas, cancelling and changing windows. A simple click in the original single-page viewer must still activate report links. In Multiple pages, clicking a page selects its page number; Previous/Next must show the selected page.
- Print the same receipt from both views on the small receipt printer. Paper dimensions, separate student pages, amounts and page ranges must be identical. Print only page 2 and then all pages. Export PDF from both views: page count, dimensions and content must be identical. There is no A4 or combined-sheet receipt layout.
- Page setup must invalidate and rebuild the on-screen overview using the chosen page settings. Cancelling print/export must leave the preview usable. Close after visiting Multiple pages to verify that streams/images are disposed.
- Review a regular multi-page report and the annual receipt report at 100% and higher Windows scaling. Existing report definitions, printing layout and annual sheet layout must remain unchanged. The multiple-page overview draws only visible pages, but holds the rendered page streams until closed.

## Direct navigation and current-page printing

- Ctrl+G must focus/select the page-number field. Enter or Go must navigate to the entered page in both views. Values stay within 1 and the report's current page count. Ctrl+Home/End must reach the first/last page, including after scrolling away from an already selected page. Navigation stays unavailable while loading.
- Ctrl+2 must return to 100% in either view. Existing Ctrl+0/1 and zoom gestures remain available. Owned print/export dialogs must retain their own keyboard handling.
- In Multiple pages, click page 2: the toolbar must say Print page 2. Right-click page 3 must select that page and offer Print page 3. The print dialog must start with From=To=3 and the page-range option selected, while normal Print still starts with all pages. Cancelling must not print; changing the range in the dialog remains supported. Check the first/last page, a single-page report and a small receipt printer.
