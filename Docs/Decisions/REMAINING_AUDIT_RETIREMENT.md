# Remaining generic audit writers — 28 October 2026

Following the redundancy review and live schema-58 inventory, the owner explicitly requested work on the remaining audit writers and delivery as migrations. Migration 59 is the scoped exception to the earlier gradual-retirement policy: it revises the 19 identified procedures together, removing their generic audit writes and audit-only snapshots. Future unrelated procedure changes still follow AGENTS.md.

The migration retains original procedure signatures, permissions, business writes, returned results, errors and transaction handling. Preview hashes and JSON used to reject stale decisions remain. The certificate-link repair cursor existed solely to write audit rows; the set-based business repair remains.

No historical audit data is deleted. AuditLogs, AuditOperations, AuditValueLeaves and historical readers remain because StudentFeeChangeHistory still uses them. That report continues to disclose incomplete history; stopping generic audit writes does not create a complete replacement change ledger.

Operational student/enrollment history, certificate issue records, bills, payments, allocations, credits, request markers and recovery state remain. The communication module's separate operational history also remains; only its writes into the retiring dbo audit system are removed.

The application's required version and installer target are 59. A restored snapshot at schema 58 must apply Migrations/59.sql before running this build. Earlier supported snapshots must apply every missing numbered migration in sequence. The DACPAC remains the reviewed schema-only baseline, not a replacement for school data.

Validation:

- Validation/CompareRemainingAuditSql.ps1 parses the 19 baseline/migrated definitions and compares business DML, procedure calls, THROW statements, transactions and returns. Audit-only statements are explicitly excluded.
- Tools/TestRemainingAuditRetirement.ps1 creates a private, owned LocalDB instance, publishes the schema-only DACPAC and adds fictional data. It checks rollback, unchanged existing rows, parameter contracts, rejection behavior, successful sample business operations and retained historical readers. It accepts no school connection or backup.
- The application builds successfully with zero warnings and errors.
- Final results: 19 parsed business-SQL comparisons and 75 isolated database checks passed, including populated historical audit rows and rollback of all procedure definitions. The native test project also builds; its only warning is NU1900 because NuGet vulnerability data could not be fetched.

Deployment status: migration 59 is prepared and tested, but has not been applied to the live database. Automatic approval review rejected live execution because the request did not explicitly authorize this exact production change. Live schema remains 58. Applying it requires explicit owner approval, a verified private backup, a definition-drift check and a single transaction with existing-row verification. No backup or live migration was performed by the rejected command.
