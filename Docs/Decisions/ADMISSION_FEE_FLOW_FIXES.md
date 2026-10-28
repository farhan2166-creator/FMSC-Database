# Admission and fee-flow fixes — 28 October 2026

The owner requested fixes for all six findings in the admission/fee-flow audit. Migration 60 and the corresponding C# changes implement that scope.

| Finding | Resolution |
|---|---|
| F01 cancellation authorization | Main button, context menu, handler, dialog and leaving-fee review share the collection-plus-adjustment requirement. ReverseBillingCharge enforces both permissions at the write. Paid/partial charges and the fixed family counter cannot use the unpaid cancellation shortcut. |
| F02 long reason | BillingCharges gains nullable CancellationReason, CancelledAt and CancelledByUserId, with a user FK. New cancellations retain the complete reason and original Remarks separately. Existing rows remain null in the new fields; old cancellation metadata is not invented. Fee Change History displays the dedicated reason where present. |
| F03 pending transport | Start/Stop Transport requires an Active placement in the collection and fee-assignment interfaces. Pending registrations retain their existing registration fee-plan editing path; the assignment view explains this. SQL's existing Active-student rule is retained. |
| F04 actor attribution | Opening-charge posting accepts the authenticated actor and every production caller passes it. Activation also passes its actor. Credit application rejects a missing/invalid actor when credit is available; it no longer borrows the previous credit operator. Historical deposit/application attribution is not rewritten. |
| F05 aging account access | Open Student Account uses the destination access rule plus Fees.View, consistently in UI checks and the student lookup. Annual-receipt permission remains separate. |
| F06 false unknown outcome | Direct admission, historical admission and registration completion mark a business rejection with SQL state 60 only after their own verified rollback and before their own commit. An enclosing application transaction retains rollback ownership and is not tagged by this new contract. C# recognizes only the bounded verified signal, retains entered details/photo and enables an explicit corrected submission. Timeouts, lost replies, ordinary SQL errors and failed reconciliation reads remain uncertain and never trigger automatic retry. |

The application and installer require schema 60. Restore the owner's original snapshot through its supported migration chain, including 59 then 60 where applicable. No certificate cancellation was reintroduced, and no retiring generic audit writes were restored. These are bill cancellation metadata and normal recovery guards.

Validation completed before live application:

- 16 focused native flow-fix assertions.
- 221 existing admission, fee-selection, family-counter, collection-return, account/recovery and statement assertions.
- 28 existing transport assertions.
- 27 isolated SQL assertions plus full application startup/restore compatibility at schema 60. These cover migration rollback, retention of existing values, distinct restricted users, 500-character old notes and reasons, explicit credit actors, errors before and after number allocation, enclosing transaction rollback and a successful admission after refusal.
- Application/native project builds successfully; the native project reports only NU1900 for unavailable NuGet vulnerability data.

The SQL fixture owns a uniquely named private LocalDB instance, uses fictional records and accepts no school connection. Its source is Tools/TestAdmissionFeeFlowFixes.ps1; native cases are in Tests/ThemeSmokeChecks/Program.AdmissionFeeFlowFixes.cs.

Live deployment status: migration 60 is prepared and validated but has not been applied. Automatic approval review rejected live deployment because the owner's “fix all” request did not clearly authorize this exact production update. The database remains at schema 59; the new build requires migration 60 before use. No backup or live change was made by the rejected command.

Once explicitly approved, check live procedure definitions for drift, create and verify a private COPY_ONLY backup, apply the migration under one transaction and compare all original column values before committing. Newly added cancellation fields must remain null on pre-existing records. The only intended data write during deployment is the new migration-version marker.
