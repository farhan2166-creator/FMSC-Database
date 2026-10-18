# Retired migrations for disposable tests only

These historical versions 7–19 reconstruct schema 19 from the retained record-free
schema-6 test fixture. They are not release migrations and are not copied into
installation packages. The current application and installer require schema 19.
The SQL runner also publishes the current root schema into a separate owned,
empty database and checks real school scenarios there.
