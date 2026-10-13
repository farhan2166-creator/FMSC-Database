# Dashboard review — 2026-10-13

Reviewed the welcome screen, dashboard shell, sidebar/quick-action routes, custom
charts, shared cards/theme/scale behavior, read cancellation and setup readiness.
Checked `GetDashboardSummary`, `GetAcademicYears` and `GetSetupReadiness` against
the embedded definitions in `FMSC.dacpac`.

The existing overview structure is retained: five totals, two static charts,
permission-filtered quick actions and conditional setup guidance. These controls
provide useful entry points without polling or chart animation. No feature,
sidebar group or database migration is added.

## Confirmed corrections

- Summary, academic year and readiness load independently. Previously, totals
  waited for both academic-year data and the entire readiness/backup check; an
  unrelated failure also erased successful totals.
- Dashboard reads share a latest-request scope and cancellation token. Leaving,
  replacing or reloading the page cancels obsolete reads, and late completions
  cannot change its controls. The shell accepts academic-year updates only from
  its current dashboard page. Setup readiness accepts optional cancellation;
  its existing dedicated page continues to work with the default token.
- Valid zero counts display `0`. Missing, null, malformed or negative count
  values are treated as unavailable data instead of being invented as zero.
  Summary errors are logged; unavailable totals are distinct from real zeros.
- The DACPAC returns school-wide student/class/staff counts and admissions since
  the beginning of the current month. Header and snapshot wording now match that
  scope instead of claiming academic-year filtering. Greeting uses “Welcome”
  rather than “Good morning” at every time of day.
- An unmarked year list no longer silently selects the first historical/future
  year. Missing selection shows “Not Set”; multiple current years show “Review
  Setup”; a failed read shows “Unavailable”. The unused, unparented year label
  was removed; the existing shell label remains the displayed year.
- The summary procedure requires `Reports.View`. Restricted accounts no longer
  issue that unauthorized read; they see “Report access required” while their
  own navigation and year/setup sections remain available. An empty quick-action
  grid provides sidebar guidance.
- Quick actions pass the same navigation callback as sidebar-created Admission,
  Profile, Registration and Reports pages. Nested page actions keep module/title
  context; the basic navigation fallback remains functional.
- Sidebar highlight changes only after a successful navigation. Cancelling the
  unsaved-change guard no longer selects the destination that was never opened.
  Sidebar rebuilds restore selection by route, and a welcome page is recognized
  as the dashboard regardless of the route used to open it.
- Card sizing reserves chart and action heights and accounts for wrapped setup
  buttons. The welcome page scrolls vertically when necessary instead of
  squeezing percentage rows to zero. Setup controls use the page's scrolling
  instead of a nested scroll area. Completed setup releases its space.
  Greeting spacing includes its bottom margin; KPI labels avoid default margins
  consuming their measured text/icon heights.
- Shortcut glyphs are disposed with their buttons; KPI fonts use the shared font
  cache; chart accent properties follow theme changes without a data reload.

## Validation and limits

288 portable audit checks pass, including 23 new checks using the production
dashboard data reader. They cover valid zeros/count mapping, missing/null/negative
values and current-year selection/ambiguity. All 32,176 performance regression
checks pass, including latest-request cancellation and stale-result rejection.
The application and Windows smoke harness cross-build with zero warnings/errors.

The Windows harness includes a dashboard fixture with injected, delayed reads
and no school SQL connection. It checks independent rendering, an unrelated
failure, late completion after disposal, cancellation, small-window scrolling,
setup space, quick-action routes, theme colors, resource disposal and restricted
account reads. These native tests are compiled but unrun on this Linux host.

On Windows, run the harness and inspect the dashboard at 1080 × 600 and 1366 × 768,
with 100/125/150% DPI, completed/incomplete setup and both Ocean Blue and
Professional Simple. Verify that cancelling an unsaved page switch leaves its
sidebar highlight unchanged and that Admission/Profile shortcuts keep nested
actions inside the main workspace.

No school database was accessed. SQL procedures, schema version and DACPAC are
unchanged. The existing summary procedure also computes finance totals unused
by this welcome screen; this review does not measure or change its SQL cost.
The loading improvements remove UI dependencies and obsolete work; actual
database latency and total CPU/RAM usage still require deployed Windows timing.
