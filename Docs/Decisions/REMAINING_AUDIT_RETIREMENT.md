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

Deployment status: the owner explicitly approved live application with “apply it”. Migration 59 was applied successfully after a COPY_ONLY backup with checksums and RESTORE VERIFYONLY verification. The original procedure definitions were checked for drift before execution. All existing data-table fingerprints (excluding the version marker) were unchanged inside the migration transaction before commit. Live schema is now 59, and no procedure calls the retiring generic audit writer. Existing audit records/readers remain. Application time (UTC): 2026-10-28T13:53:28.5961021Z. The private backup path is recorded in ignored local evidence at artifacts/audit-retirement-live-result.json.

The earlier automatic approval rejection was resolved by the owner's explicit approval. No permission remains pending.
