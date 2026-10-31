# Shared fee management — 31 October 2026

The owner confirmed that Admissions fee management and Fee Collection fee changes
must use the same rules and options through `ChangeFeeDiscountForm`.

## Decision 1: Different entry points, shared editor behavior

- Admissions opens one fee-management window with four action tabs: Give Discount,
  Waive Fee, Correct Fee Amount and Remove Discount.
- Fee Collection opens the corresponding action in a separate window from each
  right-click menu entry. The action is preselected; the action tabs are hidden.
- Both show the same applicable amounts, concession types, scopes, date limits,
  optional reason, and paid-money treatment. Both use enter details → confirm on
  the same screen. Financial validation and snapshot checks run internally before
  saving; there is no subsequent review screen.
- Both load the current saved fee terms through the shared assignment factory.
  Permissions, payment protection, stale-data checks and save recovery are shared.

## Decision 2: Collection intentionally starts with a single-month period

- Fee Collection resets **both From month and Through month to its start month**.
  The start month is the clicked charge's month, or the current month when no
  charge month is supplied, constrained to the fee's valid period.
- The operator can still change the period using the same rules and limits.
- Admissions keeps its normal period default, including its normal through month.

These entry-point and initial-period differences are intentional. Do not align
Collection's initial Through month with Admissions or introduce other differences
in fee rules/options without a new owner decision.

The C# implementation records these decisions at `ConfigureCollectionAction`.
