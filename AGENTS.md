# Established release packaging decision

The user supplies the latest detached `FMSC_DB.mdf` and `FMSC_DB_log.ldf` in the
`Database` folder adjacent to the project folder (`../Database`) when preparing
a release package. The publish configuration validates and ships that pair.
This is an established, user-owned release preparation step.

Do not treat the absence of those files during development or a readiness review
as a release blocker, an overlooked requirement, or something to repeatedly ask
the user to provide. Check their presence and validity when actually preparing
or validating the release package.

Assess database migration requirements against the intended deployment scenario
and the supplied snapshot. An empty migration folder alone does not establish
that a release is blocked. Installation, existing-database upgrades, and recovery
testing are separate verification questions; report concrete findings about them.

# Database change delivery

Put all required database schema or stored-procedure changes in versioned SQL
scripts in `Migrations` so the user can reapply them after restoring the original
database. Do not rely on changes made only to the currently attached database.

# Gradual audit retirement

When revising a stored procedure, remove its writes to the retiring audit system
and audit-only before/after JSON preparation. Do not make a separate sweep through
unrelated procedures. Preserve operational student/lifecycle history, bills,
payments, allocations, credit movements, migration versions, and request/recovery
markers: these remain necessary business and recovery records.
