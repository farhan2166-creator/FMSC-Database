# Uploaded schema-6 DACPAC verification

Reviewed repository upload: `b5cb934`. Package SHA-256:
`b66ec66f24f06fe1623445c0f433afb5cd8400270fd73612e273b7fa22f156a6`.
The supplied DACPAC was read as supplied; it was not rewritten or replaced.

## Schema and implementation comparison

Built an independent expected schema from the previously reviewed version-3
DACPAC and the exact repository migrations 4, 5 and 6 in disposable SQL Server.
Compared the extracted expected schema against the upload: all named objects,
columns, procedure bodies/signatures, dependencies, constraints and indexes match.
Anonymous constraints also match as an unordered set. Reviewed extraction-only
differences explicitly: identity last values, source-location lengths, dynamic
parser objects/disambiguators, boolean/XML formatting, database-specific recovery
GUID and Query Store default representation (SQL 2019 artifact vs SQL 2022 host).
No unexpected procedure, table, signature or constraint change was accepted.

The upload contains 60 tables, 232 procedures, 34 triggers and 273 fingerprinted
SQL modules in total. The only procedure-body changes relative to the old reviewed
artifact are migration 5's AddStudentFeeAssignment, CompleteQuickAdmission and
PostEnrollmentOpeningCharges. QuickRegistrationRequests and MutationRequests,
their keys/checks/foreign keys and the recovery scope match migrations 4 and 6.

Updated the application's complete baseline inventory and reviewed fingerprints
for this exact upload: 334 objects, 541 columns, 974 parameters, 33 TVP columns,
104 uniqueness safeguards, 34 triggers, 76 foreign keys, 52 checks and one required
database recovery property. Additive migration requirements remain checked without
weakening the version or recovery-identity contract.

## Native verification

Published the upload, including its data, only into a disposable loopback SQL
instance. Its DatabaseVersion data confirms version 6. All constraints were
trusted/enabled and DBCC CHECKCONSTRAINTS reported no violations. All 28 current
application permission labels/descriptions agree with the catalog; only
Administrator and Accountant are defined, Accountant's grants match the reviewed
snapshot, and Administrator passes every application permission check.

DacFx publishes the database extended-property GUID as nvarchar. In the disposable
publication only, converted that same GUID back to migration 4's required
uniqueidentifier SQL variant type. The production compatibility check then passed.
No school database, permission definition or artifact bytes were altered; the
application's strict property-type check remains intact. This distinction matters:
these checks do not endorse publishing a DACPAC over the school MDF/LDF.

Ran all 72 existing quick-registration SQL checks on this uploaded-artifact copy,
plus 32 financial/bank recovery checks with synthetic fixtures: actual committed
writes followed by injected lost acknowledgements, exact result lookup, concurrent
replay, changed-payload refusal, atomic marker failure, optional income, locking
and permission rejection. Verified a checksum backup, VERIFYONLY and actual restore
of the disposable data, followed by the shared startup/restore compatibility
check. Migration 6 rerun is a no-op on the current snapshot.

## Other validation and limits

Offline provenance, full inventory, whole-rupee safeguards and all 273 module
fingerprints pass; all 16 Python regression tests pass. Application and Windows
smoke project compile with zero warnings/errors. The native SQL harness still
reports NU1900 because NuGet vulnerability metadata is unreachable; it compiled
and executed successfully without changing dependency versions or disabling audit.

This closes the pending full-artifact baseline review recorded after the FQA
implementation pass. It does not substitute for native Windows UI acceptance,
physical network interruption, printer/DPI testing or the release packaging
workflow using the owner's detached MDF/LDF. The new independent post-fix audit's
RFA-01/02/03 are separate C# UI findings; they are not defects in this DACPAC.
No additional SQL migration is required by this verification.
