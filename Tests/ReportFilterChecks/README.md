# Responsive report filters

Run `dotnet run --project Tests/ReportFilterChecks/ReportFilterChecks.csproj`.

The checks exercise startup sizing, wide and narrow windows, boundary widths, and unique reachable cells for every filter. They verify the layout calculation, not native WinForms rendering.

On Windows, open Outstanding & Aging, Collections & Receipts, Billing & Discounts, Billing Status, Unposted Fees and Monthly Fee Posting. Resize between 1080, 1366 and 1920 pixels, then review at 125% and 150% display scaling. Filter controls should wrap only when crowded; buttons and grid headings should remain reachable. Select filters, resize repeatedly, and confirm the values, tab order and query parameters are retained. Also review Promotion History's separate filter card.
