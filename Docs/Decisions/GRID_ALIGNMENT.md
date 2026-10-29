# Shared grid alignment — 29 October 2026

Headings and displayed values must use the same alignment, including after
binding, resizing, theme refresh and sorting. The shared grid renderer owns
these defaults; individual pages can deliberately choose another alignment.

- Names, descriptions and other prose retain their existing left alignment.
- Compact dates, periods, admission numbers, numeric values, checkboxes and
  category badges default to centered alignment when the page has no preference.
- Explicit page choices remain authoritative. For example, financial statements,
  future-fee comparisons and receipt breakdowns retain right-aligned money.
- Fee Collection and its recent-payment amounts use centered headings and values.
  Fee-rate copy and bulk-change previews center their formatted currency strings
  without changing those strings or their underlying numeric plans.
- Last Posted/Last Billed Month and period columns receive date-style alignment.

Sortable native headers reserve an arrow slot that shifts their text relative
to body cells. The shared renderer paints centered/right-aligned sortable header
text using the full padded column bounds, and retains the sort indicator and
native sorting interaction. Unsortable and left-aligned headers keep native
painting. Existing header accents, wrapping and theme colors remain.

The implementation changes presentation only. It does not rewrite values,
change number formats, alter payment allocations or require a SQL migration.
Regression coverage includes real fee-assignment and payment-history grids,
typed student records, right-aligned statements, sorting, refreshes and resizing.
